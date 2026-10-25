using System.Diagnostics;
using System.Text.Json;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public static class PauseProbe
{
    public static int Run(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--arm") || args[0] != PhysicsPause.SupportedBuild)
            throw new ArgumentException("pause-probe <researched-full-build-sha256> [--arm]");
        var armed = args.Contains("--arm");
        var offsets = Environment.GetEnvironmentVariable("INACTIVE_RESET_OFFSETS") ?? "offsets";
        var menu = new LmuSessionClient(dataDirectory: Environment.GetEnvironmentVariable("INACTIVE_RESET_DATA") ?? "data");
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        using var game = GameSession.Attach(offsets, forWriting: armed);
        using var shared = new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets, "shared-memory.json")));
        using var pedals = new WheelMotionReader(offsets);
        var live = new LiveStateReader(game);
        var slot = live.ResolveSlotIndex();
        if (slot is < 0 or >= 104) throw new GateException("Invalid player slot.");
        var vehicle = game.Offsets.Containers.ContainerAddress(game.ModuleBase, slot) + game.Offsets.Containers.VehicleDelta;
        var physics = game.Memory.ReadUInt64(vehicle + 0x159a0);
        var first = shared.Read() ?? throw new GateException("Player telemetry unavailable.");
        bool SameContext() => !game.Process.HasExited && live.ResolveSlotIndex() == slot &&
            game.Memory.ReadUInt64(vehicle + 0x159a0) == physics && physics != 0;
        using var pause = PhysicsPause.Prepare(game, SameContext);
        TelemetrySnapshot Read()
        {
            if (!SameContext()) throw new GateException("Player physics changed.");
            var sdk = shared.Read() ?? throw new GateException("Player telemetry unavailable.");
            if (sdk.TrackName != first.TrackName || sdk.VehicleName != first.VehicleName)
                throw new GateException("Session identity changed.");
            return sdk;
        }
        if (pause.IsPaused) throw new GateException("Leave the game unpaused for this probe.");
        var watch = Stopwatch.StartNew();
        TelemetrySnapshot before;
        do
        {
            before = Read();
            VelocityProbe.RequireStationary(live.ReadArrival(slot), before);
            WheelRotationPlan.RequireReleased(pedals.Read());
            Thread.Sleep(20);
        } while (watch.ElapsedMilliseconds < 500);
        if (before.ElapsedTime <= first.ElapsedTime) throw new GateException("SDK time is not advancing.");
        if (!armed)
        {
            Console.WriteLine("Dry run passed. --arm writes the one-byte physics pause flag, checks frozen time for 500 ms, then restores it and checks time resumes. No velocity writes.");
            return 0;
        }
        var output = Path.Combine("artifacts", "velocity-probe", "pause-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var samples = new List<object>();
        string? error = null;
        var frozen = false; var resumed = false;
        double? frozenTime = null;
        watch.Restart();
        try
        {
            menu.RequirePracticeAsync().GetAwaiter().GetResult();
            VelocityProbe.RequireStationary(live.ReadArrival(slot), Read());
            WheelRotationPlan.RequireReleased(pedals.Read());
            pause.Begin();
            Thread.Sleep(100);
            frozenTime = Read().ElapsedTime;
            var time = frozenTime.Value;
            var stableUntil = watch.ElapsedMilliseconds + 500;
            do
            {
                var sdk = Read();
                VelocityProbe.RequirePhysicsPaused(menu.GameStateAsync().GetAwaiter().GetResult(), time, sdk.ElapsedTime, pause.IsPaused);
                if (!pause.IsPaused) throw new GateException("Pause flag changed.");
                samples.Add(new { phase = "paused", ms = watch.Elapsed.TotalMilliseconds, sdk.ElapsedTime, sdk.SpeedKmh, sdk.Pose.Position });
                Thread.Sleep(20);
            } while (watch.ElapsedMilliseconds < stableUntil);
            frozen = true;
        }
        catch (Exception ex) { error = ex.Message; }
        finally
        {
            try { pause.Dispose(); }
            catch (Exception ex) { error = (error is null ? "" : error + "; ") + "Pause cleanup failed: " + ex.Message; }
        }
        try
        {
            var deadline = watch.ElapsedMilliseconds + 2000;
            while (watch.ElapsedMilliseconds < deadline)
            {
                var sdk = Read();
                var state = menu.GameStateAsync().GetAwaiter().GetResult();
                samples.Add(new { phase = "resuming", ms = watch.Elapsed.TotalMilliseconds, sdk.ElapsedTime, sdk.SpeedKmh, sdk.Pose.Position, state });
                if (!pause.IsPaused && frozenTime is { } time && sdk.ElapsedTime > time &&
                    state["MultiStintState"]?.GetValue<string>() == "DRIVING") { resumed = true; break; }
                Thread.Sleep(20);
            }
            if (!resumed) throw new GateException("SDK time / driving state did not resume.");
        }
        catch (Exception ex) { error = (error is null ? "" : error + "; ") + ex.Message; }
        File.WriteAllText(output, JsonSerializer.Serialize(new { game.ExecutableSha256, first.TrackName, first.VehicleName,
            frozen, resumed, error, samples }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Observation: {output}; frozen={frozen}; resumed={resumed}; error={error ?? "none"}.");
        return error is null && frozen && resumed ? 0 : 4;
    }
}
