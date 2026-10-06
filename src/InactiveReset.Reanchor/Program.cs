using System.Diagnostics;
using System.Text.Json;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>
/// Patch day, for the maintainer only.
///
/// This is not shipped. `build.ps1 ship` publishes InactiveReset.App and
/// InactiveReset.Cli and nothing else, so no user ever holds this binary. They
/// do not need it: profiles are published to the repository and the app fetches
/// the one matching its build hash.
///
/// WHAT THIS CAN AND CANNOT FIX. It re-derives ADDRESSES -- where a thing lives.
/// It cannot re-derive BEHAVIOUR -- what the engine does when it gets there. The
/// 2026-08-11 patch moved nothing and changed two tunable values, and every gate
/// stayed green while placement went a metre wrong. Behavioural tunables are now
/// read live from the running game instead (see <c>EngineTunables</c>), which is
/// why that class of patch no longer needs this tool at all.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return args.Length == 0 ? 2 : 0;
        }

        var offsets = Environment.GetEnvironmentVariable("INACTIVE_RESET_OFFSETS")
                      ?? Path.Combine(AppContext.BaseDirectory, "offsets");
        var rest = args[1..];

        try
        {
            return args[0] switch
            {
                "dump" => ReanchorCommand.Dump(rest),
                "reanchor" => ReanchorCommand.Run(offsets, rest),
                "refs" => References(rest),
                "code-map" => CodeMap(rest),
                "spot-resolve" => SpotResolve(rest),
                "spot-live" => SpotLive(rest),
                "spot-telemetry" => SpotTelemetry(rest),
                "spot-probe" => IndexedSpotProbe.Run(rest),
                "spot-place-test" => IndexedSpotProbe.PlaceCheckpoint(rest),
                "spot-place-rules-test" => IndexedSpotProbe.PlaceCheckpointWithRules(rest),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int References(string[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("refs <dump> <hex-rva>...");
        }
        var image = File.ReadAllBytes(args[0]);
        var index = Reanchor.BuildReferenceIndex(image);
        foreach (var value in args[1..])
        {
            var rva = Convert.ToUInt64(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? value[2..] : value, 16);
            index.TryGetValue(rva, out var sites);
            Console.WriteLine($"{value}: {sites?.Count ?? 0} reference(s)");
            foreach (var site in sites ?? [])
            {
                Console.WriteLine($"  0x{site.Site:X8}");
            }
        }
        return 0;
    }

    private static int CodeMap(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("code-map <old-dump> <new-dump> <hex-rva>...");
        var older = File.ReadAllBytes(args[0]);
        var newer = File.ReadAllBytes(args[1]);
        var misses = 0;
        foreach (var arg in args[2..])
        {
            var rva = Convert.ToUInt64(arg.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? arg[2..] : arg, 16);
            var match = Reanchor.RemapCode(older, newer, rva);
            if (match is null)
            {
                Console.WriteLine($"0x{rva:X8}: no unique masked-code match");
                misses++;
            }
            else Console.WriteLine($"0x{rva:X8} -> 0x{match.NewRva:X8} ({match.BytesUsed} masked bytes; inspect semantics)");
        }
        return misses == 0 ? 0 : 4;
    }

    private static int SpotResolve(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("spot-resolve <mapped-dump.bin>");
        var image = File.ReadAllBytes(args[0]);
        var found = InactiveReset.Core.SpotTableResolver.FindPitPos(image);
        var garage = InactiveReset.Core.SpotTableResolver.FindGaragePos(image);
        var normal = InactiveReset.Core.SpotTableResolver.FindNormalSlotTable(image);
        var selection = SpotTableResolver.FindNormalSlotSelection(image);
        var trackFlags = SpotTableResolver.FindTrackLimitsFlags(image);
        var destination = SpotTableResolver.FindIndexedDestination(image);
        var mode = SpotTableResolver.FindDestinationMode(image);
        var pitState = SpotTableResolver.FindPitState(image);
        var stride = InactiveReset.Core.SpotTableResolver.FindContainerStride(image);
        var array = SpotTableResolver.FindContainerArrayBase(image);
        var owner = SpotTableResolver.FindControlOwner(image);
        var lateral = SpotTableResolver.FindLateralSign(image);
        var dimensions = SpotTableResolver.FindVehicleDimensions(image);
        var indices = SpotTableResolver.FindSpecialSlotIndices(image);
        if (stride is not null)
            Console.WriteLine($"Container stride 0x{stride.Stride:X} ({stride.Sites} separated IMUL sites; static candidate only)");
        if (array is not null)
            Console.WriteLine($"Container array base 0x{array.ArrayBaseRva:X8} ({array.Sites} separated stride-index sites; static candidate only)");
        if (owner is not null)
            Console.WriteLine($"Control owner offset 0x{owner.Offset:X} ({owner.WitnessSites} comparison sites; static candidate only)");
        if (lateral is not null)
            Console.WriteLine($"Lateral-sign offset 0x{lateral.Offset:X} ({lateral.WitnessSites} scalar reads; static candidate only)");
        if (dimensions is not null)
            Console.WriteLine($"Vehicle length/width offsets 0x{dimensions.LengthOffset:X}/0x{dimensions.WidthOffset:X} ({dimensions.LengthReads}/{dimensions.WidthReads} scalar reads, {dimensions.PairedSites} paired sites; static candidate only)");
        if (indices is not null)
            Console.WriteLine($"Special-slot indices slot 0x{indices.SlotIndex:X}, pit 0x{indices.PitIndex:X}, garage 0x{indices.GarageIndex:X}; transform call 0x{indices.TransformRva:X8} ({indices.Sites} agreeing sites; static candidate only)");
        if (normal is not null)
            Console.WriteLine($"Normal-slot table global 0x{normal.TableRva:X8} (reader/two-writer agreement; static candidate only)");
        if (selection is not null)
            Console.WriteLine($"Normal-slot selector 0x{selection.SwitchRva:X8}; override vector 0x{selection.OverrideStartRva:X8}/0x{selection.OverrideEndRva:X8}; default 0x{selection.DefaultTableRva:X8} (static candidate only)");
        if (trackFlags is not null)
            Console.WriteLine($"Track-limits derived flags 0x{trackFlags.FirstRva:X8}/0x{trackFlags.FirstRva + 1:X8}/0x{trackFlags.FirstRva + 2:X8} (initializer and {trackFlags.FirstReaders}/{trackFlags.SecondReaders}/{trackFlags.ThirdReaders} byte readers; static candidate only)");
        if (destination is not null)
            Console.WriteLine($"Indexed destination table 0x{destination.TableRva:X8}; pit-index offset 0x{destination.PitIndexOffset:X}; MULT 0x{destination.MultRva:X8}; reader 0x{destination.ReaderRva:X8} (new path candidate only)");
        if (mode is not null)
            Console.WriteLine($"Destination-mode container offset 0x{mode.Offset:X}; mode-2 reader 0x{mode.Mode2ReaderRva:X8}; {mode.WriterSites} masked writer(s) (static candidate only)");
        if (pitState is not null)
            Console.WriteLine($"Pit state/flag offsets 0x{pitState.StateOffset:X}/0x{pitState.PitFlagOffset:X}; reset writer 0x{pitState.ResetWriterRva:X8}; {pitState.CompareSites} state comparisons (static candidate only)");
        if (found is null || garage is null)
        {
            Console.Error.WriteLine($"spot tables unresolved: PitPos={found is not null}, GaragePos={garage is not null}");
            return 4;
        }
        Console.WriteLine($"PitPos global 0x{found.PitPosRva:X8}; count global 0x{found.CountRva:X8}; " +
                          $"{found.IndependentReaders} agreeing {found.ReaderPath} readers (static candidate only)");
        Console.WriteLine($"GaragePos global 0x{garage.GaragePosRva:X8}; MULT global 0x{garage.MultRva:X8}; " +
                          $"count global 0x{garage.CountRva:X8} (reader/writer agreement)");
        return 0;
    }

    private static int SpotLive(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--watch"))
            throw new ArgumentException("spot-live [--watch]");
        var watch = args.Length == 1;
        if (Process.GetProcesses().Any(p =>
                p.ProcessName.Contains("EasyAnti", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains("start_protected", StringComparison.OrdinalIgnoreCase)))
            throw new GateException("anticheat process present; refusing even read-only inspection");
        using var process = Process.GetProcessesByName(GameSession.ProcessName).FirstOrDefault()
            ?? throw new GateException($"{GameSession.ProcessName} is not running");
        var imagePath = process.MainModule?.FileName
            ?? throw new GateException("cannot read the game's image path");
        var hash = GameSession.Sha256File(imagePath);
        var capture = ModuleDumper.Capture(process);
        if (capture.UnreadableBytes != 0)
            throw new MemoryAccessException($"mapped module has {capture.UnreadableBytes} unreadable bytes; refusing");
        using (var disk = File.OpenRead(imagePath))
        {
            var diskHeader = new byte[0x1000];
            disk.ReadExactly(diskHeader);
            if (!ModuleDumper.SamePeIdentity(diskHeader, capture.Image))
                throw new GateException("running mapped image and executable on disk disagree; refusing to cache by disk hash");
        }
        var pit = SpotTableResolver.FindPitPos(capture.Image);
        var garage = SpotTableResolver.FindGaragePos(capture.Image);
        var normal = SpotTableResolver.FindNormalSlotTable(capture.Image);
        var selection = SpotTableResolver.FindNormalSlotSelection(capture.Image);
        var trackFlags = SpotTableResolver.FindTrackLimitsFlags(capture.Image);
        var destination = SpotTableResolver.FindIndexedDestination(capture.Image);
        var destinationMode = SpotTableResolver.FindDestinationMode(capture.Image);
        var pitState = SpotTableResolver.FindPitState(capture.Image);
        var pitSpeedRule = SpotTableResolver.FindPitSpeedRule(capture.Image);
        var stride = SpotTableResolver.FindContainerStride(capture.Image);
        var array = SpotTableResolver.FindContainerArrayBase(capture.Image);
        var owner = SpotTableResolver.FindControlOwner(capture.Image);
        var lateral = SpotTableResolver.FindLateralSign(capture.Image);
        var dimensions = SpotTableResolver.FindVehicleDimensions(capture.Image);
        var indices = SpotTableResolver.FindSpecialSlotIndices(capture.Image);
        Console.WriteLine($"build {hash} pid {process.Id}; mapped image 0x{capture.Image.Length:X}");
        if (pit is null || garage is null || normal is null || trackFlags is null || destinationMode is null || pitState is null || pitSpeedRule is null || stride is null || array is null || owner is null || lateral is null || dimensions is null || indices is null ||
            (pit.ReaderPath == SpotTableResolver.PitPosReaderPath.SpecialSlotBranch &&
             (selection is null || destination is null)))
        {
            Console.WriteLine($"static resolution incomplete: PitPos={pit is not null}, GaragePos={garage is not null}, normal={normal is not null}, selection={selection is not null}, indexed destination={destination is not null}, destination mode={destinationMode is not null}, pit state={pitState is not null}, track flags={trackFlags is not null}, stride={stride is not null}, array={array is not null}, owner={owner is not null}, lateral={lateral is not null}, dimensions={dimensions is not null}, indices/transform={indices is not null}");
            return 4;
        }
        Console.WriteLine($"container stride 0x{stride.Stride:X} ({stride.Sites} separated sites)");
        Console.WriteLine($"container array base 0x{array.ArrayBaseRva:X8} ({array.Sites} separated sites)");
        Console.WriteLine($"control-owner offset 0x{owner.Offset:X} ({owner.WitnessSites} comparison sites)");
        Console.WriteLine($"lateral-sign offset 0x{lateral.Offset:X} ({lateral.WitnessSites} scalar reads)");
        Console.WriteLine($"vehicle length/width offsets 0x{dimensions.LengthOffset:X}/0x{dimensions.WidthOffset:X} ({dimensions.PairedSites} paired sites)");
        Console.WriteLine($"slot/pit/garage indices 0x{indices.SlotIndex:X}/0x{indices.PitIndex:X}/0x{indices.GarageIndex:X}; transform 0x{indices.TransformRva:X8} ({indices.Sites} separated sites)");
        Console.WriteLine($"track-limits derived flags 0x{trackFlags.FirstRva:X8}..0x{trackFlags.FirstRva + 2:X8} ({trackFlags.FirstReaders}/{trackFlags.SecondReaders}/{trackFlags.ThirdReaders} readers)");
        Console.WriteLine($"destination-mode offset 0x{destinationMode.Offset:X} (mode-2 reader 0x{destinationMode.Mode2ReaderRva:X8}, {destinationMode.WriterSites} writer(s))");
        Console.WriteLine($"pit state/flag offsets 0x{pitState.StateOffset:X}/0x{pitState.PitFlagOffset:X} (reset writer 0x{pitState.ResetWriterRva:X8}, {pitState.CompareSites} comparisons)");
        Console.WriteLine($"pit-speed Flag Rules candidate 0x{pitSpeedRule.FlagRulesRva:X8} (gate reader 0x{pitSpeedRule.SpeedGateRva:X8})");
        using var memory = ProcessMemory.OpenRead(process.Id);
        var baseAddress = capture.ModuleBase;
        var pitPointer = memory.ReadUInt64(baseAddress + pit.PitPosRva);
        var garagePointer = memory.ReadUInt64(baseAddress + garage.GaragePosRva);
        var normalPointer = memory.ReadUInt64(baseAddress + normal.TableRva);
        var mult = memory.ReadInt32(baseAddress + garage.MultRva);
        var count = memory.ReadInt32(baseAddress + garage.CountRva);
        var pitCount = memory.ReadInt32(baseAddress + pit.CountRva);
        Console.WriteLine($"PitPos {pit.ReaderPath}: global 0x{pit.PitPosRva:X8} -> 0x{pitPointer:X}; count {pitCount}");
        Console.WriteLine($"GaragePos: global 0x{garage.GaragePosRva:X8} -> 0x{garagePointer:X}; MULT {mult}; COUNT {count}");
        Console.WriteLine($"normal slot: global 0x{normal.TableRva:X8} -> 0x{normalPointer:X}");
        var slotZero = baseAddress + array.ArrayBaseRva;
        var resolvedSlot = memory.ReadInt32(slotZero + indices.SlotIndex);
        if (resolvedSlot is < 0 or >= 0x68)
            throw new GateException($"resolved player slot {resolvedSlot} is outside ordinary range");
        var playerSlot = slotZero + (ulong)resolvedSlot * stride.Stride;
        if (memory.ReadInt32(playerSlot + indices.SlotIndex) != resolvedSlot)
            throw new GateException("resolved player slot does not identify itself");
        var playerPit = memory.ReadInt32(playerSlot + indices.PitIndex);
        var playerGarage = memory.ReadInt32(playerSlot + indices.GarageIndex);
        Console.WriteLine($"resolved player slot {resolvedSlot}: pit index {playerPit}, garage index {playerGarage} (read-only)");
        Console.WriteLine($"resolved player control owner/mode: {memory.ReadInt32(playerSlot + owner.Offset)}/{memory.ReadInt32(playerSlot + destinationMode.Offset)} (read-only snapshot)");
        Console.WriteLine($"resolved player pit state/flag: {memory.ReadInt32(playerSlot + pitState.StateOffset)}/{memory.ReadByte(playerSlot + pitState.PitFlagOffset)} (read-only snapshot)");
        Console.WriteLine($"Flag Rules live value: {memory.ReadInt32(baseAddress + pitSpeedRule.FlagRulesRva)} (read-only snapshot)");
        if (playerPit is < 0 or >= 0x68 || playerGarage < 0 || playerGarage >= count)
            throw new GateException("resolved player's spot indices are outside live table bounds");
        if (garagePointer == 0 || count is < 1 or > 10000)
            throw new GateException("garage spot table pointer/count is invalid");
        var garageEntry = memory.ReadBytes(garagePointer + (ulong)playerGarage * 32, 24);
        Console.WriteLine($"garage[{playerGarage}] position: {string.Join(", ", Enumerable.Range(0, 3).Select(i => BitConverter.ToSingle(garageEntry, i * 4).ToString("F3")))} (last observed lookup input, not final arrival)");
        Console.WriteLine($"track-limits live derived bytes: {memory.ReadByte(baseAddress + trackFlags.FirstRva)}/{memory.ReadByte(baseAddress + trackFlags.FirstRva + 1)}/{memory.ReadByte(baseAddress + trackFlags.FirstRva + 2)} (read-only; no effect inferred)");
        if (destination is not null)
        {
            var destinationPointer = memory.ReadUInt64(baseAddress + destination.TableRva);
            var entryIndex = playerPit * 3 + 2;
            if (destinationPointer == 0 || destinationPointer % 4 != 0 ||
                mult is < 1 or > 10000 || entryIndex >= (long)mult * 3)
                throw new GateException("indexed destination pointer or selected entry is implausible");
            for (var mode = 0; mode < 3; mode++)
            {
                var entry = memory.ReadBytes(destinationPointer + (ulong)(playerPit * 3 + mode) * 32, 24);
                Console.WriteLine($"indexed destination mode {mode}: {string.Join(", ", Enumerable.Range(0, 6).Select(i => BitConverter.ToSingle(entry, i * 4).ToString("F3")))} (position/orientation; not final arrival)");
            }
        }
        if (selection is null)
            Console.WriteLine("normal-slot selection unresolved; selected table is unknown");
        else
        {
            var enabled = memory.ReadInt32(baseAddress + selection.SwitchRva);
            var start = memory.ReadUInt64(baseAddress + selection.OverrideStartRva);
            var end = memory.ReadUInt64(baseAddress + selection.OverrideEndRva);
            var validVector = end >= start && (end - start) % 32 == 0 &&
                              (end - start) / 32 <= 0x68;
            Console.WriteLine($"normal-slot selection: switch {enabled}; override entries {(validVector ? ((end - start) / 32).ToString() : "invalid span")}; selected source {(enabled == 0 || start == end ? "default table" : validVector ? "override vector" : "unknown")}");
            if (!validVector || enabled is not (0 or 1))
                throw new GateException("normal-slot override state is implausible; refusing candidate cache");
            if (enabled != 0 && start != end)
                throw new GateException("ordinary slots currently read the override vector, not the resolved default table; refusing candidate cache");
        }
        var sharedOffsets = Path.Combine("offsets", "shared-memory.json");
        if (File.Exists(sharedOffsets))
        {
            try
            {
                using var shared = new SharedMemoryReader(SharedMemoryOffsets.Load(sharedOffsets));
                var player = shared.Read();
                Console.WriteLine(player is null
                    ? "shared memory: no active player vehicle"
                    : $"shared memory: {player.TrackName} / {player.VehicleName}; " +
                      $"pose ({player.Pose.Position.X:F3}, {player.Pose.Position.Y:F3}, {player.Pose.Position.Z:F3})");
            }
            catch (SharedMemoryException ex)
            {
                Console.WriteLine($"shared memory unavailable: {ex.Message}");
            }
        }
        if (watch)
        {
            var timer = Stopwatch.StartNew();
            var lastOwner = memory.ReadInt32(playerSlot + owner.Offset);
            var lastMode = memory.ReadInt32(playerSlot + destinationMode.Offset);
            var lastPit = memory.ReadInt32(playerSlot + indices.PitIndex);
            var lastGarage = memory.ReadInt32(playerSlot + indices.GarageIndex);
            if (lastOwner != 1)
                throw new GateException("Drive watch requires the player car in the garage under Ai control");
            TimeSpan? playerSince = null;
            Console.WriteLine("read-only transition watch armed for 45 s; no profile cache or game writes");
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                Thread.Sleep(5);
                var nextOwner = memory.ReadInt32(playerSlot + owner.Offset);
                var nextMode = memory.ReadInt32(playerSlot + destinationMode.Offset);
                var nextPit = memory.ReadInt32(playerSlot + indices.PitIndex);
                var nextGarage = memory.ReadInt32(playerSlot + indices.GarageIndex);
                if (nextOwner != lastOwner || nextMode != lastMode ||
                    nextPit != lastPit || nextGarage != lastGarage)
                    Console.WriteLine($"{timer.Elapsed.TotalSeconds:F3}s owner {lastOwner}->{nextOwner}; mode {lastMode}->{nextMode}; pit {lastPit}->{nextPit}; garage {lastGarage}->{nextGarage}");
                lastOwner = nextOwner;
                lastMode = nextMode;
                lastPit = nextPit;
                lastGarage = nextGarage;
                if (nextOwner == 0 && playerSince is null) playerSince = timer.Elapsed;
                if (playerSince is not null && timer.Elapsed - playerSince.Value > TimeSpan.FromSeconds(2))
                {
                    Console.WriteLine($"settled player mode {nextMode}; read-only watch complete");
                    return 0;
                }
            }
            Console.WriteLine("no completed AI-to-player transition observed; no inference made");
            return 4;
        }
        var candidatePath = Path.Combine("artifacts", $"{hash[..8]}.candidate.json");
        var candidateProbeMatched = true;
        if (File.Exists(candidatePath))
        {
            try
            {
            var candidate = OffsetProfile.Load(candidatePath);
            var probe = candidate.Probe;
            var probeRva = probe.Rva.Require("candidate probe");
            if (!string.Equals(candidate.ExecutableSha256, hash, StringComparison.OrdinalIgnoreCase) ||
                candidate.SizeOfImage != (ulong)capture.Image.Length ||
                probeRva > (ulong)(capture.Image.Length - probe.Bytes.Length))
                throw new GateException("read-only candidate profile identity does not match the running image");
            var liveProbe = capture.Image.AsSpan((int)probeRva, probe.Bytes.Length);
            if (!liveProbe.SequenceEqual(probe.Bytes))
                throw new GateException($"candidate probe mismatch at 0x{probeRva:X}: expected {Convert.ToHexString(probe.Bytes)}, live {Convert.ToHexString(liveProbe)}");
            var containers = candidate.Containers;
            if (containers.Stride != stride.Stride)
                throw new GateException($"candidate container stride 0x{containers.Stride:X} disagrees with mapped-code stride 0x{stride.Stride:X}");
            if (containers.ArrayBase.Require("candidate container array base") != array.ArrayBaseRva)
                throw new GateException("candidate container array base disagrees with mapped code");
            if (probeRva != indices.TransformRva ||
                containers.Field("slotIndex").Offset != indices.SlotIndex ||
                containers.Field("pitIndex").Offset != indices.PitIndex ||
                containers.Field("garageIndex").Offset != indices.GarageIndex)
                throw new GateException("candidate transform or slot/pit/garage offsets disagree with mapped code");
            if (containers.Field("controlOwner").Offset != owner.Offset)
                throw new GateException("candidate control-owner offset disagrees with mapped code");
            if (containers.Field("lateralSign").Offset != lateral.Offset)
                throw new GateException("candidate lateral-sign offset disagrees with mapped code");
            if (containers.Field("vehicleLength").Offset != dimensions.LengthOffset ||
                containers.Field("vehicleWidth").Offset != dimensions.WidthOffset)
                throw new GateException("candidate vehicle-dimension offsets disagree with mapped code");
            if (containers.Field("pitState").Offset != pitState.StateOffset ||
                containers.Field("pitFlag").Offset != pitState.PitFlagOffset)
                throw new GateException("candidate pit-state or pit-flag offset disagrees with mapped code");
            var ruleFlags = candidate.Rules.TrackLimits.DerivedFlags;
            if (ruleFlags.Count != 3 || ruleFlags.Where((field, i) =>
                    field.Rva.Confidence == Confidence.Unresolved ||
                    field.Rva.Value != trackFlags.FirstRva + (uint)i).Any())
                throw new GateException("candidate track-limits flags disagree with mapped initializer/readers");
            var reportedSlot = memory.ReadInt32(containers.FieldAddress(baseAddress, 0, "slotIndex"));
            if (reportedSlot is < 0 or >= 0x68)
                throw new GateException($"candidate slot index {reportedSlot} is outside ordinary range");
            var slot = memory.ReadInt32(containers.FieldAddress(baseAddress, reportedSlot, "slotIndex"));
            if (slot != reportedSlot)
                throw new GateException($"candidate slot index disagrees ({reportedSlot} vs {slot})");
            var pitIndex = memory.ReadInt32(containers.FieldAddress(baseAddress, slot, "pitIndex"));
            var garageIndex = memory.ReadInt32(containers.FieldAddress(baseAddress, slot, "garageIndex"));
            var liveOwner = memory.ReadInt32(containers.FieldAddress(baseAddress, slot, "controlOwner"));
            Console.WriteLine($"candidate container: slot {slot}; pit index {pitIndex}; garage index {garageIndex}; owner {liveOwner} (read-only, unvalidated offsets)");
            if (pitIndex is >= 0 and < 0x68)
            {
                var entry = memory.ReadBytes(normalPointer + (ulong)pitIndex * 32, 24);
                var xyz = Enumerable.Range(0, 3).Select(i => BitConverter.ToSingle(entry, i * 4));
                Console.WriteLine($"normal[{pitIndex}] position: {string.Join(", ", xyz.Select(v => v.ToString("F3")))} (not a Drive-consumption proof)");
            }
            }
            catch (Exception ex) when (ex is GateException or OffsetProfileException or MemoryAccessException)
            {
                candidateProbeMatched = false;
                Console.WriteLine($"candidate container inspection refused: {ex.Message}");
            }
        }
        if (pitPointer == 0 || garagePointer == 0 || normalPointer == 0 ||
            pitCount is < 1 or > 10000 || mult is < 1 or > 10000 || count is < 1 or > 10000 ||
            (pitPointer | garagePointer | normalPointer) % 4 != 0)
        {
            Console.WriteLine("live sanity FAILED: null/misaligned table or implausible count");
            return 4;
        }
        foreach (var pointer in new[] { pitPointer, garagePointer, normalPointer })
        {
            memory.RequireWritableDataPage(pointer, 32); // query only; handle has no write access
            memory.ReadBytes(pointer, 32);
        }
        Console.WriteLine("limited live sanity passed: three readable, writable-data entries; consumer/control unverified; no write performed");
        if (!candidateProbeMatched)
        {
            Console.WriteLine("candidate cache skipped: live probe is modified or unverified");
            return 4;
        }
        var cacheDir = Path.Combine("artifacts", "spot-candidates");
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, $"{hash}.candidate.json");
        var cache = JsonSerializer.Serialize(new
        {
            executableSha256 = hash,
            sizeOfImage = capture.Image.Length,
            pitPos = new { rva = pit.PitPosRva, countRva = pit.CountRva, readerPath = pit.ReaderPath.ToString() },
            garagePos = new { rva = garage.GaragePosRva, multRva = garage.MultRva, countRva = garage.CountRva },
            normalSlot = new { rva = normal.TableRva },
            indexedDestination = destination is null ? null : new { rva = destination.TableRva, readerRva = destination.ReaderRva },
            destinationModeOffset = destinationMode.Offset,
            pitState = new { stateOffset = pitState.StateOffset, flagOffset = pitState.PitFlagOffset },
            trackLimitsFlags = new[] { trackFlags.FirstRva, trackFlags.FirstRva + 1, trackFlags.FirstRva + 2 },
            containerStride = stride.Stride,
            containerArrayBase = array.ArrayBaseRva,
            controlOwnerOffset = owner.Offset,
            lateralSignOffset = lateral.Offset,
            vehicleDimensions = new { lengthOffset = dimensions.LengthOffset, widthOffset = dimensions.WidthOffset },
            containerIndices = new { slot = indices.SlotIndex, pit = indices.PitIndex, garage = indices.GarageIndex },
            transformRva = indices.TransformRva,
            status = "candidate-only; placement not enabled",
        }, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(cachePath))
        {
            if (File.ReadAllText(cachePath) != cache)
                throw new GateException($"candidate cache disagrees with live resolution: {cachePath}");
        }
        else File.WriteAllText(cachePath, cache);
        Console.WriteLine($"candidate cache: {cachePath} (not a usable offset profile)");
        return 0;
    }

    private static int SpotTelemetry(string[] args)
    {
        if (args.Length != 0) throw new ArgumentException("spot-telemetry");
        using var shared = new SharedMemoryReader(SharedMemoryOffsets.Load(
            Path.Combine("offsets", "shared-memory.json")));
        var player = shared.Read() ?? throw new GateException("no active player vehicle in SDK shared memory");
        var p = player.Pose.Position;
        var yaw = Geometry.ExtractYawFromOrientationRows(player.Pose.Row0, player.Pose.Row2);
        Console.WriteLine($"{player.TrackName} / {player.VehicleName}; pose ({p.X:F6}, {p.Y:F6}, {p.Z:F6}); yaw {yaw:F6}; gear {player.Gear}");
        return 0;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"ERROR: unknown verb '{verb}'");
        Help();
        return 2;
    }

    private static void Help() => Console.WriteLine(
        """
        inactive-reset-reanchor -- maintainer tool, not shipped to users

          inactive-reset-reanchor dump [file.bin]
              Capture the game's mapped module (file offset == RVA). Keep it:
              it is the "before" image for the NEXT patch, and you cannot make
              one retroactively.

          inactive-reset-reanchor refs <dump.bin> <hex-rva>...
              List candidate code sites for Ghidra review; matches are not proof.

          inactive-reset-reanchor code-map <old.bin> <new.bin> <hex-rva>...
              Find a unique masked-code counterpart for static inspection.

          inactive-reset-reanchor spot-resolve <mapped-dump.bin>
              Report candidate PitPos, GaragePos, and normal-slot tables from mapped code.
              A candidate alone never enables placement.

          inactive-reset-reanchor spot-live [--watch]
              Resolve those candidates from the running mapped image and check live tables.
              Read-only; caches candidate RVAs separately and never enables a profile.
              --watch observes owner/mode changes for 45 seconds; never caches or writes.

          inactive-reset-reanchor spot-telemetry
              Read the active player's pose from LMU's SDK shared memory only.

          inactive-reset-reanchor spot-probe <full-build-sha256> <mode:0..2> <x|z|yaw> <delta-metres-or-radians> [--arm]
              Maintainer-only indexed-entry causal test on the exact Barcelona checkpoint car.
              Dry-run without --arm; --arm writes one float until Drive/timeout, then verifies restore.

          inactive-reset-reanchor spot-place-test <full-build-sha256> cp-023328 <D> <L> <H> <yaw-bias> [--arm]
              Experimental matching-car checkpoint placement using measured indexed rest constants.
              Dry-run by default; never enables or publishes an offset profile.

          inactive-reset-reanchor spot-place-rules-test <full-build-sha256> cp-023328 <D> <L> <H> <yaw-bias> [--arm]
              As above, with temporary Flag Rules 2->0->2 and up to 120 s pit-state wait.
              Maintainer-only; exact bytes and rule value are verified after restoration.

          inactive-reset-reanchor reanchor --old-dump <before.bin>
                                           --base <offsets/OLD.json>
                                           [--new-dump <after.bin> --new-exe <game.exe>]
                                           [--out <file>] [--infer-adjacent]
              Captures the running game, re-derives every address against the
              old image, and writes a new profile. Anything it cannot resolve
              is marked confidence "U", so the tool refuses rather than reading
              the wrong place.

              Three techniques, cheapest first:
                signature     masked byte search. Finds a function that moved.
                              Cannot find one that was RECOMPILED.
                references    majority vote across the instructions that
                              reference a datum. Fails when those instructions
                              were themselves recompiled.
                value anchor  finds a datum by the CONTENT around it. Works for
                              constants, which survive a recompilation; never
                              for live state, whose content differs between two
                              captures anyway.

              --infer-adjacent
                  Last resort, OPT-IN. For a datum still unresolved, propose
                  old+delta where several nearby addresses that WERE re-derived
                  all moved by the same delta. Written as confidence "I", never
                  "E". This is a guess and the rest of this tool is not: on
                  0F6DCAC1 one region moved -0x2B550 and another -0x2C010, so a
                  single global shift would have been wrong for half the
                  profile. VERIFY before publishing.

        Patch day:

          1. reanchor against your last "before" dump
          2. check the result: place at two checkpoints whose RANGE from the pit
             spot differs. A wrong constant scales with range; a wrong offset
             does not. One checkpoint cannot tell you which you have.
          3. commit the new offsets/<HASH8>.json and push

        Step 3 IS the release. The app fetches profiles by build hash from the
        repository, so a push is all a user needs. There is no binary to rebuild
        and nothing for them to install.

        What this does NOT cover: a patch that retunes placement without moving
        anything. Those values are read live from the running engine and need no
        profile change at all. If placement is wrong and reanchor reports every
        address resolved, you are looking at a behaviour change, not a move --
        read GetPitDestination, do not re-derive addresses harder.
        """);
}
