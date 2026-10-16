using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

public sealed record RollingMotionResult(double RequestedSpeedKmh, double ObservedSpeedKmh,
    int? RequestedGear = null, int? ObservedGear = null, double? ObservedEngineRpm = null);

/// <summary>Opt-in researched rolling start. Speed may decay naturally after Resume.</summary>
public static class RollingMotion
{
    public static byte[] BuildPayload(Vec3 localVelocity, RecordedPose pose)
    {
        var speed = MotionCapture.SpeedKmh(localVelocity);
        if (speed < 0.5)
            throw new GateException("Use a moving checkpoint captured at 0.5 km/h or faster.");
        if (!Geometry.BuildTargetFromRecordedPose(pose).Valid)
            throw new GateException("The live orientation is not usable.");
        // SDK convention: world components are the dot products of matrix rows with local velocity.
        float Dot(Vec3 row) => row.X * localVelocity.X + row.Y * localVelocity.Y + row.Z * localVelocity.Z;
        var world = new Vec3(Dot(pose.Row0), Dot(pose.Row1), Dot(pose.Row2));
        MotionCapture.Validate(world);
        var values = new[] { world.X, world.Y, world.Z, localVelocity.X, localVelocity.Y, localVelocity.Z };
        var payload = new byte[24];
        for (var i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(payload, i * 4);
        return payload;
    }

    public static void RequireStationary(ArrivalState motion, TelemetrySnapshot snapshot, bool placementExit = false)
    {
        if (!motion.Valid || motion.ControlOwner != 0 || motion.Gear != 0 || (motion.PitState != 0 && !(placementExit && motion.PitState == 5)) ||
            motion.MotionState.Length != 9 || motion.MotionState.Any(v => !float.IsFinite(v)) ||
            motion.LargestMotionComponent > 0.05f || snapshot.Gear != 0 ||
            snapshot.SpeedKmh is not { } speed || speed > 0.18)
            throw new GateException("Stop on track in neutral, with pit state clear, before probing velocity. "
                + $"Readback: valid={motion.Valid}, owner={motion.ControlOwner}, gear={motion.Gear}, "
                + $"pitState={motion.PitState}, motion=[{string.Join(", ", motion.MotionState.Select(v => v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)))}], "
                + $"SDK gear={snapshot.Gear}, speed={snapshot.SpeedKmh:F6} km/h.");
    }

    public static void RequireIdentity(Checkpoint checkpoint, TelemetrySnapshot snapshot)
    {
        new SessionIdentity(snapshot.TrackName, snapshot.VehicleName).RequireMatches(checkpoint);
        RequireCaptureVehicle(checkpoint, snapshot.VehicleName);
    }

    public static void RequirePaused(JsonObject state, double expectedTime, double actualTime)
    {
        if (state["MultiStintState"]?.GetValue<string>() != "PAUSED")
            throw new GateException("Keep the normal pause menu open until the paused probe finishes.");
        RequireFrozenPlayer(state, expectedTime, actualTime);
    }

    public static void RequirePhysicsPaused(JsonObject state, double expectedTime, double actualTime, bool physicsPaused, bool placementExit = false)
    {
        if (!physicsPaused || state["MultiStintState"]?.GetValue<string>() != "DRIVING")
            throw new GateException("Automatic physics pause changed or driver opened the pause menu.");
        RequireFrozenPlayer(state, expectedTime, actualTime, placementExit);
    }

    private static void RequireFrozenPlayer(JsonObject state, double expectedTime, double actualTime, bool placementExit = false)
    {
        if (!double.IsFinite(expectedTime) || expectedTime < 0 || !double.IsFinite(actualTime) || actualTime != expectedTime ||
            state["playerVehicleLoaded"]?.GetValue<bool>() != true ||
            state["inControlOfVehicle"]?.GetValue<bool>() != true ||
            state["inMonitor"]?.GetValue<bool>() != false ||
            state["isReplayActive"]?.GetValue<bool>() != false ||
            (state["PitState"]?.GetValue<string>() != "NONE" && !(placementExit && state["PitState"]?.GetValue<string>() == "EXITING")))
            throw new GateException("Player/session state or SDK time changed during physics pause.");
    }

    public static bool EligibleCapture(Vec3? velocity) => velocity is { IsFinite: true } v &&
        MotionCapture.SpeedKmh(v) >= 0.5 && Math.Abs(v.X) <= 0.05 &&
        Math.Abs(v.Y) <= Math.Max(0.05, Math.Abs(v.Z) * 0.005);

    public static void RequireEligible(Checkpoint checkpoint, bool supported, SessionIdentity identity)
    {
        if (!supported || !EligibleCapture(checkpoint.LocalVelocityMetresPerSecond))
            throw new GateException("Rolling start requires the supported game build and a straight, level moving capture (at least 0.5 km/h).");
        identity.RequireMatches(checkpoint);
        RequireCaptureVehicle(checkpoint, identity.VehicleName);
    }

    private static void RequireCaptureVehicle(Checkpoint checkpoint, string loadedVehicle)
    {
        if (!string.Equals(loadedVehicle, checkpoint.VehicleName, StringComparison.OrdinalIgnoreCase))
            throw new GateException($"Checkpoint captured in {checkpoint.VehicleName}; currently driving {loadedVehicle}. Speed & gear require the capture car. Load that car or use Standing teleport.");
    }

    internal static void ApplyWhilePaused(PhysicsPause pause, Action prepare, Action apply, Action verify)
    {
        using (pause)
        {
            pause.Begin();
            var started = false;
            try
            {
                prepare();
                started = true;
                apply();
                verify();
            }
            catch
            {
                if (started) pause.LeavePaused();
                throw;
            }
        }
    }

    internal static bool DriverReady(JsonObject state) =>
        state["MultiStintState"]?.GetValue<string>() == "DRIVING" &&
        state["playerVehicleLoaded"]?.GetValue<bool>() == true &&
        state["inControlOfVehicle"]?.GetValue<bool>() == true &&
        state["inMonitor"]?.GetValue<bool>() == false &&
        state["isReplayActive"]?.GetValue<bool>() == false &&
        state["PitState"]?.GetValue<string>() is "NONE" or "EXITING";

    internal static void WaitForHandoff(Func<bool> ready, Func<long> milliseconds, Action wait,
        CancellationToken cancellation)
    {
        var start = milliseconds();
        long? stableSince = null;
        while (milliseconds() - start < 10_000)
        {
            cancellation.ThrowIfCancellationRequested();
            var now = milliseconds();
            if (ready()) stableSince ??= now;
            else stableSince = null;
            // Owner-observed post-teleport control delay is about three seconds.
            if (now - start >= 3_000 && stableSince is { } stable && now - stable >= 500) return;
            wait();
        }
        throw new GateException("Speed was not restored: driving control, neutral and released pedals did not stay ready after placement.");
    }

    public static void WaitForGarageHandoff(GameSession game, Checkpoint checkpoint, string offsets,
        LmuSessionClient menu, Func<bool> sameContext, Action<string> progress, CancellationToken cancellation)
    {
        using var sdk = new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets, "shared-memory.json")));
        using var wheels = new WheelMotionReader(offsets);
        var live = new LiveStateReader(game);
        var slot = live.ResolveSlotIndex();
        var timer = Stopwatch.StartNew();
        string? blocker = null;
        progress("Waiting for driving control and released brakes after Drive; keep neutral and pedals released.");
        try
        {
            WaitForHandoff(() =>
            {
                if (!sameContext() || live.ResolveSlotIndex() != slot) throw new GateException("Garage handoff session changed.");
                var sample = sdk.Read() ?? throw new GateException("Player telemetry unavailable.");
                RequireIdentity(checkpoint, sample);
                if (live.ReadControlOwner(slot) != 0 || sample.Gear != 0 || !DriverReady(menu.GameStateAsync(cancellation).GetAwaiter().GetResult()))
                    return false;
                try { WheelRotationPlan.RequirePedalsReleased(wheels.Read()); return true; }
                catch (GateException ex) { blocker = ex.Message; return false; }
            }, () => timer.ElapsedMilliseconds, () => Thread.Sleep(50), cancellation);
        }
        catch (GateException ex) when (blocker is not null)
        { throw new GateException(ex.Message + " " + blocker); }
    }

    public static RollingMotionResult Restore(GameSession game, Checkpoint checkpoint, string offsets,
        LmuSessionClient menu, Func<bool> sameContext, Action<string> progress, CancellationToken cancellation)
    {
        using var sdk = new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets, "shared-memory.json")));
        using var wheels = new WheelMotionReader(offsets);
        var live = new LiveStateReader(game);
        var slot = live.ResolveSlotIndex();
        if (slot is < 0 or >= 104) throw new GateException("Invalid rolling-start player slot.");
        var first = sdk.Read() ?? throw new GateException("Player telemetry unavailable.");
        var layout = game.RequireMotion();
        RequireEligible(checkpoint, true, new(first.TrackName, first.VehicleName));
        var vehicle = game.Offsets.Containers.ContainerAddress(game.ModuleBase, slot) + game.Offsets.Containers.VehicleDelta;
        var physics = game.Memory.ReadUInt64(vehicle + layout.VehiclePhysics);
        if (physics != 0) layout.ReadEngineSetter(game,game.Memory.ReadUInt64(physics + layout.PhysicsEngine));
        bool Same() => sameContext() && !game.Process.HasExited && live.ResolveSlotIndex() == slot &&
            live.ReadControlOwner(slot) == 0 && physics != 0 && game.Memory.ReadUInt64(vehicle + layout.VehiclePhysics) == physics;
        TelemetrySnapshot Read()
        {
            if (!Same()) throw new GateException("Rolling-start session changed.");
            var snapshot = sdk.Read() ?? throw new GateException("Player telemetry unavailable.");
            RequireIdentity(checkpoint, snapshot);
            return snapshot;
        }
        using var pause = PhysicsPause.Prepare(game, Same);
        var timer = Stopwatch.StartNew();
        menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
        progress("Waiting for driving control; keep neutral and both pedals released.");
        string? lastBlocker = null;
        try
        {
            WaitForHandoff(() =>
            {
                var snapshot = Read();
                var state = menu.GameStateAsync(cancellation).GetAwaiter().GetResult();
                if (!DriverReady(state))
                {
                    lastBlocker = "LMU has not reported active driving control.";
                    return false;
                }
                try
                {
                    RequireStationary(live.ReadArrival(slot), snapshot, placementExit: true);
                    WheelRotationPlan.RequireReleased(wheels.Read());
                    return true;
                }
                catch (GateException ex) { lastBlocker = ex.Message; return false; }
            }, () => timer.ElapsedMilliseconds, () => Thread.Sleep(50), cancellation);
        }
        catch (GateException ex) when (lastBlocker is not null)
        {
            throw new GateException(ex.Message + " " + lastBlocker);
        }
        menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
        if (Read().ElapsedTime <= first.ElapsedTime) throw new GateException("Game is paused or telemetry is stale before rolling start.");
        BodyVelocityPlan? bodies = null;
        WheelRotationPlan? rotation = null;
        GearMotionPlan? gears = null;
        double frozenTime = 0;
        void CheckBoundary()
        {
            cancellation.ThrowIfCancellationRequested();
            if (!pause.IsPaused || Read().ElapsedTime != frozenTime || wheels.Read().ElapsedTime != frozenTime)
                throw new GateException("Physics pause or telemetry time changed during rolling start.");
        }
        progress($"Restoring captured speed and gear {checkpoint.Gear}; keep pedals released.");
        try
        {
            ApplyWhilePaused(pause, () =>
            {
                Thread.Sleep(100);
                var before = Read(); frozenTime = before.ElapsedTime;
                RequirePhysicsPaused(menu.GameStateAsync(cancellation).GetAwaiter().GetResult(), frozenTime, before.ElapsedTime, pause.IsPaused, placementExit: true);
                RequireStationary(live.ReadArrival(slot), before, placementExit: true);
                WheelRotationPlan.RequireReleased(wheels.Read());
                bodies = BodyVelocityPlan.Prepare(game, slot, checkpoint.LocalVelocityMetresPerSecond!.Value, before.Pose);
                rotation = WheelRotationPlan.Prepare(game, slot, bodies, checkpoint.LocalVelocityMetresPerSecond!.Value);
                gears = GearMotionPlan.Prepare(game, slot, bodies, rotation, checkpoint.Gear);
                string ReadPausedState() => JsonSerializer.Serialize(new { body = bodies.ReadVelocities(), wheel = rotation.ReadState(), gear = gears.ReadState() });
                var stable = ReadPausedState();
                Thread.Sleep(100); CheckBoundary();
                if (stable != ReadPausedState())
                    throw new GateException("Physics state is not stable while paused.");
                rotation.Preflight(); gears.Preflight();
            }, () =>
            {
                bodies!.Apply(CheckBoundary);
                rotation!.Apply(CheckBoundary);
                gears!.Apply(CheckBoundary);
            }, () =>
            {
                CheckBoundary(); bodies!.VerifyApplied(); rotation!.VerifyApplied(); gears!.VerifyApplied();
                RequirePhysicsPaused(menu.GameStateAsync(cancellation).GetAwaiter().GetResult(), frozenTime, Read().ElapsedTime, pause.IsPaused, placementExit: true);
            });
        }
        catch (Exception ex)
        {
            bool stillPaused;
            try { stillPaused = pause.IsPaused; } catch (Exception) { stillPaused = false; }
            var message = stillPaused
                ? "Rolling start failed; physics remains paused after a possible partial update. Restart LMU before another reset. "
                : "Rolling start failed. ";
            throw new GateException(message + ex.Message);
        }
        progress("Speed and gear restored; simulation resumed.");
        timer.Restart();
        do
        {
            var after = Read();
            if (after.ElapsedTime > frozenTime && after.SpeedKmh is { } speed && after.Gear == checkpoint.Gear)
                return new(checkpoint.SpeedKmh!.Value, speed, checkpoint.Gear, after.Gear, gears!.ReadEngineRpm());
            Thread.Sleep(20);
        } while (timer.ElapsedMilliseconds < 2000);
        throw new GateException("Motion was written, but resumed telemetry with the captured gear was not observed.");
    }
}
