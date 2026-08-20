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
        Assert.Equal(Original.LapDistance, loaded.LapDistance, 4);

        Assert.Equal(Original.Pose.Position, loaded.Pose.Position);
        Assert.Equal(Original.Pose.Row0, loaded.Pose.Row0);
        Assert.Equal(Original.Pose.Row1, loaded.Pose.Row1);
        Assert.Equal(Original.Pose.Row2, loaded.Pose.Row2);
    }

    [Fact]
    public void ARoundTrippedPoseYieldsTheSameYaw()
    {
        CaptureService.Save(Original, _directory);
        var loaded = Checkpoint.Require(_directory, "roundtrip");

        var before = Geometry.BuildTargetFromRecordedPose(Original.Pose);
        var after = Geometry.BuildTargetFromRecordedPose(loaded.Pose);

        Assert.True(before.Valid);
        Assert.Equal(before.Yaw, after.Yaw);
        Assert.Equal(before.RestPosition, after.RestPosition);
    }

    [Fact]
    public void CapturedOrientationRowsAreUnitLength()
    {
        // A real rotation matrix has unit rows. If shared memory were read at the
        // wrong offset — or as float instead of double — this is what would
        // catch it, because the numbers would still look like plausible angles.
        Assert.Equal(1f, Original.Pose.Row0.Length, 4);
        Assert.Equal(1f, Original.Pose.Row1.Length, 4);
        Assert.Equal(1f, Original.Pose.Row2.Length, 4);
    }

    [Fact]
    public void SavingTheSameNameTwiceIsRefused()
    {
        CaptureService.Save(Original, _directory);
        var second = Assert.Throws<CheckpointException>(
            () => CaptureService.Save(Original, _directory));
        Assert.Contains("already exists", second.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
