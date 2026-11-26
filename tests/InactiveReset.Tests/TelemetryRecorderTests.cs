using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class TelemetryRecorderTests
{
    [Fact]
    public void RealtimePlayerCaptureDoesNotRequireOptionalApplicationInfo()
    {
        var entries = new List<TimelineEntry>();
        var capture = new TimelineCapture(e => { entries.Add(e); return true; });
        var sample = Sample(122.9625);
        sample = sample with { Details = sample.Details! with { OptionsLocation = 0 } };
        capture.Accept(Identity, sample, Stopwatch.GetTimestamp());
        Assert.Equal(1, capture.Samples);
        Assert.Equal("sample", entries.Single().Kind);
        foreach (var details in new[] {
            sample.Details! with { InRealtime = false },
            sample.Details! with { Control = 3 },
            sample.Details! with { Control = 1 },
            sample.Details! with { GamePhase = 9 },
            sample.Details! with { IdentityMatches = false } })
            Assert.False(details.Driving);
    }

    private static readonly RecordingIdentity Identity = new("game.exe", new string('A', 64), new string('B', 64), "track", "car", 1, 123);
    private static TelemetrySnapshot Sample(double time) => new("track", "car",
        new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), 12, 2, time,
        LocalVelocityMetresPerSecond: new(0, 0, -10))
    { Details = new(1, time, 5, true, 3, 0, new Dictionary<string, double?>(), [], null) { IdentityMatches = true } };

    [Fact]
    public void FreshFramesOnlyAndSegmentsCoverPauseRestartIdentityAndInvalidData()
    {
        var entries = new List<TimelineEntry>();
        var capture = new TimelineCapture(e => { entries.Add(e); return true; });
        var ticks = Stopwatch.GetTimestamp();
        capture.Accept(Identity, Sample(10), ticks);
        capture.Accept(Identity, Sample(10), ticks + 1);
        capture.Accept(Identity, Sample(10.02), ticks + 2);
        Assert.Equal(1, entries[1].Duplicates);
        capture.Accept(Identity, Sample(10.5), ticks + 3);
        Assert.Equal(.48, entries[2].SimulationGapSeconds!.Value, 6);
        capture.Accept(Identity, Sample(10.5), ticks + Stopwatch.Frequency * 2);
        Assert.Equal("stale-or-paused", entries[^1].Reason);
        var count = entries.Count;
        capture.Accept(Identity, Sample(10.5), ticks + Stopwatch.Frequency * 3);
        Assert.Equal(count, entries.Count); // Do not repeatedly record the same stale frame.
        capture.Accept(Identity, Sample(10.52), ticks + Stopwatch.Frequency * 3 + 1);
        Assert.NotEqual(entries[0].SegmentId, entries[^1].SegmentId);
        Assert.Equal(entries[0].RecordedSessionId, entries[^1].RecordedSessionId);
        capture.Accept(Identity, Sample(0), ticks + Stopwatch.Frequency * 3 + 2);
        Assert.Equal("simulation-discontinuity", entries[^2].Reason);
        Assert.NotEqual(entries[0].RecordedSessionId, entries[^1].RecordedSessionId);
        capture.Accept(Identity with { Vehicle = "another car" }, Sample(.02), ticks + Stopwatch.Frequency * 3 + 3);
        Assert.Equal("identity-change", entries[^2].Reason);
        capture.Accept(Identity, Sample(double.NaN), ticks + Stopwatch.Frequency * 3 + 4);
        Assert.Equal("invalid-sample", entries[^1].Reason);
        capture.Accept(Identity, Sample(.04) with { Details = Sample(.04).Details! with { Control = 3 } }, ticks + Stopwatch.Frequency * 3 + 5);
        Assert.DoesNotContain(entries, e => e.Sample?.Details?.Control == 3);
    }

    [Fact]
    public void QueueLossIsReportedOnNextSuccessfulEntryAndHeadersCannotBeLost()
    {
        var entries = new List<TimelineEntry>();
        var accept = false;
        var capture = new TimelineCapture(e => { if (accept) entries.Add(e); return accept; });
        capture.Accept(Identity, Sample(1), 1);
        capture.Suspend("pause", 2);
        accept = true;
        capture.Accept(Identity, Sample(2), 3);
        Assert.Equal(2, entries.Single().Dropped);
        Assert.Equal(Identity, entries.Single().Identity);
        Assert.NotNull(entries.Single().SegmentId);
    }

    [Fact]
    public async Task DiskRecordsRoundTripAndRecoverCompleteLinesAfterInterruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-timeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var queue = Channel.CreateBounded<TimelineEntry>(2);
            var writing = TelemetryRecorder.WriteAsync(directory, queue.Reader);
            await queue.Writer.WriteAsync(new("sample", 1, 42, Identity: Identity, Sample: Sample(1), SegmentId: "a"));
            await queue.Writer.WriteAsync(new("sample", 3, 44, Identity: Identity, Sample: Sample(1.04), Dropped: 1, SegmentId: "a"));
            // A lost end record still cannot mix the next segment into this file.
            await queue.Writer.WriteAsync(new("sample", 5, 46, Identity: Identity, Sample: Sample(2), SegmentId: "b"));
            queue.Writer.Complete();
            await writing;
            var files = Directory.GetFiles(directory, "*.jsonl").Order().ToArray();
            Assert.Equal(2, files.Length);
            var entries = TelemetryRecorder.ReadComplete(files[0]).ToArray();
            Assert.Equal("header", entries[0].Kind);
            Assert.Equal(Stopwatch.Frequency, entries[0].HostFrequency);
            Assert.Equal(Identity, entries[0].Identity);
            Assert.Equal(1, entries[^1].Dropped);
            Assert.Equal(36, entries[1].Sample!.SpeedKmh);
            Assert.Equal(Sample(1).Pose, entries[1].Sample!.Pose);
            await File.AppendAllTextAsync(files[0], "{\"Kind\":\"sam");
            Assert.Equal(entries.Length, TelemetryRecorder.ReadComplete(files[0]).Count());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void GeneratedLayoutDecodesUnitsAndPreservesUnavailableValues()
    {
        var offsets = SharedMemoryOffsets.Load(ProfileFixture.PathFor("shared-memory"));
        var layout = offsets.Layout;
        var bytes = new byte[offsets.LayoutSize];
        var fields = layout.GetProperty("telemInfo");
        void Double(string field, double value) => BitConverter.GetBytes(value).CopyTo(bytes, offsets.TelemInfo + fields.GetProperty(field).GetInt32());
        Double("mFuel", 42.5);
        Double("mBatteryChargeFraction", .75);
        Double("mLocalRot", 1.25);
        Double("mLocalAccel", double.NaN);
        var details = TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes);
        Assert.Equal(42.5, details.Values["mFuel"]);
        Assert.Equal(1.25, details.Values["mLocalRot.x"]);
        Assert.Null(details.Values["mLocalAccel.x"]);
        Assert.Null(details.Values["mBatteryChargeFraction"]);
        bytes[offsets.TelemInfo + fields.GetProperty("mElectricBoostMotorState").GetInt32()] = 1;
        details = TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes);
        Assert.Equal(.75, details.Values["mBatteryChargeFraction"]);
        Assert.Equal(4, details.Wheels.Count);
        Assert.False(details.Driving);
        var scoring = layout.GetProperty("scoringInfo");
        var vehicle = layout.GetProperty("vehScoringInfo");
        void Text(int address, string value) => System.Text.Encoding.ASCII.GetBytes(value + "\0").CopyTo(bytes, address);
        BitConverter.GetBytes(1).CopyTo(bytes, offsets.ScoringInfo + offsets.NumVehicles);
        bytes[offsets.VehScoringInfo + offsets.VehIsPlayer] = 1;
        bytes[offsets.ScoringInfo + scoring.GetProperty("mInRealtime").GetInt32()] = 1;
        bytes[offsets.ScoringInfo + scoring.GetProperty("mGamePhase").GetInt32()] = 5;
        bytes[layout.GetProperty("generic").GetProperty("optionsLocation").GetInt32()] = 3;
        Text(offsets.ScoringInfo + offsets.TrackName, "track");
        Text(offsets.VehScoringInfo + offsets.VehName, "car");
        Text(offsets.TelemInfo + fields.GetProperty("mTrackName").GetInt32(), "track");
        Text(offsets.TelemInfo + fields.GetProperty("mVehicleName").GetInt32(), "car");
        details = TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes);
        Assert.True(details.Driving);
        BitConverter.GetBytes(123).CopyTo(bytes, offsets.TelemInfo + fields.GetProperty("mID").GetInt32());
        Assert.False(TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes).Driving);
        BitConverter.GetBytes(0).CopyTo(bytes, offsets.TelemInfo + fields.GetProperty("mID").GetInt32());
        bytes[offsets.VehScoringInfo + vehicle.GetProperty("mControl").GetInt32()] = 3;
        Assert.False(TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes).Driving);
        bytes[offsets.VehScoringInfo + vehicle.GetProperty("mControl").GetInt32()] = 0;
        bytes[offsets.ScoringInfo + scoring.GetProperty("mGamePhase").GetInt32()] = 9;
        Assert.False(TelemetryDetails.Decode(offsets, offsets.TelemInfo, bytes).Driving);
        var old = JsonNode.Parse(layout.GetRawText())!;
        old["telemInfo"]!.AsObject().Remove("mFuel");
        using var document = JsonDocument.Parse(old.ToJsonString());
        // Re-load the legacy shape through the public layout contract.
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, document.RootElement.GetRawText());
            details = TelemetryDetails.Decode(SharedMemoryOffsets.Load(path), offsets.TelemInfo, bytes);
            Assert.Null(details.Values["mFuel"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IdleShutdownDoesNotPruneSavedRecordings()
    {
        var directory = Directory.CreateTempSubdirectory("ir-idle-retention-");
        try
        {
            var saved = Path.Combine(directory.FullName, "000-saved.jsonl");
            var recording = new byte[1024 * 1024];
            await File.WriteAllBytesAsync(saved, recording);
            var queue = Channel.CreateBounded<TimelineEntry>(2);
            await queue.Writer.WriteAsync(new("end", 1, 1, "shutdown"));
            await queue.Writer.WriteAsync(new("summary", 2, 2, "shutdown-totals"));
            queue.Writer.Complete();

            await TelemetryRecorder.WriteAsync(directory.FullName, queue.Reader,
                fileBudget: 256 * 1024, storageBudget: 1024 * 1024);

            Assert.Equal(recording, await File.ReadAllBytesAsync(saved));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RetentionReservesSpaceForActiveFileAndLeavesOtherDataAlone()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (var i = 0; i < 4; i++)
            {
                using var file = File.Create(Path.Combine(directory, $"000-old-{i}.jsonl"));
                file.SetLength(256 * 1024);
            }
            var keep = Path.Combine(directory, "notes.txt");
            await File.WriteAllTextAsync(keep, "keep");
            var queue = Channel.CreateBounded<TimelineEntry>(2);
            await queue.Writer.WriteAsync(new("sample", 1, 1, Identity: Identity, Sample: Sample(1), SegmentId: "a"));
            queue.Writer.Complete();
            await TelemetryRecorder.WriteAsync(directory, queue.Reader, fileBudget: 256 * 1024, storageBudget: 1024 * 1024);
            Assert.False(File.Exists(Path.Combine(directory, "000-old-0.jsonl")));
            Assert.False(File.Exists(Path.Combine(directory, "000-old-1.jsonl")));
            Assert.True(File.Exists(keep));
            Assert.True(new DirectoryInfo(directory).GetFiles("*.jsonl").Sum(f => f.Length) <= 1024 * 1024);
            var newest = Directory.GetFiles(directory, "*.jsonl").Order().Last();
            Assert.Equal(2, TelemetryRecorder.ReadComplete(newest).Count());
        }
        finally { Directory.Delete(directory, true); }
    }
}
