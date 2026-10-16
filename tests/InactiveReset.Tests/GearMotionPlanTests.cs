using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;
public sealed class GearMotionPlanTests
{
    private const double Idle = 223.0530784048753;
    private const double Limit = 837.7580409572781;
    [Fact]
    public void RacingSpeedUsesTheCapturedGearAndLoadedRatio()
    {
        var wheel = -300 / 3.6 / 0.36;
        var result = GearMotionPlan.Calculate(6, 6, 3.333333333333334, [wheel, wheel, wheel, wheel], 0, Idle, Limit);
        Assert.Equal(6, result.Gear); Assert.Equal(0, result.Clutch);
        Assert.InRange(result.EngineRpm, 7300, 7400);
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 8.662207357859533,
            [wheel, wheel, wheel, wheel], 0, Idle, Limit));
    }
    [Theory]
    [InlineData(0, 150)]
    [InlineData(1, 125)]
    [InlineData(2, 100)]
    public void DrivenAxleMatchesNativeAverage(int drive, double expected)
    {
        var result = GearMotionPlan.Calculate(1, 6, 5, [-20, -20, -30, -30], drive, 50, 1000);
        Assert.Equal(expected, result.EngineAngularVelocity); Assert.Equal(0, result.Clutch);
    }
    [Fact]
    public void BelowIdleKeepsClutchDisengagedWithoutDroppingTheCapturedGear()
    {
        var result = GearMotionPlan.Calculate(1, 6, 8, [-5, -5, -5, -5], 0, Idle, Limit);
        Assert.Equal(1, result.Gear); Assert.Equal(Idle, result.EngineAngularVelocity); Assert.Equal(1, result.Clutch);
        var neutral = GearMotionPlan.Calculate(0, 6, 0, [-200, -200, -200, -200], 0, Idle, Limit);
        Assert.Equal(Idle, neutral.EngineAngularVelocity); Assert.Equal(1, neutral.Clutch);
    }
    [Fact]
    public void ReverseUsesItsNegativeGearRatioAndRefusesOppositeDirection()
    {
        var reverse = GearMotionPlan.Calculate(-1, 6, -8, [30, 30, 30, 30], 0, Idle, Limit);
        Assert.Equal(240, reverse.EngineAngularVelocity);
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 8, [30, 30, 30, 30], 0, Idle, Limit));
    }
    [Fact]
    public void MissingGearsInvalidConfigAndNonFiniteStateAreRejected()
    {
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(7, 6, 5, [-30, -30, -30, -30], 0, Idle, Limit));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(-2, 6, -5, [30, 30, 30, 30], 0, Idle, Limit));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 5, [double.NaN, -30, -30, -30], 0, Idle, Limit));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 5, [-30, -30, -30, -30], 3, Idle, Limit));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, double.PositiveInfinity, [-30, -30, -30, -30], 0, Idle, Limit));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 5, [-30, -30, -30, -30], 0, Idle, double.NaN));
        Assert.Throws<GateException>(() => GearMotionPlan.Calculate(1, 6, 5, [-30, -30, -30, -30], 0, Idle, Idle - 1));
    }
}
