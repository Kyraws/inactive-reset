using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class BodyVelocityPlanTests
{
    [Fact]
    public void ASharedTranslationPreservesRelativeBodyVelocityAndUsesThreeDoubles()
    {
        double[] delta = [1.5, 0.1, -1.8];
        double[] first = [0.01, -0.02, 0.03];
        double[] second = [-0.01, 0.02, -0.03];
        var a = BodyVelocityPlan.ShiftVelocity(first, delta);
        var b = BodyVelocityPlan.ShiftVelocity(second, delta);
        Assert.Equal(24, a.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(first[i] + delta[i], BitConverter.ToDouble(a, i * 8), 12);
            Assert.Equal(first[i] - second[i], BitConverter.ToDouble(a, i * 8) - BitConverter.ToDouble(b, i * 8), 12);
        }
    }

    [Fact]
    public void InvalidAndMovingShiftsAreRefused()
    {
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([0, 0], [0, 0, 1]));
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([0, 0, 0], [0, 1]));
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([double.NaN, 0, 0], [0, 0, 1]));
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([0, 0, 0], [0, double.PositiveInfinity, 1]));
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([0.2, 0, 0], [0, 0, 1]));
    }
}
