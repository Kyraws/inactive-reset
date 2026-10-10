using System.Buffers.Binary;

namespace InactiveReset.Core;

/// <summary>
/// Resolve candidate spot-table globals from a mapped LMU image without an old
/// dump. Structural matches are not permission to place the car: callers still
/// need the other addresses, session gates, and a validated consumer.
/// </summary>
public static class SpotTableResolver
{
    public enum PitPosReaderPath { OrdinarySlotLoop, SpecialSlotBranch }
    public sealed record Candidate(uint PitPosRva, uint CountRva, int IndependentReaders,
                                   PitPosReaderPath ReaderPath);
    public sealed record GarageCandidate(uint GaragePosRva, uint MultRva, uint CountRva);
    public sealed record NormalSlotCandidate(uint TableRva, int WriterSites);
    public sealed record NormalSlotSelectionCandidate(uint SwitchRva, uint OverrideStartRva,
                                                      uint OverrideEndRva, uint DefaultTableRva);
    public sealed record TrackLimitsFlagsCandidate(uint FirstRva, int FirstReaders,
                                                   int SecondReaders, int ThirdReaders);
    public sealed record IndexedDestinationCandidate(uint TableRva, uint PitIndexOffset,
                                                     uint MultRva, uint ReaderRva);
    public sealed record DestinationModeCandidate(uint Offset, uint Mode2ReaderRva, int WriterSites);
    public sealed record PitStateCandidate(uint StateOffset, uint PitFlagOffset,
                                           uint ResetWriterRva, int CompareSites);
    public sealed record PitSpeedRuleCandidate(uint FlagRulesRva, uint SpeedGateRva);

