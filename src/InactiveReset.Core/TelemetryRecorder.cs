using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace InactiveReset.Core;

public sealed record RecordingIdentity(string ExecutablePath, string ExecutableSha256,
    string SdkHeaderSha256, string Track, string Vehicle, int Session, int ProcessId);
public sealed record TimelineEntry(string Kind, long Sequence, long HostTicks,
    string? Reason = null, RecordingIdentity? Identity = null,
    TelemetrySnapshot? Sample = null, long Dropped = 0, long Duplicates = 0,
    double? SimulationGapSeconds = null, int SchemaVersion = 1,
    long HostFrequency = 0, DateTimeOffset? Utc = null, string? SegmentId = null,
    double? ReadDurationMilliseconds = null, string? RecordedSessionId = null, DateTimeOffset? RecordedSessionUtc = null);

public sealed record RecorderStatus(string State, long Samples, long Duplicates, long Dropped,
    string? Error = null, string? Reason = null);

// Classifies fresh frames before they enter the bounded disk queue.
internal sealed class TimelineCapture
{
    private RecordingIdentity? _identity;
    private double? _time;
    private long _sequence, _duplicates, _dropped, _lastFresh;
    private RecordingIdentity? _observedIdentity;
    private double? _observedTime;
    private string? _segment;
    private string? _recordedSession;
    private DateTimeOffset? _recordedSessionUtc;
    private double? _observedScoringTime;
    private string? _suspensionReason;
    internal long Samples { get; private set; }
    internal long Duplicates { get; private set; }
    internal long Dropped { get; private set; }
    internal bool IsRecording => _identity is not null;
    internal string? SuspensionReason => _suspensionReason;
    private readonly Func<TimelineEntry, bool> _emit;
    internal TimelineCapture(Func<TimelineEntry, bool> emit) => _emit = emit;
    internal TimelineEntry Summary(long ticks) => new("summary", ++_sequence, ticks,
        "shutdown-totals", Identity: _observedIdentity, Dropped: Dropped, Duplicates: Duplicates, SegmentId: _segment);

    private void Emit(TimelineEntry entry)
    {
        if (_emit(entry with { Dropped = _dropped, Duplicates = _duplicates }))
            _dropped = _duplicates = 0;
        else { _dropped++; Dropped++; }
    }

    internal void Suspend(string reason, long ticks)
    {
        if (reason is not ("stale-or-paused" or "not-in-realtime" or "session-inactive-or-paused"))
            _recordedSession = null;
        if (_identity is not null || _suspensionReason != reason)
            Emit(new("end", ++_sequence, ticks, reason, Identity: _identity, SegmentId: _segment));
        _suspensionReason = reason;
        _identity = null;
        _time = null;
    }

    internal void Accept(RecordingIdentity identity, TelemetrySnapshot? sample, long ticks, double? readDuration = null)
    {
        if (sample is null || sample.Details?.Driving != true)
        { Suspend(sample?.Details?.DrivingUnavailableReason ?? "no-player-telemetry", ticks); return; }
        if (!double.IsFinite(sample.ElapsedTime) || sample.ElapsedTime < 0 ||
            !sample.Pose.Position.IsFinite || !sample.Pose.Row0.IsFinite ||
            !sample.Pose.Row1.IsFinite || !sample.Pose.Row2.IsFinite || !float.IsFinite(sample.LapDistance) ||
            sample.LocalVelocityMetresPerSecond is not { IsFinite: true } ||
            string.IsNullOrWhiteSpace(sample.TrackName) || string.IsNullOrWhiteSpace(sample.VehicleName))
        { Suspend("invalid-sample", ticks); return; }
        if (!ValidTyres(sample.Tyres)) { Suspend("invalid-tyres", ticks); return; }
        try
        {
            var p = sample.Pose;
            DirectPosePlan.RequireRotation([p.Row0.X, p.Row0.Y, p.Row0.Z, p.Row1.X, p.Row1.Y, p.Row1.Z, p.Row2.X, p.Row2.Y, p.Row2.Z]);
        }
        catch (GateException) { Suspend("invalid-orientation", ticks); return; }
        var scoringTime = sample.Details.ScoringTime;
        if (_observedIdentity == identity && _observedTime == sample.ElapsedTime &&
            !(scoringTime < _observedScoringTime))
        {
            Duplicates++; _duplicates++;
            if (Stopwatch.GetElapsedTime(_lastFresh, ticks).TotalSeconds >= 1) Suspend("stale-or-paused", ticks);
            return;
        }
        var restart = _observedIdentity != identity || sample.ElapsedTime < _observedTime ||
            scoringTime < _observedScoringTime;
        _observedIdentity = identity;
        _observedTime = sample.ElapsedTime;
        _observedScoringTime = scoringTime;
        var delta = sample.ElapsedTime - _time;
        if (restart || _identity != identity || delta is < 0 or > 2)
        {
            if (_identity is not null)
                Suspend(_identity != identity ? "identity-change" : "simulation-discontinuity", ticks);
            delta = null;
        }
        if (restart || _recordedSession is null)
        {
            _recordedSession = Guid.NewGuid().ToString("N");
            _recordedSessionUtc = DateTimeOffset.UtcNow;
        }
        if (_identity is null)
        {
            // Repeat identity on samples so queue pressure cannot orphan a segment.
            _identity = identity;
            _segment = Guid.NewGuid().ToString("N");
            _suspensionReason = null;
        }
        Emit(new("sample", ++_sequence, ticks, Identity: identity, Sample: sample,
            SimulationGapSeconds: delta is > .1 ? delta : null,
            SegmentId: _segment, ReadDurationMilliseconds: readDuration,
            RecordedSessionId: _recordedSession, RecordedSessionUtc: _recordedSessionUtc));
        Samples++;
        _time = sample.ElapsedTime;
        _lastFresh = ticks;
    }

