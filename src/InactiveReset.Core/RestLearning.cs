using System.Globalization;

namespace InactiveReset.Core;

/// <summary>What learning did with a placement, in words a report can print.</summary>
public sealed record LearningResult(
    bool Accepted, string Message, float ImpliedForward, float ImpliedLateral,
    double ImpliedSearchCandidate);

/// <summary>
/// Turns a finished placement into better constants.
///
/// <para><b>Why one placement is sufficient.</b> The miss IS the correction. A
/// placement made with D too small by x lands exactly x short, which was
/// measured directly: placing with D deliberately doubled moved the error by
/// exactly -deltaD, agreeing to 0.2 mm. So there is no iteration and no
/// learning rate -- <c>D_correct = D_used + error_forward</c>.</para>
///
/// <para><b>Pure.</b> Everything here is arithmetic on a finished outcome, so
/// the decision to trust a sample is testable without a running game. That
/// matters more than usual: this code writes constants that all later
/// placements inherit.</para>
/// </summary>
public static class RestLearning
{
    /// <summary>
    /// Decompose a placement's miss into the vehicle frame and decide whether it
    /// may be learned from.
    ///
    /// The lateral component is measured against <c>lateral(yaw)</c> and the
    /// forward against <c>heading(yaw)</c>, the same axes the model builds the
    /// rest offset on, so the two numbers ARE the corrections to D and L.
    /// </summary>
    public static LearningResult Evaluate(PlacementPlan plan, PlacementOutcome outcome)
    {
        var yaw = plan.Target.Yaw;
        var heading = Geometry.HeadingAxis(yaw);
        var lateral = Geometry.LateralAxis(yaw);

        var dx = outcome.Achieved.X - plan.Target.RestPosition.X;
        var dz = outcome.Achieved.Z - plan.Target.RestPosition.Z;

        var forwardError = dx * heading.X + dz * heading.Z;
        var lateralError = dx * lateral.X + dz * lateral.Z;

        var impliedForward = plan.Model.RestForwardDistance + forwardError;
        var impliedLateral = plan.Model.RestLateralOffset + lateralError;

        var candidate = ImpliedSearchCandidate(plan, outcome);

        var reject = LearnedRest.RejectReason(
            outcome, plan.Live.Container, forwardError, lateralError, candidate);

        return new LearningResult(
            reject is null,
            reject ?? $"learned D = {impliedForward:F6}, L = {impliedLateral:F6} "
                    + $"from a {MathF.Sqrt(forwardError * forwardError + lateralError * lateralError):F4} m miss",
            impliedForward, impliedLateral, candidate);
    }

    /// <summary>
    /// Which clearance-search candidate the miss implies the engine actually
    /// used. The model assumes 0.
    ///
    /// <para>A wrong candidate displaces the car purely along
    /// <c>lateral(entryYaw)</c> -- the axis the engine builds from the
    /// UNADJUSTED stored yaw -- in whole steps of <c>0.1 * width</c>. Projecting
    /// the miss onto that axis and dividing by the step recovers the index.</para>
    ///
    /// <para>Measured: a placement against a wall missed by 1.794 m, of which
    /// 1.7937 m lay on this axis and 0.16 mm did not, giving candidate 9 -- the
    /// last one the engine is allowed before the 1.5 * width limit.</para>
    /// </summary>
    public static double ImpliedSearchCandidate(PlacementPlan plan, PlacementOutcome outcome)
    {
        var width = plan.Live.Container.VehicleWidth;
        if (!(width > 0f))
        {
            return 0;
        }

        var entryYaw = plan.ComputedEntry.Orientation.Y;
        var axis = Geometry.LateralAxis(entryYaw);

        var along =
            (outcome.Achieved.X - plan.Target.RestPosition.X) * axis.X
            + (outcome.Achieved.Z - plan.Target.RestPosition.Z) * axis.Z;

        return along / (0.1f * width);
    }

    /// <summary>
    /// Record the placement and, when the sample is trustworthy, fold it into
    /// the learned constants for this checkpoint.
    ///
    /// Every placement is logged either way. A rejected sample is evidence too:
    /// the run that exposed the clearance-search assumption was one.
    /// </summary>
    public static LearningResult Apply(
        PlacementPlan plan, PlacementOutcome outcome,
        LearnedRestStore store, string buildSha256)
    {
        var result = Evaluate(plan, outcome);
        var container = plan.Live.Container;

        store.Record(new PlacementObservation
        {
            TimestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            BuildSha256 = buildSha256,
            TrackName = plan.Checkpoint.TrackName,
            VehicleName = plan.Checkpoint.VehicleName,
            CheckpointName = plan.Checkpoint.Name,
            VehicleLength = container.VehicleLength,
            VehicleWidth = container.VehicleWidth,
            EnginePlacementDistance = container.EnginePlacementDistance,
            UsedForward = plan.Model.RestForwardDistance,
            UsedLateral = plan.Model.RestLateralOffset,
            UsedSource = plan.RestSource.ToString(),
            ErrorForward = result.ImpliedForward - plan.Model.RestForwardDistance,
            ErrorLateral = result.ImpliedLateral - plan.Model.RestLateralOffset,
            ErrorVertical = outcome.VerticalErrorMetres,
            TargetYaw = plan.Target.Yaw,
            TargetX = plan.Target.RestPosition.X,
            TargetY = plan.Target.RestPosition.Y,
            TargetZ = plan.Target.RestPosition.Z,
            ImpliedSearchCandidate = result.ImpliedSearchCandidate,
            Accepted = result.Accepted,
            Verdict = result.Message,
        });

        if (!result.Accepted)
        {
            return result;
        }

        var existing = store.Load(
            buildSha256, plan.Checkpoint.TrackName,
            plan.Checkpoint.VehicleName, plan.Checkpoint.Name);

        store.Save(existing is null
            ? LearnedRest.First(
                buildSha256, plan.Checkpoint.TrackName, plan.Checkpoint.VehicleName,
                plan.Checkpoint.Name, result.ImpliedForward, result.ImpliedLateral)
            : existing.With(result.ImpliedForward, result.ImpliedLateral));

        return result;
    }
}
