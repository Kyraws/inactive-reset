using System.Text.Json;
using System.Text.Json.Serialization;

namespace InactiveReset.Core;

public sealed class CalibrationException(string message) : Exception(message);

/// <summary>
/// Measured placement behaviour for ONE track and ONE vehicle.
///
/// D and H do not describe the car. They describe the ENGINE'S placement
/// behaviour, measured through the car. That distinction matters when deciding
/// whether a game patch invalidates them: a code-only patch cannot move a car
/// body, but it CAN change how the engine places one.
///
/// There is deliberately no default. Every <see cref="PlacementModel"/> must be
/// built from a real calibration, so that a preview and the placement it
/// previews cannot silently use different constants. When they diverged, a dry
/// run predicted a landing point ~3 mm from the one a real placement targeted.
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

    public required bool Valid { get; init; }

    /// <summary>Build this calibration was measured against.</summary>
    public string? ExecutableSha256 { get; init; }

    /// <summary>
    /// Hard refusal. Only an explicit <c>"locked": true</c> blocks use.
    /// </summary>
    public bool Locked { get; init; }

    /// <summary>
    /// Advisory attached to the calibration — typically a characterised
    /// inaccuracy, or a warning about how NOT to fix one.
    ///
    /// This is deliberately NOT a refusal. A calibration can be usable and
    /// imprecise at the same time: the current Barcelona profile places with a
    /// known ~0.57 m heading error but ~1.5 mm repeatability, which is wrong for
    /// hot-lap work and perfectly fine for getting back to a corner. Treating
    /// the note as a lock conflates "here is what is wrong with this" with
    /// "you may not use this", and would have made a working tool refuse.
    ///
    /// It is shown every time the calibration is used, so it cannot be
    /// forgotten without also being ignored.
    /// </summary>
    public string? Advisory { get; init; }

    public int SampleCount { get; init; }
    public float WorstHorizontalErrorMetres { get; init; }
    public string? SourcePath { get; init; }

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
    /// Find the calibration for a track/vehicle pair, and refuse to return one
    /// that is marked invalid or locked. A calibration is per track AND per
    /// vehicle; reusing one across combinations is meaningless.
    /// </summary>
    public static CalibrationProfile Require(string directory, string track, string vehicle)
    {
        var all = LoadAll(directory);
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
    public PlacementModel ToPlacementModel(EngineModelSpec engine) =>
        PlacementModel.Create(engine, ForwardDistance, VerticalOffset);
}
