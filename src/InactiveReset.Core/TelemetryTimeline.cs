using System.Text.Json;

namespace InactiveReset.Core;

public sealed record RecordedRestoration(int SchemaVersion, string SegmentId, long Sequence,
    double SimulationTime, TelemetryDetails? Details);
public sealed record RecordingSummary(string SegmentId, RecordingIdentity Identity, double Start, double End, int Samples);
public sealed record TimelineSelection(Checkpoint Checkpoint, string SegmentId, long Sequence,
    double Timestamp, double? SpeedKmh, string? RollingUnavailableReason)
{
    public void RequireContext(string build, SessionIdentity identity)
    {
        identity.RequireMatches(Checkpoint);
        if (!string.Equals(identity.VehicleName, Checkpoint.VehicleName, StringComparison.OrdinalIgnoreCase))
            throw new GateException("Recorded reset requires the recorded car.");
        if (!string.Equals(build, Checkpoint.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new GateException("Recorded reset requires the recorded executable build.");
    }
}

/// <summary>Reads retained complete records; selections never interpolate physical state.</summary>
public sealed class TelemetryTimeline(string dataDirectory)
{
    private string DirectoryPath => Path.Combine(dataDirectory, "recordings");
    private IEnumerable<(string Path, TimelineEntry Header)> Parts()
    {
        if (!Directory.Exists(DirectoryPath)) yield break;
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.jsonl").Order(StringComparer.Ordinal))
        {
            TimelineEntry? header;
            try { header = TelemetryRecorder.ReadComplete(path).FirstOrDefault(); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { continue; }
            if (header is { Kind: "header", SegmentId: not null, Identity: not null }) yield return (path, header);
        }
    }

    public RecordingSummary[] List()
    {
        // shortcut: explicit refresh scans retained JSONL; add an index if store scans become slow.
        var summaries = new Dictionary<string, RecordingSummary>();
        foreach (var part in Parts())
            foreach (var entry in TelemetryRecorder.ReadComplete(part.Path))
                if (entry is { Kind: "sample", Sample: { } sample, SegmentId: { } segment } &&
                    segment == part.Header.SegmentId && entry.Identity == part.Header.Identity && double.IsFinite(sample.ElapsedTime))
                {
                    if (!summaries.TryGetValue(segment, out var summary))
                        summary = new(segment, entry.Identity!, sample.ElapsedTime, sample.ElapsedTime, 0);
                    summaries[segment] = summary with { Start = Math.Min(summary.Start, sample.ElapsedTime),
                        End = Math.Max(summary.End, sample.ElapsedTime), Samples = summary.Samples + 1 };
                }
        return summaries.Values.ToArray();
    }

    public TimelineSelection Select(string segmentId, double? timestamp = null, long? sequence = null)
    {
        if (string.IsNullOrWhiteSpace(segmentId) || (timestamp is null) == (sequence is null) ||
            timestamp is { } time && (!double.IsFinite(time) || time < 0))
            throw new GateException("Select a recording and a finite simulation timestamp or exact sequence.");
        TimelineEntry? selected = null;
        foreach (var part in Parts().Where(p => p.Header.SegmentId == segmentId))
            foreach (var entry in TelemetryRecorder.ReadComplete(part.Path))
            {
                if (entry.Kind != "sample" || entry.SegmentId != segmentId || entry.Sample is null) continue;
                if (entry.Identity != part.Header.Identity) throw new GateException("Recording identity changed within a segment.");
                if (sequence is { } seq ? entry.Sequence == seq : selected is null ||
                    Math.Abs(entry.Sample.ElapsedTime - timestamp!.Value) < Math.Abs(selected.Sample!.ElapsedTime - timestamp.Value))
                    selected = entry;
            }
        if (selected?.Sample is not { } sample || selected.Identity is not { } identity)
            throw new GateException("Recorded sample is unavailable; it may have expired from retention.");
        if (sample.Details?.Driving != true || sample.TrackName != identity.Track || sample.VehicleName != identity.Vehicle ||
            !double.IsFinite(sample.ElapsedTime) || sample.ElapsedTime < 0 || !float.IsFinite(sample.LapDistance) ||
            sample.LocalVelocityMetresPerSecond is not { IsFinite: true } || !sample.Pose.Position.IsFinite ||
            !Geometry.BuildTargetFromRecordedPose(sample.Pose).Valid)
            throw new GateException("Recorded sample has invalid driving state, identity or geometry.");
        var pose = sample.Pose;
        DirectPosePlan.RequireRotation([pose.Row0.X, pose.Row0.Y, pose.Row0.Z, pose.Row1.X, pose.Row1.Y, pose.Row1.Z, pose.Row2.X, pose.Row2.Y, pose.Row2.Z]);
        MotionCapture.Validate(sample.LocalVelocityMetresPerSecond.Value);
        var checkpoint = new Checkpoint {
            Name = $"timeline-{segmentId}-{selected.Sequence}", TrackName = identity.Track, VehicleName = identity.Vehicle,
            Pose = sample.Pose, LapDistance = sample.LapDistance, Gear = sample.Gear, Tyres = sample.Tyres,
            LocalVelocityMetresPerSecond = sample.LocalVelocityMetresPerSecond, ExecutableSha256 = identity.ExecutableSha256,
            Restoration = new(1, segmentId, selected.Sequence, sample.ElapsedTime, sample.Details)
        };
        return new(checkpoint, segmentId, selected.Sequence, sample.ElapsedTime, sample.SpeedKmh,
            RollingMotion.EligibleCapture(sample.LocalVelocityMetresPerSecond) && sample.Gear is >= -1 and <= 8
                ? null : "Speed/gear requires a straight, level moving sample in a supported gear. Standing teleport remains available.");
    }
}
