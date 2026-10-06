using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class IndexedPlacementMathTests
{
    [Fact]
    public void Mode_two_address_uses_the_live_pit_index_and_direct_engine_distance()
    {
        Assert.Equal(0x100040UL, IndexedPlacementMath.EntryAddress(0x100000, 0));
        Assert.Equal(0x100340UL, IndexedPlacementMath.EntryAddress(0x100000, 8));
        Assert.Throws<GateException>(() => IndexedPlacementMath.EntryAddress(0x100000, -1));
        Assert.Throws<GateException>(() => IndexedPlacementMath.EntryAddress(0x100000, 104));
        var car = new ContainerState(0, 8, 0, 1, 5, 2, -1, -2.3195267f, -0.232022f);
        var rest = IndexedPlacementMath.Derive(car)!.Value;
        Assert.Equal(2.5515487f, rest.Forward, 5);
        Assert.Equal(0f, rest.Lateral);
        Assert.Null(IndexedPlacementMath.Derive(car with { RestOffsetPrimary = 0, RestOffsetSecondary = 0 }));
    }

    [Fact]
    public void Independent_baseline_and_yaw_probe_agree_on_vehicle_frame_offset()
    {
        var entry = new SpotEntry(new Vec3(119.906578f, 0.3762889f, 198.283630f),
                                  new Vec3(-0.008f, -0.207398176f, -0.015f));
        var rest = new Vec3(119.386253f, 0.758449f, 200.777328f);
        var baseline = IndexedPlacementMath.Calibrate(entry, rest, -0.207205f);
        var turned = entry with { Orientation = entry.Orientation with { Y = entry.Orientation.Y + 0.1f } };
        var probe = IndexedPlacementMath.Calibrate(
            turned, new Vec3(119.638145f, 0.755254f, 200.816971f), -0.107263f);
        Assert.InRange(MathF.Abs(baseline.Forward - probe.Forward), 0, 0.001f);
        Assert.InRange(MathF.Abs(baseline.Lateral - probe.Lateral), 0, 0.001f);
        Assert.InRange(MathF.Abs(baseline.YawBias - probe.YawBias), 0, 0.001f);
        Assert.InRange(MathF.Abs(IndexedPlacementMath.PredictRest(entry, baseline).X - rest.X), 0, 0.0001f);
        var target = new Vec3(-87.676720f, -1.530338f, -146.856659f);
        var yaw = MathF.Atan2(0.4938262f, 0.8691229f);
        var inverse = IndexedPlacementMath.Invert(target, yaw, entry, baseline);
        var predicted = IndexedPlacementMath.PredictRest(inverse, baseline);
        Assert.InRange(new Vec3(predicted.X - target.X, predicted.Y - target.Y,
                                predicted.Z - target.Z).Length, 0, 0.0001f);
        Assert.Throws<CalibrationException>(() => IndexedPlacementMath.Calibrate(
            entry, rest with { X = rest.X + 10f }, -0.207205f));
    }
}
