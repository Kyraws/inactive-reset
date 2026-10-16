using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>Bounded causal test of the discovered motion block; never a production reset.</summary>
public static class VelocityProbe
{
    public static byte[] BuildPayload(Vec3 v, RecordedPose p) => RollingMotion.BuildPayload(v, p);
    public static void RequireStationary(ArrivalState m, TelemetrySnapshot s, bool placed = false) => RollingMotion.RequireStationary(m, s, placed);
    public static void RequireIdentity(Checkpoint c, TelemetrySnapshot s) => RollingMotion.RequireIdentity(c, s);
    public static void RequirePaused(JsonObject s, double a, double b) => RollingMotion.RequirePaused(s, a, b);
    public static void RequirePhysicsPaused(JsonObject s, double a, double b, bool p, bool placed = false) => RollingMotion.RequirePhysicsPaused(s, a, b, p, placed);

    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4 || args[2..].Any(a => a is not ("--arm" or "--bodies" or "--wheels" or "--paused-wheels" or "--auto-wheels" or "--placement-wheels" or "--gear-wheels")) ||
            args[2..].Distinct().Count() != args.Length - 2 ||
            args.Count(a => a is "--bodies" or "--wheels" or "--paused-wheels" or "--auto-wheels" or "--placement-wheels" or "--gear-wheels") > 1 ||
            args[0].Length != 64 || !args[0].All(Uri.IsHexDigit))
            throw new ArgumentException("velocity-probe <full-build-sha256> <checkpoint.json> [--bodies | --wheels | --paused-wheels | --auto-wheels | --placement-wheels | --gear-wheels] [--arm]");
        var armed = args.Contains("--arm");
        var gearMode = args.Contains("--gear-wheels");
        var placementExit = args.Contains("--placement-wheels") || gearMode;
        var automatic = args.Contains("--auto-wheels") || placementExit;
        var pausedMode = args.Contains("--paused-wheels") || (automatic && armed);
        var wheelMode = args.Contains("--wheels") || pausedMode || automatic;
        var bodyMode = args.Contains("--bodies") || wheelMode;
        WheelRotationPlan? wheelPlan = null;
        GearMotionPlan? gearPlan = null;
        BodyVelocityPlan? bodyPlan = null;
        var checkpoint = Checkpoint.Load(args[1]);
        var velocity = checkpoint.LocalVelocityMetresPerSecond
            ?? throw new GateException("This checkpoint has no captured velocity; capture a new moving checkpoint.");
        var offsets = Environment.GetEnvironmentVariable("INACTIVE_RESET_OFFSETS") ?? "offsets";
        var data = Environment.GetEnvironmentVariable("INACTIVE_RESET_DATA") ?? "data";
        var menu = new LmuSessionClient(dataDirectory: data);
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        using var session = GameSession.Attach(offsets, forWriting: armed);
        if (!string.Equals(session.ExecutableSha256, args[0], StringComparison.OrdinalIgnoreCase))
            throw new GateException("The full executable hash does not match the requested experiment.");
        using var shared = new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets, "shared-memory.json")));
        using var wheelReader = wheelMode ? new WheelMotionReader(offsets) : null;
        var reader = new LiveStateReader(session);
        var slot = reader.ResolveSlotIndex();
        if (slot is < 0 or >= 104) throw new GateException("Invalid player slot.");
        var field = session.Offsets.Containers.Field("vehMotionState");
        if (field.Type != "float[9]" || field.Base != OffsetBase.Vehicle)
            throw new GateException("The discovered motion block has an unsupported layout.");
        var address = session.Offsets.Containers.FieldAddress(session.ModuleBase, slot, "vehMotionState");
        var tableAddress = session.Resolve(session.Offsets.SpotTable.IndexedDestination ??
            session.Offsets.SpotTable.PitPosTable, "placement table");
        var table = session.Memory.ReadUInt64(tableAddress);
        if (table == 0) throw new GateException("No active placement table.");
        using var automaticPause = automatic ? PhysicsPause.Prepare(session, () =>
            !session.Process.HasExited && reader.ResolveSlotIndex() == slot &&
            session.Memory.ReadUInt64(tableAddress) == table && reader.ReadControlOwner(slot) == 0) : null;
        var startingPitState = reader.ReadPitState(slot);
        var firstTime = ReadChecked().ElapsedTime;
        var timer = Stopwatch.StartNew();
        void CheckPause()
        {
            menu.RequirePracticeAsync().GetAwaiter().GetResult();
            var state = menu.GameStateAsync().GetAwaiter().GetResult();
            if (automatic) RequirePhysicsPaused(state, firstTime, ReadChecked().ElapsedTime, automaticPause!.IsPaused, placementExit);
            else RequirePaused(state, firstTime, ReadChecked().ElapsedTime);
            if (wheelReader!.Read().ElapsedTime != firstTime)
                throw new GateException("Wheel telemetry time changed while paused.");
        }
        TelemetrySnapshot ReadChecked()
        {
            if (session.Process.HasExited || reader.ResolveSlotIndex() != slot ||
                session.Memory.ReadUInt64(tableAddress) != table || reader.ReadControlOwner(slot) != 0)
                throw new GateException("The active player session changed; experiment stopped.");
            bodyPlan?.RequireCurrent();
            wheelPlan?.RequireCurrent();
            gearPlan?.RequireCurrent();
            var snapshot = shared.Read() ?? throw new GateException("Player telemetry is unavailable.");
            RequireIdentity(checkpoint, snapshot);
            return snapshot;
        }
        // Require sustained rest, not a single frame sampled during a transition.
        TelemetrySnapshot before;
        do
        {
            if (pausedMode && !automatic) CheckPause();
            before = ReadChecked();
            RequireStationary(reader.ReadArrival(slot), before, placementExit);
            if (wheelReader is not null) WheelRotationPlan.RequireReleased(wheelReader.Read());
            Thread.Sleep(20);
        } while (timer.ElapsedMilliseconds < 500);
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        before = ReadChecked();
        RequireStationary(reader.ReadArrival(slot), before, placementExit);
        if (wheelReader is not null) WheelRotationPlan.RequireReleased(wheelReader.Read());
        if (automatic && armed)
        {
            if (before.ElapsedTime <= firstTime) throw new GateException("SDK time is not advancing before automatic pause.");
            automaticPause!.Begin();
            Thread.Sleep(100);
            before = ReadChecked();
            firstTime = before.ElapsedTime;
        }
        if (pausedMode) CheckPause();
        else if (before.ElapsedTime <= firstTime)
            throw new GateException("Telemetry is paused or stale; run the experiment in an active session.");
        var payload = BuildPayload(velocity, before.Pose);
        if (bodyMode) bodyPlan = BodyVelocityPlan.Prepare(session, slot, velocity, before.Pose);
        if (wheelMode) wheelPlan = WheelRotationPlan.Prepare(session, slot, bodyPlan!, velocity);
        if (gearMode) gearPlan = GearMotionPlan.Prepare(session, slot, bodyPlan!, wheelPlan!, checkpoint.Gear);
        if (pausedMode)
        {
            // Confirm actual physics values are stable as well as the API pause flag.
            var stable = JsonSerializer.Serialize(new { bodies = bodyPlan!.ReadVelocities(), wheels = wheelPlan!.ReadState(), gear = gearPlan?.ReadState() });
            for (var i = 0; i < 10; i++)
            {
                Thread.Sleep(20);
                CheckPause();
                if (stable != JsonSerializer.Serialize(new { bodies = bodyPlan.ReadVelocities(), wheels = wheelPlan.ReadState(), gear = gearPlan?.ReadState() }))
                    throw new GateException("Physics state changed during the paused dry run.");
            }
        }
        Console.WriteLine($"Build {session.ExecutableSha256}; slot {slot}; motion block 0x{address:X}");
        Console.WriteLine($"Captured speed {checkpoint.SpeedKmh:F2} km/h; gear {checkpoint.Gear}. Gear remains unchanged.");
        if (wheelPlan is not null) Console.WriteLine($"Four wheels: rates [{string.Join(", ", wheelPlan.Rates.Select(v => v.ToString("F3")))}] rad/s; body and wheel batch totals {bodyPlan!.BytesToWrite + wheelPlan.BytesToWrite} bytes. Batch may partially complete; no tyre-contact restoration.");
        else if (bodyPlan is not null) Console.WriteLine($"Linked bodies: {bodyPlan.Bodies.Length}; one 24-byte double-velocity write per body ({bodyPlan.BytesToWrite} bytes total). Batch is not atomic; gear and wheel rotation are unchanged.");
        if (gearPlan is not null) Console.WriteLine($"Gear {gearPlan.Requested.Gear}, engine {gearPlan.Requested.EngineRpm:F0} RPM, clutch {gearPlan.Requested.Clutch:F1}; adds {gearPlan.BytesToWrite} verified gearbox/engine bytes.");
        if (!armed)
        {
            if (gearMode)
            {
                Console.WriteLine($"Dry run: no writes. --gear-wheels --arm requests {bodyPlan!.BytesToWrite + wheelPlan!.BytesToWrite + gearPlan!.BytesToWrite + 2} total bytes, including pause/resume and captured gear with synchronized engine/clutch state.");
                return 0;
            }
            Console.WriteLine(automatic
                ? "Dry run: no writes. --auto-wheels --arm pauses physics, applies and verifies 512 motion bytes, then resumes physics automatically (two one-byte pause writes)."
                : pausedMode
                ? "Dry run: no game writes. --paused-wheels --arm applies the 512-byte batch while paused and leaves the game paused."
                : wheelPlan is not null
                ? "Dry run: no game writes. --wheels --arm applies linked body velocity and wheel spin once, then observes for two seconds."
                : bodyPlan is null
                ? "Dry run: no game writes. --arm performs ONE 24-byte cached velocity write, then observes for two seconds."
                : "Dry run: no game writes. --bodies --arm shifts each linked body once, then observes for two seconds.");
            return 0;
        }

        var outputDirectory = Path.Combine("artifacts", "velocity-probe");
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
        var samples = new List<object>();
        string? error = null;
        var writeVerified = false;
        var motionAttempted = false;
        var automaticResumeVerified = false;
        timer.Restart();
        try
        {
            // This tests a candidate state block, not an established physics setter.
            // Never loop writes or restore stale velocity into a car that has begun moving.
            before = ReadChecked();
            RequireStationary(reader.ReadArrival(slot), before, placementExit);
            if (wheelReader is not null) WheelRotationPlan.RequireReleased(wheelReader.Read());
            wheelPlan?.Preflight(); gearPlan?.Preflight();
            if (bodyPlan is not null)
            {
                motionAttempted = true;
                bodyPlan.Apply(pausedMode ? CheckPause : null);
                wheelPlan?.Apply(pausedMode ? CheckPause : null);
                gearPlan?.Apply(CheckPause);
                if (pausedMode)
                {
                    CheckPause();
                    bodyPlan.VerifyApplied();
                    wheelPlan!.VerifyApplied(); gearPlan?.VerifyApplied();
                }
            }
            else
            {
                session.Memory.RequireWritableDataPage(address, payload.Length);
                session.Memory.WriteVerified(address, payload);
            }
            writeVerified = true;
        }
        // A guard/SDK/API failure can happen after an earlier write; always preserve the partial-batch record.
        catch (Exception ex)
        { error = "Write/readback failed: " + ex.Message; }
        var writeDurationMilliseconds = timer.Elapsed.TotalMilliseconds;
        var observationDeadline = pausedMode ? timer.ElapsedMilliseconds + 2000 : 2000;
        try
        {
            void Sample(bool observingPaused)
            {
                if (observingPaused)
                {
                    CheckPause();
                    if (writeVerified) { bodyPlan!.VerifyApplied(); wheelPlan!.VerifyApplied(); gearPlan?.VerifyApplied(); }
                }
                var snapshot = ReadChecked();
                var motion = reader.ReadArrival(slot);
                samples.Add(new { phase = observingPaused ? "paused" : "driving", milliseconds = timer.Elapsed.TotalMilliseconds, snapshot.ElapsedTime,
                    snapshot.SpeedKmh, snapshot.LocalVelocityMetresPerSecond, snapshot.Pose.Position,
                    snapshot.Gear, motion.Valid, motion.PitState, motion.MotionState, bodyWorld = bodyPlan?.ReadVelocities(),
                    wheelTelemetry = wheelReader?.Read(), wheelState = wheelPlan?.ReadState(), gearState = gearPlan?.ReadState() });
            }
            while (timer.ElapsedMilliseconds < observationDeadline)
            { Sample(pausedMode); Thread.Sleep(20); }
            if (automatic)
            {
                if (!writeVerified && motionAttempted)
                    automaticPause!.LeavePaused();
                else
                {
                    automaticPause!.Dispose();
                    var until = timer.ElapsedMilliseconds + 2000;
                    while (timer.ElapsedMilliseconds < until)
                    {
                        Sample(false);
                        if (ReadChecked().ElapsedTime > firstTime && !automaticPause.IsPaused) automaticResumeVerified = true;
                        Thread.Sleep(20);
                    }
                    if (!automaticResumeVerified) throw new GateException("Automatic Resume was not observed in SDK time.");
                }
            }
        }
        catch (Exception ex) when (ex is GateException or MemoryAccessException or SharedMemoryException or CheckpointException)
        {
            if (automatic && motionAttempted) automaticPause!.LeavePaused();
            error = (error is null ? "" : error + "; ") + ex.Message;
        }
        finally
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                session.ExecutableSha256, checkpoint.TrackName, checkpoint.VehicleName,
                checkpoint.SourcePath, requestedSpeedKmh = checkpoint.SpeedKmh, localVelocity = velocity,
                before, startingPitState, writeVerified, error, samples, pausedMode, automatic, placementExit, automaticResumeVerified, writeDurationMilliseconds,
                writeTarget = gearMode ? "linked rigid-body velocity, wheel rotation and gearbox/engine state" : wheelMode ? "linked rigid-body linear velocity and wheel rotation" : bodyMode ? "linked rigid-body linear velocity" : "cached motion block",
                bodies = bodyPlan?.Bodies.Select(p => $"0x{p:X}").ToArray(),
                verifiedBodyWrites = bodyPlan?.WrittenBodies,
                verifiedWheelFields = wheelPlan?.WrittenFields,
                requestedWheelRates = wheelPlan?.Rates,
                gearTarget = gearPlan?.Requested, verifiedGearFields = gearPlan?.WrittenFields,
                requestedBytes = (bodyPlan?.BytesToWrite ?? payload.Length) + (wheelPlan?.BytesToWrite ?? 0) + (gearPlan?.BytesToWrite ?? 0) + (automatic ? 2 : 0),
                interpretation = gearMode
                    ? "Experimental body/wheel/gear/engine batch. Review resumed speed, gear, RPM, position and elapsed time together. Tyre-contact state is not restored; no broader compatibility claim."
                    : "Candidate velocity write only. Review elapsed-time progression, speed and position together. No drivetrain or contact-state restoration; no production compatibility claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Observation file: {path}. This is experimental evidence, not a verified rolling reset.");
        if (pausedMode && !automatic) Console.WriteLine("Game left paused. Written motion may take effect on Resume; do not treat frozen SDK speed as a failed write.");
        if (error is not null) Console.Error.WriteLine(error);
        return error is null ? 0 : 4;
    }
}
