using System.Text.Json;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>
/// Records a point on the racing line from LMU's own telemetry.
///
/// Read-only, and it touches no game memory: the pose comes from the supported
/// shared-memory interface. A checkpoint is world geometry, so it stays valid
/// across game patches that only move code — what invalidates one is the track
/// or car content changing.
/// </summary>
public sealed class CaptureService(SharedMemoryReader reader)
{
    private readonly SharedMemoryReader _reader = reader;

    /// <summary>
    /// Take one sample and validate it before it is worth saving.
    ///
    /// The pose is checked through the same path a placement will use, so a
    /// checkpoint that cannot be placed is rejected at capture time rather than
    /// discovered later in the garage.
    /// </summary>
    public CaptureResult Capture(string name)
    {
        var snapshot = _reader.Read()
            ?? throw new CheckpointException(
                "no player vehicle in shared memory - are you on track?");

        if (string.IsNullOrWhiteSpace(snapshot.TrackName)
            || string.IsNullOrWhiteSpace(snapshot.VehicleName))
        {
            throw new CheckpointException(
                "shared memory has no track or vehicle name yet - wait for the session to load");
        }

        var target = Geometry.BuildTargetFromRecordedPose(snapshot.Pose);

        return new CaptureResult
        {
            Checkpoint = new Checkpoint
            {
                Name = name,
                TrackName = snapshot.TrackName,
                VehicleName = snapshot.VehicleName,
                Pose = snapshot.Pose,
                CapturedUtc = DateTimeOffset.UtcNow,
                LapDistance = snapshot.LapDistance,
                Gear = snapshot.Gear,
            },
            Target = target,
            Snapshot = snapshot,
        };
    }

    /// <summary>
    /// Write a checkpoint to disk, under a folder per track/vehicle pair so that
    /// combinations cannot get mixed up.
    /// </summary>
    public static string Save(Checkpoint checkpoint, string checkpointDirectory)
    {
        var folder = Path.Combine(checkpointDirectory, SanitiseFolder(checkpoint));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, checkpoint.Name + ".json");

        if (File.Exists(path))
        {
            throw new CheckpointException(
                $"'{checkpoint.Name}' already exists at {path}; choose another name");
        }

        var json = new JsonObject
        {
            ["format_version"] = 1,
            ["captured_utc"] = checkpoint.CapturedUtc?.ToString("O"),
            ["source"] = "LMU official shared memory",
            ["track"] = new JsonObject { ["name"] = checkpoint.TrackName },
            ["vehicle"] = new JsonObject
            {
                ["name"] = checkpoint.VehicleName,
                ["gear"] = checkpoint.Gear,
            },
            ["lap"] = new JsonObject { ["distance"] = checkpoint.LapDistance },
            ["pose"] = new JsonObject
            {
                ["position"] = Vec(checkpoint.Pose.Position),
                ["orientation"] = new JsonArray(
                    Vec(checkpoint.Pose.Row0), Vec(checkpoint.Pose.Row1), Vec(checkpoint.Pose.Row2)),
            },
        };

        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;

        static JsonNode Vec(Vec3 v) => new JsonObject
        {
            ["x"] = v.X,
            ["y"] = v.Y,
            ["z"] = v.Z,
        };
    }

    private static string SanitiseFolder(Checkpoint checkpoint)
    {
        var raw = $"{checkpoint.TrackName}__{checkpoint.VehicleName}";
        var clean = new string(raw.Select(c =>
            char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return clean;
    }
}

public sealed record CaptureResult
{
    public required Checkpoint Checkpoint { get; init; }
    public required TargetPose Target { get; init; }
    public required TelemetrySnapshot Snapshot { get; init; }

    /// <summary>
    /// False when the pose could not become a placement target — for example a
    /// near-vertical forward axis mid-crash, where the heading is meaningless.
    /// </summary>
    public bool Usable => Target.Valid;
}
