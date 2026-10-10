using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// Pins the lateral rest term to the placement that measured it.
///
/// Measured against Le Mans Ultimate build 0F6DCAC1 (file version 1.4.1.3) on
/// 2026-08-22, at Circuit de Barcelona in the Richard Mille AF Corse 2025 #50
/// ELMS, checkpoint cp-023328. The placement was made with D deliberately
/// DOUBLED (5.09529 instead of the calibrated 2.54765) as a diagnostic probe.
///
/// What the probe established: the error moved by exactly -delta-D along the
/// heading and its lateral component did not move at all, so the engine's
/// displacement from a written destination does not depend on where that
/// destination is. The miss is a constant vehicle-frame vector, and the model
/// had no term for its off-axis half.
///
/// What the probe did NOT establish, and what no experiment can: whether the
/// engine gets there by rotating or by translating. Both produce a constant
/// vehicle-frame vector, and the engine's own range is not something this tool
/// can vary.
/// </summary>
public sealed class RestLateralOffsetTests
{
    // Live values read from the game during that run.
    private static readonly ContainerState Container = new(
        SlotIndex: 0,
        PitIndex: 8,
        GarageIndex: 0,
        ControlOwner: 1,
        VehicleLength: 4.7457f,
        VehicleWidth: 2.03186f,
        LateralSignSource: 8.60127f);

    /// <summary>Tunables read live from 0F6DCAC1 during the run.</summary>
    private static readonly PlacementModel Probe = new()
    {
        YawOffsetMode2 = 0.785398f,
        SearchStartFactor = 0.55f,
        SearchStepFactor = 0.1f,
        SearchMaxFactor = 1.5f,
        RestForwardDistance = 5.09529447555542f,
        RestVerticalOffset = 0.37306222319602966f,
        RestLateralOffset = 0f,
    };

    /// <summary>
    /// The same run's constants corrected by what it measured. D absorbs the
    /// forward miss; L is the off-axis remainder that had nowhere to go before.
    /// </summary>
    private static readonly PlacementModel Corrected = Probe with
    {
        RestForwardDistance = 3.0494228f,
        RestLateralOffset = 0.50325315f,
    };

    // The live PitPos[8] entry at the time; its pitch and roll are carried through.
    private static readonly SpotEntry CurrentEntry = new(
        new Vec3(117.554001f, 0.413000f, 199.453995f),
        new Vec3(-0.008000f, 0.578000f, -0.015000f));

    private const float TargetYaw = 0.516701f;

    /// <summary>Where cp-023328 asked the car to come to rest.</summary>
    private static readonly Vec3 TargetRest = new(-87.676720f, -1.530338f, -146.856659f);

    /// <summary>Where the car actually came to rest.</summary>
    private static readonly Vec3 AchievedRest = new(-88.249855f, -1.576400f, -148.884064f);

    /// <summary>The entry the probe placement actually wrote, as `plan` printed it.</summary>
    private static readonly SpotEntry WrittenEntry = new(
        new Vec3(-90.490547f, -1.903400f, -150.209366f),
        new Vec3(-0.008000f, 1.302099f, -0.015000f));

    [Fact]
    public void TheCorrectedModelPredictsWhereTheEngineActuallyPutTheCar()
    {
        // Forward only, and measured at BOTH ends: the entry that was written
        // goes in, the position the car reached comes out. Nothing here is
        // derived from the model, so the lateral term cannot cancel itself out
        // the way it does in an invert-then-predict round trip.
        var predicted = PlacementMath.PredictRestPosition(PlacementMath.PredictDriveDestination(WrittenEntry, Container, Corrected), Container, Corrected);

        Assert.Equal(AchievedRest.X, predicted.X, 3);
        Assert.Equal(AchievedRest.Z, predicted.Z, 3);

        // Y is predicted 0.046 m high. That is terrain under THIS run's
        // deliberately displaced destination, not a standing error in H: the
        // confirming placement with the corrected constants came in at 0.9 mm
        // vertical. Horizontal is what the lateral term fixes.
        Assert.Equal(AchievedRest.Y + 0.046062f, predicted.Y, 3);
    }

    [Fact]
    public void TheCorrectedModelEmitsTheEntryThatWasActuallyWritten()
    {
        // The bytes the probe placement wrote, decoded: pos and ori[1] as
        // printed by `plan`. Pitch and roll are carried through from
        // CurrentEntry, so only these four numbers are the model's to produce.
        var entry = PlacementMath.InvertToPitPosEntry(
            AchievedRest, TargetYaw, Container, Corrected, CurrentEntry);

        Assert.Equal(-90.490547f, entry.Position.X, 3);
        Assert.Equal(-150.209366f, entry.Position.Z, 3);
        Assert.Equal(1.302099f, entry.Orientation.Y, 4);

        // Y does NOT come back as the written -1.903400. It comes back 0.046 m
        // lower, because the input here is where the car SETTLED and settling
        // drops it onto the terrain under this run's displaced destination.
        // H is correct and must not absorb it: the confirming placement with
        // the corrected constants landed 0.9 mm out vertically.
        Assert.Equal(-1.903400f - 0.046062f, entry.Position.Y, 3);
    }

    /// <summary>
    /// The CONFIRMING placement, made minutes later with the corrected constants
    /// in the calibration. A second, independent destination -- 2.5 m from the
    /// probe's -- so the constants are pinned by two measured placements rather
    /// than fitted to the one they came from.
    ///
    /// It landed 0.000500 m horizontal and +0.000883 m vertical from its target,
    /// down from 0.710 m, with arrival verified.
    /// </summary>
    [Fact]
    public void TheConfirmingPlacementLandsOnItsTarget()
    {
        var written = new SpotEntry(
            new Vec3(-89.917412f, -1.903400f, -148.181961f),
            new Vec3(-0.008000f, 1.302099f, -0.015000f));
        var achieved = new Vec3(-87.676292f, -1.529455f, -146.856400f);

        var predicted = PlacementMath.PredictRestPosition(PlacementMath.PredictDriveDestination(written, Container, Corrected), Container, Corrected);

        // Asserted as a residual, not to N decimal places: 0.5 mm is the real
        // agreement between model and engine on this run, so a tighter
        // assertion would be pinning noise. The bar is 5 mm -- still 140x
        // better than the 0.710 m this replaced, and loose enough that only a
        // genuine regression trips it.
        var horizontal = MathF.Sqrt(
            (predicted.X - achieved.X) * (predicted.X - achieved.X)
            + (predicted.Z - achieved.Z) * (predicted.Z - achieved.Z));

        Assert.True(horizontal < 0.005f, $"horizontal residual {horizontal:F6} m");
        Assert.True(MathF.Abs(predicted.Y - achieved.Y) < 0.005f,
            $"vertical residual {predicted.Y - achieved.Y:F6} m");
    }

    [Fact]
    public void Zero_lateral_offset_preserves_forward_only_prediction()
    {
        var withoutTerm = PlacementMath.PredictRestPosition(
            new SpotEntry(TargetRest, new Vec3(0f, TargetYaw, 0f)), Container, Probe);
        var heading = Geometry.HeadingAxis(TargetYaw);

        Assert.Equal(TargetRest.X + heading.X * Probe.RestForwardDistance, withoutTerm.X, 4);
        Assert.Equal(TargetRest.Z + heading.Z * Probe.RestForwardDistance, withoutTerm.Z, 4);
    }
}
