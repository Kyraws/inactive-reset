using System.Text.Json;
using System.Text.Json.Serialization;

namespace InactiveReset.Core;

public sealed class CalibrationException(string message) : Exception(message);

/// <summary>
/// Measured placement behaviour for ONE track and ONE vehicle.
///
/// D, H and L do not describe the car. They describe the ENGINE'S placement
/// behaviour, measured through the car. That distinction matters when deciding
/// whether a game patch invalidates them: a code-only patch cannot move a car
/// body, but it CAN change how the engine places one.
///
/// When no eligible measurement exists, a placeholder supplies the default
/// vertical offset and planning derives horizontal offsets from the loaded car.
/// </summary>
public sealed class CalibrationProfile
{
    [JsonPropertyName("track_name")]
    public required string TrackName { get; init; }

    [JsonPropertyName("vehicle_name")]
    public required string VehicleName { get; init; }

    /// <summary>Forward distance from the drive-to point to rest, in metres.</summary>
    public required float ForwardDistance { get; init; }

    /// <summary>Vertical offset from the drive-to point to rest, in metres.</summary>
    public required float VerticalOffset { get; init; }

    /// <summary>
    /// Lateral offset from the drive-to point to rest, in metres — the
    /// off-axis component of the engine's rest displacement.
    ///
    /// Absent from format_version 1 profiles, where it reads as 0 and the model
    /// behaves exactly as it did before this term existed. That default is
    /// deliberate: an old profile keeps its old, characterised miss rather than
    /// silently acquiring a correction that was never measured for it.
    /// </summary>
    public float LateralOffset { get; init; }

    public required bool Valid { get; init; }

    /// <summary>Build this calibration was measured against.</summary>
    public string? ExecutableSha256 { get; init; }

    /// <summary>
    /// Hard refusal. Only an explicit <c>"locked": true</c> blocks use.
    /// </summary>
    public bool Locked { get; init; }

    /// <summary>
    /// Calibration provenance or limitations shown by the front ends.
    /// An advisory is not a lock and does not block placement.
    /// </summary>
    public string? Advisory { get; init; }

    public int SampleCount { get; init; }
    public float WorstHorizontalErrorMetres { get; init; }
    public string? SourcePath { get; init; }

    /// <summary>
    /// True when nothing measured this: a stand-in so a placement can proceed on
    /// constants derived from the running engine. Before these existed, `place`
    /// refused outright in any combination the user had not calibrated by hand,
    /// and there was no command to calibrate with.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>
    /// A calibration for a combination nobody has measured. D and L are filled
    /// in later from <see cref="RestModel.Derive"/>, which needs the live
    /// container; H falls back to <see cref="RestModel.DefaultVerticalOffset"/>.
    /// </summary>
    public static CalibrationProfile Placeholder(string track, string vehicle) => new()
    {
        TrackName = track,
        VehicleName = vehicle,
        ForwardDistance = 0f,
        VerticalOffset = RestModel.DefaultVerticalOffset,
        LateralOffset = 0f,
        Valid = true,
        IsPlaceholder = true,
    };

    /// <summary>
    /// The calibration for this pair, or null. <see cref="Require"/> is the
    /// same lookup with a refusal attached; callers that can fall back on
    /// derived constants want this one.
    /// </summary>
    public static CalibrationProfile? Find(string dataDirectory, string track, string vehicle)
    {
        var match = LoadAllForData(dataDirectory).FirstOrDefault(p =>
            string.Equals(p.TrackName, track, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.VehicleName, vehicle, StringComparison.OrdinalIgnoreCase));
        return match is { Valid: true, Locked: false } ? match : null;
    }

    public string Key => MakeKey(TrackName, VehicleName);

    public static string MakeKey(string track, string vehicle) =>
        $"{track}||{vehicle}";

    public static CalibrationProfile Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = document.RootElement;
        var calibration = root.TryGetProperty("calibration", out var c)
            ? c
            : throw new CalibrationException($"{path}: no 'calibration' block");