    /// <summary>Find the Flag Rules global read immediately after the pit-speed comparison.</summary>
    public static PitSpeedRuleCandidate? FindPitSpeedRule(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;
        ReadOnlySpan<byte> speedLoad = [0xF3, 0x0F, 0x10, 0x8B];
        ReadOnlySpan<byte> speedCompare = [0x0F, 0x2F, 0xCA, 0x76];
        PitSpeedRuleCandidate? found = null;
        foreach (var section in code)
        for (var at = (int)section.Start; at + 22 <= section.End; at++)
        {
            if (!image.Slice(at, 4).SequenceEqual(speedLoad) ||
                !image.Slice(at + 8, 4).SequenceEqual(speedCompare) ||
                image[at + 13] != 0x83 || image[at + 14] != 0x3D ||
                image[at + 19] != 0 || image[at + 20] != 0x74 ||
                !TryRip(image, at + 0x0D, 2, 7, out var flag) ||
                !InData(data, flag, 4)) continue;
            // Both exits bypass the penalty call and clear the same field that was read.
            var clear = at + 13 + (sbyte)image[at + 12];
            var field = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 4, 4));
            if (clear != at + 22 + (sbyte)image[at + 21] || clear < at + 27 ||
                clear + 10 > section.End || image[clear - 5] != 0xE8 ||
                image[clear] != 0xC7 || image[clear + 1] != 0x83 ||
                field is < 0x20 or > 0x50000 ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(clear + 2, 4)) != field ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(clear + 6, 4)) != 0) continue;
            if (found is not null) return null;
            found = new PitSpeedRuleCandidate(flag, (uint)at);
        }
        return found;
    }

    /// <summary>Resolve the tuning block from its search and yaw consumers, independent of values.</summary>
    public static uint? FindTuningBlock(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;
        ReadOnlySpan<byte> maxMultiply = [0xF3, 0x0F, 0x59, 0x3D];
        ReadOnlySpan<byte> startMultiply = [0xF3, 0x0F, 0x59, 0x35];
        ReadOnlySpan<byte> stepMultiply = [0xF3, 0x44, 0x0F, 0x59, 0x0D];
        ReadOnlySpan<byte> searchCompare = [0x0F, 0x2F, 0xF7, 0x77];
        ReadOnlySpan<byte> yawLoad = [0xF3, 0x0F, 0x10, 0x0D];
        ReadOnlySpan<byte> yawConvert = [0x0F, 0x28, 0xC6, 0xF3, 0x0F, 0x59, 0x0D];
        ReadOnlySpan<byte> yawScale = [0x45, 0x0F, 0x28, 0xDA, 0xF3, 0x44, 0x0F, 0x59, 0x1D];
        uint? found = null;
        foreach (var section in code)
        for (var at = (int)section.Start; at + 30 <= section.End; at++)
        {
            if (!image.Slice(at, 4).SequenceEqual(maxMultiply) ||
                !image.Slice(at + 8, 4).SequenceEqual(startMultiply) ||
                !image.Slice(at + 16, 5).SequenceEqual(stepMultiply) ||
                !image.Slice(at + 25, 4).SequenceEqual(searchCompare) ||
                !TryRip(image, at, 4, 8, out var maximum) ||
                !TryRip(image, at + 8, 4, 8, out var start) ||
                !TryRip(image, at + 16, 5, 9, out var step) ||
                !InData(data, start, 24) || maximum != start + 8 || step != start + 4) continue;
            var yawReaders = 0;
            for (var yaw = Math.Max((int)section.Start, at - 0x100); yaw + 32 <= at; yaw++)
            {
                if (!image.Slice(yaw, 4).SequenceEqual(yawLoad) ||
                    !image.Slice(yaw + 8, 7).SequenceEqual(yawConvert) ||
                    !image.Slice(yaw + 19, 9).SequenceEqual(yawScale) ||
                    !TryRip(image, yaw, 4, 8, out var degrees) || degrees != start + 16 ||
                    !TryRip(image, yaw + 23, 5, 9, out var scale) || scale != start + 20) continue;
                yawReaders++;
            }
            if (yawReaders == 0) continue;
            if (yawReaders != 1 || found is not null) return null;
            found = start;
        }
        return found;
    }

    public static void RequirePitState(ReadOnlySpan<byte> image, ContainerSpec profile)
    {
        var found = FindPitState(image);
        var state = profile.Field("pitState");
        var flag = profile.Field("pitFlag");
        if (found is null || state.Base != OffsetBase.Container ||
            flag.Base != OffsetBase.Container || state.Offset != found.StateOffset ||
            flag.Offset != found.PitFlagOffset)
            throw new GateException(
                "the live pit-state/flag layout does not match the offset profile; placement is disabled");
    }

    /// <summary>Resolve pit state and adjacent pit flag from reset writes and state readers.</summary>
    public static PitStateCandidate? FindPitState(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var writers = new List<(uint Site, uint State, uint Flag)>();
        var comparisons = new Dictionary<uint, List<int>>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 16; at++)
        {
            if (image[at] == 0x89 && image[at + 1] == 0xAF &&
                image[at + 6] == 0x66 && image[at + 7] == 0xC7 &&
                image[at + 8] == 0x87 && image[at + 13] == 0 && image[at + 14] == 0)
            {
                var state = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 2, 4));
                var flag = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 9, 4));
                if (state is >= 0x8000 and < 0xB000 && flag == state + 0x10)
                    writers.Add(((uint)at, state, flag));
            }
            if (image[at] == 0x83 && (image[at + 1] & 0xF8) == 0xB8 &&
                image[at + 1] != 0xBC && image[at + 6] is >= 1 and <= 6)
            {
                var state = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 2, 4));
                if (state is < 0x8000 or >= 0xB000) continue;
                if (!comparisons.TryGetValue(state, out var sites)) comparisons[state] = sites = [];
                sites.Add(at);
            }
        }
        var matches = writers.Where(w => comparisons.TryGetValue(w.State, out var sites) &&
                                         sites.Count >= 5 && sites[^1] - sites[0] > 0x1000)
                             .ToArray();
        return matches.Length == 1
            ? new PitStateCandidate(matches[0].State, matches[0].Flag,
                                    matches[0].Site, comparisons[matches[0].State].Count)
            : null;
    }

    /// <summary>Resolve the container's destination mode from its mode-2 branch and masked writer.</summary>
    public static DestinationModeCandidate? FindDestinationMode(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var readers = new List<(uint Site, uint Offset)>();
        var writers = new Dictionary<uint, int>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 36; at++)
        {
            if (image[at] == 0x83 && image[at + 1] == 0xBA && image[at + 6] == 2)
            {
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 2, 4));
                if (offset is >= 0x8F00 and < 0x9200 &&
                    image.Slice(at + 18, 8).SequenceEqual(new byte[] {
                        0xC7, 0x44, 0x24, 0x20, 0x02, 0x00, 0x00, 0x00 }) &&
                    image[at + 26] == 0xE8)
                    readers.Add(((uint)at, offset));
            }
            if (image[at] == 0x83 && image[at + 1] == 0xE1 && image[at + 2] == 3 &&
                image[at + 3] == 0x89 && image[at + 4] == 0x88)
            {
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 5, 4));
                if (offset is >= 0x8F00 and < 0x9200)
                    writers[offset] = writers.GetValueOrDefault(offset) + 1;
            }
        }
        var matches = readers.Where(r => writers.GetValueOrDefault(r.Offset) >= 1).ToArray();
        return matches.Length == 1
            ? new DestinationModeCandidate(matches[0].Offset, matches[0].Site,
                                           writers[matches[0].Offset]) : null;
    }

    /// <summary>Identify the newer pit-index * 3 + mode destination entry reader.</summary>
    public static IndexedDestinationCandidate? FindIndexedDestination(ReadOnlySpan<byte> image)
    {
        var indices = FindSpecialSlotIndices(image);
        var garage = FindGaragePos(image);
        if (indices is null || garage is null || !TrySections(image, out var data, out var code))
            return null;
        var matches = new List<IndexedDestinationCandidate>();
        foreach (var section in code)
        for (var at = (int)section.Start + 40; at < (int)section.End - 80; at++)
        {
            if (!image.Slice(at, 15).SequenceEqual(new byte[] {
                    0x48, 0x8D, 0x0C, 0x40, 0x48, 0x63, 0x44, 0x24, 0x50,
                    0x48, 0x03, 0xC8, 0x48, 0x8B, 0x05 }) ||
                !TryRip(image, at + 12, 3, 7, out var table) || !InData(data, table, 8) ||
                !image.Slice(at + 19, 9).SequenceEqual(new byte[] {
                    0x48, 0xC1, 0xE1, 0x05, 0xF2, 0x0F, 0x10, 0x04, 0x01 }) ||
                !image.Slice(at + 39, 3).SequenceEqual(new byte[] { 0x48, 0x8B, 0x05 }) ||
                !TryRip(image, at + 39, 3, 7, out var second) || second != table) continue;
            var before = image.Slice(at - 40, 40);
            var hasPit = false;
            var hasMult = false;
            for (var offset = 0; offset < before.Length - 7; offset++)
            {
                var site = at - 40 + offset;
                hasPit |= before[offset] == 0x8B && before[offset + 1] == 0x8B &&
                          BinaryPrimitives.ReadUInt32LittleEndian(before.Slice(offset + 2, 4)) == indices.PitIndex;
                hasMult |= before[offset] == 0x8B && before[offset + 1] == 0x05 &&
                           TryRip(image, site, 2, 6, out var mult) && mult == garage.MultRva;
            }
            if (hasPit && hasMult)
                matches.Add(new IndexedDestinationCandidate(table, indices.PitIndex,
                                                            garage.MultRva, (uint)at));
        }
        return matches.Count == 1 &&
               HasIndexedSpotFallback(image, code, matches[0]) &&
               HasIndexedAssignmentLoop(image, code, matches[0], indices.TransformRva) &&
               HasMode2IndexedCaller(image, matches[0])
            ? matches[0] : null;
    }

    private static bool HasMode2IndexedCaller(ReadOnlySpan<byte> image,
                                              IndexedDestinationCandidate candidate)
    {
        var mode = FindDestinationMode(image);
        if (mode is null) return false;
        var call = (int)mode.Mode2ReaderRva + 26;
        if (call + 5 > image.Length || image[call] != 0xE8 ||
            !TryRip(image, call, 1, 5, out var helper) ||
            helper >= candidate.ReaderRva || candidate.ReaderRva - helper is < 0x20 or > 0x200)
            return false;
        return image.Slice((int)helper, 15).SequenceEqual(new byte[] {
            0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10,
            0x57, 0x48, 0x83, 0xEC, 0x20 });
    }

    private static bool HasIndexedAssignmentLoop(ReadOnlySpan<byte> image,
                                                 List<(uint Start, uint End)> code,
                                                 IndexedDestinationCandidate candidate,
                                                 uint transform)
    {
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 0x120; at++)
        {
            if (!image.Slice(at, 11).SequenceEqual(new byte[] {
                    0x48, 0x8D, 0x0C, 0x40, 0x48, 0xC1, 0xE1, 0x05,
                    0x48, 0x8B, 0x05 }) ||
                !TryRip(image, at + 8, 3, 7, out var table) || table != candidate.TableRva)
                continue;
            var copy = image.Slice(at + 15, 40);
            if (copy.IndexOf(new byte[] { 0xF2, 0x0F, 0x10, 0x74, 0x01, 0x20 }) < 0 ||
                copy.IndexOf(new byte[] { 0x44, 0x8B, 0x74, 0x01, 0x28 }) < 0 ||
                copy.IndexOf(new byte[] { 0xF2, 0x0F, 0x10, 0x44, 0x01, 0x2C }) < 0 ||
                copy.IndexOf(new byte[] { 0x44, 0x8B, 0x7C, 0x01, 0x34 }) < 0)
                continue;
            var callsTransform = false;
            for (var call = at + 40; call < at + 90; call++)
                callsTransform |= image[call] == 0xE8 &&
                                  TryRip(image, call, 1, 5, out var target) && target == transform;
            if (!callsTransform) continue;
            for (var writer = at + 90; writer < at + 0x120 - 6; writer++)
                if (image[writer] == 0x89 && image[writer + 1] == 0xBB &&
                    BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(writer + 2, 4)) ==
                    candidate.PitIndexOffset)
                    return true;
        }
        return false;
    }

    private static bool HasIndexedSpotFallback(ReadOnlySpan<byte> image,
                                               List<(uint Start, uint End)> code,
                                               IndexedDestinationCandidate candidate)
    {
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 90; at++)
        {
            if (Math.Abs(at - (long)candidate.ReaderRva) < 0x1000 ||
                !image.Slice(at, 3).SequenceEqual(new byte[] { 0x4C, 0x8B, 0x05 }) ||
                !TryRip(image, at, 3, 7, out var table) || table != candidate.TableRva)
                continue;
            var after = image.Slice(at + 7, 80);
            var triple = after.IndexOf(new byte[] { 0x4C, 0x8D, 0x0C, 0x40 });
            var stride = after.IndexOf(new byte[] { 0x49, 0xC1, 0xE1, 0x05 });
            var entry = after.IndexOf(new byte[] { 0xF2, 0x43, 0x0F, 0x10, 0x44, 0x01, 0x20 });
            if (triple >= 0 && stride > triple && entry > stride)
                return true;
        }
        return false;
    }

    /// <summary>Find the initialized flag pair and independent readers of all three bytes.</summary>
    public static TrackLimitsFlagsCandidate? FindTrackLimitsFlags(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;
        var initialized = new HashSet<uint>();
        var reads = new Dictionary<uint, HashSet<int>>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 9; at++)
        {
            if (image[at] == 0x66 && image[at + 1] == 0xC7 && image[at + 2] == 0x05 &&
                image[at + 7] == 1 && image[at + 8] == 1 &&
                TryRip(image, at, 3, 9, out var first) && InData(data, first, 3))
                initialized.Add(first);
            if (image[at] == 0x80 && image[at + 1] == 0x3D && image[at + 6] == 0 &&
                TryRip(image, at, 2, 7, out var target) && InData(data, target, 1))
            {
                if (!reads.TryGetValue(target, out var sites)) reads[target] = sites = [];
                sites.Add(at);
            }
        }
        var matches = initialized.Where(first =>
            reads.GetValueOrDefault(first)?.Count >= 1 &&
            reads.GetValueOrDefault(first + 1)?.Count >= 2 &&
            reads.GetValueOrDefault(first + 2)?.Count >= 1).ToArray();
        return matches.Length == 1
            ? new TrackLimitsFlagsCandidate(matches[0], reads[matches[0]].Count,
                                            reads[matches[0] + 1].Count,
                                            reads[matches[0] + 2].Count)
            : null;
    }
    public sealed record ContainerStrideCandidate(uint Stride, int Sites);
    public sealed record ContainerArrayCandidate(uint ArrayBaseRva, int Sites);
    public sealed record ControlOwnerCandidate(uint Offset, int WitnessSites);
    public sealed record LateralSignCandidate(uint Offset, int WitnessSites);
    public sealed record VehicleDimensionsCandidate(uint LengthOffset, uint WidthOffset,
                                                    int LengthReads, int WidthReads,
                                                    int PairedSites);

    public static void RequireVehicleDimensions(ReadOnlySpan<byte> image, ContainerSpec profile)
    {
        var found = FindVehicleDimensions(image);
        var length = profile.Field("vehicleLength");
        var width = profile.Field("vehicleWidth");
        if (found is null || length.Base != OffsetBase.Container ||
            width.Base != OffsetBase.Container || length.Offset != found.LengthOffset ||
            width.Offset != found.WidthOffset)
            throw new GateException(
                "the live vehicle-dimension layout does not match the offset profile; placement is disabled");
    }

    /// <summary>
    /// Identify the dominant adjacent scalar-load pair in the vehicle geometry
    /// region, with independent reads of both fields in separated code paths.
    /// ponytail: this still depends on the geometry region; live car-dimension
    /// plausibility and a matching-car placement are required before promotion.
    /// </summary>
    public static VehicleDimensionsCandidate? FindVehicleDimensions(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var reads = new List<(int Site, uint Offset, int BaseReg)>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 9; at++)
        {
            if (image[at] != 0xF3) continue;
            var op = image[at + 1] is >= 0x40 and <= 0x4F ? at + 1 : at;
            if (image[op + 1] != 0x0F || image[op + 2] != 0x10 ||
                (image[op + 3] & 0xC0) != 0x80 || (image[op + 3] & 7) == 4) continue;
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(op + 4, 4));
            if (offset is < 0x44000 or >= 0x46000 || offset % 4 != 0) continue;
            reads.Add((at, offset, (image[op + 3] & 7) +
                                   (op != at && (image[op] & 1) != 0 ? 8 : 0)));
        }
        var counts = reads.GroupBy(r => r.Offset).ToDictionary(g => g.Key, g => g.Count());
        var pairs = counts.Where(p => p.Value >= 10 && counts.GetValueOrDefault(p.Key + 4) >= 20)
            .Select(p => (Length: p.Key, LengthReads: p.Value,
                          WidthReads: counts[p.Key + 4]))
            .OrderByDescending(p => p.LengthReads + p.WidthReads).ToArray();
        if (pairs.Length == 0 || (pairs.Length > 1 &&
            (pairs[0].LengthReads + pairs[0].WidthReads) * 2 <=
            (pairs[1].LengthReads + pairs[1].WidthReads) * 3)) return null;
        var best = pairs[0];
        var paired = (from length in reads
                      from width in reads
                      where length.Offset == best.Length && width.Offset == best.Length + 4 &&
                            length.BaseReg == width.BaseReg &&
                            width.Site > length.Site && width.Site - length.Site <= 0x100
                      select length.Site).Distinct().Order().ToArray();
        if (paired.Length < 1 ||
            !reads.Any(r => r.Offset == best.Length && Math.Abs(r.Site - paired[0]) > 0x1000) ||
            !reads.Any(r => r.Offset == best.Length + 4 && Math.Abs(r.Site - paired[0]) > 0x1000))
            return null;
        return new VehicleDimensionsCandidate(best.Length, best.Length + 4,
                                              best.LengthReads, best.WidthReads, paired.Length);
    }

    public static LateralSignCandidate? FindLateralSign(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var strong = new List<(int Site, uint Offset)>();
        var reads = new List<(int Site, uint Offset)>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 58; at++)
        {
            if (image[at] != 0xF3 || image[at + 1] != 0x0F || image[at + 2] != 0x10 ||
                (image[at + 3] & 0xC0) != 0x80 || (image[at + 3] & 7) == 4) continue;
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 4, 4));
            if (offset is < 0x40000 or > 0x48000 || offset % 4 != 0) continue;
            reads.Add((at, offset));
            if (image[at + 3] != 0x87 ||
                !image.Slice(at + 8, 9).SequenceEqual(
                    new byte[] { 0x0F, 0x2F, 0xC6, 0x76, 0x16, 0xF3, 0x0F, 0x5C, 0x05 }) ||
                image[at + 21] != 0x0F || image[at + 22] != 0x2F ||
                image[at + 23] != 0x87 || image[at + 28] != 0x77 ||
                image[at + 29] != 0x22 || image[at + 30] != 0xE9 ||
                !image.Slice(at + 35, 4).SequenceEqual(new byte[] { 0xF3, 0x0F, 0x10, 0x87 }) ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 39, 4)) != offset ||
                !image.Slice(at + 43, 4).SequenceEqual(new byte[] { 0xF3, 0x0F, 0x58, 0x05 }) ||
                !image.Slice(at + 51, 3).SequenceEqual(new byte[] { 0x0F, 0x2F, 0x87 }) ||
                !image.Slice(at + 24, 4).SequenceEqual(image.Slice(at + 54, 4))) continue;
            strong.Add((at, offset));
        }
        if (strong.Count == 0 || strong.Any(s => s.Offset != strong[0].Offset) ||
            !reads.Any(r => r.Offset == strong[0].Offset &&
                            Math.Abs(r.Site - strong[0].Site) > 0x1000)) return null;
        return new LateralSignCandidate(strong[0].Offset,
            reads.Count(r => r.Offset == strong[0].Offset));
    }

    public static void RequireLateralSign(ReadOnlySpan<byte> image, ContainerSpec profile)
    {
        var found = FindLateralSign(image);
        var field = profile.Field("lateralSign");
        if (found is null || field.Base != OffsetBase.Container || field.Offset != found.Offset)
            throw new GateException(
                "the live lateral-sign layout does not match the offset profile; placement is disabled");
    }

    public static void RequireControlOwner(ReadOnlySpan<byte> image, ContainerSpec profile)
    {
        var found = FindControlOwner(image);
        var field = profile.Field("controlOwner");
        if (found is null || field.Base != OffsetBase.Container || field.Offset != found.Offset)
            throw new GateException(
                "the live control-owner layout does not match the offset profile; placement is disabled");
    }

    /// <summary>Find the AI-owner guard preceding handoff and slot placement.</summary>
    public static ControlOwnerCandidate? FindControlOwner(ReadOnlySpan<byte> image)
    {
        var indices = FindSpecialSlotIndices(image);
        if (indices is null || !TrySections(image, out _, out var code)) return null;
        var strong = new List<(int Site, uint Offset)>();
        var witnesses = new List<(int Site, uint Offset)>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 33; at++)
        {
            if (image[at] != 0x83 || image[at + 1] != 0xBB ||
                image[at + 6] != 0x01) continue;
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 2, 4));
            if (owner < 0x1000 || owner >= indices.SlotIndex || owner % 4 != 0) continue;
            witnesses.Add((at, owner));
            if (image[at + 7] != 0x0F || image[at + 8] != 0x85 ||
                !image.Slice(at + 13, 8).SequenceEqual(
                    new byte[] { 0x41, 0xB0, 0x01, 0x33, 0xD2, 0x48, 0x8B, 0xCB }) ||
                image[at + 21] != 0xE8 || image[at + 26] != 0x83 ||
                image[at + 27] != 0xBB || image[at + 32] != 0x68 ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 28, 4)) != indices.SlotIndex)
                continue;
            var handoff = (long)at + 26 +
                BinaryPrimitives.ReadInt32LittleEndian(image.Slice(at + 22, 4));
            if (!code.Any(s => handoff >= s.Start && handoff < s.End)) continue;
            strong.Add((at, owner));
        }
        if (strong.Count == 0 || strong.Any(s => s.Offset != strong[0].Offset) ||
            !witnesses.Any(w => w.Offset == strong[0].Offset &&
                                Math.Abs(w.Site - strong[0].Site) > 0x1000)) return null;
        return new ControlOwnerCandidate(strong[0].Offset,
            witnesses.Count(w => w.Offset == strong[0].Offset));
    }

    public static void RequireContainerArrayBase(ReadOnlySpan<byte> image, ulong profileArrayBase)
    {
        var found = FindContainerArrayBase(image);
        if (found is null || found.ArrayBaseRva != profileArrayBase)
            throw new GateException(
                "the live container array base does not match the offset profile; placement is disabled");
    }

    /// <summary>Resolve a writable array base used in stride-indexed slot access.</summary>
    public static ContainerArrayCandidate? FindContainerArrayBase(ReadOnlySpan<byte> image)
    {
        var stride = FindContainerStride(image);
        if (stride is null || !TrySections(image, out var data, out var code)) return null;
        var sites = new Dictionary<uint, List<int>>();
        foreach (var section in code)
        for (var at = (int)section.Start + 128; at < (int)section.End - 10; at++)
        {
            if (image[at] != 0x48 || image[at + 1] != 0x69 ||
                (image[at + 2] & 0xC0) != 0xC0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 3, 4)) != stride.Stride ||
                image[at + 7] != 0x49 || image[at + 8] != 0x03 ||
                (image[at + 9] & 0xC0) != 0xC0 ||
                ((image[at + 9] >> 3) & 7) != ((image[at + 2] >> 3) & 7))
                continue;
            var baseReg = image[at + 9] & 7;
            for (var lea = at - 7; lea >= at - 128; lea--)
            {
                if (image[lea] != 0x4C || image[lea + 1] != 0x8D ||
                    image[lea + 2] != 0x05 + (baseReg << 3) ||
                    !TryRip(image, lea, 3, 7, out var arrayBase) ||
                    !InData(data, arrayBase, checked((int)(stride.Stride * 0x69)))) continue;
                if (!sites.TryGetValue(arrayBase, out var list))
                    sites[arrayBase] = list = [];
                list.Add(at);
                break;
            }
        }
        var ranked = sites.OrderByDescending(pair => pair.Value.Count).ToArray();
        if (ranked.Length == 0 || ranked[0].Value.Count < 2 ||
            ranked[0].Value[^1] - ranked[0].Value[0] <= 0x1000 ||
            (ranked.Length > 1 && ranked[0].Value.Count <= ranked[1].Value.Count * 3))
            return null;
        return new ContainerArrayCandidate(ranked[0].Key, ranked[0].Value.Count);
    }
    public sealed record SpecialSlotIndexCandidate(uint SlotIndex, uint PitIndex,
                                                   uint GarageIndex, uint TransformRva, int Sites);

    public static void RequireSpecialSlotIndices(ReadOnlySpan<byte> image, ContainerSpec profile,
                                                 ulong profileTransformRva)
    {
        var found = FindSpecialSlotIndices(image);
        if (found is null || found.TransformRva != profileTransformRva ||
            profile.Field("slotIndex").Base != OffsetBase.Container ||
            profile.Field("pitIndex").Base != OffsetBase.Container ||
            profile.Field("garageIndex").Base != OffsetBase.Container ||
            found.SlotIndex != profile.Field("slotIndex").Offset ||
            found.PitIndex != profile.Field("pitIndex").Offset ||
            found.GarageIndex != profile.Field("garageIndex").Offset)
            throw new GateException(
                "the live slot/pit/garage layout or transform call does not match the offset profile; placement is disabled");
    }

    /// <summary>
    /// Find the special-slot guard and the pit/garage indices passed together
    /// to its transform call. This is static evidence, not placement approval.
    /// </summary>
    public static SpecialSlotIndexCandidate? FindSpecialSlotIndices(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var matches = new List<(int Site, uint Slot, uint Pit, uint Garage, uint Transform)>();
        foreach (var section in code)
        for (var site = (int)section.Start; site < (int)section.End - 160; site++)
        {
            // cmp dword ptr [rbx+slot], 0x68
            if (image[site] != 0x83 || image[site + 1] != 0xBB ||
                image[site + 6] != 0x68) continue;
            var slot = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(site + 2, 4));
            for (var at = site + 7; at < site + 136; at++)
            {
                // mov r8d,[rbx+garage]; optional argument setup;
                // mov edx,[rbx+pit]; call rel32
                if (image[at] != 0x44 || image[at + 1] != 0x8B || image[at + 2] != 0x83)
                    continue;
                var garage = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 3, 4));
                for (var p = at + 7; p < at + 27; p++)
                {
                    if (image[p] != 0x8B || image[p + 1] != 0x93) continue;
                    var pit = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(p + 2, 4));
                    if (pit <= slot || pit - slot > 0x100 || garage != pit + 4) continue;
                    for (var call = p + 6; call < p + 24; call++)
                    {
                        if (image[call] != 0xE8) continue;
                        var target = (long)call + 5 +
                            BinaryPrimitives.ReadInt32LittleEndian(image.Slice(call + 1, 4));
                        if (target < 0 || target >= image.Length ||
                            !code.Any(s => target >= s.Start && target < s.End)) continue;
                        matches.Add((site, slot, pit, garage, (uint)target));
                        break;
                    }
                    break;
                }
                if (matches.Count > 0 && matches[^1].Site == site) break;
            }
        }
        if (matches.Count < 2 ||
            !matches.Any(m => m.Site - matches[0].Site > 0x1000) ||
            matches.Any(m => m.Slot != matches[0].Slot || m.Pit != matches[0].Pit ||
                             m.Garage != matches[0].Garage ||
                             m.Transform != matches[0].Transform)) return null;
        return new SpecialSlotIndexCandidate(matches[0].Slot, matches[0].Pit,
                                             matches[0].Garage, matches[0].Transform,
                                             matches.Count);
    }

    public static void RequireContainerStride(ReadOnlySpan<byte> image, ulong profileStride)
    {
        var found = FindContainerStride(image);
        if (found is null || found.Stride != profileStride)
            throw new GateException(
                "the live container stride does not match the offset profile; placement is disabled");
    }

    /// <summary>
    /// Require a dominant large stride across separated code paths.
    /// ponytail: frequency is not full dataflow proof; require live slot-layout
    /// checks before promoting a newly resolved build.
    /// </summary>
    public static ContainerStrideCandidate? FindContainerStride(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out _, out var code)) return null;
        var sites = new Dictionary<uint, List<int>>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 7; at++)
        {
            if (image[at] != 0x48 || image[at + 1] != 0x69 ||
                (image[at + 2] & 0xC0) != 0xC0) continue;
            var stride = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 3, 4));
            if (stride is < 0x40000 or > 0x80000 || stride % 8 != 0) continue;
            if (!sites.TryGetValue(stride, out var list)) sites[stride] = list = [];
            list.Add(at);
        }
        var ranked = sites.OrderByDescending(pair => pair.Value.Count).ToArray();
        if (ranked.Length == 0 || ranked[0].Value.Count < 20 ||
            ranked[0].Value[^1] - ranked[0].Value[0] <= 0x1000 ||
            (ranked.Length > 1 && ranked[0].Value.Count <= ranked[1].Value.Count * 10))
            return null;
        return new ContainerStrideCandidate(ranked[0].Key, ranked[0].Value.Count);
    }

    /// <summary>Only the ordinary-slot consumer supports the proven PitPos write.</summary>
    public static void RequireOrdinaryPitPos(ReadOnlySpan<byte> image, ulong profilePitPosRva)
    {
        if (!MatchesOrdinaryPitPos(FindPitPos(image), profilePitPosRva))
            throw new GateException(
                "the live ordinary-slot path does not read the profile's PitPos table; " +
                "placement is disabled for this build");
    }

    public static bool MatchesOrdinaryPitPos(Candidate? found, ulong profilePitPosRva) =>
        found is { ReaderPath: PitPosReaderPath.OrdinarySlotLoop } &&
        found.PitPosRva == profilePitPosRva;

    /// <summary>Require the ordinary-slot reader and both position/orientation writers.</summary>
    public static NormalSlotCandidate? FindNormalSlotTable(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;
        var readers = new List<(int Site, uint Table)>();
        var positions = new List<(int Site, uint Table)>();
        var rotations = new List<(int Site, uint Table)>();
        foreach (var section in code)
        for (var site = (int)section.Start; site < (int)section.End - 96; site++)
        {
            if (image[site] != 0x48 || image[site + 1] != 0x8B || image[site + 2] != 0x05 ||
                !TryRip(image, site, 3, 7, out var table) || !InData(data, table, 8)) continue;
            var after = image.Slice(site + 7, 72);
            var before = image.Slice(Math.Max((int)section.Start, site - 128),
                                     site - Math.Max((int)section.Start, site - 128));
            // Ordinary slots are clamped to 0..0x67, indexed in 32-byte entries.
            var slotClamp = before.IndexOf(new byte[] { 0x83, 0xFA, 0x67 }) >= 0 ||
                            before.IndexOf(new byte[] { 0x83, 0xF8, 0x67 }) >= 0 ||
                            before.IndexOf(new byte[] { 0x41, 0x83, 0xFA, 0x68 }) >= 0;
            var index = before.IndexOf(new byte[] { 0x48, 0xC1, 0xE1, 0x05 }) >= 0 ||
                        after.IndexOf(new byte[] { 0x48, 0xC1, 0xE1, 0x05 }) >= 0;
            if (!slotClamp || !index) continue;
            if (after[..32].IndexOf(new byte[] { 0x0F, 0x2E, 0x04, 0x01 }) >= 0 &&
                after[..32].IndexOf(new byte[] { 0x0F, 0x85 }) >= 0)
                readers.Add((site, table));
            if (after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x04, 0x01 }) >= 0 &&
                after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x04 }) >= 0 &&
                after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x54, 0x01, 0x08 }) >= 0)
                positions.Add((site, table));
            if (after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x44, 0x01, 0x0C }) >= 0 &&
                after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x10 }) >= 0 &&
                after.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x54, 0x01, 0x14 }) >= 0)
                rotations.Add((site, table));
        }
        var agreed = (from reader in readers
                      from position in positions
                      from rotation in rotations
                      where reader.Table == position.Table && reader.Table == rotation.Table &&
                            Math.Abs(reader.Site - position.Site) > 0x1000 &&
                            Math.Abs(reader.Site - rotation.Site) > 0x1000 &&
                            position.Site != rotation.Site
                      select reader.Table).Distinct().ToArray();
        return agreed.Length == 1 ? new NormalSlotCandidate(agreed[0], 2) : null;
    }

    /// <summary>Identify the override switch and vector that precede the verified normal-slot reader.</summary>
    public static NormalSlotSelectionCandidate? FindNormalSlotSelection(ReadOnlySpan<byte> image)
    {
        var normal = FindNormalSlotTable(image);
        if (normal is null || !TrySections(image, out var data, out var code)) return null;
        var matches = new List<NormalSlotSelectionCandidate>();
        foreach (var section in code)
        for (var at = (int)section.Start; at < (int)section.End - 100; at++)
        {
            if (!image.Slice(at, 2).SequenceEqual(new byte[] { 0x83, 0x3D }) ||
                image[at + 6] != 0 || image[at + 7] != 0x74 ||
                !image.Slice(at + 9, 3).SequenceEqual(new byte[] { 0x48, 0x8B, 0x05 }) ||
                !image.Slice(at + 16, 3).SequenceEqual(new byte[] { 0x48, 0x3B, 0x05 }) ||
                image[at + 23] != 0x74 ||
                !image.Slice(at + 25, 7).SequenceEqual(new byte[] { 0x49, 0x63, 0xCA, 0x48, 0xC1, 0xE1, 0x05 }) ||
                !TryRip(image, at, 2, 7, out var selector) || !InData(data, selector, 4) ||
                !TryRip(image, at + 9, 3, 7, out var start) || !InData(data, start, 8) ||
                !TryRip(image, at + 16, 3, 7, out var end) || !InData(data, end, 8)) continue;
            var fallback = at + 9 + unchecked((sbyte)image[at + 8]);
            if (fallback != at + 25 + unchecked((sbyte)image[at + 24]) ||
                fallback <= at + 32 || fallback + 7 > section.End ||
                !image.Slice(fallback, 3).SequenceEqual(new byte[] { 0x48, 0x8B, 0x05 }) ||
                !TryRip(image, fallback, 3, 7, out var table) || table != normal.TableRva ||
                image.Slice(at + 32, fallback - at - 32)
                    .IndexOf(new byte[] { 0xF2, 0x0F, 0x10, 0x04, 0x01 }) < 0)
                continue;
            matches.Add(new NormalSlotSelectionCandidate(selector, start, end, table));
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Require an independent indexed reader and indexed writer.</summary>
    public static GarageCandidate? FindGaragePos(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;
        var reads = new List<(int Site, uint Table, uint Mult, uint Count)>();
        var writes = new List<(int Site, uint Table, uint Mult, uint Count)>();
        foreach (var section in code)
        for (var site = (int)section.Start; site < (int)section.End - 160; site++)
        {
            if (image[site] == 0x48 && image[site + 1] == 0x8B && image[site + 2] == 0x1D &&
                TryRip(image, site, 3, 7, out var count) && InData(data, count, 4))
            {
                for (var m = site + 12; m < site + 36; m++)
                {
                    if (image[m] != 0x8B || image[m + 1] != 0x0D ||
                        !TryRip(image, m, 2, 6, out var mult) || !InData(data, mult, 4)) continue;
                    for (var p = m + 16; p < m + 55; p++)
                    {
                        if (image[p] == 0x48 && image[p + 1] == 0x8B && image[p + 2] == 0x05 &&
                            TryRip(image, p, 3, 7, out var table) && InData(data, table, 8) &&
                            HasEntryCopy(image.Slice(p + 7, 72)))
                            reads.Add((site, table, mult, count));
                    }
                }
            }
            if (image[site] == 0x8B && image[site + 1] == 0x05 &&
                TryRip(image, site, 2, 6, out var writerCount) && InData(data, writerCount, 4))
            {
                for (var m = site + 12; m < site + 35; m++)
                {
                    if (image[m] != 0x0F || image[m + 1] != 0xAF || image[m + 2] != 0x0D ||
                        !TryRip(image, m, 3, 7, out var mult) || !InData(data, mult, 4)) continue;
                    for (var p = m + 10; p < m + 36; p++)
                    {
                        if (image[p] == 0x48 && image[p + 1] == 0x8B && image[p + 2] == 0x05 &&
                            TryRip(image, p, 3, 7, out var table) && InData(data, table, 8) &&
                            HasPositionWrite(image.Slice(p + 7, 48)))
                            writes.Add((site, table, mult, writerCount));
                    }
                }
            }
        }
        var agreed = (from read in reads
                      from write in writes
                      where read.Table == write.Table && read.Mult == write.Mult &&
                            read.Count == write.Count && Math.Abs(read.Site - write.Site) > 0x1000
                      select new GarageCandidate(read.Table, read.Mult, read.Count)).Distinct().ToArray();
        return agreed.Length == 1 ? agreed[0] : null;
    }

    public static Candidate? FindPitPos(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x1000 || image.Length > 0x20000000 ||
            !TrySections(image, out var data, out var code)) return null;

        // Older builds also contain this special-slot branch, but Drive uses
        // their ordinary-slot table. Require the indexed destination consumer
        // before interpreting the special branch as the placement path.
        if (FindIndexedDestination(image) is null)
            return FindLegacyPitPos(image, data, code);
        var indices = FindSpecialSlotIndices(image);
        var matches = new List<(int Site, uint Table, uint Count)>();
        foreach (var section in code)
        for (var site = (int)section.Start; site < (int)section.End - 150; site++)
        {
            // cmp dword ptr [r64+slotIndex], 0x68; the register may change.
            if (image[site] != 0x83 || (image[site + 1] & 0xF8) != 0xB8 ||
                indices is null ||
                BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(site + 2, 4)) != indices.SlotIndex ||
                image[site + 6] != 0x68) continue;

            uint? count = null, table = null;
            for (var at = site + 7; at < site + 72; at++)
            {
                if (at < site + 28 && image[at] == 0x8B && image[at + 1] == 0x05 &&
                    TryRip(image, at, 2, 6, out var c) && InData(data, c, 4)) count ??= c;
                if (at >= site + 20 && image[at] == 0x48 && image[at + 1] == 0x8B &&
                    (image[at + 2] == 0x05 || image[at + 2] == 0x0D) &&
                    TryRip(image, at, 3, 7, out var p) && InData(data, p, 8) &&
                    HasEntryCopy(image.Slice(at + 7, 72))) table ??= p;
            }
            if (count is null || table is null || count.Value <= table.Value ||
                count.Value - table.Value > 0x400) continue;
            matches.Add((site, table.Value, count.Value));
        }

        if (matches.Count >= 2)
        {
            var first = matches[0];
            if (matches.All(m => m.Table == first.Table && m.Count == first.Count) &&
                matches.Any(m => Math.Abs(m.Site - first.Site) > 0x1000))
                return new Candidate(first.Table, first.Count, matches.Count,
                                     PitPosReaderPath.SpecialSlotBranch);
            return null;
        }
        return null;
    }

    private static Candidate? FindLegacyPitPos(
        ReadOnlySpan<byte> image, List<(uint Start, uint End)> data,
        List<(uint Start, uint End)> code)
    {
        var garage = FindGaragePos(image);
        if (garage is null) return null;
        var reads = new List<(int Site, uint Table)>();
        foreach (var section in code)
        for (var site = (int)section.Start; site < (int)section.End - 120; site++)
        {
            if (image[site] != 0x8B || image[site + 1] != 0x05 ||
                !TryRip(image, site, 2, 6, out var mult) || mult != garage.MultRva) continue;
            for (var p = site + 20; p < site + 56; p++)
            {
                if (image[p] != 0x48 || image[p + 1] != 0x8B || image[p + 2] != 0x05 ||
                    !TryRip(image, p, 3, 7, out var table) ||
                    table == garage.GaragePosRva || !InData(data, table, 8)) continue;
                var before = image.Slice(site + 6, p - site - 6);
                var after = image.Slice(p + 7, 42);
                if (before.IndexOf(new byte[] { 0x48, 0xC1, 0xE1, 0x05 }) < 0 ||
                    after.IndexOf(new byte[] { 0xF2, 0x0F, 0x10 }) < 0 ||
                    after.IndexOf(new byte[] { 0xF2, 0x0F, 0x10, 0x44, 0x01, 0x0C }) < 0)
                    continue;
                reads.Add((site, table));
            }
        }
        var agreed = reads.Select(r => r.Table).Distinct().ToArray();
        if (agreed.Length != 1 || reads.Count < 2 ||
            !reads.Any(r => Math.Abs(r.Site - reads[0].Site) > 0x400)) return null;
        return new Candidate(agreed[0], garage.CountRva, reads.Count,
                             PitPosReaderPath.OrdinarySlotLoop);
    }

    private static bool HasEntryCopy(ReadOnlySpan<byte> code)
    {
        bool y = false, direction = false, z = false;
        for (var i = 0; i < code.Length - 5; i++)
        {
            y |= code[i] == 0x8B && code[i + 1] == 0x44 && code[i + 3] == 0x08;
            direction |= code[i] == 0xF2 && code[i + 1] == 0x0F && code[i + 2] == 0x10 &&
                         code[i + 3] == 0x44 && code[i + 5] == 0x0C;
            z |= code[i] == 0x8B && code[i + 1] == 0x44 && code[i + 3] == 0x14;
        }
        return y && direction && z;
    }

    private static bool HasPositionWrite(ReadOnlySpan<byte> code) =>
        code.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x04, 0x01 }) >= 0 &&
        code.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x4C, 0x01, 0x04 }) >= 0 &&
        code.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x54, 0x01, 0x08 }) >= 0;

    private static bool TryRip(ReadOnlySpan<byte> image, int at, int disp, int length, out uint rva)
    {
        var value = (long)at + length + BinaryPrimitives.ReadInt32LittleEndian(image.Slice(at + disp, 4));
        rva = value >= 0 && value < image.Length ? (uint)value : 0;
        return value >= 0 && value < image.Length;
    }

    private static bool InData(List<(uint Start, uint End)> sections, uint rva, int bytes) =>
        sections.Any(s => rva >= s.Start && (ulong)rva + (uint)bytes <= s.End);

    private static bool TrySections(ReadOnlySpan<byte> image,
                                    out List<(uint Start, uint End)> data,
                                    out List<(uint Start, uint End)> code)
    {
        data = [];
        code = [];
        if (image[0] != 'M' || image[1] != 'Z') return false;
        var pe = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(0x3C, 4));
        if (pe < 0 || pe > 0x1000 || pe + 24 > image.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(pe, 4)) != 0x4550) return false;
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(pe + 6, 2));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(pe + 20, 2));
        if (optionalSize < 0x40 || pe + 24 + optionalSize > image.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(pe + 24, 2)) != 0x20B ||
            BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(pe + 24 + 0x38, 4)) > image.Length)
            return false;
        var sections = pe + 24 + optionalSize;
        if (sectionCount == 0 || sectionCount > 96 ||
            (long)sections + sectionCount * 40 > image.Length) return false;
        for (var i = 0; i < sectionCount; i++)
        {
            var at = sections + i * 40;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 8, 4));
            var start = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 12, 4));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at + 36, 4));
            if ((flags & 0x80000000) != 0 && (flags & 0x20000000) == 0 &&
                (ulong)start + size <= (uint)image.Length) data.Add((start, start + size));
            if ((flags & 0x20000000) != 0 && (ulong)start + size <= (uint)image.Length)
                code.Add((start, start + size));
        }
        return data.Count > 0 && code.Count > 0;
    }
}
