using System.Diagnostics;
using System.Globalization;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>One-off, fail-closed causal experiment. Never shipped or cached as a profile.</summary>
public static class IndexedSpotProbe
{
    private const string Track = "Circuit de Barcelona";
    private const string Vehicle = "Richard Mille AF Corse 2025 #50:ELMS";

    public static byte[] ShiftComponent(ReadOnlySpan<byte> entry, string component, float delta)
    {
        var offset = component switch { "x" => 0, "z" => 8, "yaw" => 16, _ => -1 };
        var magnitude = MathF.Abs(delta);
        if (entry.Length != 32 || offset < 0 || !float.IsFinite(delta) ||
            (component == "yaw" ? magnitude is < 0.05f or > 0.25f
                                : magnitude is < 0.25f or > 5f))
            throw new ArgumentException("probe requires X/Z shift 0.25–5 m or yaw shift 0.05–0.25 rad");
        var value = BitConverter.ToSingle(entry.Slice(offset, 4));
        if (!float.IsFinite(value) || !float.IsFinite(value + delta))
            throw new ArgumentException("indexed entry has an invalid position coordinate");
        var payload = entry[..24].ToArray();
        BitConverter.GetBytes(value + delta).CopyTo(payload, offset);
        return payload;
    }

    public static bool CanRestore(ReadOnlySpan<byte> current, ReadOnlySpan<byte> original,
                                  ReadOnlySpan<byte> payload)
    {
        if (current.Length != 32 || original.Length != 32 || payload.Length != 24) return false;
        for (var i = 0; i < 24; i++)
            if (current[i] != original[i] && current[i] != payload[i]) return false;
        return true;
    }

