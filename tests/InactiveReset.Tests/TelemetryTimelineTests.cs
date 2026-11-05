using System.Text.Json;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class TelemetryTimelineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("timeline-selection-").FullName;
    private static readonly RecordingIdentity Identity = new("game.exe", new string('A', 64), new string('B', 64), "track", "car", 1, 123);
    private static TimelineEntry Entry(long sequence, double time, string segment = "one") => new("sample", sequence, sequence,
        Identity: Identity, SegmentId: segment, Sample: new("track", "car",
            new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), 12, 2, time,
            LocalVelocityMetresPerSecond: new(0, 0, -10))
        { Details = new(1, time, 5, true, 0, 0, new Dictionary<string, double?>(), [], null) { IdentityMatches = true } });
    private void Part(string name, params TimelineEntry[] entries)
    {
        var folder = Path.Combine(_directory, "recordings");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, name + ".jsonl"),
            new[] { new TimelineEntry("header", 0, 0, Identity: Identity, SegmentId: entries[0].SegmentId) }
                .Concat(entries).Select(e => JsonSerializer.Serialize(e)));
    }

    [Fact]
    public void SnapsAcrossRotatedFilesAndResetResolvesExactSequence()
    {
        Part("a", Entry(2, 10));
        Part("b", Entry(3, 10.5), Entry(4, 11));
        Part("c", Entry(8, 10.48, "other"));
        File.AppendAllText(Path.Combine(_directory, "recordings", "b.jsonl"), "{interrupted");
        var timeline = new TelemetryTimeline(_directory);
        var summary = timeline.List().Single(s => s.SegmentId == "one");
        Assert.Equal((10d, 11d, 3), (summary.Start, summary.End, summary.Samples));
        var selected = timeline.Select("one", timestamp: 10.46);
        Assert.Equal(3, selected.Sequence);
        Assert.Equal(10.5, selected.Timestamp);
        Assert.Equal(selected.Checkpoint.Pose, timeline.Select("one", sequence: 3).Checkpoint.Pose);
        Assert.Equal(36d, selected.SpeedKmh!.Value, 5);
        Assert.Null(selected.RollingUnavailableReason);
        Assert.Equal(3, selected.Checkpoint.Restoration!.Sequence);
        Assert.Throws<GateException>(() => timeline.Select("one", sequence: 8));
        Assert.Throws<GateException>(() => timeline.Select("../a", timestamp: 10));
        Assert.Throws<GateException>(() => timeline.Select("one", timestamp: double.NaN));
        Assert.Throws<GateException>(() => timeline.Select("one", timestamp: 10, sequence: 2));
    }

    [Fact]
    public void RecordedContextRequiresBuildTrackAndCarEvenForStandingPose()
    {
        Part("a", Entry(2, 10));
        var selection = new TelemetryTimeline(_directory).Select("one", sequence: 2);
        selection.RequireContext(Identity.ExecutableSha256, new("track", "car"));
        Assert.Throws<GateException>(() => selection.RequireContext(new string('C', 64), new("track", "car")));
        Assert.Throws<CheckpointException>(() => selection.RequireContext(Identity.ExecutableSha256, new("other", "car")));
        Assert.Throws<GateException>(() => selection.RequireContext(Identity.ExecutableSha256, new("track", "other")));
        var path = CaptureService.Save(selection.Checkpoint, Path.Combine(_directory, "checkpoints"));
        Assert.Equal(selection.Checkpoint.Restoration!.Sequence, Checkpoint.Load(path).Restoration!.Sequence);
    }

    [Fact]
    public void UnsuitableMotionIsExplainedAndInvalidDrivingIsRejected()
    {
        var entry = Entry(2, 10);
        Part("a", entry with { Sample = entry.Sample! with { LocalVelocityMetresPerSecond = new(1, 0, -10) } });
        Assert.NotNull(new TelemetryTimeline(_directory).Select("one", sequence: 2).RollingUnavailableReason);
        Part("a", entry with { Sample = entry.Sample! with { Details = entry.Sample.Details! with { Control = 3 } } });
        Assert.Throws<GateException>(() => new TelemetryTimeline(_directory).Select("one", sequence: 2));
        Part("a", entry with { Sample = entry.Sample! with { VehicleName = "other" } });
        Assert.Throws<GateException>(() => new TelemetryTimeline(_directory).Select("one", sequence: 2));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
