using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class WheelRotationPlanTests
{
    private static readonly double[] Identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    [Theory]
    [InlineData(-2, -5)]
    [InlineData(2, 5)]
    public void ForwardAndReverseUseSdkSignAndWheelRadius(double z, double expected)
    {
        var result = WheelRotationPlan.Calculate(z, 0.4, Identity, [2, 3, 4], [0, 0.01, 0]);
        Assert.Equal(expected, result.Rate, 12);
        Assert.Equal(new[] { expected, 0.01, 0 }, result.Omega);
        Assert.Equal(expected * 2, result.Momentum[0], 12);
        Assert.Equal(0.03, result.Momentum[1], 12);
    }

    [Fact]
    public void RotatedAxleAndInertiaProduceWorldAngularState()
    {
        double[] rotation = [0, -1, 0, 1, 0, 0, 0, 0, 1];
        var result = WheelRotationPlan.Calculate(-2, 0.4, rotation, [2, 3, 4], [0.01, 0, 0.02]);
        Assert.Equal(new[] { 0.01, -5, 0.02 }, result.Omega);
        Assert.Equal(new[] { 0.03, -10, 0.08 }, result.Momentum);
    }

    [Fact]
    public void InvalidStateIsRefused()
    {
        foreach (var radius in new[] { 0, double.NaN, 1 })
            Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, radius, Identity, [2, 3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(double.PositiveInfinity, 0.4, Identity, [2, 3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(double.NaN, 0.4, Identity, [2, 3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, 0.4, [1, 2], [2, 3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, 0.4, Identity, [2, -3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, 0.4, Identity, [2, 3, 4], [0.2, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, 0.4, [1, 0, 0, 0, 1, 0, 0, 0, -1], [2, 3, 4], [0, 0, 0]));
        Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(-2, 0.4, [2, 0, 0, 0, 1, 0, 0, 0, 1], [2, 3, 4], [0, 0, 0]));
    }

    [Fact]
    public void PedalAndSpinGateRequiresAllChannelsReleased()
    {
        var rest = new WheelMotionTelemetry(1, 0, 0, 0, 0, [0, 0, 0, 0], [], []);
        WheelRotationPlan.RequireReleased(rest);
        Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(rest with { Brake = 0.1 }));
        Assert.Contains("still applying brakes", Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(rest with { FilteredBrake = 0.1 })).Message);
        Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(rest with { Throttle = 0.1 }));
        Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(rest with { FilteredThrottle = 0.1 }));
        Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(rest with { RotationRadiansPerSecond = [0, 0, 1, 0] }));
    }
}
