using System.Text.Json;

namespace InactiveReset.Core;

public sealed class CheckpointException(string message) : Exception(message);

/// <summary>
/// A recorded point on the racing line, captured from LMU's official shared
/// memory.
///
/// The pose is world geometry on one track. Its capture vehicle is provenance;
/// another vehicle can use the pose with its own placement offsets. Changes to
/// track geometry can invalidate it independently of executable code layout.
/// </summary>
public sealed class Checkpoint
{
    public required string Name { get; init; }
    public required string TrackName { get; init; }
    public required string VehicleName { get; init; }
    public required RecordedPose Pose { get; init; }

    public DateTimeOffset? CapturedUtc { get; init; }
    public float LapDistance { get; init; }
    public int Gear { get; init; }
    public TyreState? Tyres { get; init; }
    public string? SourcePath { get; init; }

    /// <summary>Build this was captured against, for provenance only.</summary>
    public string? ExecutableSha256 { get; init; }

    public static Checkpoint Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = document.RootElement;
        var pose = root.TryGetProperty("pose", out var p)
            ? p
            : throw new CheckpointException($"{path}: no 'pose' block");

        var rows = pose.GetProperty("orientation");
        if (rows.GetArrayLength() != 3)
        {
            throw new CheckpointException($"{path}: orientation must have three rows");
        }

        return new Checkpoint
        {
            Name = Path.GetFileNameWithoutExtension(path),
            TrackName = root.GetProperty("track").GetProperty("name").GetString()!,
            VehicleName = root.GetProperty("vehicle").GetProperty("name").GetString()!,
            Pose = new RecordedPose(
                ReadVec(pose.GetProperty("position")),
                ReadVec(rows[0]), ReadVec(rows[1]), ReadVec(rows[2])),
            CapturedUtc = root.TryGetProperty("captured_utc", out var c)
                && DateTimeOffset.TryParse(c.GetString(), out var when) ? when : null,
            LapDistance = root.TryGetProperty("lap", out var lap)
                && lap.TryGetProperty("distance", out var d) ? d.GetSingle() : 0f,
            Gear = root.TryGetProperty("vehicle", out var v)
                && v.TryGetProperty("gear", out var g) ? g.GetInt32() : 0,
            ExecutableSha256 = root.TryGetProperty("game", out var game)
                && game.TryGetProperty("executable_sha256", out var h) ? h.GetString() : null,
            SourcePath = path,
            Tyres = root.TryGetProperty("tyres", out var tyres)
                ? tyres.Deserialize<TyreState>(TyreState.JsonOptions) : null,
        };

        static Vec3 ReadVec(JsonElement element) => new(
            element.GetProperty("x").GetSingle(),
            element.GetProperty("y").GetSingle(),
            element.GetProperty("z").GetSingle());
    }

    /// <summary>
    /// All checkpoints under a directory, searched recursively so they can be
    /// organised per track/vehicle in folders.
    /// </summary>
    public static IReadOnlyList<Checkpoint> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }
        var found = new List<Checkpoint>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            found.Add(Load(file));
        }
        return found;
    }

    /// <summary>Stable selection ID relative to the checkpoint directory.</summary>
    public string SelectionId(string directory) => Path.GetRelativePath(directory,
        SourcePath ?? throw new CheckpointException("checkpoint has no source path")).Replace('\\', '/');

    // Preserve existing same-car learning files; cross-car captures get their
    // own key so two source cars' identically named targets cannot share a fit.
    public string LearningKey(string placedVehicle) =>
        string.Equals(VehicleName, placedVehicle, StringComparison.OrdinalIgnoreCase)
            ? Name : $"{VehicleName}__{Name}";

    public static Checkpoint Require(string directory, string name, SessionIdentity? session = null)
    {
        var all = LoadAll(directory);
        var byId = all.Where(c => string.Equals(c.SelectionId(directory),
            name.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (byId.Length == 1) return byId[0];
        var matches = all.Where(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (session is { } identity && matches.Length > 1)
        {
            matches = matches.Where(c => string.Equals(c.TrackName, identity.TrackName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var sameCar = matches.Where(c => string.Equals(c.VehicleName, identity.VehicleName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sameCar.Length > 0) matches = sameCar;
        }

        if (matches.Length == 0)
        {
            var known = all.Count == 0 ? "  (none)"
                : string.Join('\n', all.Select(c => $"  {c.Name,-16} {c.TrackName} / {c.VehicleName}"));
            throw new CheckpointException($"no checkpoint named '{name}'.\nKnown:\n{known}");
        }
        if (matches.Length > 1)
            throw new CheckpointException($"checkpoint name '{name}' is ambiguous; use its selection ID:\n" +
                string.Join('\n', matches.Select(c => $"  {c.SelectionId(directory)}")));
        return matches[0];
    }
}
