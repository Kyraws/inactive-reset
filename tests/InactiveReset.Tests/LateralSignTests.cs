using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// L is signed, and the sign is the engine's own.
///
/// <para>For months L was applied unsigned, which was invisible because every
/// car it had ever been measured in reported lateral sign +1. The first car with
/// sign -1 missed by 1.07 m of pure lateral at BOTH tracks it was tried on,
/// which read convincingly as a track problem -- Daytona's banking was the
/// leading suspect -- until the corpus was re-split by the LIVE vehicle
/// geometry rather than the checkpoint's recorded name.</para>
///
/// <para>Everything below is measured, from build 0F6DCAC1 on 2026-08-22.</para>
/// </summary>
public sealed class LateralSignTests
{
    /// <summary>Genesis Magma Racing 2026 #17:LM. Lateral sign -1.</summary>
    private static readonly ContainerState Genesis = new(
        SlotIndex: 0, PitIndex: 0, GarageIndex: 0, ControlOwner: 1,
        VehicleLength: 5.0982f, VehicleWidth: 1.99268f, LateralSignSource: -13.496212f);

    private static readonly PlacementModel Model = new()
    {
        YawOffsetMode2 = 0.7853982f,
        SearchStartFactor = 0.55f,
        SearchStepFactor = 0.10f,
        SearchMaxFactor = 1.50f,
        RestForwardDistance = 3.30625f,
        RestVerticalOffset = 0.373062f,
        RestLateralOffset = 0.542440f,
    };

    /// <summary>The 24 bytes the failing Daytona placement actually wrote.</summary>
    private static readonly SpotEntry WrittenEntry = new(
        new Vec3(-197.908646f, 8.378745f, -232.236511f),
        new Vec3(-0.005000f, -0.485192f, 0.016000f));

    /// <summary>Where the car was asked to stop.</summary>
    private static readonly Vec3 TargetRest = new(-197.382233f, 8.751807f, -229.749680f);
    private const float TargetYaw = 0.300206f;

    /// <summary>Where it actually stopped: 1.075 m away, all of it lateral.</summary>
    private static readonly Vec3 AchievedRest = new(-198.408585f, 8.787745f, -229.431183f);

    [Fact]
    public void TheSignedModelPredictsWhereTheEngineActuallyPutTheCar()
    {
        // Measured at BOTH ends -- the entry that was written goes in, the
        // position the car reached comes out -- so the lateral term cannot
        // cancel itself out the way it does in an invert-then-predict round trip.
        var predicted = PlacementMath.PredictRestPosition(
            PlacementMath.PredictDriveDestination(WrittenEntry, Genesis, Model), Genesis, Model);

        var miss = MathF.Sqrt(
            (AchievedRest.X - predicted.X) * (AchievedRest.X - predicted.X)
            + (AchievedRest.Z - predicted.Z) * (AchievedRest.Z - predicted.Z));

        // ~1 cm, against 1.075 m unsigned. The remainder is a per-car settle
        // difference and is exactly what automatic learning absorbs.
        Assert.True(miss < 0.02f, $"signed model missed by {miss:F4} m");
    }

    [Fact]
    public void TheUnsignedModelMissesByTwiceLOnTheLateralAxis()
    {
        // Fails on the corrected model, passes on the old one: this is the
        // behaviour the sign exists to remove. The old model is reproduced by
        // flipping L, since sign here is -1.
        var unsigned = Model with { RestLateralOffset = -Model.RestLateralOffset };
        var predicted = PlacementMath.PredictRestPosition(
            PlacementMath.PredictDriveDestination(WrittenEntry, Genesis, unsigned),
            Genesis, unsigned);

        var lateral = Geometry.LateralAxis(TargetYaw);
        var along = (AchievedRest.X - predicted.X) * lateral.X
                  + (AchievedRest.Z - predicted.Z) * lateral.Z;

        // 1 dp: the measured miss is -1.0746 and 2L is -1.0849. The 1 cm gap is
        // the same per-car settle residual the signed model leaves behind, not
        // slack in the claim.
        Assert.Equal(-2f * Model.RestLateralOffset, along, 1);
    }

    [Fact]
    public void TheInverseRoundTripsInASignNegativeCar()
    {
        var entry = PlacementMath.InvertToPitPosEntry(
            TargetRest, TargetYaw, Genesis, Model, WrittenEntry);
        var rest = PlacementMath.PredictRestPosition(
            PlacementMath.PredictDriveDestination(entry, Genesis, Model), Genesis, Model);

        Assert.Equal(TargetRest.X, rest.X, 3);
        Assert.Equal(TargetRest.Y, rest.Y, 3);
        Assert.Equal(TargetRest.Z, rest.Z, 3);
    }

    [Fact]
    public void FlippingOnlyTheSignMirrorsTheLateralOffset()
    {
        // The whole claim, isolated: two identical cars differing only in the
        // engine's lateral sign land 2L apart, and nowhere else.
        var mirrored = Genesis with { LateralSignSource = -Genesis.LateralSignSource };
        var destination = new SpotEntry(TargetRest, new Vec3(0f, TargetYaw, 0f));

        var negative = PlacementMath.PredictRestPosition(destination, Genesis, Model);
        var positive = PlacementMath.PredictRestPosition(destination, mirrored, Model);

        var lateral = Geometry.LateralAxis(TargetYaw);
        var separation = (positive.X - negative.X) * lateral.X
                       + (positive.Z - negative.Z) * lateral.Z;

        Assert.Equal(2f * Model.RestLateralOffset, separation, 4);
        Assert.Equal(positive.Y, negative.Y, 6);
    }
}
