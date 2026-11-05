using System.Globalization;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class CaptureIssueTests
{
    [Fact]
    public void ExperimentalToleranceAdmitsTheRecordedRoadAtlantaVelocity()
    {
        // Observed capture: 114.67 km/h, third gear. No reset was performed.
        var velocity = new Vec3(0.010204315f, 0.33538005f, -31.852272f);
        Assert.True(RollingMotion.EligibleCapture(velocity));
        Assert.Null(RollingMotion.CaptureIssue(velocity));
    }

    [Theory]
    [InlineData(0, 0, -2)]
    [InlineData(0.010424137f, 0.07474714f, -16.163006f)]
    [InlineData(0.0034713745f, 0.11587092f, -27.625631f)]
    public void EligibleCapturesHaveNoRejectionReason(float lateral, float vertical, float forward)
    {
        var velocity = new Vec3(lateral, vertical, forward);
        Assert.True(RollingMotion.EligibleCapture(velocity));
        Assert.Null(RollingMotion.CaptureIssue(velocity));
    }

    [Fact]
    public void UnknownInvalidStationaryAndLateralMotionHaveDistinctReasons()
    {
        Assert.Contains("No recorded speed", RollingMotion.CaptureIssue(null));
        Assert.Contains("Invalid", RollingMotion.CaptureIssue(new(float.NaN, 0, -2)));
        Assert.Contains("0.5 km/h", RollingMotion.CaptureIssue(new(0, 0, 0)));
        var sideways = RollingMotion.CaptureIssue(new(0.06f, 0, -20));
        Assert.Contains("sideways", sideways);
        Assert.Contains("0.060 m/s", sideways);
        Assert.Contains("0.050 m/s", sideways);
    }

    [Fact]
    public void MotionGateUsesTheSameReasonAsTheCheckpointProjection()
    {
        var checkpoint = new Checkpoint
        {
            Name = "capture", TrackName = "Track", VehicleName = "Car",
            Pose = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
            LocalVelocityMetresPerSecond = new(0.010204315f, 0.7f, -31.852272f),
        };
        var error = Assert.Throws<GateException>(() =>
            RollingMotion.RequireEligible(checkpoint, true, new("Track", "Car")));
        Assert.Equal(RollingMotion.CaptureIssue(checkpoint.LocalVelocityMetresPerSecond), error.Message);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void DiagnosticValuesUseUnambiguousUnitsAndFormatting(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var issue = RollingMotion.CaptureIssue(new(0, 0.7f, -31.852272f));
            Assert.Contains("0.700 m/s", issue);
            Assert.Contains("0.500 m/s", issue);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
