namespace InactiveReset.Core;

/// <summary>
/// What the engine will do with the lap you are on. This is LMU's
/// <c>countLapFlag</c>, inherited from rF2 -- the binary still carries the
/// strings COUNT_NEITHER / COUNT_LAP_ONLY / COUNT_LAP_AND_TIME.
/// </summary>
public enum LapCounting
{
    /// <summary>The lap counts for neither position nor time.</summary>
    Neither = 0,

    /// <summary>The lap is counted but NOT timed. This is what an out-lap gets.</summary>
    LapOnly = 1,

    /// <summary>The lap is counted and timed. The only value that produces a lap time.</summary>
    LapAndTime = 2,
}

/// <summary>
/// Lap-validity state for one slot, read live.
///
/// Two fields, not one, and conflating them is the mistake to avoid:
/// <see cref="CountLapFlag"/> describes the lap you are ON, and
/// <see cref="LapCountsNext"/> is a latch consumed at the NEXT start/finish
/// crossing to compute the next value of <see cref="CountLapFlag"/>.
/// </summary>
public sealed record LapValidityState
{
    public required int SlotIndex { get; init; }

    /// <summary>Raw value; may be outside <see cref="LapCounting"/> if something is wrong.</summary>
    public required int CountLapFlag { get; init; }

    /// <summary>The latch at +0x1CFF4, consumed at the next start/finish crossing.</summary>
    public required bool LapCountsNext { get; init; }

    /// <summary>mInPits. The out-lap rule keys off this.</summary>
    public required bool PitFlag { get; init; }

    /// <summary>Pit sub-state; clears with distance on its own.</summary>
    public required int PitState { get; init; }

    /// <summary>
    /// Elapsed time at which the current lap started.
    ///
    /// The promote block writes this, and nothing else does. That makes it the
    /// witness for a question the other fields cannot answer: whether a
    /// start/finish crossing was accepted as a lap completion AT ALL. If this
    /// does not move across a crossing, the handler did not run, and no amount
    /// of reasoning about countLapFlag applies.
    /// </summary>
    public required double LapStartEt { get; init; }

    /// <summary>Lap counter, incremented by the same handler.</summary>
    public required int LapNumber { get; init; }

    /// <summary>
    /// Current sector index. Zeroed on lap completion, and also by
    /// <c>Slot_Reset</c> — which is the suspicion: a teleport zeroes this and
    /// then drops the car in a late sector, so the next start/finish crossing is
    /// not recognised as completing a lap. UNPROVEN.
    /// </summary>
    public required int Sector { get; init; }

    public required ulong CountLapFlagAddress { get; init; }
    public required ulong LapCountsNextAddress { get; init; }
    public required ulong PitFlagAddress { get; init; }

    public LapCounting Counting => (LapCounting)CountLapFlag;

    /// <summary>The lap currently being driven will produce a lap time.</summary>
    public bool CurrentLapIsTimed => CountLapFlag == (int)LapCounting.LapAndTime;

    /// <summary>
    /// Whether the NEXT lap will be timed, applying both halves of the rule the
    /// engine applies: the latch is promoted at the line, and then demoted again
    /// if <see cref="PitFlag"/> is still set. Predicting from the latch alone is
    /// exactly the mistake that hides the teleport problem.
    /// </summary>
    public bool NextLapWillBeTimed => LapCountsNext && !PitFlag;

    /// <summary>One sentence a driver can act on.</summary>
    public string Summary => (CurrentLapIsTimed, NextLapWillBeTimed) switch
    {
        (true, _) => "this lap is being timed",
        (false, true) => "this lap is not timed; the next one will be",
        (false, false) when PitFlag =>
            "this lap is not timed, and neither will the next one - pit flag is still set",
        _ => "this lap is not timed, and neither will the next one",
    };
}

/// <summary>Outcome of clearing the pit flag. Reports the before-value rather than assuming one.</summary>
public sealed record PitFlagClearResult
{
    public required ulong Address { get; init; }
    public required int Before { get; init; }
    public required int After { get; init; }

    /// <summary>False when the flag was already clear, or when writing was refused.</summary>
    public required bool Changed { get; init; }

    /// <summary>True when the write was attempted at all.</summary>
    public required bool Attempted { get; init; }

    public required string Message { get; init; }
}

/// <summary>
/// Reads lap-validity state, and clears the pit flag that keeps a teleported car
/// on an out-lap.
///
/// <para><b>Why this exists.</b> The engine recomputes <c>countLapFlag</c> at the
/// start/finish line and nowhere else:</para>
/// <code>
///   countLapFlag = lapCountsNext ? 2 : 1;      // promote
///   ...
///   if (!isRace &amp;&amp; pitFlag &amp;&amp; countLapFlag == 2)
///       countLapFlag = 1;                      // demote: the out-lap rule
/// </code>
/// <para>Slot_Reset -- the placement path -- sets <c>pitFlag = 1</c>. For a
/// locally-driven car that flag is EVENT-driven: only the pit-exit handler
/// clears it, and the geometry-driven recompute is gated on
/// <c>controlOwner == 2</c>, i.e. remote cars. A car placed on the racing line
/// never crosses the pit exit, so the flag can survive to the next crossing and
/// demote the lap that was just promoted -- costing a whole extra lap.</para>
///
/// <para><b>Why clearing it is a whole operation and not half of one.</b> The
/// pit-entry and pit-exit handlers reach the same byte as
/// <c>controller[0x2050]</c>, where <c>controller = container + 0x6F80</c> and
/// <c>0x6F80 + 0x2050 == 0x8FD0</c>. There is no second, controller-side copy to
/// leave inconsistent. Writing 0 is precisely what the pit-exit handler writes,
/// so a later genuine pit entry -- which refuses to run unless the byte is 0 --
/// still works.</para>
///
/// <para><b>What is NOT established.</b> That the flag is in fact still set by
/// the time you reach the line. Its neighbour <c>pitState</c> does clear with
/// distance unaided. So this deliberately reports
/// <see cref="PitFlagClearResult.Before"/> instead of asserting it: a run that
/// reports <c>before = 0</c> has disproved the diagnosis, and that is worth more
/// than a silent no-op.</para>
/// </summary>
public sealed class LapValidityController(GameSession session)
{
    private readonly GameSession _session = session;

