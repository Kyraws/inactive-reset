using System.Globalization;
using System.Text.Json;

namespace InactiveReset.Core;

/// <summary>
/// Rest constants learned from the user's own placements, one set per
/// CHECKPOINT.
///
/// <para><b>Why per checkpoint and not per vehicle.</b> The settle is physical.
/// It happens on real ground, and the ground is not the same everywhere: slope,
/// surface and the pit spot all plausibly move it. Keying on the checkpoint
/// makes no assumption that they do not, and the observation log records enough
/// to find the general rule later, at which point this can collapse to
/// something smaller.</para>
///
/// <para><b>Why one sample is enough.</b> The relationship is exactly linear and
/// was measured as such: placing with D deliberately doubled moved the error by
/// exactly -deltaD, agreeing to 0.2 mm. So the miss IS the correction, and a
/// second placement refines terrain noise rather than converging on an answer.
/// Later samples are averaged in, but the first one already works.</para>
///
/// <para><b>Keyed by build.</b> D moved 0.5 m across a single patch. A learned
/// value from another build is not evidence about this one.</para>
/// </summary>
public sealed record LearnedRest
{
    public required string BuildSha256 { get; init; }
    public required string TrackName { get; init; }
    public required string VehicleName { get; init; }
    public required string CheckpointName { get; init; }
    public required float Forward { get; init; }
    public required float Lateral { get; init; }
    public float? Vertical { get; init; }
    public int VerticalSampleCount { get; init; }
    public required int SampleCount { get; init; }
    public required string UpdatedUtc { get; init; }

    public const string DirectoryName = "learned-rest";

    /// <summary>
    /// Whether a placement may be learned from, and why not when it may not.
    ///
    /// <para>This guard is the whole safety of automatic learning. A bad sample
    /// is not merely ignored, it is written down as truth and every later
    /// placement inherits it.</para>
    ///
    /// <para>The case that forced it is real and measured. A placement made
    /// against a wall missed by 1.794 m, and the miss lay ENTIRELY along the
    /// clearance-search axis -- 0.16 mm off it -- because the engine had
    /// rejected the first nine candidates and stepped out to the last one it is
    /// allowed. Learning from that would have written D = 1.95, L = 1.80 and
    /// quietly ruined the car. The tool reported it as a calibration problem,
    /// which it was not.</para>
    /// </summary>
    public static string? RejectReason(
        PlacementOutcome outcome, ContainerState container,
        float errorForward, float errorLateral, double impliedCandidate)
    {
        if (!outcome.Completed)
        {
            return "the placement did not complete";
        }

        // Deliberately NOT `Arrival.Verified`. That check demands the motion
        // block be bit-zero, which is right for the arrival report but wrong
        // here: a settled car idles at ~0.001 and the field is read a moment
        // later, so Verified is true or false essentially at random. Gating
        // learning on it threw away two consecutive 3 mm placements.
        //
        // What actually matters is that the car arrived by the normal placement
        // path and has stopped moving.
        if (outcome.Arrival is not { Checked: true } arrival)
        {
            return "the arrival state could not be read, so nothing about this placement is known";
        }
        if (!arrival.ControlIsPlayer)
        {
            return "the car is not under player control, so this did not go through "
                 + "the normal placement path";
        }
        if (!arrival.GearIsNeutral)
        {
            return $"gear is {arrival.Gear}, not neutral, so this did not go through the "
                 + "normal placement path";
        }

        // Still rolling means it has not finished settling, and the settle is
        // the thing being measured. Two orders of magnitude above the ~0.001
        // residual a stationary car shows.
        const float AtRest = 0.05f;
        if (arrival.LargestMotionComponent > AtRest)
        {
            return $"the car was still moving ({arrival.LargestMotionComponent:F4}), "
                 + "so it had not finished settling";
        }

        // The danger this guard exists for is the clearance search, which
        // displaces the car by WHOLE steps of 0.1 * width -- 0.199 m for a
        // Hypercar -- along one axis. The engine cannot take a fraction of one,
        // so a miss that does not land on a whole step is not that failure,
        // whatever else it is.
        var step = 0.1f * container.VehicleWidth;
        var magnitude = MathF.Sqrt(errorForward * errorForward + errorLateral * errorLateral);
        if (step <= 0f)
        {
            return null;
        }

        var nearest = Math.Round(impliedCandidate);
        if (Math.Abs(impliedCandidate - nearest) < 0.15 && Math.Abs(nearest) >= 1)
        {
            return $"the {magnitude:F3} m miss lies on a whole clearance-search step "
                 + $"({step:F3} m): it implies the engine used search candidate "
                 + $"{nearest:F0}, not 0";
        }

        // Everything else is a constant error, and learning is per CHECKPOINT --
        // so a correction measured here can only ever be applied here, and a
        // genuinely spot-specific settle is the thing worth keeping rather than
        // the thing to fear. This cap was a quarter step, which refused a 0.061 m
        // sample on Daytona's banking: real, repeatable, and exactly what this
        // checkpoint needed. It sits above the largest settle measured and below
        // one whole step, so no clearance-search miss can reach it.
        const float MaxConstantErrorSteps = 0.6f;
        if (magnitude > MaxConstantErrorSteps * step)
        {
            return $"the {magnitude:F3} m miss is too large to be a calibration error "
                 + $"(more than {MaxConstantErrorSteps:F1} of the {step:F3} m "
                 + $"clearance-search step), but it does NOT lie on a whole step "
                 + $"({impliedCandidate:F2}), so the clearance search is not the cause "
                 + "-- suspect the constants or the car";
        }

        return null;
    }

