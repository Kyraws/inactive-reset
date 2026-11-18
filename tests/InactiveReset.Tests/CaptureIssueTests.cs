using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class CaptureIssueTests
{
    [Theory]
    [InlineData(0.090f, -0.115f, -63.056f, 5)]
    [InlineData(0.131f, -0.174f, -66.722f, 6)]
    [InlineData(-0.067f, 0.748f, -63.056f, 6)]
    [InlineData(0.010204315f, 0.33538005f, -31.852272f, 3)]
    public void RecordedStraightFramesPassAndPreserveAllVelocityComponents(float x, float y, float z, int gear)
    {
        var checkpoint = new Checkpoint
        {
            Name = "capture", TrackName = "Track", VehicleName = "Car",
            Pose = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
            LocalVelocityMetresPerSecond = new(x, y, z), Gear = gear,
        };
        Assert.Null(RollingMotion.CaptureIssue(checkpoint.LocalVelocityMetresPerSecond, gear));
        RollingMotion.RequireEligible(checkpoint, true, new("Track", "Car"));
        var payload = RollingMotion.BuildPayload(checkpoint.LocalVelocityMetresPerSecond.Value, checkpoint.Pose);
        Assert.Equal(new[] { x, y, z, x, y, z }, Enumerable.Range(0, 6).Select(i => BitConverter.ToSingle(payload, i * 4)));
    }

    [Fact]
    public void MissingInvalidStationaryAndIncompatibleGearHaveDistinctReasons()
    {
        Assert.Contains("No recorded speed", RollingMotion.CaptureIssue(null));
        Assert.Contains("Invalid", RollingMotion.CaptureIssue(new(float.NaN, 0, -2)));
        Assert.Contains("0.5 km/h", RollingMotion.CaptureIssue(new(0, 0, 0)));
        Assert.Contains("supported range", RollingMotion.CaptureIssue(new(0, 0, -2), 9));
        Assert.Contains("direction", RollingMotion.CaptureIssue(new(0, 0, 2), 1));
        Assert.Contains("direction", RollingMotion.CaptureIssue(new(0, 0, -2), -1));
        Assert.Null(RollingMotion.CaptureIssue(new(0, 0, 2), -1));
        Assert.Null(RollingMotion.CaptureIssue(new(0, 0, 2), 0));
    }
}
