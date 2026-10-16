using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// A captured checkpoint must load back as the same pose. If save and load
/// disagree, placements silently target the wrong point — and every downstream
/// number would still look plausible.
/// </summary>
public sealed class CaptureRoundTripTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ir-tests-" + Guid.NewGuid().ToString("N"));

    // A real capture from Circuit de Barcelona, taken through shared memory.
    private static readonly Checkpoint Original = new()
    {
        Name = "roundtrip",
        TrackName = "Circuit de Barcelona",
        VehicleName = "Richard Mille AF Corse 2025 #50:ELMS",
        Pose = new RecordedPose(
            new Vec3(110.76835f, 0.83845913f, 206.76581f),
            new Vec3(0.53692055f, 0.0009529372f, -0.8436323f),
            new Vec3(5.547176E-05f, 0.99999934f, 0.0011648682f),
            new Vec3(0.8436328f, -0.00067223946f, 0.53692013f)),
        CapturedUtc = DateTimeOffset.UtcNow,
        LapDistance = 76.6936f,
        Gear = 0,
    };

    [Fact]
    public void SavedCheckpointLoadsBackIdentically()
    {
        CaptureService.Save(Original, _directory);
        var loaded = Checkpoint.Require(_directory, "roundtrip");

        Assert.Equal(Original.TrackName, loaded.TrackName);
        Assert.Equal(Original.VehicleName, loaded.VehicleName);
        Assert.Equal(Original.Gear, loaded.Gear);
        Assert.Null(loaded.Tyres); // Legacy captures have no tyre block.
        Assert.Null(loaded.LocalVelocityMetresPerSecond);
        Assert.Null(loaded.SpeedKmh);
        Assert.Equal(Original.LapDistance, loaded.LapDistance, 4);

        Assert.Equal(Original.Pose.Position, loaded.Pose.Position);
        Assert.Equal(Original.Pose.Row0, loaded.Pose.Row0);
        Assert.Equal(Original.Pose.Row1, loaded.Pose.Row1);
        Assert.Equal(Original.Pose.Row2, loaded.Pose.Row2);
        var before = Geometry.BuildTargetFromRecordedPose(Original.Pose);
        var after = Geometry.BuildTargetFromRecordedPose(loaded.Pose);
        Assert.True(before.Valid);
        Assert.True(after.Valid);
        Assert.Equal(before.Yaw, after.Yaw);
        Assert.Equal(before.RestPosition, after.RestPosition);
    }

    [Fact]
    public void SavingTheSameNameTwiceIsRefused()
    {
        CaptureService.Save(Original, _directory);
        var second = Assert.Throws<CheckpointException>(
            () => CaptureService.Save(Original, _directory));
        Assert.Contains("already exists", second.Message);
    }

    [Fact]
    public void QuickCaptureReplacesOnlyItsOwnTrackAndCarSlot()
    {
        Checkpoint Quick(string vehicle, float distance, int gear) => new()
        {
            Name = CaptureService.QuickCaptureName, TrackName = Original.TrackName,
            VehicleName = vehicle, Pose = Original.Pose, LapDistance = distance,
            Gear = gear, LocalVelocityMetresPerSecond = new(0, 0, -35),
        };
        CaptureService.Save(Original, _directory);
        var first = CaptureService.Save(Quick(Original.VehicleName, 100, 2), _directory, quick: true);
        var otherCar = CaptureService.Save(Quick("Other car", 200, 3), _directory, quick: true);
        var replaced = CaptureService.Save(Quick(Original.VehicleName, 300, 4), _directory, quick: true);
        Assert.Equal(first, replaced);
        var loaded = Checkpoint.Load(replaced);
        Assert.Equal(300, loaded.LapDistance);
        Assert.Equal(4, loaded.Gear);
        Assert.Equal(Original.Pose, loaded.Pose);
        Assert.Equal(new Vec3(0, 0, -35), loaded.LocalVelocityMetresPerSecond);
        Assert.Equal(200, Checkpoint.Load(otherCar).LapDistance);
        Assert.Equal(Original.LapDistance, Checkpoint.Require(_directory, Original.Name).LapDistance);
        Assert.Equal(3, Checkpoint.LoadAll(_directory).Count);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        Assert.Throws<CheckpointException>(() => CaptureService.Save(loaded, _directory));
        Assert.Throws<CheckpointException>(() => CaptureService.Save(Original, _directory, quick: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