    private static bool ValidTyres(TyreState? tyres)
    {
        if (tyres is null) return true;
        foreach (var wheel in new[] { tyres.FrontLeft, tyres.FrontRight, tyres.RearLeft, tyres.RearRight })
            if (new[] { wheel.CarcassCelsius, wheel.WearRaw,
                wheel.SurfaceCelsius.Left, wheel.SurfaceCelsius.Centre, wheel.SurfaceCelsius.Right,
                wheel.InnerLayerCelsius.Left, wheel.InnerLayerCelsius.Centre, wheel.InnerLayerCelsius.Right }.Any(v => !double.IsFinite(v))) return false;
        return true;
    }
}

/// <summary>One background sampler and one bounded disk consumer, owned by the server.</summary>
public sealed class TelemetryRecorder(string offsetDirectory, string dataDirectory)
{
    public const int SamplePeriodMilliseconds = 20;
    public const int BufferSamples = 128;
    public const long FileBudgetBytes = 16 * 1024 * 1024;
    public const long StorageBudgetBytes = 512 * 1024 * 1024;
    private RecorderStatus _status = new("stopped", 0, 0, 0);
    public RecorderStatus Status => Volatile.Read(ref _status);

    public async Task RunAsync(CancellationToken cancellation)
    {
        var directory = Path.Combine(dataDirectory, "recordings");
        try
        {
            Directory.CreateDirectory(directory);
            // A second app can keep running, but cannot compete for this recording store/retention.
            using var ownership = new FileStream(Path.Combine(directory, "recorder.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var queue = Channel.CreateBounded<TimelineEntry>(new BoundedChannelOptions(BufferSamples)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var disk = WriteAsync(directory, queue.Reader);
            var capture = new TimelineCapture(entry => queue.Writer.TryWrite(entry));
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    SharedMemoryReader? reader = null;
                    try
                    {
                        var process = GameLauncher.Running();
                        if (process.ProcessId is not { } pid) throw new SharedMemoryException("Game not running.");
                        var path = RecordingProcessPath.Read(pid);
                        using var executable = File.OpenRead(path);
                        var hash = Convert.ToHexString(SHA256.HashData(executable));
                        var offsets = SharedMemoryOffsets.Load(Path.Combine(offsetDirectory, "shared-memory.json"));
                        reader = new SharedMemoryReader(offsets);
                        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SamplePeriodMilliseconds));
                        var checkedProcess = Stopwatch.GetTimestamp();
                        while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false))
                        {
                            if (disk.IsCompleted) await disk.ConfigureAwait(false);
                            var ticks = Stopwatch.GetTimestamp();
                            if (Stopwatch.GetElapsedTime(checkedProcess, ticks).TotalSeconds >= 1)
                            {
                                if (GameLauncher.Running().ProcessId != pid) break;
                                checkedProcess = ticks;
                            }
                            // Events are hints, not proof of a fresh telemetry timestamp.
                            reader.WaitForFrame(0);
                            var readStart = Stopwatch.GetTimestamp();
                            var sample = reader.Read();
                            var identity = new RecordingIdentity(path, hash, offsets.SdkHeaderSha256,
                                sample?.TrackName ?? "", sample?.VehicleName ?? "", sample?.Details?.Session ?? -1, pid);
                            capture.Accept(identity, sample, ticks, Stopwatch.GetElapsedTime(readStart).TotalMilliseconds);
                            Volatile.Write(ref _status, new(capture.IsRecording ? "recording" : "suspended", capture.Samples, capture.Duplicates, capture.Dropped,
                                Reason: capture.SuspensionReason));
                        }
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                    catch (Exception ex) when (ex is SharedMemoryException or CheckpointException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        capture.Suspend("interrupted: " + ex.Message, Stopwatch.GetTimestamp());
                        Volatile.Write(ref _status, new("waiting", capture.Samples, capture.Duplicates, capture.Dropped, ex.Message));
                    }
                    finally { reader?.Dispose(); }
                    if (capture.IsRecording) capture.Suspend("reconnect", Stopwatch.GetTimestamp());
                    if (disk.IsCompleted) await disk.ConfigureAwait(false);
                    try { await Task.Delay(1000, cancellation).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                }
            }
            finally
            {
                capture.Suspend("shutdown", Stopwatch.GetTimestamp());
                try
                {
                    var final = queue.Writer.WriteAsync(capture.Summary(Stopwatch.GetTimestamp())).AsTask();
                    await Task.WhenAny(final, disk).ConfigureAwait(false);
                    if (disk.IsCompleted) await disk.ConfigureAwait(false);
                    else await final.ConfigureAwait(false);
                }
                finally
                {
                    queue.Writer.TryComplete();
                    await disk.ConfigureAwait(false);
                }
            }
            Volatile.Write(ref _status, new("stopped", capture.Samples, capture.Duplicates, capture.Dropped));
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _status, Status with { State = "failed", Error = ex.Message });
        }
    }

    internal static async Task WriteAsync(string directory, ChannelReader<TimelineEntry> queue,
        long fileBudget = FileBudgetBytes, long storageBudget = StorageBudgetBytes)
    {
        StreamWriter? writer = null;
        FileStream? stream = null;
        RecordingIdentity? identity = null;
        string? segment = null;
        var flush = Stopwatch.GetTimestamp();
        try
        {
            await foreach (var entry in queue.ReadAllAsync().ConfigureAwait(false))
            {
                if (writer is null || entry.Kind == "sample" && (entry.Identity != identity || segment != entry.SegmentId || stream!.Length >= fileBudget))
                {
                    if (writer is not null) await writer.DisposeAsync().ConfigureAwait(false);
                    if (entry.Kind == "sample") Retain(directory, fileBudget, storageBudget);
                    var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.jsonl");
                    stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
                    writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 65536);
                    identity = entry.Identity;
                    segment = entry.SegmentId;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new TimelineEntry("header", entry.Sequence,
                        entry.HostTicks, Identity: identity, HostFrequency: Stopwatch.Frequency, Utc: DateTimeOffset.UtcNow,
                        SegmentId: segment, RecordedSessionId: entry.RecordedSessionId,
                        RecordedSessionUtc: entry.RecordedSessionUtc))).ConfigureAwait(false);
                }
                if (writer is null) continue;
                var json = JsonSerializer.Serialize(entry);
                if (json.Length > 65534) throw new InvalidDataException("Recording entry exceeds schema line limit.");
                await writer.WriteLineAsync(json).ConfigureAwait(false);
                // Flush complete records incrementally; forced termination can lose at most this buffered interval.
                if (Stopwatch.GetElapsedTime(flush).TotalSeconds >= 1 || entry.Kind == "end")
                { await writer.FlushAsync().ConfigureAwait(false); flush = Stopwatch.GetTimestamp(); }
                if (entry.Kind == "end")
                { await writer.DisposeAsync().ConfigureAwait(false); writer = null; stream = null; identity = null; }
            }
        }
        finally { if (writer is not null) await writer.DisposeAsync().ConfigureAwait(false); }
    }

    private static void Retain(string directory, long fileBudget, long storageBudget)
    {
        var files = new DirectoryInfo(directory).GetFiles("*.jsonl").OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= storageBudget - fileBudget - 131072) break;
            var length = file.Length;
            file.Delete();
            TelemetryTimeline.DeleteLapIndex(file.FullName + TelemetryTimeline.LapIndexSuffix);
            total -= length;
        }
    }

    /// <summary>Recover only newline-terminated records; an interrupted final line is ignored.</summary>
    public static IEnumerable<TimelineEntry> ReadComplete(string path) =>
        ReadComplete(path, 0).Select(record => record.Entry);

    internal static IEnumerable<(TimelineEntry Entry, long NextOffset)> ReadComplete(string path, long offset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        using var line = new MemoryStream();
        int value;
        while ((value = stream.ReadByte()) >= 0)
        {
            if (value != '\n')
            {
                if (line.Length >= 65536) throw new InvalidDataException("Recording line exceeds schema limit.");
                line.WriteByte((byte)value); continue;
            }
            var entry = JsonSerializer.Deserialize<TimelineEntry>(line.ToArray()) ?? throw new InvalidDataException("Empty recording entry.");
            if (entry.SchemaVersion != 1) throw new InvalidDataException("Unsupported recording schema.");
            yield return (entry, stream.Position);
            line.SetLength(0);
        }
    }
}
