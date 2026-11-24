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
    public void CompletedLapAppearsAcrossRotatedPartsAndKeepsExactPoints()
    {
        static TimelineEntry Point(long sequence, double time, float distance)
        {
            var entry = Entry(sequence, time);
            return entry with { Sample = entry.Sample! with { LapDistance = distance, Gear = sequence == 4 ? 9 : 2 } };
        }

        Part("a", Point(1, 10, 500), Point(2, 10.2, 0), Point(3, 10.4, 250));
        var timeline = new TelemetryTimeline(_directory);
        Assert.Empty(timeline.ListLaps());
        Part("b", Point(4, 10.6, 500), Point(5, 10.8, 2), Point(6, 11, 250));
        var lap = Assert.Single(timeline.ListLaps());
        Assert.Equal(0.6, lap.Duration, 6);
        var points = timeline.LapPoints(lap.Id);
        Assert.Equal(0, points[0].Elapsed);
        Assert.All(points, point => Assert.InRange(point.Elapsed, 0, lap.Duration));
        Assert.Equal(new long[] { 2, 3, 4 }, points.Select(point => point.Sequence));
        Assert.Equal(new[] { true, true, false }, points.Select(point => point.SpeedEligible));
        Assert.Equal(250, timeline.Select(lap.SegmentId, sequence: 3).Checkpoint.LapDistance);
        Assert.Equal(lap, Assert.Single(timeline.ListLaps()));
        Assert.Same(points, timeline.LapPoints(lap.Id));

        var firstPart = Path.Combine(_directory, "recordings", "a.jsonl");
        using (File.Open(firstPart, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var reopened = new TelemetryTimeline(_directory);
            Assert.Equal(lap, Assert.Single(reopened.ListLaps()));
            Assert.Equal(points, reopened.LapPoints(lap.Id));
        }
        File.WriteAllText(firstPart + TelemetryTimeline.LapIndexSuffix, "{interrupted");
        Assert.Equal(lap, Assert.Single(new TelemetryTimeline(_directory).ListLaps()));

        Part("c", Point(7, 12, 500), Point(8, 12.2, 0));
        Assert.Equal(lap, Assert.Single(timeline.ListLaps()));
        File.Delete(firstPart);
        Assert.Empty(timeline.ListLaps());
        Assert.Throws<GateException>(() => timeline.LapPoints(lap.Id));
        Assert.False(File.Exists(firstPart + TelemetryTimeline.LapIndexSuffix));
    }

    [Fact]
    public void OpeningRunEndsAtFirstCrossingAndDoesNotConsumeALapNumber()
    {
        static TimelineEntry Point(long sequence, double time, float distance)
        {
            var entry = Entry(sequence, time);
            return entry with { Sample = entry.Sample! with { LapDistance = distance } };
        }

        Part("a", Point(1, 10, -85), Point(2, 10.2, 1), Point(3, 10.4, 500),
            Point(4, 10.6, 0), Point(5, 10.8, 500), Point(6, 11, 0));
        var timeline = new TelemetryTimeline(_directory);
        var rows = timeline.ListLaps();
        Assert.Equal(2, rows.Length);
        Assert.False(rows[0].IsOpeningRun);
        Assert.Equal(1, rows[0].Number);
        Assert.True(rows[1].IsOpeningRun);
        Assert.Equal(new long[] { 1, 2, 3 }, timeline.LapPoints(rows[1].Id).Select(point => point.Sequence));
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
        Part("a", entry with { Sample = entry.Sample! with { Gear = 9 } });
        Assert.NotNull(new TelemetryTimeline(_directory).Select("one", sequence: 2).RollingUnavailableReason);
        Part("a", entry with { Sample = entry.Sample! with { Details = entry.Sample.Details! with { Control = 3 } } });
        Assert.Throws<GateException>(() => new TelemetryTimeline(_directory).Select("one", sequence: 2));
        Part("a", entry with { Sample = entry.Sample! with { VehicleName = "other" } });
        Assert.Throws<GateException>(() => new TelemetryTimeline(_directory).Select("one", sequence: 2));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
