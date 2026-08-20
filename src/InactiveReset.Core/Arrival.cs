namespace InactiveReset.Core;

/// <summary>
/// A raw sample of the vehicle the moment a placement lands.
///
/// TIMING MATTERS. This must be sampled the instant control flips to the player,
/// NOT after any settle delay. Afterwards the driver has control and may legally
/// shift or move, which would make <see cref="Gear"/> and <see cref="MotionState"/>
/// meaningless as assertions about what the ENGINE produced.
/// </summary>
public sealed record ArrivalState
{
    public int ControlOwner { get; init; } = -99;
    public int Gear { get; init; } = -99;
    public int PitState { get; init; } = -1;

    /// <summary>
    /// vehicle +0x159F4: [0..2] world velocity, [3..5] local velocity,
    /// [6..8] angular velocity. Read to verify arrival; NEVER written.
    /// </summary>
    public float[] MotionState { get; init; } = new float[9];

    public bool Valid { get; init; }

    /// <summary>
    /// Largest absolute component across all nine slots. Zero for a correct
    /// arrival — the engine zeroes the block unconditionally.
    /// </summary>
    public float LargestMotionComponent =>
        MotionState.Length == 0 ? 0f : MotionState.Max(MathF.Abs);
}

public sealed record ArrivalVerification
{
    /// <summary>False when the state could not be read at all.</summary>
    public bool Checked { get; init; }

    public bool ControlIsPlayer { get; init; }
    public bool GearIsNeutral { get; init; }
    public bool MotionStateZeroed { get; init; }

    /// <summary>Pit state non-zero. Expected, not an anomaly — see below.</summary>
    public bool InPitState { get; init; }

    public int Gear { get; init; } = -99;
    public int PitState { get; init; } = -1;
    public float LargestMotionComponent { get; init; }

    /// <summary>
    /// Every check that did NOT hold. Empty means the arrival was verified on
    /// every field, not merely that it looked right.
    /// </summary>
    public IReadOnlyList<string> Anomalies { get; init; } = [];

    public bool Verified => Checked && Anomalies.Count == 0;
}

public static class ArrivalCheck
{
    /// <summary>
    /// Pure. Thresholds are absolute rather than tolerant on purpose: the engine
    /// zeroes the motion block and forces neutral UNCONDITIONALLY on every
    /// placement path, so anything else is a real deviation from the model.
    ///
    /// Pit state is deliberately NOT an anomaly. It is EXPECTED to be non-zero
    /// here — it is the known defect, not a surprise — and flagging it on every
    /// single placement would train the operator to ignore the list.
    /// </summary>
    public static ArrivalVerification Verify(ArrivalState state)
    {
        if (!state.Valid)
        {
            return new ArrivalVerification
            {
                Anomalies = ["arrival state could not be read - the placement is UNVERIFIED"],
            };
        }

        var largest = state.LargestMotionComponent;
        var anomalies = new List<string>();

        var controlIsPlayer = state.ControlOwner == 0;
        var gearIsNeutral = state.Gear == 0;
        var motionZeroed = largest == 0f;

        if (!controlIsPlayer)
        {
            anomalies.Add($"control owner is {state.ControlOwner}, expected 0 (Player)");
        }
        if (!gearIsNeutral)
        {
            anomalies.Add($"gear is {state.Gear}, expected 0 (neutral) - every placement path forces neutral");
        }
        if (!motionZeroed)
        {
            anomalies.Add($"motion state is not zeroed, largest component {largest} "
                        + "- the engine zeroes all nine slots unconditionally");
        }

        return new ArrivalVerification
        {
            Checked = true,
            ControlIsPlayer = controlIsPlayer,
            GearIsNeutral = gearIsNeutral,
            MotionStateZeroed = motionZeroed,
            InPitState = state.PitState != 0,
            Gear = state.Gear,
            PitState = state.PitState,
            LargestMotionComponent = largest,
            Anomalies = anomalies,
        };
    }
}
