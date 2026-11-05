using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class CheckpointNameBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ir-name-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CheckpointNameCannotEscapeTrackFolder()
    {
        // One parent segment stays within this test's own root even on the historical vulnerable code.
        var checkpoint = Named("../escaped");
        Assert.Throws<CheckpointException>(() => CaptureService.Save(checkpoint, _directory));
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void CheckpointNameLengthBoundary(int length, bool accepted)
    {
        var checkpoint = Named(new string('a', length));
        if (accepted)
        {
            var path = CaptureService.Save(checkpoint, _directory);
            Assert.True(File.Exists(path));
            Assert.Equal(checkpoint.Name, Checkpoint.Require(_directory, checkpoint.Name).Name);
        }
        else
        {
            Assert.Throws<CheckpointException>(() => CaptureService.Save(checkpoint, _directory));
            Assert.False(Directory.Exists(_directory));
        }
    }

    private static Checkpoint Named(string name) => new()
    {
        Name = name, TrackName = "Test track", VehicleName = "Test car",
        Pose = new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
