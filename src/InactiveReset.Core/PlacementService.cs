using System.Diagnostics;

namespace InactiveReset.Core;

public enum PlacementPhase
{
    Idle,
    WaitingForGarage,
    Gating,
    Armed,
    Settling,
    Restoring,
    WaitingToClear,
    Clear,
    LapValidity,
    Done,
    Failed,
}

public sealed record PlacementProgress(PlacementPhase Phase, string Message);

/// <summary>
/// What a placement WOULD do. Computed without writing anything, and reused
/// verbatim as the payload if the placement proceeds — so a preview cannot
/// describe a different placement from the one that happens.
/// </summary>
public sealed record PlacementPlan
{
    public required Checkpoint Checkpoint { get; init; }
    public required CalibrationProfile Calibration { get; init; }
    public required PlacementModel Model { get; init; }
    public required LiveState Live { get; init; }
    public required TargetPose Target { get; init; }
    public required SpotEntry ComputedEntry { get; init; }
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Feeding the computed entry back through the forward model must return the
    /// target. A non-zero residual means the inverse and forward models
    /// disagree, i.e. one of them is wrong.
    /// </summary>
    public required Vec3 ForwardRecheck { get; init; }

    public float ResidualMetres => MathF.Sqrt(
        MathF.Pow(ForwardRecheck.X - Target.RestPosition.X, 2)
        + MathF.Pow(ForwardRecheck.Y - Target.RestPosition.Y, 2)
        + MathF.Pow(ForwardRecheck.Z - Target.RestPosition.Z, 2));

    public bool CanProceed => Target.Valid && Live.Preconditions.Passed;
}

public sealed record PlacementOutcome
{
    public required bool Completed { get; init; }
    public required string Message { get; init; }
    public Vec3 Achieved { get; init; }
    public float HorizontalErrorMetres { get; init; }
    public float VerticalErrorMetres { get; init; }
    public ArrivalVerification? Arrival { get; init; }
    public TimeSpan TimeToClear { get; init; }

    /// <summary>
    /// Whether pit state actually cleared. False means we stopped waiting, NOT
    /// that it is safe to accelerate — the two must never be conflated.
    /// </summary>
    public bool PitStateCleared { get; init; }

    /// <summary>How far the car travelled while waiting. Pit state clears with distance.</summary>
    public float DistanceToClearMetres { get; init; }

    /// <summary>
    /// True when the pit-state wait was skipped because the pit limiter penalty
    /// is switched off. Distinguishes "we did not need to wait" from "it
    /// cleared", which look the same in the numbers but are not the same claim.
    /// </summary>
    public bool PitWaitSkipped { get; init; }

    /// <summary>
    /// What clearing the pit flag did, or null if it was not written. A
    /// secondary guard, NOT the reason a placed lap was being lost -- see
    /// <see cref="SectorWrite"/> for that.
    /// </summary>
    public PitFlagClearResult? LapValidity { get; init; }

    /// <summary>
    /// Why the pit flag was not cleared, when it was attempted and failed. Null
    /// when it succeeded, and null when it was never asked for.
    /// </summary>
    public string? PitFlagFailure { get; init; }

    /// <summary>
    /// What restoring the sector index did, or null if it was not written.
    /// This is the one that actually makes the first crossing count.
    /// </summary>
    public RuleWriteResult? SectorWrite { get; init; }

    /// <summary>
    /// Why the sector was not restored, when it was attempted and failed.
    ///
    /// <para>This field exists because "not requested" and "tried and failed"
    /// are different outcomes that used to look identical -- both left
    /// <see cref="SectorWrite"/> null. The CLI told them apart by inspecting its
    /// own command-line arguments, which is front-end knowledge standing in for
    /// a fact the outcome should have carried, and the web page could not tell
    /// them apart at all.</para>
    /// </summary>
    public string? SectorFailure { get; init; }

    /// <summary>
    /// What disabling the pit-speeding penalty did, or null if it was not
    /// written. Performed at arm time, before the car is placed.
    /// </summary>
    public RuleWriteResult? PitSpeedingPenalty { get; init; }

