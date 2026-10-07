using System.Text.Json;

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

    /// <summary>Learned from this exact checkpoint's own placements. Sub-millimetre.</summary>
    Learned,
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
/// a fourth that had never been calibrated: 2.4 mm. See docs/archive/HEADING_BUG.md.</para>
///
/// <para><b>Limits.</b> One track, one session, one setup. The settle is
/// physical, so ride height, surface or slope may move these. They are per
/// build: the placement distance is read live and follows a patch by itself,
/// but the settle coefficients are fitted and do not. A learned constant beats
/// a derived one wherever the user has placed before.</para>
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

/// <summary>
/// One placement, as measured. The corpus that a general settle model would be
/// fitted from.
///
/// Every placement appends one of these, accepted or not, with the reason. A
/// rejected sample is as informative as an accepted one -- the run that first
/// exposed the clearance-search assumption was a rejected sample -- so nothing
/// is thrown away.
///
/// Written as JSON Lines: append-only, one record per line, survives a crash
/// mid-write, and reads into any analysis tool without a parser.
/// </summary>
public sealed record PlacementObservation
{
    public required string TimestampUtc { get; init; }
    public required string BuildSha256 { get; init; }
    public required string TrackName { get; init; }
    public required string VehicleName { get; init; }
    public required string CheckpointName { get; init; }

    /// <summary>Live vehicle geometry, so a later fit need not re-measure it.</summary>
    public required float VehicleLength { get; init; }
    public required float VehicleWidth { get; init; }

    /// <summary>The engine's own placement distance, before settling.</summary>
    public required float EnginePlacementDistance { get; init; }

    /// <summary>
    /// The engine's lateral sign (-1 or +1) for this spot. The model applies L
    /// UNSIGNED, so if the settle actually follows this sign, a track whose sign
    /// differs from the one L was fitted on misses by roughly 2L. Recorded so
    /// that question is answerable from the corpus rather than from a rerun.
    /// </summary>
    public required float LateralSign { get; init; }

    /// <summary>The constants this placement was made with, and their origin.</summary>
    public required float UsedForward { get; init; }
    public required float UsedLateral { get; init; }
    public required string UsedSource { get; init; }

    /// <summary>Miss in the vehicle frame. Forward and lateral ARE the corrections.</summary>
    public required float ErrorForward { get; init; }
    public required float ErrorLateral { get; init; }
    public required float ErrorVertical { get; init; }

    /// <summary>What the constants should have been: used + error.</summary>
    public float ImpliedForward => UsedForward + ErrorForward;
    public float ImpliedLateral => UsedLateral + ErrorLateral;

    /// <summary>Target pose, for a fit that wants to model terrain or slope.</summary>
    public required float TargetYaw { get; init; }
    public required double TargetX { get; init; }
    public required double TargetY { get; init; }
    public required double TargetZ { get; init; }

    /// <summary>Which clearance-search candidate the miss implies. 0 is the assumed one.</summary>
    public required double ImpliedSearchCandidate { get; init; }

    public required bool Accepted { get; init; }
    public required string Verdict { get; init; }

    public JsonObject_ ToJson() => new(this);

    /// <summary>Minimal writer, so the record's shape is defined in exactly one place.</summary>
    public sealed class JsonObject_(PlacementObservation o)
    {
        public string Write() => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["timestamp_utc"] = o.TimestampUtc,
            ["build_sha256"] = o.BuildSha256,
            ["track_name"] = o.TrackName,
            ["vehicle_name"] = o.VehicleName,
            ["checkpoint"] = o.CheckpointName,
            ["vehicle_length"] = o.VehicleLength,
            ["vehicle_width"] = o.VehicleWidth,
            ["engine_placement_distance"] = o.EnginePlacementDistance,
            ["lateral_sign"] = o.LateralSign,
            ["used_forward_D"] = o.UsedForward,
            ["used_lateral_L"] = o.UsedLateral,
            ["used_source"] = o.UsedSource,
            ["error_forward"] = o.ErrorForward,
            ["error_lateral"] = o.ErrorLateral,
            ["error_vertical"] = o.ErrorVertical,
            ["implied_forward_D"] = o.ImpliedForward,
            ["implied_lateral_L"] = o.ImpliedLateral,
            ["target_yaw"] = o.TargetYaw,
            ["target_x"] = o.TargetX,
            ["target_y"] = o.TargetY,
            ["target_z"] = o.TargetZ,
            ["implied_search_candidate"] = o.ImpliedSearchCandidate,
            ["accepted"] = o.Accepted,
            ["verdict"] = o.Verdict,
        });
    }
}
