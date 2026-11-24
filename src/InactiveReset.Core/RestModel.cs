namespace InactiveReset.Core;

/// <summary>
/// Where a placement's forward and lateral constants came from.
///
/// Ordered by how much the tool trusts them, least first, so a report can say
/// something honest about the numbers it used.
/// </summary>
public enum RestSource
{
    /// <summary>Derived from the running engine. Works in any car, ~2-10 mm.</summary>
    Derived,

    /// <summary>A calibration file the user measured by hand, per track and vehicle.</summary>
    Profile,
}

/// <summary>The forward and lateral rest constants, and where they came from.</summary>
public readonly record struct RestConstants(float Forward, float Lateral, RestSource Source);

/// <summary>
/// The engine's rest displacement, derived rather than calibrated.
///
/// <para><b>What was measured.</b> ApplyVehicleTransform places the car at
/// <c>destination - M*(0, b, a)</c>, so the forward placement distance is
/// <c>-a</c>, and <c>a</c> is the sum of two per-vehicle floats the engine
/// already holds. The car then SETTLES, moving further by roughly
/// <c>0.105 * vehicleLength</c> -- and by very nearly the same amount sideways.
/// That equal split is the 45 degree bias this project carried for months as
/// "an annoying coincidence"; it is the settle.</para>
///
/// <para><b>Why this matters.</b> Every term but the two coefficients is read
/// live, so two numbers per build replace three numbers per
/// (track, vehicle, build) measured by hand. Before this, placement refused in
/// any combination the user had not personally calibrated, and there was no
/// command to calibrate with.</para>
///
/// <para><b>Evidence.</b> Coefficients fitted to three cars at Circuit de
/// Barcelona on build 0F6DCAC1, residuals under 1.7 mm, then validated blind on
/// a fourth that had never been calibrated: 2.4 mm. See docs/research/PLACEMENT_EVIDENCE.md.</para>
///
/// <para><b>Limits.</b> One track, one session, one setup. The settle is
/// physical, so ride height, surface or slope may move these. They are per
/// build: the placement distance is read live and follows a patch by itself,
/// but the settle coefficients are fitted and do not.</para>
/// </summary>
public static class RestModel
{
    /// <summary>Settle, forward, as a fraction of vehicle length.</summary>
    public const float SettleForwardFactor = 0.104855f;

    /// <summary>Settle, lateral, as a fraction of vehicle length.</summary>
    public const float SettleLateralFactor = 0.106398f;

    /// <summary>
    /// H, when nothing has measured it for this combination.
    ///
    /// UNEXPLAINED, and the weakest number here. ApplyVehicleTransform's own
    /// vertical term reads 0.362 for a 296 GT3 and 0.322 for a 499P, and this
    /// value matches neither -- it is simply the figure calibrated for the GT3,
    /// reused. It survives because vertical error has been ~1 mm on all four
    /// cars measured, including one it was borrowed onto blind. Do not mistake
    /// that for understanding it; the vertical settle has not been worked out.
    /// </summary>
    public const float DefaultVerticalOffset = 0.373062f;

    /// <summary>
    /// The rest constants derived from the running engine, or null when this
    /// build's offset profile does not carry the placement fields.
    /// </summary>
    public static RestConstants? Derive(ContainerState container)
    {
        if (!container.HasEnginePlacementDistance || !(container.VehicleLength > 0f))
        {
            return null;
        }
        return new RestConstants(
            container.EnginePlacementDistance + SettleForwardFactor * container.VehicleLength,
            SettleLateralFactor * container.VehicleLength,
            RestSource.Derived);
    }
}
