using System.Diagnostics;
using System.Text.Json;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public static class SuspensionProbe
{
    public static void RequireTimeProgress(double previous, double current)
    {
        if (!double.IsFinite(previous) || !double.IsFinite(current) || current < previous)
            throw new GateException("SDK simulation time restarted during native observation.");
    }
    public static void RequireTimeBracket(double before, double after, double milliseconds)
    {
        if (!double.IsFinite(before) || !double.IsFinite(after) || !double.IsFinite(milliseconds) ||
            before != after || milliseconds is < 0 or > 25)
        {
            var error = new GateException($"SDK time bracket {before:R} -> {after:R}; native read {milliseconds:F3} ms (limit 25 ms).");
            error.Data["observationTiming"] = true;
            throw error;
        }
    }
    private static void Save(string path, object value)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
    }

    public static int Layout(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("suspension-layout <mapped-image> <new-report.json>");
        var image = File.ReadAllBytes(args[0]);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image));
        var motion = MotionLayout.Discover(image, hash);
        var layout = SuspensionObservationLayout.Discover(image, motion);
        Save(args[1], new { schemaVersion = 1, inputSha256 = hash, layout.Fields, layout.ProbeCount,
            scope = "static observation-layout match only; not live compatibility or restoration support" });
        return 0;
    }

    public static int TyreTableLayout(string[] args) => TyreModelLayout(args, true);
    public static int TyreModelLayout(string[] args) => TyreModelLayout(args, false);
    private static int TyreModelLayout(string[] args, bool tables)
    {
        if (args.Length != 2) throw new ArgumentException($"suspension-tyre-{(tables ? "table" : "model")}-layout <mapped-image> <new-report.json>");
        var image = File.ReadAllBytes(args[0]);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image));
        var motion = MotionLayout.Discover(image, hash);
        var layout = TyreModelObservationLayout.Discover(image, motion);
        layout.Verify((rva, count) => image.AsSpan(checked((int)rva), count).ToArray());
        var tableLayout = tables ? TyreModelTableLayout.Discover(image, motion) : null;
        tableLayout?.Verify((rva, count) => image.AsSpan(checked((int)rva), count).ToArray());
        Save(args[1], new { schemaVersion = 1, mode = tables ? "offline-tyre-table-layout" : "offline-tyre-model-layout", inputSha256 = hash, layout.ProbeCount,
            tyreTableChecks = tableLayout?.ProbeCount,
            scope = "Static carrier observation contracts only; no live compatibility or restoration proof." });
        return 0;
    }

    public static int PausedSnapshot(string[] args) => PausedSnapshot(args, false);
    public static int PausedConstraintState(string[] args) => PausedSnapshot(args, true);
    public static int PausedTyreModelState(string[] args) => PausedSnapshot(args, true, true);
    public static int PausedTyreTableState(string[] args) => PausedSnapshot(args, true, true, true);
    private static int PausedSnapshot(string[] args, bool jointState, bool tyreModel = false, bool tyreTables = false)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return Snapshot(args, true, cancellation.Token, jointState: jointState, tyreModel: tyreModel, tyreTables: tyreTables); }
        finally { Console.CancelKeyPress -= handler; }
    }

    public static string StateKey(SuspensionObservation value) => JsonSerializer.Serialize(value with
    {
        Aggregate = value.Aggregate with { ReadStartedTimestamp = 0, ReadCompletedTimestamp = 0 },
        Bodies = value.Bodies.Select(b => b with { ReadStartedTimestamp = 0, ReadCompletedTimestamp = 0 }).ToArray(),
        Wheels = value.Wheels.Select(w => w with { ReadStartedTimestamp = 0, ReadCompletedTimestamp = 0 }).ToArray(),
        BodyVerificationCompletedTimestamp = 0
    });

    public static void RequirePausedPair(double before, double after, SuspensionObservation first, SuspensionObservation second)
    {
        if (!double.IsFinite(before) || !double.IsFinite(after) || before != after ||
            first.NativeUpdateObserved || second.NativeUpdateObserved || StateKey(first) != StateKey(second))
            throw new GateException("Native or SDK state advanced during the owned pause.");
    }

    public static int Snapshot(string[] args) => Snapshot(args, false, CancellationToken.None);
    public static int Inventory(string[] args) => Snapshot(args, false, CancellationToken.None, inventory: true);
    public static int ConstraintState(string[] args) => Snapshot(args, false, CancellationToken.None, inventory: true, jointState: true);
    public static int TyreModelState(string[] args) => Snapshot(args, false, CancellationToken.None, tyreModel: true);
    public static int TyreTableState(string[] args) => Snapshot(args, false, CancellationToken.None, tyreModel: true, tyreTables: true);

    private static int Snapshot(string[] args, bool ownedPause, CancellationToken cancellation, bool inventory = false, bool jointState = false, bool tyreModel = false, bool tyreTables = false)
    {
        if (args.Length is < 3 or > 4 || ((ownedPause || inventory || tyreModel) && args.Length != 3) || (args.Length == 4 && args[3] != "--window") ||
            args[0].Length != 64 || !args[0].All(Uri.IsHexDigit))
            throw new ArgumentException(tyreModel ? $"suspension-{(ownedPause ? "paused-" : "")}tyre-{(tyreTables ? "table" : "model")}-state <full-build-sha256> <checkpoint.json> <new-report.json>" : inventory ? $"suspension-constraint-{(jointState ? "state" : "inventory")} <full-build-sha256> <checkpoint.json> <new-report.json>" : ownedPause
                ? "suspension-paused-snapshot <full-build-sha256> <checkpoint.json> <new-report.json>"
            : "suspension-snapshot <full-build-sha256> <checkpoint.json> <new-report> [--window]");
        if (File.Exists(args[2])) throw new IOException("Output already exists.");
        var checkpoint = Checkpoint.Load(args[1]);
        var offsets = Environment.GetEnvironmentVariable("INACTIVE_RESET_OFFSETS") ?? "offsets";
        var data = Environment.GetEnvironmentVariable("INACTIVE_RESET_DATA") ?? "data";
        var menu = new LmuSessionClient(dataDirectory: data);
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult());
        cancellation.ThrowIfCancellationRequested();
        using var game = GameSession.Attach(offsets, forWriting: ownedPause);
        if (!string.Equals(game.ExecutableSha256, args[0], StringComparison.OrdinalIgnoreCase))
            throw new GateException("The requested full build hash does not match.");
        if (checkpoint.ExecutableSha256 is { } capturedBuild &&
            !string.Equals(capturedBuild, game.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new GateException("The checkpoint belongs to a different executable build.");
        var motion = game.RequireMotion();
        var mapped = game.Memory.CaptureMappedModule(game.Process);
        var layout = SuspensionObservationLayout.Discover(mapped, motion);
        layout.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        using var sdk = new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets, "shared-memory.json")));
        var live = new LiveStateReader(game); var slot = live.ResolveSlotIndex();
        if (slot is < 0 or >= 104) throw new GateException("Invalid player slot.");
        var vehicle = game.Offsets.Containers.ContainerAddress(game.ModuleBase, slot) + game.Offsets.Containers.VehicleDelta;
        var physics = game.Memory.ReadUInt64(vehicle + motion.VehiclePhysics);
        void Context()
        {
            if (game.Process.HasExited || live.ResolveSlotIndex() != slot || live.ReadControlOwner(slot) != 0 ||
                physics == 0 || game.Memory.ReadUInt64(vehicle + motion.VehiclePhysics) != physics)
                throw new GateException("Native observation player context changed.");
        }
        double? lastSdkTime = null;
        TelemetrySnapshot Read()
        {
            cancellation.ThrowIfCancellationRequested();
            Context(); var sample = sdk.Read() ?? throw new GateException("Player SDK telemetry unavailable.");
            if (sample.TrackName != checkpoint.TrackName || sample.VehicleName != checkpoint.VehicleName || !double.IsFinite(sample.ElapsedTime))
                throw new GateException("Native observation requires the checkpoint's track and car.");
            if (lastSdkTime is { } previous) RequireTimeProgress(previous, sample.ElapsedTime);
            lastSdkTime = sample.ElapsedTime;
            return sample;
        }
        var initial = Read(); Thread.Sleep(50);
        if (Read().ElapsedTime <= initial.ElapsedTime) throw new GateException("SDK time is not progressing; resume the game before observation.");
        var tyreLayout = tyreModel ? TyreModelObservationLayout.Discover(mapped, motion) : null;
        tyreLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        var tableLayout = tyreTables ? TyreModelTableLayout.Discover(mapped, motion) : null;
        tableLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        if (tyreModel && !ownedPause)
        {
            menu.RequirePracticeAsync().GetAwaiter().GetResult();
            DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult()); Context();
            var utc = DateTimeOffset.UtcNow; var before = Read(); var timer = Stopwatch.StartNew();
            object result;
            try
            {
                var acquisition = TyreModelAcquisition.Acquire(game.Memory.ReadBytes, vehicle, motion, layout, tyreLayout!, tableLayout);
                var after = Read(); timer.Stop();
                RequireTimeBracket(before.ElapsedTime, after.ElapsedTime, timer.Elapsed.TotalMilliseconds);
                var observation = acquisition.Decode();
                menu.RequirePracticeAsync().GetAwaiter().GetResult();
                DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult()); Context();
                layout.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                tyreLayout!.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                tableLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                result = new { schemaVersion = 1, mode = tyreTables ? "read-only-tyre-table-state" : "read-only-tyre-model-state", accepted = true,
                    game.ExecutableSha256, track = before.TrackName, car = before.VehicleName, pid = game.Process.Id, utc,
                    sdkBefore = before, sdkAfter = after, hostReadMilliseconds = timer.Elapsed.TotalMilliseconds,
                    tyreModelChecks = tyreLayout.ProbeCount, tyreTableChecks = tableLayout?.ProbeCount, observation };
            }
            catch (Exception ex)
            {
                timer.Stop();
                Save(args[2], new { schemaVersion = 1, mode = tyreTables ? "read-only-tyre-table-refusal" : "read-only-tyre-model-refusal", accepted = false,
                    game.ExecutableSha256, track = before.TrackName, car = before.VehicleName, pid = game.Process.Id, utc,
                    sdkBefore = before, hostReadMilliseconds = timer.Elapsed.TotalMilliseconds, tyreModelChecks = tyreLayout!.ProbeCount,
                    tyreTableChecks = tableLayout?.ProbeCount, failure = ex.Message, change = ex.Data["tyreModelReadChange"] });
                throw;
            }
            Save(args[2], result); return 0;
        }
        var pausedConstraints = ownedPause && jointState ? ConstraintInventoryLayout.Discover(mapped, game.ModuleBase) : null;
        var pausedState = pausedConstraints is not null ? ConstraintJointStateLayout.Discover(mapped, pausedConstraints) : null;
        pausedConstraints?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        pausedState?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        if (inventory)
        {
            var constraints = ConstraintInventoryLayout.Discover(mapped, game.ModuleBase);
            constraints.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
            var stateLayout = jointState ? ConstraintJointStateLayout.Discover(mapped, constraints) : null;
            stateLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
            var attempts = new List<object>();
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                menu.RequirePracticeAsync().GetAwaiter().GetResult();
                DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult()); Context();
                var utc = DateTimeOffset.UtcNow; var before = Read(); var timer = Stopwatch.StartNew();
                ConstraintInventoryAcquisition acquisition;
                try { acquisition = ConstraintInventory.Acquire(game.Memory.ReadBytes, vehicle, motion, constraints, stateLayout); }
                catch (GateException error) when (error.Data["constraintReadChange"] is ConstraintReadChange)
                {
                    timer.Stop();
                    var change = (ConstraintReadChange)error.Data["constraintReadChange"]!;
                    attempts.Add(new { attempt, accepted = false, reason = error.Message, change,
                        before = before.ElapsedTime, milliseconds = timer.Elapsed.TotalMilliseconds });
                    // A fresh bounded sample may avoid an advancing numeric row; structural changes still stop.
                    if (jointState && change.RowDataOnly && attempt < 3) continue;
                    Save(args[2], new { schemaVersion = 1, mode = "read-only-constraint-refusal", accepted = false,
                        game.ExecutableSha256, track = before.TrackName, car = before.VehicleName, pid = game.Process.Id,
                        utc, attempt, hostReadMilliseconds = timer.Elapsed.TotalMilliseconds, sdkBefore = before,
                        previousAttempts = attempts, constraints.ProbeCount, jointStateProofCount = stateLayout?.ProbeCount,
                        failure = error.Message, change,
                        limitation = "First mismatching reread only; no accepted state or SDK after bracket. No atomicity proof." });
                    throw;
                }
                var after = Read(); timer.Stop();
                attempts.Add(new { attempt, before = before.ElapsedTime, after = after.ElapsedTime, milliseconds = timer.Elapsed.TotalMilliseconds });
                try { RequireTimeBracket(before.ElapsedTime, after.ElapsedTime, timer.Elapsed.TotalMilliseconds); }
                catch (GateException) when (attempt < 3) { continue; }
                menu.RequirePracticeAsync().GetAwaiter().GetResult();
                DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult()); Context();
                constraints.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                stateLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                layout.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                var result = acquisition.Decode();
                Save(args[2], new { schemaVersion = 1, mode = jointState ? "read-only-constraint-state" : "read-only-constraint-inventory", game.ExecutableSha256,
                    track = before.TrackName, car = before.VehicleName, pid = game.Process.Id, utc,
                    hostReadMilliseconds = timer.Elapsed.TotalMilliseconds, attempts, sdkBefore = before, sdkAfter = after,
                    constraints.ProbeCount, constraints.Fields, jointStateProofCount = stateLayout?.ProbeCount, inventory = result });
                return 0;
            }
        }
        if (ownedPause)
        {
            using var report = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write);
            var events = new List<object>();
            void Event(string message) => events.Add(new { utc = DateTimeOffset.UtcNow, hostTimestamp = Stopwatch.GetTimestamp(), message });
            bool Same() { Context(); return true; }
            var pause = PhysicsPause.Prepare(game, Same);
            object? observation = null; string? failure = null; byte? finalFlag = null; object? tyreModelChange = null;
            var started = Stopwatch.GetTimestamp();
            try
            {
                menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
                DirectReset.RequireDriver(menu.GameStateAsync(cancellation).GetAwaiter().GetResult());
                Read();
                observation = OwnedPauseObservation.Run(pause, () =>
                {
                    // Allow an already executing tick to finish, then measure stability rather than infer it.
                    if (cancellation.WaitHandle.WaitOne(25)) cancellation.ThrowIfCancellationRequested();
                    var before = Read();
                    var first = SuspensionObservation.Read(game.Memory.ReadBytes, vehicle, motion, layout);
                    observation = new { sdkBefore = before, first };
                    ConstraintInventoryAcquisition? firstJoints = null;
                    object? firstJointState = null;
                    object CaptureJoints(out ConstraintInventoryAcquisition acquisition)
                    {
                        var sdkBefore = Read(); var timer = Stopwatch.StartNew();
                        acquisition = ConstraintInventory.Acquire(game.Memory.ReadBytes, vehicle, motion, pausedConstraints!, pausedState!);
                        var sdkAfter = Read(); timer.Stop();
                        RequireTimeBracket(sdkBefore.ElapsedTime, sdkAfter.ElapsedTime, timer.Elapsed.TotalMilliseconds);
                        return new { sdkBefore, sdkAfter, hostReadMilliseconds = timer.Elapsed.TotalMilliseconds, inventory = acquisition.Decode() };
                    }
                    if (pausedConstraints is not null)
                    {
                        firstJointState = CaptureJoints(out firstJoints);
                        observation = new { sdkBefore = before, first, firstJointState };
                    }
                    TyreModelAcquisition? firstTyres = null;
                    object? firstTyreState = null;
                    object CaptureTyres(out TyreModelAcquisition acquisition)
                    {
                        var sdkBefore = Read(); var timer = Stopwatch.StartNew();
                        acquisition = TyreModelAcquisition.Acquire(game.Memory.ReadBytes, vehicle, motion, layout, tyreLayout!, tableLayout);
                        var sdkAfter = Read(); timer.Stop();
                        RequireTimeBracket(sdkBefore.ElapsedTime, sdkAfter.ElapsedTime, timer.Elapsed.TotalMilliseconds);
                        return new { sdkBefore, sdkAfter, hostReadMilliseconds = timer.Elapsed.TotalMilliseconds, observation = acquisition.Decode() };
                    }
                    if (tyreLayout is not null)
                    {
                        firstTyreState = CaptureTyres(out firstTyres);
                        observation = new { sdkBefore = before, first, firstJointState, firstTyreState };
                    }
                    if (cancellation.WaitHandle.WaitOne(25)) cancellation.ThrowIfCancellationRequested();
                    var second = SuspensionObservation.Read(game.Memory.ReadBytes, vehicle, motion, layout);
                    object? secondJointState = null;
                    if (pausedConstraints is not null)
                    {
                        secondJointState = CaptureJoints(out var secondJoints);
                        observation = new { sdkBefore = before, first, second, firstJointState, secondJointState, firstTyreState };
                        firstJoints!.RequireSame(secondJoints);
                    }
                    object? secondTyreState = null;
                    if (tyreLayout is not null)
                    {
                        secondTyreState = CaptureTyres(out var secondTyres);
                        observation = new { sdkBefore = before, first, second, firstJointState, secondJointState, firstTyreState, secondTyreState };
                        firstTyres!.RequireSame(secondTyres);
                    }
                    var after = Read();
                    observation = new { sdkBefore = before, sdkAfter = after, first, second, firstJointState, secondJointState, firstTyreState, secondTyreState };
                    RequirePausedPair(before.ElapsedTime, after.ElapsedTime, first, second);
                    layout.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                    pausedConstraints?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                    pausedState?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                    tyreLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                    tableLayout?.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
                    return observation;
                }, () =>
                {
                    Context();
                    menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
                    DirectReset.RequireDriver(menu.GameStateAsync(cancellation).GetAwaiter().GetResult());
                }, Event, cancellation);
                finalFlag = pause.IsPaused ? (byte)1 : (byte)0;
                if (finalFlag != 0) throw new GateException("Physics pause was set again after cleanup.");
                Event("post-cleanup readback flag=0");
            }
            catch (Exception ex) { failure = ex.Message; tyreModelChange = ex.Data["tyreModelReadChange"]; Event("aborted: " + ex.Message); throw; }
            finally
            {
                JsonSerializer.Serialize(report, new { schemaVersion = 1, mode = tyreTables ? "owned-physics-pause-tyre-table-state" : tyreModel ? "owned-physics-pause-tyre-model-state" : jointState ? "owned-physics-pause-constraint-state" : "owned-physics-pause-observation", game.ExecutableSha256,
                    track = initial.TrackName, car = initial.VehicleName, pid = game.Process.Id,
                    started, completed = Stopwatch.GetTimestamp(), hostFrequency = Stopwatch.Frequency,
                    events, finalFlag, failure, observation, inventoryChecks = pausedConstraints?.ProbeCount, stateChecks = pausedState?.ProbeCount,
                    tyreModelChecks = tyreLayout?.ProbeCount, tyreTableChecks = tableLayout?.ProbeCount, tyreModelChange,
                    limitation = "Only pause flag written; no restoration. Stable rereads do not prove a scheduler barrier, SDK phase alignment or complete solver/contact state. Cleanup can refuse changed context; inspect events before recovery." },
                    new JsonSerializerOptions { WriteIndented = true });
            }
            return 0;
        }
        object Observe()
        {
        DateTimeOffset utc = default; long hostStartTimestamp = 0, hostEndTimestamp = 0;
        var before = initial; var after = initial; SuspensionObservation? native = null;
        var duration = 0d; var attempts = new List<object>();
        // Keep the same read-only attachment warm; retry timing refusals only.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            utc = DateTimeOffset.UtcNow; hostStartTimestamp = Stopwatch.GetTimestamp();
            before = Read(); var timer = Stopwatch.StartNew();
            native = SuspensionObservation.Read(game.Memory.ReadBytes, vehicle, motion, layout);
            after = Read(); timer.Stop(); hostEndTimestamp = Stopwatch.GetTimestamp();
            duration = timer.Elapsed.TotalMilliseconds;
            attempts.Add(new { attempt, before = before.ElapsedTime, after = after.ElapsedTime, milliseconds = duration });
            try { RequireTimeBracket(before.ElapsedTime, after.ElapsedTime, duration); break; }
            catch (GateException) when (attempt < 3)
            { if (args.Length == 3) Console.Error.WriteLine($"Timing refusal on observation {attempt}; retrying within this attachment."); }
        }
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        DirectReset.RequireDriver(menu.GameStateAsync().GetAwaiter().GetResult());
        Context();
        layout.Verify((rva, count) => game.Memory.ReadBytes(game.ModuleBase + rva, count));
        return new { schemaVersion = 1, game.ExecutableSha256, track = before.TrackName,
            car = before.VehicleName, utc, hostStartTimestamp, hostEndTimestamp, hostFrequency = Stopwatch.Frequency,
            hostReadMilliseconds = duration, attempts,
            sdkBefore = before, sdkAfter = after, native,
            limitation = "No native lock or pause; equal SDK time and stable topology do not establish atomic physics state. No restoration payload." };
        }
        if (args.Length == 3) Save(args[2], Observe());
        else
        {
            using var output = new StreamWriter(new FileStream(args[2], FileMode.CreateNew, FileAccess.Write)) { AutoFlush = true };
            void Event(object value) => output.WriteLine(JsonSerializer.Serialize(value));
            Event(new { kind = "header", schemaVersion = 1, game.ExecutableSha256,
                track = initial.TrackName, car = initial.VehicleName, utc = DateTimeOffset.UtcNow,
                seconds = 45, mode = "read-only; no pause; bracketed not atomic" });
            Console.WriteLine("CAPTURE WINDOW READY: 45 seconds, read-only.");
            var window = Stopwatch.StartNew(); var accepted = 0; var refused = 0;
            try
            {
                while (window.Elapsed.TotalSeconds < 45)
                {
                    try { Event(new { kind = "sample", sample = Observe() }); accepted++; }
                    catch (GateException ex) when (ex.Data["observationTiming"] is true)
                    { Event(new { kind = "timing-refusal", utc = DateTimeOffset.UtcNow, error = ex.Message }); refused++; }
                    Thread.Sleep(200);
                }
                Event(new { kind = "complete", accepted, refused, seconds = window.Elapsed.TotalSeconds });
                Console.WriteLine($"Capture complete: {accepted} accepted, {refused} timing refusals.");
            }
            catch (Exception ex)
            { Event(new { kind = "aborted", utc = DateTimeOffset.UtcNow, error = ex.Message, accepted, refused }); throw; }
        }
        return 0;
    }
}