    /// <summary>Why the pit-speeding penalty could not be disabled, if it could not.</summary>
    public string? PitSpeedingPenaltyFailure { get; init; }
}

/// <summary>
/// The one implementation of plan → arm → write → settle → restore → clear.
///
/// CLI and UI both call this. When two front ends implement the same sequence
/// separately they drift, and in the predecessor they did — one of them kept an
/// older gate-write-restore ordering for a while.
/// </summary>
public sealed class PlacementService(GameSession session)
{
    private readonly GameSession _session = session;

    /// <summary>
    /// Compute everything without touching the game.
    ///
    /// The model comes from the calibration, which is required — there is no
    /// nominal fallback, so this preview necessarily uses the same constants the
    /// placement will.
    /// </summary>
    public PlacementPlan Plan(Checkpoint checkpoint, CalibrationProfile calibration)
    {
        if (!string.Equals(checkpoint.TrackName, calibration.TrackName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(checkpoint.VehicleName, calibration.VehicleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CalibrationException(
                $"calibration is for '{calibration.TrackName}' / '{calibration.VehicleName}' but the " +
                $"checkpoint is '{checkpoint.TrackName}' / '{checkpoint.VehicleName}'. " +
                "D and H are per track AND per vehicle.");
        }

        var model = calibration.ToPlacementModel(_session.Offsets.EngineModel);
        var live = new LiveStateReader(_session).Read(model);
        var target = Geometry.BuildTargetFromRecordedPose(checkpoint.Pose);

        var entry = PlacementMath.InvertToPitPosEntry(
            target.RestPosition, target.Yaw, live.Container, model, live.CurrentEntry);

        var destination = PlacementMath.PredictDriveDestination(entry, live.Container, model);

        return new PlacementPlan
        {
            Checkpoint = checkpoint,
            Calibration = calibration,
            Model = model,
            Live = live,
            Target = target,
            ComputedEntry = entry,
            Payload = PlacementMath.EncodeSpotEntry(entry),
            ForwardRecheck = PlacementMath.PredictRestPosition(destination, model),
        };
    }

    /// <summary>
    /// Perform the placement.
    ///
    /// The caller must press Drive when <see cref="PlacementPhase.Armed"/> is
    /// reported. The car MUST still be under Ai when the override goes in — if
    /// it is already under the player, Drive was pressed early and accepting
    /// that would measure a placement that never consumed the override.
    /// </summary>
    /// <param name="clearPitFlag">
    /// Clear the pit flag once the car is placed, so the first start/finish
    /// crossing is not demoted to an out-lap. A secondary guard: measurement
    /// shows the engine clears this itself within about half a minute, so this
    /// only matters if you reach the line before it does.
    /// </param>
    /// <param name="setSector">
    /// Sector index to restore after placing, or null to leave it alone.
    /// <see cref="LapValidityController.LastSector"/> by default, which is what
    /// makes the first crossing count at all. See
    /// <see cref="LapValidityController.SetSector"/> for why the value is a
    /// constant and why it is safe when the car is placed earlier in the lap.
    /// </param>
    public PlacementOutcome Place(
        PlacementPlan plan,
        Action<PlacementProgress>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default,
        bool clearPitFlag = true,
        int? setSector = LapValidityController.LastSector)
    {
        if (!plan.CanProceed)
        {
            var reasons = plan.Target.Failures.Concat(plan.Live.Preconditions.Failures);
            return Failed($"preconditions not met: {string.Join("; ", reasons)}", progress);
        }

        var reader = new LiveStateReader(_session);
        var slot = plan.Live.Container.SlotIndex;
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(120));

        Report(progress, PlacementPhase.Gating, "checking gates");
        if (reader.ReadControlOwner(slot) != 1)
        {
            return Failed("the car is not parked under Ai - return to the garage first", progress);
        }

        // Disable the pit-speeding penalty before the car is placed.
        //
        // A placement always leaves the car carrying pit state, so the penalty is
        // never wanted during practice -- there is no case where you want to be
        // given a stop/go for a position the tool put you in. Doing it here rather
        // than asking the driver to remember means it is true of every placement.
        //
        // Deliberately OUTSIDE the spot-entry transaction: those 24 bytes are
        // borrowed and always given back, whereas this is meant to stay off.
        var (penalty, penaltyFailure) = TryDisablePitSpeedingPenalty(progress);

        using var transaction = SpotWriteTransaction.Begin(
            _session, plan.Live.EntryAddress, plan.Live.EntryBytes);

        transaction.Write(plan.Payload);
        Report(progress, PlacementPhase.Armed, "ARMED - press Drive");

        // Wait for a FRESH Ai -> Player transition.
        var sawTransition = false;
        while (DateTime.UtcNow < deadline && !cancellation.IsCancellationRequested)
        {
            if (reader.ReadControlOwner(slot) == 0)
            {
                sawTransition = true;
                break;
            }
            Thread.Sleep(15);
        }

        if (!sawTransition)
        {
            transaction.Restore();
            return Failed("timed out waiting for Drive; the bytes were restored", progress);
        }

        // Sample arrival HERE, before any settle delay: afterwards the driver has
        // control and may legally move, which would make gear and motion state
        // meaningless as assertions about what the engine produced.
        var arrival = reader.ReadArrival(slot);

        Report(progress, PlacementPhase.Settling, "placed - letting the car settle");
        Thread.Sleep(400);

        Report(progress, PlacementPhase.Restoring, "restoring the original bytes");
        transaction.Restore();

        var achieved = ReadAchievedPosition(slot);
        var dx = achieved.X - plan.Target.RestPosition.X;
        var dy = achieved.Y - plan.Target.RestPosition.Y;
        var dz = achieved.Z - plan.Target.RestPosition.Z;

        // After a placement the car briefly carries pit state, and accelerating
        // while it is set earns a stop/go for pit-lane speeding.
        //
        // But ONLY if that penalty is enabled. When the pit limiter penalty is
        // switched off there is nothing to wait for, so waiting would be pure
        // ceremony -- and pit state clears with DISTANCE, not time, so it would
        // also strand a stationary driver until the timeout.
        var pitPenaltyOn = PitPenaltyEnabled();
        if (!pitPenaltyOn)
        {
            Report(progress, PlacementPhase.Clear,
                   "pit-speeding penalty is off - nothing to wait for");
            var (sectorEarly, sectorEarlyFailure) = TrySetSector(slot, setSector, progress);
            var (lapFixEarly, lapFixEarlyFailure) = TryClearPitFlag(slot, clearPitFlag, progress);
            Report(progress, PlacementPhase.Done, "done");
            return new PlacementOutcome
            {
                Completed = true,
                Message = "placed",
                Achieved = achieved,
                HorizontalErrorMetres = MathF.Sqrt(dx * dx + dz * dz),
                VerticalErrorMetres = dy,
                Arrival = ArrivalCheck.Verify(arrival),
                TimeToClear = TimeSpan.Zero,
                PitStateCleared = true,
                PitWaitSkipped = true,
                LapValidity = lapFixEarly,
                PitFlagFailure = lapFixEarlyFailure,
                SectorWrite = sectorEarly,
                SectorFailure = sectorEarlyFailure,
                PitSpeedingPenalty = penalty,
                PitSpeedingPenaltyFailure = penaltyFailure,
            };
        }

        Report(progress, PlacementPhase.WaitingToClear,
               "pit state is set - keep the pit limiter on until CLEAR");

        var stopwatch = Stopwatch.StartNew();
        var clearTimeout = TimeSpan.FromSeconds(120);
        var cleared = false;
        var startedAt = achieved;
        var distance = 0f;

        while (stopwatch.Elapsed < clearTimeout && !cancellation.IsCancellationRequested)
        {
            if (reader.ReadPitState(slot) == 0)
            {
                cleared = true;
                break;
            }
            var now = ReadAchievedPosition(slot);
            distance = MathF.Sqrt(
                MathF.Pow(now.X - startedAt.X, 2) + MathF.Pow(now.Z - startedAt.Z, 2));
            Thread.Sleep(50);
        }
        stopwatch.Stop();

        // Never claim CLEAR on a timeout. Giving up is not the same as the pit
        // state having cleared, and reporting it as such would tell the driver
        // it is safe to accelerate when we simply stopped looking.
        if (cleared)
        {
            Report(progress, PlacementPhase.Clear, "CLEAR - safe to accelerate");
        }
        else
        {
            Report(progress, PlacementPhase.WaitingToClear,
                   $"gave up waiting after {clearTimeout.TotalSeconds:F0} s and {distance:F0} m - " +
                   "pit state is STILL SET. Accelerating now may earn a stop/go.");
        }

        // Deliberately AFTER the pit-state wait. That wait is about the speeding
        // penalty and is well exercised; clearing the flag first would change the
        // conditions it observes for no benefit, since the demotion this fixes
        // does not happen until the car reaches the start/finish line.
        var (sector, sectorFailure) = TrySetSector(slot, setSector, progress);
        var (lapFix, lapFixFailure) = TryClearPitFlag(slot, clearPitFlag, progress);

        Report(progress, PlacementPhase.Done, "done");
        return new PlacementOutcome
        {
            Completed = true,
            Message = "placed",
            Achieved = achieved,
            HorizontalErrorMetres = MathF.Sqrt(dx * dx + dz * dz),
            VerticalErrorMetres = dy,
            Arrival = ArrivalCheck.Verify(arrival),
            TimeToClear = stopwatch.Elapsed,
            PitStateCleared = cleared,
            DistanceToClearMetres = distance,
            LapValidity = lapFix,
            PitFlagFailure = lapFixFailure,
            SectorWrite = sector,
            SectorFailure = sectorFailure,
            PitSpeedingPenalty = penalty,
            PitSpeedingPenaltyFailure = penaltyFailure,
        };
    }


    /// <summary>
    /// Restore the sector index so the next start/finish crossing is accepted as
    /// a lap completion.
    ///
    /// This is the fix. <c>Slot_Reset</c> zeroes the sector because it assumes
    /// you are leaving the pits at the start of a lap; a placement then breaks
    /// that assumption by putting the car mid-lap. The engine spends the rest of
    /// the lap waiting for a sector line that is already behind the car, and
    /// discards the start/finish crossing entirely -- no lap, no time, no
    /// promotion. Measured, not inferred: see docs/LAP_VALIDITY.md.
    ///
    /// Like the pit-flag clear, never allowed to fail a placement that already
    /// succeeded.
    /// </summary>
    private (RuleWriteResult? Result, string? Failure) TrySetSector(
        int slot, int? sector, Action<PlacementProgress>? progress)
    {
        if (sector is null)
        {
            return (null, null);
        }

        try
        {
            var result = new LapValidityController(_session).SetSector(slot, sector.Value);
            Report(progress, PlacementPhase.LapValidity,
                   result.Changed
                       ? $"sector {result.Before} -> {result.After}; the next start/finish " +
                         "crossing will count as a lap"
                       : $"sector already {result.After}");
            return (result, null);
        }
        catch (Exception ex)
            when (ex is MemoryAccessException or OffsetProfileException or StaleOffsetException)
        {
            Report(progress, PlacementPhase.LapValidity,
                   $"sector NOT restored - {ex.Message}. The first crossing will be ignored.");
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Clear the pit flag so the next start/finish crossing is not demoted to an
    /// out-lap.
    ///
    /// Never allowed to fail a placement that already succeeded. A profile from
    /// before this field was derived has no <c>pitFlag</c> entry and will throw
    /// here; that is a missing improvement, not a broken placement, and the
    /// difference must survive into what the driver is told.
    /// </summary>
    private (PitFlagClearResult? Result, string? Failure) TryClearPitFlag(
        int slot, bool enabled, Action<PlacementProgress>? progress)
    {
        if (!enabled)
        {
            return (null, null);
        }

        try
        {
            var result = new LapValidityController(_session).ClearPitFlag(slot);
            Report(progress, PlacementPhase.LapValidity, result.Message);
            return (result, null);
        }
        catch (Exception ex)
            when (ex is MemoryAccessException or OffsetProfileException or StaleOffsetException)
        {
            Report(progress, PlacementPhase.LapValidity,
                   $"lap validity NOT applied - {ex.Message}");
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Switch off the stop/go for pit-lane speeding, at arm time.
    ///
    /// Never allowed to fail a placement. A profile that cannot reach this field
    /// still produces a perfectly good placement -- the driver simply has to keep
    /// the pit limiter on until the pit state clears, which is what the wait
    /// further down exists for. The difference has to survive into what they are
    /// told, so the reason is returned rather than swallowed.
    /// </summary>
    private (RuleWriteResult? Result, string? Failure) TryDisablePitSpeedingPenalty(
        Action<PlacementProgress>? progress)
    {
        try
        {
            var result = new RulesController(_session).SetPitSpeedingPenalty(enabled: false);
            Report(progress, PlacementPhase.Gating,
                   result.Changed
                       ? $"pit-speeding penalty {result.Before} -> {result.After}"
                       : "pit-speeding penalty already off");
            return (result, null);
        }
        catch (Exception ex)
            when (ex is MemoryAccessException or OffsetProfileException
                     or StaleOffsetException or InvalidOperationException)
        {
            Report(progress, PlacementPhase.Gating,
                   $"pit-speeding penalty NOT disabled - {ex.Message}");
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Is the pit-lane speeding penalty currently active?
    ///
    /// Read live rather than assumed: this decides whether the driver is asked
    /// to wait at all, and being wrong in one direction wastes two minutes
    /// while being wrong in the other earns a stop/go.
    /// </summary>
    private bool PitPenaltyEnabled()
    {
        try
        {
            var field = _session.Offsets.Rules.FlagRules;
            var address = _session.ModuleBase + field.Rva.Value;
            var value = field.Type == FieldType.Byte
                ? _session.Memory.ReadByte(address)
                : _session.Memory.ReadInt32(address);
            return value != 0;
        }
        catch (MemoryAccessException)
        {
            // If it cannot be read, assume the penalty is live. The cautious
            // direction is the one that does not invite a stop/go.
            return true;
        }
    }

    private Vec3 ReadAchievedPosition(int slot)
    {
        var address = _session.Offsets.Containers.FieldAddress(
            _session.ModuleBase, slot, "vehCachedPose");
        return new Vec3(
            _session.Memory.ReadSingle(address),
            _session.Memory.ReadSingle(address + 4),
            _session.Memory.ReadSingle(address + 8));
    }

    private static PlacementOutcome Failed(string message, Action<PlacementProgress>? progress)
    {
        progress?.Invoke(new PlacementProgress(PlacementPhase.Failed, message));
        return new PlacementOutcome { Completed = false, Message = message };
    }

    /// <summary>
    /// Invoked SYNCHRONOUSLY and in order, deliberately.
    ///
    /// This was originally an <c>IProgress&lt;T&gt;</c>, which captures a
    /// SynchronizationContext and posts callbacks — on a console app, to the
    /// thread pool. Reports then arrive out of order, and "CLEAR" was observed
    /// printing after "done". These phases drive AUDIO CUES the driver acts on,
    /// so an out-of-order CLEAR tone could tell someone to accelerate while
    /// still carrying pit state. Ordering is a correctness requirement here, not
    /// a cosmetic one.
    ///
    /// A UI that needs to marshal to its own thread should do so inside its own
    /// callback, where it can preserve order explicitly.
    /// </summary>
    private static void Report(Action<PlacementProgress>? progress, PlacementPhase phase, string message) =>
        progress?.Invoke(new PlacementProgress(phase, message));
}
