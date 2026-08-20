using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// Golden-value tests proving the C# placement math reproduces the C++
/// predecessor exactly.
///
/// The expected values are not invented. They were produced by the original
/// `LMUStage0DryRun checkpoint` against Le Mans Ultimate build 1AC2F605 on
/// 2026-08-11, at Circuit de Barcelona in the Richard Mille AF Corse 296 GT3.
/// If any of these change, the port has drifted from the implementation whose
/// behaviour was actually measured against the game — which is the only
/// evidence any of these constants are right.
/// </summary>
public sealed class PlacementPortTests
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

    /// <summary>
    /// The model the golden values were produced with.
    ///
    /// IMPORTANT: these are the ENGINE MODEL DEFAULTS (D = 2.548, H = 0.37), not
    /// the measured calibration (D = 2.54765, H = 0.373062). `LMUStage0DryRun`
    /// is stage0-only and never loads a calibration profile, while `LMURouteC`
    /// does — so in the predecessor the dry run predicts a landing point about
    /// 3 mm below the one a real placement targets.
    ///
    /// That is a wart worth knowing about, not a rounding error. Any tool that
    /// previews a placement must use the SAME model the placement will use, or
    /// the preview is quietly answering a different question.
    /// </summary>
    /// <remarks>
    /// These constants are the 266D1AF6 build's, and are PINNED ON PURPOSE. This
    /// fixture exists to prove the C# port reproduces the C++ predecessor bit for
    /// bit, so it must keep using the values the predecessor used. It is NOT a
    /// check that the profile describes the current game build, and it will pass
    /// happily while `offsets/<hash>.json` is stale -- which it was: the live
    /// build uses 45 deg and a 0.55 search start. See docs/HEADING_BUG.md.
    /// </remarks>
    private static readonly PlacementModel Model = new()
    {
        YawOffsetMode2 = 0.6108652f,
        SearchStartFactor = 0.2f,
        SearchStepFactor = 0.1f,
        SearchMaxFactor = 1.5f,
        RestForwardDistance = 2.548f,
        RestVerticalOffset = 0.37f,
    };

    /// <summary>The measured calibration, used to prove the two differ.</summary>
    private static readonly PlacementModel Calibrated = Model with
    {
        RestForwardDistance = 2.54764723777771f,
        RestVerticalOffset = 0.37306222319602966f,
    };

    // The live PitPos[8] entry at the time; its pitch and roll are carried through.
    private static readonly SpotEntry CurrentEntry = new(
        new Vec3(117.554001f, 0.413000f, 199.453995f),
        new Vec3(-0.008000f, 0.578000f, -0.015000f));

    /// <summary>
    /// Checkpoint cp-023328 exactly as recorded from LMU's shared memory.
    ///
    /// The target is DERIVED from this rather than hardcoded from the tool's
    /// printout: the printout rounds yaw to six decimals, and feeding that back
    /// in shifts the encoded orientation by 2 ULPs. Round-tripping through the
    /// real pipeline is both more faithful and a better test.
    /// </summary>
    private static readonly RecordedPose Recorded = new(
        Position: new Vec3(-87.67671966552734f, -1.5303380489349365f, -146.85665893554688f),
        Row0: new Vec3(0.8694382309913635f, -0.014588003046810627f, 0.4938262104988098f),
        Row1: new Vec3(0.0011039676610380411f, 0.9996188282966614f, 0.027585839852690697f),
        Row2: new Vec3(-0.4940403997898102f, -0.023439016193151474f, 0.8691229224205017f));

    private static readonly TargetPose Target = Geometry.BuildTargetFromRecordedPose(Recorded);
    private static Vec3 DesiredRest => Target.RestPosition;
    private static float DesiredYaw => Target.Yaw;

    [Fact]
    public void TheRecordedPoseIsAcceptedAndYieldsTheReportedYaw()
    {
        Assert.True(Target.Valid, string.Join("; ", Target.Failures));
        // The C++ tool reported: extracted yaw 0.516701 rad, heading horizontal
        // magnitude 0.999619.
        Assert.Equal(0.516701f, Target.Yaw, 6);
        Assert.Equal(0.999619f, Target.HeadingHorizontalMagnitude, 6);
    }

    [Fact]
    public void InvertProducesTheEntryTheCppToolComputed()
    {
        var entry = PlacementMath.InvertToPitPosEntry(
            DesiredRest, DesiredYaw, Container, Model, CurrentEntry);

        Assert.Equal(-89.109749f, entry.Position.X, 4);
        Assert.Equal(-1.900338f, entry.Position.Y, 4);
        Assert.Equal(-148.704910f, entry.Position.Z, 4);

        // Pitch and roll are carried through from the live entry, unchanged.
        Assert.Equal(-0.008000f, entry.Orientation.X, 6);
        Assert.Equal(1.127566f, entry.Orientation.Y, 4);
        Assert.Equal(-0.015000f, entry.Orientation.Z, 6);
    }

    [Fact]
    public void EncodeProducesTheExactBytesTheCppToolWouldWrite()
    {
        var entry = PlacementMath.InvertToPitPosEntry(
            DesiredRest, DesiredYaw, Container, Model, CurrentEntry);

        var expected = Convert.FromHexString(
            "3138B2C2473EF3BF75B414C36F1203BC1454903F8FC275BC");

        Assert.Equal(24, expected.Length);
        Assert.Equal(expected, PlacementMath.EncodeSpotEntry(entry));
    }

    [Fact]
    public void ForwardModelRoundTripsTheInverseToZeroResidual()
    {
        // The C++ tool reported "forward re-check ... residual 0 m". The inverse
        // and forward models must agree exactly, or one of them is wrong.
        var entry = PlacementMath.InvertToPitPosEntry(
            DesiredRest, DesiredYaw, Container, Model, CurrentEntry);

        var destination = PlacementMath.PredictDriveDestination(entry, Container, Model);
        var rest = PlacementMath.PredictRestPosition(destination, Model);

        Assert.Equal(DesiredRest.X, rest.X, 3);
        Assert.Equal(DesiredRest.Y, rest.Y, 3);
        Assert.Equal(DesiredRest.Z, rest.Z, 3);
    }

    [Fact]
    public void ForwardModelReproducesTheProbeReadout()
    {
        // From `LMUStage0DryRun probe` on the same session:
        //   engine would drive to : [117.894363, 0.413000, 199.231979] yaw -0.0328652
        //   and come to rest at   : [117.810638, 0.783000, 201.778610]
        var destination = PlacementMath.PredictDriveDestination(CurrentEntry, Container, Model);

        Assert.Equal(117.894363f, destination.Position.X, 3);
        Assert.Equal(0.413000f, destination.Position.Y, 3);
        Assert.Equal(199.231979f, destination.Position.Z, 3);
        Assert.Equal(-0.0328652f, destination.Orientation.Y, 5);

        var rest = PlacementMath.PredictRestPosition(destination, Model);
        Assert.Equal(117.810638f, rest.X, 3);
        Assert.Equal(0.783000f, rest.Y, 3);
        Assert.Equal(201.778610f, rest.Z, 3);
    }

    [Fact]
    public void YawComesFromTheThirdColumnNotTheThirdRow()
    {
        // Taking the third ROW instead of the third COLUMN produces a
        // plausible-looking heading with the wrong sign, which is silent and
        // ruinous. Pin the correct one.
        var row0 = new Vec3(0.8694382f, -0.014588f, 0.4938262f);
        var row2 = new Vec3(-0.4940404f, 0f, 0.8693000f);

        var yaw = Geometry.ExtractYawFromOrientationRows(row0, row2);
        Assert.Equal(MathF.Atan2(row0.Z, row2.Z), yaw, 6);

        // The row-based mistake would give a different value entirely.
        Assert.NotEqual(MathF.Atan2(row2.X, row2.Z), yaw, 3);
    }

    [Fact]
    public void PreconditionsPassForTheKnownGoodContainer()
    {
        var globals = new SpotTableGlobals(PitPosTable: 0x25C28996AA0, GarPosTable: 0x25C289AA420,
                                           Mult: 104, Count: 1);
        var gate = PlacementMath.EvaluatePlacementPreconditions(Container, globals, Model);
        Assert.True(gate.Passed, string.Join("; ", gate.Failures));
    }

    [Fact]
    public void PreconditionsRefuseWhenTheCarIsNotUnderAi()
    {
        var globals = new SpotTableGlobals(0x25C28996AA0, 0x25C289AA420, 104, 1);
        var driving = Container with { ControlOwner = 0 };
        var gate = PlacementMath.EvaluatePlacementPreconditions(driving, globals, Model);

        Assert.False(gate.Passed);
        Assert.Contains(gate.Failures, f => f.Contains("controlOwner"));
    }

    [Fact]
    public void EncodeNeverTouchesThePaddingTail()
    {
        var bytes = PlacementMath.EncodeSpotEntry(
            new SpotEntry(new Vec3(1, 2, 3), new Vec3(4, 5, 6)));

        // 24 bytes by construction. The 8 bytes of padding at +0x18 are excluded
        // because they are not produced, not because someone remembered to skip.
        Assert.Equal(24, bytes.Length);
        Assert.Equal(new SpotEntry(new Vec3(1, 2, 3), new Vec3(4, 5, 6)),
                     PlacementMath.DecodeSpotEntry(bytes));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(8.60127f, 1f)]
    [InlineData(-8.60127f, -1f)]
    public void LateralSignMirrorsTheEngine(float source, float expected) =>
        Assert.Equal(expected, Geometry.LateralSign(source));

    [Fact]
    public void DefaultAndCalibratedModelsDisagree()
    {
        // Pins the wart described on `Model` above. The predecessor's dry-run
        // preview and its real placement use different constants, so the preview
        // is answering a slightly different question. ~3 mm vertically here.
        var withDefaults = PlacementMath.PredictRestPosition(
            PlacementMath.PredictDriveDestination(CurrentEntry, Container, Model), Model);
        var withCalibration = PlacementMath.PredictRestPosition(
            PlacementMath.PredictDriveDestination(CurrentEntry, Container, Calibrated), Calibrated);

        Assert.NotEqual(withDefaults.Y, withCalibration.Y, 4);
        Assert.Equal(0.003062f, withCalibration.Y - withDefaults.Y, 5);
    }
}