        return new CalibrationProfile
        {
            TrackName = Text(root, "track_name") ?? throw new CalibrationException($"{path}: no track_name"),
            VehicleName = Text(root, "vehicle_name") ?? throw new CalibrationException($"{path}: no vehicle_name"),
            ForwardDistance = calibration.GetProperty("forward_distance_D").GetSingle(),
            VerticalOffset = calibration.GetProperty("vertical_offset_H").GetSingle(),
            LateralOffset = calibration.TryGetProperty("lateral_offset_L", out var l)
                ? l.GetSingle() : 0f,
            Valid = !calibration.TryGetProperty("valid", out var v) || v.GetBoolean(),
            ExecutableSha256 = Text(root, "executable_sha256"),
            Locked = root.TryGetProperty("locked", out var locked) && locked.GetBoolean(),
            Advisory = string.IsNullOrWhiteSpace(Text(root, "lock_reason")) ? null : Text(root, "lock_reason"),
            SampleCount = root.TryGetProperty("sample_count", out var s) ? s.GetInt32() : 0,
            WorstHorizontalErrorMetres = root.TryGetProperty("worst_horizontal_error_m", out var w)
                ? w.GetSingle() : 0f,
            SourcePath = path,
        };

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>Calibrations the user measured. A release never writes here.</summary>
    public const string UserDirectoryName = "profiles";

    /// <summary>
    /// Calibrations shipped with the tool. A release overwrites this directory
    /// wholesale, which is precisely why the user's own measurements do not live
    /// in it: upgrading must never silently replace something they measured.
    /// </summary>
    public const string DefaultDirectoryName = "profiles-default";

    public static IReadOnlyList<CalibrationProfile> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }
        var profiles = new List<CalibrationProfile>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            profiles.Add(Load(file));
        }
        return profiles;
    }

    /// <summary>
    /// Every calibration visible to the tool: the user's own, then the shipped
    /// ones for any track/vehicle the user has not measured themselves.
    ///
    /// The user always wins, silently. Their calibration was measured on their
    /// machine and they chose to make it; a shipped default is a convenience for
    /// combinations they have not got to yet, not an authority over them.
    /// </summary>
    public static IReadOnlyList<CalibrationProfile> LoadAllForData(string dataDirectory)
    {
        var user = LoadAll(Path.Combine(dataDirectory, UserDirectoryName));
        var shipped = LoadAll(Path.Combine(dataDirectory, DefaultDirectoryName));

        var keys = user.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. user, .. shipped.Where(p => !keys.Contains(p.Key))];
    }

    /// <summary>True if this profile came from the shipped directory.</summary>
    public bool IsShipped =>
        SourcePath is not null
        && string.Equals(
            Path.GetFileName(Path.GetDirectoryName(SourcePath)),
            DefaultDirectoryName,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Find the calibration for a track/vehicle pair, and refuse to return one
    /// that is marked invalid or locked. A calibration is per track AND per
    /// vehicle; reusing one across combinations is meaningless.
    /// </summary>
    public static CalibrationProfile Require(string dataDirectory, string track, string vehicle)
    {
        var all = LoadAllForData(dataDirectory);
        var match = all.FirstOrDefault(p =>
            string.Equals(p.TrackName, track, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.VehicleName, vehicle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = all.Count == 0
                ? "  (none)"
                : string.Join('\n', all.Select(p => $"  {p.TrackName} / {p.VehicleName}"));
            throw new CalibrationException(
                $"no calibration for '{track}' / '{vehicle}'. Calibration is per track AND per " +
                $"vehicle.\nKnown:\n{known}");
        }

        if (!match.Valid)
        {
            throw new CalibrationException(
                $"the calibration for '{track}' / '{vehicle}' is marked invalid.");
        }

        if (match.Locked)
        {
            throw new CalibrationException(
                $"the calibration for '{track}' / '{vehicle}' is locked:\n  {match.Advisory}");
        }

        return match;
    }

    /// <summary>
    /// The one way to build a placement model. Requires a calibration, so no
    /// caller can accidentally fall back to nominal constants.
    /// </summary>
    public PlacementModel ToPlacementModel(EngineTunables engine) =>
        PlacementModel.Create(engine, ForwardDistance, VerticalOffset, LateralOffset);
}