    /// <summary>
    /// The sector index the engine must see to accept a start/finish crossing as
    /// a lap completion. Zero-based, so the third sector is 2.
    /// </summary>
    public const int LastSector = 2;

    public LapValidityState Read(int slotIndex)
    {
        var memory = _session.Memory;
        return new LapValidityState
        {
            SlotIndex = slotIndex,
            CountLapFlag = memory.ReadInt32(Address(slotIndex, "countLapFlag")),
            LapCountsNext = memory.ReadByte(Address(slotIndex, "lapCountsNext")) != 0,
            PitFlag = memory.ReadByte(Address(slotIndex, "pitFlag")) != 0,
            PitState = memory.ReadInt32(Address(slotIndex, "pitState")),
            LapStartEt = BitConverter.Int64BitsToDouble(
                (long)memory.ReadUInt64(Address(slotIndex, "lapStartEt"))),
            LapNumber = memory.ReadInt32(Address(slotIndex, "lapNumber")),
            Sector = memory.ReadInt32(Address(slotIndex, "sector")),
            CountLapFlagAddress = Address(slotIndex, "countLapFlag"),
            LapCountsNextAddress = Address(slotIndex, "lapCountsNext"),
            PitFlagAddress = Address(slotIndex, "pitFlag"),
        };
    }

    /// <summary>
    /// Clear the pit flag for a slot, so the next start/finish crossing is not
    /// demoted by the out-lap rule.
    ///
    /// One byte, read back and verified. Never touches
    /// <c>countLapFlag</c> itself: that would paper over the current lap while
    /// leaving the cause in place, and the cause is what recurs every placement.
    /// </summary>
    public PitFlagClearResult ClearPitFlag(int slotIndex)
    {
        var address = Address(slotIndex, "pitFlag");

        if (!_session.Memory.CanWrite)
        {
            throw new MemoryAccessException(
                "this session was attached read-only; reattach for writing");
        }

        int before;
        try
        {
            before = _session.Memory.ReadByte(address);
        }
        catch (MemoryAccessException ex)
        {
            return new PitFlagClearResult
            {
                Address = address,
                Before = -1,
                After = -1,
                Changed = false,
                Attempted = false,
                Message = $"could not read the pit flag: {ex.Message}",
            };
        }

        // Already clear is a RESULT, not a failure -- and an informative one. It
        // means the out-lap rule was not what cost the lap, and the next place to
        // look is the latch at +0x1CFF4.
        if (before == 0)
        {
            return new PitFlagClearResult
            {
                Address = address,
                Before = 0,
                After = 0,
                Changed = false,
                Attempted = false,
                Message = "pit flag was already clear - the out-lap rule was not holding this lap back",
            };
        }

        try
        {
            _session.Memory.WriteVerified(address, stackalloc byte[] { 0 });
        }
        catch (MemoryAccessException ex)
        {
            return new PitFlagClearResult
            {
                Address = address,
                Before = before,
                After = before,
                Changed = false,
                Attempted = true,
                Message = $"write failed: {ex.Message}",
            };
        }

        var after = _session.Memory.ReadByte(address);
        return new PitFlagClearResult
        {
            Address = address,
            Before = before,
            After = after,
            Changed = after != before,
            Attempted = true,
            Message = after == 0
                ? "pit flag cleared - the next start/finish crossing will be timed"
                : $"pit flag did not stay clear (reads {after}); something re-set it",
        };
    }

    /// <summary>
    /// Set the sector index.
    ///
    /// EXPERIMENTAL. The engine accepts a start/finish crossing as a lap
    /// completion only on the last-sector edge. <c>Slot_Reset</c> zeroes this,
    /// so a car placed past the sector-1 line arrives at the line still reading
    /// sector 0 and the crossing is discarded — no lap, no time, no promotion.
    ///
    /// Writing the sector the car was actually placed in restores the sequence.
    /// Unlike the pit flag this is NOT a byte the engine writes for the same
    /// purpose from another path, so it is a real intervention: keep it behind
    /// an explicit flag until it has been watched working.
    /// </summary>
    public RuleWriteResult SetSector(int slotIndex, int value)
    {
        if (!_session.Memory.CanWrite)
        {
            throw new MemoryAccessException(
                "this session was attached read-only; reattach for writing");
        }
        if (value is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "sector index is 0, 1 or 2");
        }

        var address = Address(slotIndex, "sector");
        var before = _session.Memory.ReadInt32(address);
        if (before == value)
        {
            return new RuleWriteResult("sector", address, before, value, Changed: false);
        }

        _session.Memory.WriteVerified(address, BitConverter.GetBytes(value));
        return new RuleWriteResult(
            "sector", address, before, _session.Memory.ReadInt32(address), Changed: true);
    }

    private ulong Address(int slotIndex, string field) =>
        _session.Offsets.Containers.FieldAddress(_session.ModuleBase, slotIndex, field);
}
