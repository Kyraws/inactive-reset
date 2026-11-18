using System.Text.Json;

namespace InactiveReset.Core;

public sealed record RecordedRestoration(int SchemaVersion, string SegmentId, long Sequence,
    double SimulationTime, TelemetryDetails? Details);
public sealed record RecordingSummary(string SegmentId, RecordingIdentity Identity, double Start, double End, int Samples);
public sealed record RecordedLap(string Id, string SegmentId, RecordingIdentity Identity, int Number,
    DateTimeOffset? RecordedUtc, double Duration, bool IsOpeningRun = false);
public sealed record RecordedLapPoint(long Sequence, float X, float Z, bool SpeedEligible);
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
    private sealed record LapFrame(long Sequence, double Time, float Distance, float X, float Z, float? Forward,
        bool SpeedEligible);
    private sealed class LapPart
    {
        public long Offset;
        public TimelineEntry? Header;
        public List<LapFrame> Frames { get; } = [];
    }
    private readonly Dictionary<string, LapPart> _lapParts = new(StringComparer.Ordinal);
    private readonly object _lapLock = new();
    private Dictionary<string, (RecordedLap Lap, RecordedLapPoint[] Points)> _laps = [];
    private string DirectoryPath => Path.Combine(dataDirectory, "recordings");

    public RecordedLap[] ListLaps()
    {
        lock (_lapLock)
        {
            var paths = Directory.Exists(DirectoryPath)
                ? Directory.GetFiles(DirectoryPath, "*.jsonl").Order(StringComparer.Ordinal).ToArray() : [];
            foreach (var removed in _lapParts.Keys.Except(paths, StringComparer.Ordinal).ToArray())
                _lapParts.Remove(removed);

            foreach (var path in paths)
            {
                if (!_lapParts.TryGetValue(path, out var part)) _lapParts[path] = part = new();
                try
                {
                    foreach (var (entry, next) in TelemetryRecorder.ReadComplete(path, part.Offset))
                    {
                        if (part.Offset == 0) part.Header = entry;
                        if (part.Header is { Kind: "header", SegmentId: not null, Identity: not null } header &&
                            entry.Kind == "sample" && entry.SegmentId == header.SegmentId && entry.Identity == header.Identity &&
                            entry.Sample is { } sample && double.IsFinite(sample.ElapsedTime) &&
                            float.IsFinite(sample.LapDistance) && sample.Pose.Position.IsFinite)
                            part.Frames.Add(new(entry.Sequence, sample.ElapsedTime, sample.LapDistance,
                                sample.Pose.Position.X, sample.Pose.Position.Z, sample.LocalVelocityMetresPerSecond?.Z,
                                RollingMotion.CaptureIssue(sample.LocalVelocityMetresPerSecond, sample.Gear) is null &&
                                Geometry.BuildTargetFromRecordedPose(sample.Pose).Valid));
                        part.Offset = next;
                    }
                }
                catch (IOException) { } // Retention can remove a part while the list is being refreshed.
            }

            var completed = new Dictionary<string, (RecordedLap Lap, RecordedLapPoint[] Points)>();
            foreach (var segment in paths.Select(path => _lapParts[path])
                         .Where(part => part.Frames.Count > 0).GroupBy(part => part.Header!.SegmentId!))
            {
                var header = segment.First().Header!;
                LapFrame? previous = null;
                List<LapFrame>? lap = null;
                var number = 0;
                var openingRun = false;
                foreach (var frame in segment.SelectMany(part => part.Frames))
                {
                    if (previous is null && frame.Distance < 0)
                    {
                        // Pit departures can start before zero on the pit lane, before the first timed lap.
                        lap = [];
                        openingRun = true;
                    }
                    else if (previous is { } before)
                    {
                        if (frame.Time - before.Time is <= 0 or > 0.5 || frame.Sequence <= before.Sequence)
                        {
                            lap = null;
                            openingRun = false;
                        }
                        // Older recordings have lap distance but no lap counter. Require forward passage near the line.
                        else if (before.Distance - frame.Distance > 200 && frame.Distance is >= -100 and <= 150 &&
                                 before.Forward is < 0 && frame.Forward is < 0)
                        {
                            if (lap is { Count: > 1 })
                            {
                                var id = $"{segment.Key}:{lap[0].Sequence}";
                                if (!openingRun) number++;
                                if (_laps.TryGetValue(id, out var cached))
                                    completed[id] = cached;
                                else
                                    completed[id] = (new(id, segment.Key, header.Identity!, number, header.Utc,
                                        frame.Time - lap[0].Time, openingRun),
                                        lap.Select(point => new RecordedLapPoint(point.Sequence, point.X, point.Z, point.SpeedEligible)).ToArray());
                            }
                            lap = [];
                            openingRun = false;
                        }
                    }
                    lap?.Add(frame);
                    previous = frame;
                }
            }
            _laps = completed;
            return completed.Values.Reverse().Select(value => value.Lap).ToArray();
        }
    }

    public RecordedLapPoint[] LapPoints(string id)
    {
        lock (_lapLock)
            return _laps.TryGetValue(id, out var lap) ? lap.Points :
                throw new GateException("This lap is no longer available. Choose another recorded lap.");
    }
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
        var parts = Parts().Where(p => p.Header.SegmentId == segmentId);
        if (sequence is { } target)
            parts = parts.Where(p => p.Header.Sequence <= target).OrderByDescending(p => p.Header.Sequence);
        foreach (var part in parts)
        {
            foreach (var entry in TelemetryRecorder.ReadComplete(part.Path))
            {
                if (entry.Kind != "sample" || entry.SegmentId != segmentId || entry.Sample is null) continue;
                if (entry.Identity != part.Header.Identity) throw new GateException("Recording identity changed within a segment.");
                if (sequence is { } seq ? entry.Sequence == seq : selected is null ||
                    Math.Abs(entry.Sample.ElapsedTime - timestamp!.Value) < Math.Abs(selected.Sample!.ElapsedTime - timestamp.Value))
                {
                    selected = entry;
                    if (sequence is not null) break;
                }
            }
            if (sequence is not null && selected is not null) break;
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
            RollingMotion.CaptureIssue(sample.LocalVelocityMetresPerSecond, sample.Gear));
    }
}
