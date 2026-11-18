using System.Diagnostics;
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
    public const string QuickCaptureName = "Quick capture";
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
        ValidateName(name);
        var baseline = _reader.Read()
            ?? throw new CheckpointException(
                "no player vehicle in shared memory - are you on track?");
        RequireDriving(baseline);

        var start = Stopwatch.GetTimestamp();
        TelemetrySnapshot snapshot;
        do
        {
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 250)
                throw new CheckpointException("No fresh driving frame arrived. Resume driving and try again.");
            Thread.Sleep(5);
            snapshot = _reader.Read() ?? throw new CheckpointException("The player vehicle disappeared during capture.");
            RequireDriving(snapshot);
            if (snapshot.TrackName != baseline.TrackName || snapshot.VehicleName != baseline.VehicleName ||
                snapshot.Details?.Session != baseline.Details?.Session || snapshot.ElapsedTime < baseline.ElapsedTime)
                throw new CheckpointException("The driving session changed during capture. Try again.");
        } while (snapshot.ElapsedTime == baseline.ElapsedTime);

        if (string.IsNullOrWhiteSpace(snapshot.TrackName)
            || string.IsNullOrWhiteSpace(snapshot.VehicleName))
        {
            throw new CheckpointException(
                "shared memory has no track or vehicle name yet - wait for the session to load");
        }

        var target = Geometry.BuildTargetFromRecordedPose(snapshot.Pose);
        if (target.Valid)
        {
            var pose = snapshot.Pose;
            try
            {
                DirectPosePlan.RequireRotation([pose.Row0.X, pose.Row0.Y, pose.Row0.Z,
                    pose.Row1.X, pose.Row1.Y, pose.Row1.Z, pose.Row2.X, pose.Row2.Y, pose.Row2.Z]);
            }
            catch (GateException ex)
            {
                target = target with { Valid = false, Failures = [ex.Message] };
            }
        }

        return new CaptureResult
        {
            Checkpoint = new Checkpoint
            {
                Name = name,
                TrackName = snapshot.TrackName,
                VehicleName = snapshot.VehicleName,
                Pose = snapshot.Pose,
                CapturedUtc = DateTimeOffset.UtcNow,
                CapturedSimulationTime = snapshot.ElapsedTime,
                LapDistance = snapshot.LapDistance,
                Gear = snapshot.Gear,
                Tyres = snapshot.Tyres,
                LocalVelocityMetresPerSecond = snapshot.LocalVelocityMetresPerSecond,
            },
            Target = target,
            Snapshot = snapshot,
        };
    }

    private static void RequireDriving(TelemetrySnapshot snapshot)
    {
        if (snapshot.Details is not { Driving: true })
            throw new CheckpointException($"No player-controlled driving frame ({snapshot.Details?.DrivingUnavailableReason ?? "missing driving state"}). Press Drive and try again.");
        if (!double.IsFinite(snapshot.ElapsedTime) || snapshot.ElapsedTime < 0 || !float.IsFinite(snapshot.LapDistance))
            throw new CheckpointException("The driving frame has invalid timing or lap distance.");
    }

    /// <summary>
    /// Write a checkpoint to disk, under a folder per track/vehicle pair so that
    /// combinations cannot get mixed up.
    /// </summary>
    public static string Save(Checkpoint checkpoint, string checkpointDirectory, bool quick = false)
    {
        ValidateName(checkpoint.Name);
        var reserved = string.Equals(checkpoint.Name, QuickCaptureName, StringComparison.OrdinalIgnoreCase);
        if (quick != reserved)
            throw new CheckpointException("'Quick capture' is reserved for the quick-capture action; use another name for a library checkpoint.");
        if (quick && RollingMotion.CaptureIssue(checkpoint.LocalVelocityMetresPerSecond, checkpoint.Gear) is { } issue)
            throw new CheckpointException($"Quick capture requires eligible speed and gear. {issue} Use named Capture to save a standing destination.");
        var folder = Path.Combine(checkpointDirectory, SanitiseFolder(checkpoint));
        Directory.CreateDirectory(folder);
        var path = Path.GetFullPath(Path.Combine(folder, checkpoint.Name + ".json"));
        if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new CheckpointException("The checkpoint must stay inside its track and car folder.");

        if (!quick && File.Exists(path))
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

        if (checkpoint.CapturedSimulationTime is { } time)
            json["captured_simulation_time_s"] = time;

        if (checkpoint.LocalVelocityMetresPerSecond is { } velocity)
            json["motion"] = new JsonObject
            {
                ["local_velocity_mps"] = Vec(MotionCapture.Validate(velocity)),
            };

        if (checkpoint.Tyres is { } tyres)
            json["tyres"] = JsonSerializer.SerializeToNode(tyres, TyreState.JsonOptions);

        if (checkpoint.Restoration is { } restoration)
            json["restoration"] = JsonSerializer.SerializeToNode(restoration);

        var payload = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, payload);
            File.Move(temporary, path, overwrite: quick);
        }
        catch (IOException) when (!quick && File.Exists(path))
        {
            throw new CheckpointException($"'{checkpoint.Name}' already exists at {path}; choose another name");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;

        static JsonNode Vec(Vec3 v) => new JsonObject
        {
            ["x"] = v.X,
            ["y"] = v.Y,
            ["z"] = v.Z,
        };
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new CheckpointException("Use a checkpoint name of 1–100 characters without filename punctuation or a trailing dot or space.");
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            "123456789¹²³".Contains(stem[3]))
            throw new CheckpointException("This checkpoint name is reserved by Windows.");
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