    /// <summary>
    /// Fold a new measurement in. The stored value is the running mean, so one
    /// unusual spot cannot dominate once several samples exist.
    /// </summary>
    public LearnedRest With(float forward, float lateral, float? vertical = null)
    {
        var n = SampleCount + 1;
        return this with
        {
            Forward = Forward + (forward - Forward) / n,
            Lateral = Lateral + (lateral - Lateral) / n,
            Vertical = vertical is null ? Vertical
                : Vertical is null ? vertical
                : Vertical + (vertical - Vertical) / (VerticalSampleCount + 1),
            VerticalSampleCount = VerticalSampleCount + (vertical is null ? 0 : 1),
            SampleCount = n,
            UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    public static LearnedRest First(
        string build, string track, string vehicle, string checkpoint,
        float forward, float lateral, float? vertical = null) => new()
        {
            BuildSha256 = build,
            TrackName = track,
            VehicleName = vehicle,
            CheckpointName = checkpoint,
            Forward = forward,
            Lateral = lateral,
            Vertical = vertical,
            VerticalSampleCount = vertical is null ? 0 : 1,
            SampleCount = 1,
            UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
}

/// <summary>Reads and writes <see cref="LearnedRest"/> under the data directory.</summary>
public sealed class LearnedRestStore(string dataDirectory)
{
    private string Directory_ => Path.Combine(dataDirectory, LearnedRest.DirectoryName);

    /// <summary>
    /// One file per (build, track, vehicle, checkpoint). The build hash is in
    /// the NAME rather than only inside the file, so a stale one is visible in a
    /// directory listing instead of having to be opened.
    /// </summary>
    private string PathFor(string build, string track, string vehicle, string checkpoint) =>
        Path.Combine(Directory_,
            $"{Sanitise(build[..Math.Min(8, build.Length)])}__{Sanitise(track)}__" +
            $"{Sanitise(vehicle)}__{Sanitise(checkpoint)}.json");

    public LearnedRest? Load(string build, string track, string vehicle, string checkpoint)
    {
        var path = PathFor(build, track, vehicle, checkpoint);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            // A learned value from another build says nothing about this one.
            var stored = root.GetProperty("build_sha256").GetString();
            if (!string.Equals(stored, build, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new LearnedRest
            {
                BuildSha256 = stored!,
                TrackName = root.GetProperty("track_name").GetString()!,
                VehicleName = root.GetProperty("vehicle_name").GetString()!,
                CheckpointName = root.GetProperty("checkpoint").GetString()!,
                Forward = root.GetProperty("forward_distance_D").GetSingle(),
                Lateral = root.GetProperty("lateral_offset_L").GetSingle(),
                Vertical = root.TryGetProperty("vertical_offset_H", out var vertical)
                    && vertical.ValueKind == JsonValueKind.Number ? vertical.GetSingle() : null,
                VerticalSampleCount = root.TryGetProperty("vertical_sample_count", out var verticalCount)
                    ? verticalCount.GetInt32() : 0,
                SampleCount = root.GetProperty("sample_count").GetInt32(),
                UpdatedUtc = root.GetProperty("updated_utc").GetString()!,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            // A corrupt learned file must not stop a placement. Falling back to
            // the derived constants costs millimetres; refusing costs the run.
            return null;
        }
    }

    public void Save(LearnedRest learned)
    {
        System.IO.Directory.CreateDirectory(Directory_);
        var path = PathFor(learned.BuildSha256, learned.TrackName,
                           learned.VehicleName, learned.CheckpointName);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["_comment"] = "Learned automatically from placements at this checkpoint. "
                         + "Delete to re-learn. Per build: D moves between game patches.",
            ["build_sha256"] = learned.BuildSha256,
            ["track_name"] = learned.TrackName,
            ["vehicle_name"] = learned.VehicleName,
            ["checkpoint"] = learned.CheckpointName,
            ["forward_distance_D"] = learned.Forward,
            ["lateral_offset_L"] = learned.Lateral,
            ["vertical_offset_H"] = learned.Vertical,
            ["vertical_sample_count"] = learned.VerticalSampleCount,
            ["sample_count"] = learned.SampleCount,
            ["updated_utc"] = learned.UpdatedUtc,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>Append one observation to the corpus. Never throws at a caller.</summary>
    public void Record(PlacementObservation observation)
    {
        try
        {
            var directory = Path.Combine(dataDirectory, "observations");
            System.IO.Directory.CreateDirectory(directory);
            var file = Path.Combine(
                directory, $"{Sanitise(observation.BuildSha256[..8])}.jsonl");
            File.AppendAllText(file, observation.ToJson().Write() + "\n");
        }
        catch (IOException)
        {
            // The log is for a future analysis, not for this placement. Losing a
            // line is not worth failing a run the driver is waiting on.
        }
    }

    private static string Sanitise(string value)
    {
        var chars = value.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
