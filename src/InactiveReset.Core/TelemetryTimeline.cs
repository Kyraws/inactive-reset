using System.Text.Json;

namespace InactiveReset.Core;

public sealed record RecordedRestoration(int SchemaVersion, string SegmentId, long Sequence,
    double SimulationTime, TelemetryDetails? Details);
public sealed record RecordingSummary(string SegmentId, RecordingIdentity Identity, double Start, double End, int Samples);
public sealed record RecordedLap(string Id, string SegmentId, RecordingIdentity Identity, int Number,
    DateTimeOffset? RecordedUtc, double Duration, bool IsOpeningRun = false, int? LmuLapNumber = null,
    string? RecordedSessionId = null, bool HasSpeedEligiblePoints = false)
{
    public string VehicleModel => VehicleCatalog.Model(Identity.Vehicle) ?? VehicleCatalog.UnknownModel;
    public string VehicleCompatibilityKey => VehicleCatalog.CompatibilityKey(Identity.Vehicle);
}
public sealed record RecordedLapPoint(long Sequence, float X, float Z, bool SpeedEligible, double Elapsed = 0,
    double? SpeedKmh = null, int Gear = 0, string? RollingUnavailableReason = null);
public sealed record TimelineSelection(Checkpoint Checkpoint, string SegmentId, long Sequence,
    double Timestamp, double? SpeedKmh, string? RollingUnavailableReason)
{
    public string VehicleCompatibilityKey => VehicleCatalog.CompatibilityKey(Checkpoint.VehicleName);

    public void RequireContext(string build, SessionIdentity identity)
    {
        identity.RequireMatches(Checkpoint);
        if (!VehicleCatalog.Compatible(identity.VehicleName, Checkpoint.VehicleName))
            throw new GateException("Recorded reset requires a car compatible with the recorded chassis.");
        if (!string.Equals(build, Checkpoint.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new GateException("Recorded reset requires the recorded executable build.");
    }
}

/// <summary>Reads retained complete records; selections never interpolate physical state.</summary>
public sealed class TelemetryTimeline(string dataDirectory)
{
    internal const string LapIndexSuffix = ".laps-v1";
    private const int LapIndexVersion = 2;
    private sealed record LapFrame(long Sequence, double Time, float Distance, float X, float Z, float? Forward,
        bool SpeedEligible, int? LmuLapNumber, double? SpeedKmh, int Gear, string? RollingUnavailableReason);
    private readonly record struct FileStamp(long Length, DateTime Updated);
    private sealed record LapIndex(FileStamp Source, long Offset, TimelineEntry? Header, List<LapFrame> Frames, int Version);
    private sealed class LapPart
    {
        public FileStamp? Source;
        public long Offset;
        public TimelineEntry? Header;
        public List<LapFrame> Frames { get; } = [];
    }
    private readonly Dictionary<string, LapPart> _lapParts = new(StringComparer.Ordinal);
    private readonly object _lapLock = new();
    private Dictionary<string, (RecordedLap Lap, RecordedLapPoint[] Points)> _laps = [];
    private RecordedLap[] _lapList = [];
    private string DirectoryPath => Path.Combine(dataDirectory, "recordings");

    public RecordedLap[] ListLaps()
    {
        lock (_lapLock)
        {
            var directory = new DirectoryInfo(DirectoryPath);
            var files = directory.Exists
                ? directory.GetFiles("*.jsonl").OrderBy(file => file.Name, StringComparer.Ordinal).ToArray() : [];
            var paths = files.Select(file => file.FullName).ToHashSet(StringComparer.Ordinal);
            if (_lapParts.Count == 0 && directory.Exists)
                foreach (var index in directory.EnumerateFiles("*.jsonl" + LapIndexSuffix))
                    if (!paths.Contains(index.FullName[..^LapIndexSuffix.Length])) DeleteLapIndex(index.FullName);
            var changed = false;
            foreach (var removed in _lapParts.Keys.Except(paths, StringComparer.Ordinal).ToArray())
            {
                _lapParts.Remove(removed);
                DeleteLapIndex(removed + LapIndexSuffix);
                changed = true;
            }

            foreach (var file in files)
            {
                var path = file.FullName;
                try
                {
                    var stamp = Stamp(file);
                    if (!_lapParts.TryGetValue(path, out var part))
                    {
                        _lapParts[path] = part = ReadLapIndex(path, stamp) ?? new();
                        changed = true;
                    }
                    if (part.Source == stamp) continue;
                    if (part.Source is { } previousStamp && stamp.Length <= previousStamp.Length)
                    {
                        _lapParts[path] = part = new();
                        _laps.Clear();
                    }
                    changed = true;
                    foreach (var (entry, next) in TelemetryRecorder.ReadComplete(path, part.Offset))
                    {
                        if (part.Offset == 0) part.Header = entry;
                        if (part.Header is { Kind: "header", SegmentId: not null, Identity: not null } header &&
                            entry.Kind == "sample" && entry.SegmentId == header.SegmentId && entry.Identity == header.Identity &&
                            entry.Sample is { } sample && double.IsFinite(sample.ElapsedTime) &&
                            float.IsFinite(sample.LapDistance) && sample.Pose.Position.IsFinite)
                        {
                            var rollingIssue = RollingMotion.CaptureIssue(sample.LocalVelocityMetresPerSecond, sample.Gear);
                            part.Frames.Add(new(entry.Sequence, sample.ElapsedTime, sample.LapDistance,
                                sample.Pose.Position.X, sample.Pose.Position.Z, sample.LocalVelocityMetresPerSecond?.Z,
                                rollingIssue is null && Geometry.BuildTargetFromRecordedPose(sample.Pose).Valid,
                                sample.Details?.Values.GetValueOrDefault("mLapNumber") is >= 1 and <= int.MaxValue and var number
                                    ? (int)number : null,
                                sample.LocalVelocityMetresPerSecond is { IsFinite: true } ? sample.SpeedKmh : null,
                                sample.Gear, rollingIssue));
                        }
                        part.Offset = next;
                    }
                    file.Refresh();
                    if (Stamp(file) == stamp)
                    {
                        part.Source = stamp;
                        SaveLapIndex(path, stamp, part);
                    }
                }
                catch (IOException) { } // Retention can remove a part while the list is being refreshed.
            }
            if (!changed) return _lapList.ToArray();

            var completed = new Dictionary<string, (RecordedLap Lap, RecordedLapPoint[] Points)>();
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var segment in files.Select(file => _lapParts.GetValueOrDefault(file.FullName))
                         .OfType<LapPart>()
                         .Where(part => part.Frames.Count > 0).GroupBy(part => part.Header!.SegmentId!))
            {
                var header = segment.First().Header!;
                LapFrame? previous = null;
                List<LapFrame>? lap = null;
                var sessionId = header.RecordedSessionId ?? segment.Key;
                var number = numbers.GetValueOrDefault(sessionId);
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
                                    completed[id] = (cached.Lap with { Number = number }, cached.Points);
                                else
                                    completed[id] = (new(id, segment.Key, header.Identity!, number,
                                        header.RecordedSessionUtc ?? header.Utc, frame.Time - lap[0].Time,
                                        openingRun, lap[0].LmuLapNumber, sessionId, lap.Any(point => point.SpeedEligible)),
                                        lap.Select(point => new RecordedLapPoint(point.Sequence, point.X, point.Z,
                                            point.SpeedEligible, point.Time - lap[0].Time,
                                            point.SpeedKmh, point.Gear, point.RollingUnavailableReason)).ToArray());
                            }
                            lap = [];
                            openingRun = false;
                        }
                    }
                    lap?.Add(frame);
                    previous = frame;
                }
                numbers[sessionId] = number;
            }
            _laps = completed;
            _lapList = completed.Values.Reverse().Select(value => value.Lap).ToArray();
            return _lapList.ToArray();
        }
    }

    private static FileStamp Stamp(FileInfo file) => new(file.Length, file.LastWriteTimeUtc);

    private static LapPart? ReadLapIndex(string path, FileStamp stamp)
    {
        try
        {
            using var stream = File.OpenRead(path + LapIndexSuffix);
            var index = JsonSerializer.Deserialize<LapIndex>(stream);
            if (index is null || index.Version != LapIndexVersion || index.Source != stamp || index.Offset < 0 || index.Offset > stamp.Length ||
                index.Frames is null || index.Frames.Count != 0 && index.Header is not { Kind: "header", SegmentId: not null, Identity: not null })
                return null;
            var part = new LapPart { Source = stamp, Offset = index.Offset, Header = index.Header };
            part.Frames.AddRange(index.Frames);
            return part;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void SaveLapIndex(string path, FileStamp stamp, LapPart part)
    {
        var indexPath = path + LapIndexSuffix;
        var temporaryPath = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
                JsonSerializer.Serialize(stream, new LapIndex(stamp, part.Offset, part.Header, part.Frames, LapIndexVersion));
            File.Move(temporaryPath, indexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { DeleteLapIndex(temporaryPath); }
    }

    internal static void DeleteLapIndex(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