    public static int Run(string[] args)
    {
        if (args.Length is not (4 or 5) || (args.Length == 5 && args[4] != "--arm") ||
            args[0].Length != 64 || !args[0].All(Uri.IsHexDigit) ||
            !int.TryParse(args[1], out var mode) || mode is < 0 or > 2 ||
            args[2] is not ("x" or "z" or "yaw") ||
            !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            throw new ArgumentException("spot-probe <full-build-sha256> <mode:0..2> <x|z|yaw> <delta-metres-or-radians> [--arm]");
        var armed = args.Length == 5;
        return RunWithPayload(args[0], mode, armed,
            original => ShiftComponent(original, args[2], delta), null);
    }

    public static int PlaceCheckpoint(string[] args) => PlaceCheckpoint(args, false);

    public static int PlaceCheckpointWithRules(string[] args) => PlaceCheckpoint(args, true);

    private static int PlaceCheckpoint(string[] args, bool testRules)
    {
        if (args.Length is not (6 or 7) || (args.Length == 7 && args[6] != "--arm") ||
            args[0].Length != 64 || !args[0].All(Uri.IsHexDigit) || args[1] != "cp-023328" ||
            !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var forward) ||
            !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lateral) ||
            !float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var vertical) ||
            !float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var yawBias))
            throw new ArgumentException("spot-place-test <full-build-sha256> cp-023328 <D> <L> <H> <yaw-bias> [--arm]");
        var offset = new IndexedRestOffset(forward, lateral, vertical, yawBias);
        if (!float.IsFinite(forward) || !float.IsFinite(lateral) || !float.IsFinite(vertical) ||
            !float.IsFinite(yawBias) || forward is < 0 or > 5 || MathF.Abs(lateral) > 2 ||
            MathF.Abs(vertical) > 2 || MathF.Abs(yawBias) > 0.1f)
            throw new ArgumentException("indexed rest calibration is outside pit-box bounds");
        var checkpoint = Checkpoint.Require(Path.Combine("data", "checkpoints"), args[1]);
        if (checkpoint.TrackName != Track || checkpoint.VehicleName != Vehicle)
            throw new CheckpointException("checkpoint car/track does not match this experiment");
        var target = Geometry.BuildTargetFromRecordedPose(checkpoint.Pose);
        if (!target.Valid) throw new CheckpointException(string.Join("; ", target.Failures));
        return RunWithPayload(args[0], 2, args.Length == 7, original =>
        {
            var template = PlacementMath.DecodeSpotEntry(original);
            var entry = IndexedPlacementMath.Invert(target.RestPosition, target.Yaw, template, offset);
            return PlacementMath.EncodeSpotEntry(entry);
        }, target, testRules);
    }

    private static int RunWithPayload(string expectedHash, int mode, bool armed,
                                      Func<byte[], byte[]> makePayload, TargetPose? target,
                                      bool testRules = false)
    {
        if (Process.GetProcesses().Any(p =>
                p.ProcessName.Contains("EasyAnti", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains("start_protected", StringComparison.OrdinalIgnoreCase)))
            throw new GateException("anticheat process present; refusing probe");
        using var process = Process.GetProcessesByName(GameSession.ProcessName).SingleOrDefault()
            ?? throw new GateException("expected exactly one LMU process");
        if (process.Modules.Cast<ProcessModule>().Any(m =>
                m.ModuleName.StartsWith("lmu_", StringComparison.OrdinalIgnoreCase) &&
                m.ModuleName.EndsWith("_observer.dll", StringComparison.OrdinalIgnoreCase) ||
                m.ModuleName.StartsWith("EasyAnti", StringComparison.OrdinalIgnoreCase) ||
                m.ModuleName.StartsWith("EAC", StringComparison.OrdinalIgnoreCase)))
            throw new GateException("observer or anticheat module loaded; use a clean direct-launch process");
        var imagePath = process.MainModule?.FileName ?? throw new GateException("game image path unavailable");
        var sha = GameSession.Sha256File(imagePath);
        if (!string.Equals(sha, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new GateException("running executable hash differs from requested build");
        var capture = ModuleDumper.Capture(process);
        using (var disk = File.OpenRead(imagePath))
        {
            var header = new byte[0x1000];
            disk.ReadExactly(header);
            if (capture.UnreadableBytes != 0 || !ModuleDumper.SamePeIdentity(header, capture.Image))
                throw new GateException("mapped executable is unreadable or mismatches disk");
        }
        var destination = SpotTableResolver.FindIndexedDestination(capture.Image)
            ?? throw new GateException("indexed destination consumer is not structurally resolved");
        var modeField = SpotTableResolver.FindDestinationMode(capture.Image)
            ?? throw new GateException("destination mode field is unresolved");
        var indices = SpotTableResolver.FindSpecialSlotIndices(capture.Image)
            ?? throw new GateException("slot indices are unresolved");
        var stride = SpotTableResolver.FindContainerStride(capture.Image)
            ?? throw new GateException("container stride is unresolved");
        var array = SpotTableResolver.FindContainerArrayBase(capture.Image)
            ?? throw new GateException("container array is unresolved");
        var owner = SpotTableResolver.FindControlOwner(capture.Image)
            ?? throw new GateException("control owner is unresolved");
        var garage = SpotTableResolver.FindGaragePos(capture.Image)
            ?? throw new GateException("spot multiplier is unresolved");
        var selection = SpotTableResolver.FindNormalSlotSelection(capture.Image)
            ?? throw new GateException("normal-slot selector is unresolved");
        var speedRule = testRules
            ? SpotTableResolver.FindPitSpeedRule(capture.Image)
                ?? throw new GateException("pit-speed rule reader is unresolved")
            : null;
        var pitState = testRules
            ? SpotTableResolver.FindPitState(capture.Image)
                ?? throw new GateException("pit state is unresolved")
            : null;
        using var shared = new SharedMemoryReader(SharedMemoryOffsets.Load(
            Path.Combine("offsets", "shared-memory.json")));
        var player = shared.Read() ?? throw new GateException("no SDK player vehicle");
        if (player.TrackName != Track || player.VehicleName != Vehicle)
            throw new GateException("this probe requires the exact Barcelona/Richard Mille checkpoint car");

        using var memory = armed ? ProcessMemory.OpenWrite(process.Id) : ProcessMemory.OpenRead(process.Id);
        var module = capture.ModuleBase;
        var slot = module + array.ArrayBaseRva;
        if (memory.ReadInt32(slot + indices.SlotIndex) != 0 ||
            memory.ReadInt32(slot + owner.Offset) != 1 ||
            memory.ReadInt32(slot + modeField.Offset) != 0)
            throw new GateException("player must be slot 0 in the garage with owner/mode 1/0");
        var pit = memory.ReadInt32(slot + indices.PitIndex);
        var garageIndex = memory.ReadInt32(slot + indices.GarageIndex);
        var mult = memory.ReadInt32(module + garage.MultRva);
        if (pit is < 0 or >= 0x68 || garageIndex < 0 ||
            mult is < 1 or > 10000 || pit >= mult || stride.Stride < indices.PitIndex + 4 ||
            memory.ReadInt32(module + selection.SwitchRva) != 0)
            throw new GateException("spot indices, multiplier, stride, or selector are implausible");
        var table = memory.ReadUInt64(module + destination.TableRva);
        if (table == 0 || table % 4 != 0) throw new GateException("indexed table pointer is invalid");
        var address = checked(table + (ulong)(pit * 3 + mode) * 32);
        memory.RequireWritableDataPage(address, 32);
        var original = memory.ReadBytes(address, 32);
        var payload = makePayload(original);
        var ruleAddress = speedRule is null ? 0UL : module + speedRule.FlagRulesRva;
        var originalRule = speedRule is null ? -1 : memory.ReadInt32(ruleAddress);
        if (testRules && (originalRule != 2 || pitState is null ||
                          memory.ReadInt32(slot + pitState.StateOffset) != 5))
            throw new GateException("rules experiment requires Full (2) and garage pit state 5");
        if (payload.Length != 24 || !PlacementMath.DecodeSpotEntry(payload).Position.IsFinite ||
            !PlacementMath.DecodeSpotEntry(payload).Orientation.IsFinite)
            throw new GateException("computed indexed payload is invalid");
        var before = PlacementMath.DecodeSpotEntry(original);
        var after = PlacementMath.DecodeSpotEntry(payload);
        Console.WriteLine($"build {sha[..8]} pid {process.Id}; mode {mode}, pit {pit}, garage {garageIndex}; " +
                          $"entry 0x{address:X}; position ({before.Position}) -> ({after.Position}); " +
                          $"yaw {before.Orientation.Y:F6} -> {after.Orientation.Y:F6}");
        if (target is not null)
            Console.WriteLine($"target rest ({target.RestPosition}), yaw {target.Yaw:F6}");
        Console.WriteLine($"original 32 bytes {Convert.ToHexString(original)}");
        if (testRules)
            Console.WriteLine($"Flag Rules 0x{ruleAddress:X}: {originalRule} -> 0 -> {originalRule}; " +
                              $"pit state offset 0x{pitState!.StateOffset:X}; wait <=120 s after Drive");
        if (!armed) { Console.WriteLine("dry run; no game write"); return 0; }

        var modified = false;
        var ruleModified = false;
        void RestoreSpot()
        {
            if (!modified) return;
            if (memory.ReadUInt64(module + destination.TableRva) != table ||
                !CanRestore(memory.ReadBytes(address, 32), original, payload))
                throw new GateException("cannot safely restore indexed entry; bytes or pointer changed unexpectedly");
            memory.WriteVerified(address, original.AsSpan(0, 24));
            if (!memory.ReadBytes(address, 32).AsSpan().SequenceEqual(original))
                throw new GateException("indexed entry restore did not reproduce all 32 original bytes");
            modified = false;
            Console.WriteLine("original 32-byte entry verified after restore");
        }
        try
        {
            if (memory.ReadUInt64(module + destination.TableRva) != table ||
                !memory.ReadBytes(address, 32).AsSpan().SequenceEqual(original) ||
                memory.ReadInt32(slot + owner.Offset) != 1 ||
                (testRules && memory.ReadInt32(ruleAddress) != originalRule))
                throw new GateException("table or garage state changed before arm");
            if (testRules)
            {
                memory.RequireWritableDataPage(ruleAddress, 4);
                ruleModified = true; // a failed write may have changed a prefix
                memory.WriteVerified(ruleAddress, BitConverter.GetBytes(0));
                Console.WriteLine("Flag Rules temporarily 0; original value will be restored");
            }
            modified = true; // a failed write may have changed a prefix
            memory.WriteVerified(address, payload);
            Console.WriteLine("temporary entry verified; press Drive once now (45 s timeout)");
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (memory.ReadUInt64(module + destination.TableRva) != table)
                    throw new GateException("indexed table pointer changed while armed");
                if (memory.ReadInt32(slot + owner.Offset) == 0)
                {
                    Thread.Sleep(2000);
                    var arrived = shared.Read();
                    if (arrived is null || arrived.TrackName != Track || arrived.VehicleName != Vehicle)
                        throw new GateException("SDK player identity changed after Drive");
                    var p = arrived.Pose.Position;
                    var yaw = Geometry.ExtractYawFromOrientationRows(arrived.Pose.Row0, arrived.Pose.Row2);
                    Console.WriteLine($"player pose ({p.X:F6}, {p.Y:F6}, {p.Z:F6}), yaw {yaw:F6}; mode {memory.ReadInt32(slot + modeField.Offset)}");
                    if (target is not null)
                    {
                        var dx = p.X - target.RestPosition.X;
                        var dz = p.Z - target.RestPosition.Z;
                        Console.WriteLine($"target error horizontal {MathF.Sqrt(dx * dx + dz * dz):F3} m, vertical {p.Y - target.RestPosition.Y:+0.000;-0.000} m");
                    }
                    RestoreSpot();
                    if (testRules)
                    {
                        Console.WriteLine("pit state set; keep limiter on until CLEAR (120 s timeout)");
                        var clearTimer = Stopwatch.StartNew();
                        while (clearTimer.Elapsed < TimeSpan.FromSeconds(120) &&
                               memory.ReadInt32(slot + pitState!.StateOffset) != 0)
                            Thread.Sleep(50);
                        if (memory.ReadInt32(slot + pitState!.StateOffset) != 0)
                        {
                            Console.WriteLine("pit state STILL SET at timeout; keep limiter on");
                            return 4;
                        }
                        Console.WriteLine($"CLEAR after {clearTimer.Elapsed.TotalSeconds:F1} s");
                    }
                    return 0;
                }
                Thread.Sleep(10);
            }
            Console.WriteLine("no player-control transition in 45 s");
            return 4;
        }
        finally
        {
            try { RestoreSpot(); }
            finally
            {
                if (ruleModified)
                {
                    var live = memory.ReadInt32(ruleAddress);
                    if (live != 0 && live != originalRule)
                        throw new GateException($"Flag Rules changed unexpectedly to {live}; refusing overwrite");
                    memory.WriteVerified(ruleAddress, BitConverter.GetBytes(originalRule));
                    if (memory.ReadInt32(ruleAddress) != originalRule)
                        throw new GateException("Flag Rules exact restoration failed");
                    Console.WriteLine($"Flag Rules original value {originalRule} verified after restore");
                }
            }
        }
    }
}
