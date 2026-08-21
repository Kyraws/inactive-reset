using System.Linq;
using InactiveReset.Core;
namespace InactiveReset.Reanchor;

/// <summary>
/// Re-derives addresses across an LMU build change by comparing two module
/// dumps.
///
/// The two techniques exist because code and data need different treatment:
///
///   CODE is found by MASKED SIGNATURE SEARCH — take the bytes at the old
///   address and wildcard every RIP-relative displacement, because those change
///   whenever data moves even when the function is byte-for-byte identical.
///   An unmasked search reports "not found" for functions that did not change.
///
///   DATA cannot be signature-searched: an address has no bytes of its own. So
///   we find every instruction that REFERENCES the old address, locate those
///   same instructions in the new image (masked the same way), decode their new
///   displacements, and take the majority vote. A mis-decode shows up as a
///   dissenting vote instead of a silent wrong answer.
/// </summary>
public static class Reanchor
{
    /// <summary>
    /// A decoded RIP-relative memory reference.
    /// </summary>
    /// <param name="DisplacementOffset">Where the disp32 starts, relative to the instruction.</param>
    /// <param name="Length">
    /// Total instruction length. This is NOT always DisplacementOffset + 4: forms
    /// like <c>cmp byte ptr [rip+X], 0</c> carry an immediate AFTER the
    /// displacement, so RIP is relative to the end of the IMMEDIATE, not the end
    /// of the displacement. Assuming otherwise puts every such target one to
    /// four bytes off — and the wrong address still reads fine, which is how it
    /// escapes notice.
    /// </param>
    private readonly record struct RipRef(int DisplacementOffset, int Length);

    public sealed record CodeMatch(ulong OldRva, ulong NewRva, int BytesUsed)
    {
        public long Delta => (long)NewRva - (long)OldRva;
    }

    /// <param name="Sites">
    /// References actually EXAMINED, not the total available. Sampling stops
    /// early once enough agree, so a small number here is the search being
    /// efficient, not the evidence being thin.
    /// </param>
    public sealed record DataMatch(
        ulong OldRva, ulong NewRva, int Votes, int Sites, int CandidateCount)
    {
        public long Delta => (long)NewRva - (long)OldRva;

        /// <summary>
        /// True when every resolved reference agreed. More than one candidate
        /// means something decoded differently and the result needs review.
        /// </summary>
        public bool Unanimous => CandidateCount == 1;
    }

    // ---- code ---------------------------------------------------------------

    /// <summary>
    /// Locate a function from the old image in the new one. Returns null when no
    /// window length produces a unique match — which happens either because the
    /// function genuinely changed, or because it is duplicated (linker COMDAT
    /// folding). Both deserve a human, so neither guesses.
    /// </summary>
    public static CodeMatch? RemapCode(ReadOnlySpan<byte> older, ReadOnlySpan<byte> newer, ulong rva)
    {
        foreach (var length in (int[])[96, 64, 48, 32, 24, 16])
        {
            if ((int)rva + length > older.Length)
            {
                continue;
            }

            var pattern = older.Slice((int)rva, length);
            if (DistinctByteCount(pattern) < 4)
            {
                continue;   // too bland to identify anything
            }

            var mask = BuildDisplacementMask(pattern);
            var hits = FindMasked(newer, pattern, mask, limit: 2);
            if (hits.Count == 1)
            {
                return new CodeMatch(rva, (ulong)hits[0], length);
            }
        }
        return null;
    }

    // ---- data ---------------------------------------------------------------

    /// <summary>
    /// Re-derive a data address by majority vote across every instruction that
    /// references it.
    /// </summary>
    public static DataMatch? RemapData(
        ReadOnlySpan<byte> older, ReadOnlySpan<byte> newer, ulong target,
        Dictionary<ulong, List<(int Site, int DisplacementOffset)>> oldIndex,
        int context = 24, int maxSites = 32, int enough = 8)
    {
        if (!oldIndex.TryGetValue(target, out var indexed) || indexed.Count == 0)
        {
            return null;
        }

        // Sample the references; do not walk all of them.
        //
        // Each site costs a masked search of the whole new image, so the cost is
        // sites x image size. `containers.pointer` has 860 sites, and measured on
        // 0F6DCAC1 only 121 of them produced a unique match -- the other 739
        // searches were 64 MB of pure waste. A single address took minutes.
        //
        // The vote exists to catch a DISSENTER, and a sample catches one just as
        // well as an exhaustive count: 8 agreeing votes and 800 agreeing votes
        // say the same thing. What matters is trying the sites most likely to
        // match uniquely first, so order by how distinctive each one's
        // surroundings are -- a window of varied bytes pins down a location, a
        // window of repeated ones does not.
        var ranked = new List<(int Site, int DisplacementOffset, int Distinct)>(indexed.Count);
        foreach (var (site, displacementOffset) in indexed)
        {
            var from = Math.Max(0, site - context);
            var to = Math.Min(older.Length, site + 16 + context);
            ranked.Add((site, displacementOffset, DistinctByteCount(older[from..to])));
        }
        ranked.Sort((a, b) => b.Distinct.CompareTo(a.Distinct));

        var votes = new Dictionary<ulong, int>();
        var examined = 0;
        foreach (var (site, displacementOffset, _) in ranked)
        {
            if (examined >= maxSites)
            {
                break;
            }
            examined++;

            var start = Math.Max(0, site - context);
            var end = Math.Min(older.Length, site + 16 + context);
            var pattern = older[start..end];

            var mask = BuildDisplacementMask(pattern);
            // Also wildcard the displacement of the referencing instruction
            // itself — it is precisely the value we are trying to recover.
            var local = site - start + displacementOffset;
            for (var i = local; i < local + 4 && i < mask.Length; i++)
            {
                mask[i] = false;
            }

            var hits = FindMasked(newer, pattern, mask, limit: 2);
            if (hits.Count != 1)
            {
                continue;
            }

            var newSite = hits[0] + (site - start);
            if (DecodeRipRef(newer, newSite) is not { } reference)
            {
                continue;
            }
            var displacement = BitConverter.ToInt32(newer.Slice(newSite + reference.DisplacementOffset, 4));
            var resolved = (ulong)(newSite + reference.Length + displacement);
            votes[resolved] = votes.GetValueOrDefault(resolved) + 1;

            // Enough agreement, no dissent: more searching cannot change the
            // answer, only the size of the number printed beside it.
            if (votes.Count == 1 && votes[resolved] >= enough)
            {
                break;
            }
        }

        if (votes.Count == 0)
        {
            return null;
        }

        var best = votes.OrderByDescending(v => v.Value).First();
        return new DataMatch(target, best.Key, best.Value, examined, votes.Count);
    }

    /// <summary>
    /// A data address recovered from the CONTENT at that address rather than
    /// from the code that references it.
    /// </summary>
    /// <param name="Windows">
    /// How many differently-sized windows agreed. One is a coincidence risk;
    /// two or more independently landing on the same address is not.
    /// </param>
    public sealed record ValueMatch(ulong OldRva, ulong NewRva, int Windows, int WidestBytes)
    {
        public long Delta => (long)NewRva - (long)OldRva;
        public bool Corroborated => Windows >= 2;
    }

    /// <summary>
    /// Re-derive a data address by looking for the bytes AROUND it.
    ///
    /// <para><b>Why this exists.</b> <see cref="RemapCode"/> fails outright when
    /// the compiler regenerates a function, and <see cref="RemapData"/> then
    /// fails with it, because it needs to locate the referencing instructions
    /// and those live inside the functions that just changed. The 1.4.1.3 patch
    /// did exactly that: 20 of 40 addresses came back unresolved, including all
    /// four engine tunables.</para>
    ///
    /// <para>Constants survive a recompilation. The tunables are a run of floats
    /// -- 0.55, 0.1, 1.5, 25, 45 -- and that run is still in the new image,
    /// verbatim and unique, even though every instruction touching it moved.
    /// Anchoring on the value finds it in one pass.</para>
    ///
    /// <para><b>What this cannot do.</b> Volatile runtime state -- tick
    /// counters, handles, session flags -- differs between two captures taken at
    /// different moments, so its content identifies nothing. Those addresses
    /// return null here and stay unresolved, which is the correct outcome:
    /// measured on this build, they produce either no match or six, and both are
    /// refusals.</para>
    /// </summary>
    public static ValueMatch? RemapDataByValue(
        ReadOnlySpan<byte> older, ReadOnlySpan<byte> newer, ulong target)
    {
        // Deliberately asymmetric and varied, in both directions.
        //
        // A constant run has to be caught WITHOUT spilling past either end of
        // it, because what surrounds it is usually volatile and differs between
        // two captures. So the target may be at the run's head (leading windows
        // find it), at its tail (trailing windows do), or inside it.
        //
        // Measured: yawOffsetDegrees sits at the END of the tunable block, and
        // with leading windows only it was missed while its three neighbours
        // were found -- the forward extent ran off the constants into live data.
        (int Before, int After)[] windows =
        [
            (0, 32), (0, 20), (0, 12),
            (16, 4), (24, 8), (32, 4), (20, 12),
            (16, 48), (32, 32), (64, 64), (8, 24),
        ];

        var agreed = new Dictionary<ulong, (int Count, int Widest)>();

        foreach (var (before, after) in windows)
        {
            var start = (long)target - before;
            var end = (long)target + after;
            if (start < 0 || end > older.Length)
            {
                continue;
            }

            var pattern = older[(int)start..(int)end];

            // A window of mostly zeroes or padding matches everywhere and
            // proves nothing. Demand more variety than the code path does:
            // there is no instruction structure here to make a short pattern
            // meaningful.
            if (DistinctByteCount(pattern) < 6)
            {
                continue;
            }

            var mask = new bool[pattern.Length];
            Array.Fill(mask, true);
            var hits = FindMasked(newer, pattern, mask, limit: 2);
            if (hits.Count != 1)
            {
                continue;
            }

            var resolved = (ulong)(hits[0] + before);
            var seen = agreed.GetValueOrDefault(resolved);
            agreed[resolved] = (seen.Count + 1, Math.Max(seen.Widest, pattern.Length));
        }

        if (agreed.Count != 1)
        {
            // Zero windows matched, or two windows disagreed. Either way this is
            // not evidence, and guessing between them is exactly the failure
            // this whole confidence system exists to prevent.
            return null;
        }

        var (newRva, (count, widest)) = (agreed.Keys.First(), agreed.Values.First());
        return new ValueMatch(target, newRva, count, widest);
    }

    /// <summary>
    /// Every RIP-relative reference in an image, indexed by the address it
    /// points at.
    ///
    /// Built once and reused for every address. Scanning per-address instead
    /// means re-walking 64 MB for each one, which for a profile of thirty
    /// addresses is a billion-plus redundant decodes — slow enough that the
    /// tool looks hung.
    /// </summary>
    public static Dictionary<ulong, List<(int Site, int DisplacementOffset)>> BuildReferenceIndex(
        ReadOnlySpan<byte> image)
    {
        var index = new Dictionary<ulong, List<(int, int)>>();
        for (var i = 0; i + 5 <= image.Length; i++)
        {
            if (DecodeRipRef(image, i) is not { } reference)
            {
                continue;
            }
            var displacement = BitConverter.ToInt32(image.Slice(i + reference.DisplacementOffset, 4));
            var target = i + reference.Length + (long)displacement;
            if (target < 0 || target >= image.Length)
            {
                continue;
            }

            if (!index.TryGetValue((ulong)target, out var sites))
            {
                index[(ulong)target] = sites = [];
            }
            sites.Add((i, reference.DisplacementOffset));
        }
        return index;
    }

    // ---- primitives ---------------------------------------------------------

    /// <summary>
    /// Decode a RIP-relative reference at <paramref name="i"/>, or null.
    ///
    /// Only the forms that actually appear in this project's address set are
    /// handled, deliberately: a partial decoder that is right is worth more than
    /// a general one that is approximately right, and every unhandled form
    /// simply produces no reference rather than a wrong one.
    /// </summary>
    private static RipRef? DecodeRipRef(ReadOnlySpan<byte> image, int i)
    {
        var p = i;

        // Optional REX. Byte-sized forms often have none.
        if (p < image.Length && (image[p] & 0xF0) == 0x40)
        {
            p++;
        }
        var prefix = p - i;

        if (p + 1 >= image.Length)
        {
            return null;
        }

        int opcodeLength;
        int immediate;
        var opcode = image[p];

        if (opcode == 0x0F)
        {
            // Two-byte opcodes: MOVZX/MOVSX r, rm8/rm16.
            if (p + 2 >= image.Length)
            {
                return null;
            }
            var second = image[p + 1];
            if (second is not (0xB6 or 0xB7 or 0xBE or 0xBF))
            {
                return null;
            }
            opcodeLength = 2;
            immediate = 0;
        }
        else
        {
            (opcodeLength, immediate) = opcode switch
            {
                // reg <-> rm, no immediate
                0x88 or 0x89 or 0x8A or 0x8B or 0x8D => (1, 0),
                0x38 or 0x39 or 0x3A or 0x3B => (1, 0),
                0x00 or 0x01 or 0x02 or 0x03 => (1, 0),   // ADD
                0x28 or 0x29 or 0x2A or 0x2B => (1, 0),   // SUB
                0x84 or 0x85 => (1, 0),                   // TEST

                // group with immediate AFTER the displacement
                0x80 => (1, 1),   // CMP/ADD/... rm8, imm8
                0x83 => (1, 1),   // ... rm32/64, imm8
                0x81 => (1, 4),   // ... rm32/64, imm32
                0xC6 => (1, 1),   // MOV rm8, imm8
                0xC7 => (1, 4),   // MOV rm32/64, imm32
                0xF6 => (1, 1),   // TEST rm8, imm8
                0xF7 => (1, 4),   // TEST rm32/64, imm32
                _ => (0, 0),
            };
            if (opcodeLength == 0)
            {
                return null;
            }
        }

        var modrmIndex = p + opcodeLength;
        if (modrmIndex >= image.Length)
        {
            return null;
        }

        // RIP-relative is mod == 00 and rm == 101.
        var modrm = image[modrmIndex];
        if ((modrm & 0xC7) != 0x05)
        {
            return null;
        }

        var displacementOffset = prefix + opcodeLength + 1;
        var length = displacementOffset + 4 + immediate;
        return i + length <= image.Length ? new RipRef(displacementOffset, length) : null;
    }

    /// <summary>
    /// True where a byte must match, false where it is a RIP-relative disp32 and
    /// must be ignored.
    /// </summary>
    private static bool[] BuildDisplacementMask(ReadOnlySpan<byte> pattern)
    {
        var mask = new bool[pattern.Length];
        Array.Fill(mask, true);

        for (var i = 0; i + 5 <= pattern.Length; i++)
        {
            if (DecodeRipRef(pattern, i) is not { } reference)
            {
                continue;
            }
            for (var b = i + reference.DisplacementOffset;
                 b < i + reference.DisplacementOffset + 4 && b < mask.Length;
                 b++)
            {
                mask[b] = false;
            }
        }
        return mask;
    }

    private static List<int> FindMasked(
        ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> pattern, bool[] mask, int limit)
    {
        var hits = new List<int>();

        // Anchor on the first byte that actually has to match, so the scan skips
        // cheaply instead of comparing every window.
        var anchor = Array.IndexOf(mask, true);
        if (anchor < 0)
        {
            return hits;   // entirely wildcards; refuse rather than match everything
        }

        var anchorByte = pattern[anchor];
        var last = haystack.Length - pattern.Length;

        for (var start = 0; start <= last; start++)
        {
            if (haystack[start + anchor] != anchorByte)
            {
                continue;
            }

            var matched = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (mask[i] && haystack[start + i] != pattern[i])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                hits.Add(start);
                if (hits.Count >= limit)
                {
                    return hits;
                }
            }
        }
        return hits;
    }

    private static int DistinctByteCount(ReadOnlySpan<byte> bytes)
    {
        Span<bool> seen = stackalloc bool[256];
        var count = 0;
        foreach (var b in bytes)
        {
            if (!seen[b])
            {
                seen[b] = true;
                count++;
            }
        }
        return count;
    }

    // ---- reference profile --------------------------------------------------

    /// <summary>
    /// A data address recovered by comparing the SHAPE of the code that
    /// references it, rather than by locating those instructions exactly.
    /// </summary>
    /// <param name="Score">Weight of instruction contexts shared with the old address.</param>
    /// <param name="RunnerUpScore">
    /// The best competing candidate. This is the number that matters: a win by
    /// one over a close second is a coin toss wearing a rosette.
    /// </param>
    public sealed record ReferenceProfileMatch(
        ulong OldRva, ulong NewRva, int Score, int RunnerUpScore, int OldSites, int NewSites)
    {
        public long Delta => (long)NewRva - (long)OldRva;

        /// <summary>
        /// True when nothing else scored as well. Deliberately NOT a confidence
        /// claim: measured on the 1.4.1.3 patch, correct answers won by ratios
        /// from 1.5x to 3x, so no threshold separates right from lucky. This
        /// technique earns an E only by AGREEING with a different one.
        /// </summary>
        public bool Unrivalled => Score > RunnerUpScore;
    }

    /// <summary>
    /// Re-derive a data address from the PROFILE of instructions referencing it.
    ///
    /// <para><b>Why this exists.</b> <see cref="RemapData"/> must find each
    /// referencing instruction in the new image by masked search, so a recompile
    /// that reallocates registers defeats it: the bytes around the reference
    /// changed even though the reference itself did not. <see
    /// cref="RemapDataByValue"/> then fails too whenever the address holds a
    /// pointer, because pointers differ between captures.</para>
    ///
    /// <para><c>spotTable.garPosTable</c> is both at once -- a heap pointer read
    /// by recompiled code -- and it survived every other tier unresolved.</para>
    ///
    /// <para>What does survive is the STATISTICS of how an address is used. A
    /// table read by 11 instructions in the old image is read by 11 in the new
    /// one, and most of those instructions still open with the same few bytes
    /// even when their registers changed. Score every candidate in the new image
    /// by how much of that context multiset it shares, and the right answer
    /// ranks first.</para>
    ///
    /// <para><b>What this cannot do.</b> Separate an address from its immediate
    /// neighbours when the same functions read both: the runner-up for
    /// <c>garPosTable</c> was <c>pitPosTable</c>, eight bytes away. It also says
    /// nothing useful for an address with one or two references, where the
    /// multiset is too small to be evidence. Both cases must be settled by
    /// agreement with another tier, never by this score alone.</para>
    /// </summary>
    public static ReferenceProfileMatch? RemapDataByReferenceProfile(
        ReadOnlySpan<byte> older, ulong target,
        Dictionary<ulong, List<(int Site, int DisplacementOffset)>> oldIndex,
        ReferenceProfileIndex newProfiles,
        int prefix = 5, int minimumSites = 2)
    {
        if (!oldIndex.TryGetValue(target, out var indexed))
        {
            return null;
        }

        var oldSites = Distinct(indexed);
        if (oldSites.Count < minimumSites)
        {
            return null;
        }

        var want = ContextCounts(older, oldSites, prefix);
        if (want.Count == 0)
        {
            return null;
        }

        ulong bestTarget = 0;
        var best = 0;
        var runnerUp = 0;
        var bestSites = 0;

        foreach (var (candidate, entry) in newProfiles.Profiles)
        {
            var score = 0;
            foreach (var (context, count) in want)
            {
                if (entry.Contexts.TryGetValue(context, out var mine))
                {
                    score += Math.Min(count, mine);
                }
            }

            if (score > best)
            {
                runnerUp = best;
                best = score;
                bestTarget = candidate;
                bestSites = entry.Sites;
            }
            else if (score > runnerUp)
            {
                runnerUp = score;
            }
        }

        return best == 0
            ? null
            : new ReferenceProfileMatch(
                target, bestTarget, best, runnerUp, oldSites.Count, bestSites);
    }

    /// <summary>
    /// Every candidate address in an image, with the multiset of instruction
    /// contexts that read it, computed once.
    ///
    /// Built once and reused for every address being re-derived. Profiling
    /// per-address instead re-walks all hundred thousand candidates each time;
    /// for a profile of thirty addresses that is millions of redundant passes,
    /// slow enough that the tool looks hung — which is exactly how it behaved
    /// before this existed.
    /// </summary>
    public sealed class ReferenceProfileIndex
    {
        internal readonly record struct Entry(Dictionary<string, int> Contexts, int Sites);

        private readonly Dictionary<ulong, Entry> _profiles;

        private ReferenceProfileIndex(Dictionary<ulong, Entry> profiles) => _profiles = profiles;

        public int Count => _profiles.Count;

        internal IEnumerable<KeyValuePair<ulong, Entry>> Profiles => _profiles;

        public static ReferenceProfileIndex Build(
            ReadOnlySpan<byte> image,
            Dictionary<ulong, List<(int Site, int DisplacementOffset)>> index,
            int prefix = 5, int minimumSites = 2)
        {
            var profiles = new Dictionary<ulong, Entry>();
            foreach (var (target, indexed) in index)
            {
                // Cheap rejection first: the deduplicated count can only shrink,
                // so anything already below the floor cannot clear it.
                if (indexed.Count < minimumSites)
                {
                    continue;
                }
                var sites = Distinct(indexed);
                if (sites.Count < minimumSites)
                {
                    continue;
                }
                profiles[target] = new Entry(ContextCounts(image, sites, prefix), sites.Count);
            }
            return new ReferenceProfileIndex(profiles);
        }
    }

    /// <summary>
    /// The multiset of bytes immediately preceding each referencing instruction.
    ///
    /// A multiset, not a set: a function that reads a table twice in a loop is
    /// more evidence than one that reads it once, and flattening that away
    /// discards the difference.
    /// </summary>
    private static Dictionary<string, int> ContextCounts(
        ReadOnlySpan<byte> image, List<(int Site, int DisplacementOffset)> sites, int prefix)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (site, _) in sites)
        {
            var start = site - prefix;
            if (start < 0)
            {
                continue;
            }
            var key = Convert.ToHexString(image.Slice(start, prefix));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    /// <summary>
    /// Collapse index entries that describe the SAME instruction.
    ///
    /// <see cref="BuildReferenceIndex"/> reports a REX-prefixed reference twice:
    /// <c>48 8B 05 disp</c> decodes at its own address with length 7, and
    /// <c>8B 05 disp</c> decodes one byte later with length 6, and both resolve
    /// to the identical target. Harmless for a majority vote, where it only
    /// scales every tally alike. Not harmless here, where a count IS the
    /// evidence: undeduplicated, one reference looks like two and clears any
    /// minimum a caller sets.
    /// </summary>
    private static List<(int Site, int DisplacementOffset)> Distinct(
        List<(int Site, int DisplacementOffset)> sites)
    {
        var ordered = sites.OrderBy(s => s.Site).ToList();
        var kept = new List<(int, int)>();
        int? last = null;
        foreach (var site in ordered)
        {
            // Nullable rather than a sentinel: `site.Site - int.MinValue`
            // overflows in unchecked arithmetic and comes out negative, which
            // silently discards the first site of every group.
            if (last is null || site.Site - last.Value > 4)
            {
                kept.Add(site);
            }
            last = site.Site;
        }
        return kept;
    }

    // ---- function fingerprint -----------------------------------------------

    /// <summary>
    /// A function located by the set of data addresses it reads.
    /// </summary>
    public sealed record FingerprintMatch(
        ulong OldRva, ulong NewRva, int TargetsFound, int TargetsSought,
        int OldLength, int NewLength)
    {
        public long Delta => (long)NewRva - (long)OldRva;

        /// <summary>
        /// A recompile reallocates registers and may add or drop a reference,
        /// but it does not change a function's length by much. A large swing
        /// means the fingerprint landed in the wrong function.
        /// </summary>
        public bool LengthPlausible =>
            Math.Abs(NewLength - OldLength) <= Math.Max(16, OldLength / 8);
    }

    /// <summary>
    /// Locate a RECOMPILED function by the data it reads.
    ///
    /// <para><b>Why this exists.</b> Signature search cannot find a function
    /// whose bytes were regenerated. That is not a limitation to engineer
    /// around, it is arithmetic. On the 1.4.1.3 patch it left <c>probe</c>
    /// unresolved, and <c>probe</c> is the gate every other address depends
    /// on.</para>
    ///
    /// <para>A recompiled function still does the same job, so it still reads
    /// the same globals. Once those globals have been re-derived by the data
    /// tiers, the function is findable as the one place in the new image that
    /// reads all of them together. <c>getSpotTransform</c> reads four spotTable
    /// addresses; exactly one region of the new image reads that same four.</para>
    ///
    /// <para><b>The ordering this implies.</b> Data must be resolved BEFORE
    /// code, which is the reverse of the older tiers. That is why the caller
    /// runs tiers to a fixpoint rather than in one pass: every address resolved
    /// is a seed for the next round.</para>
    ///
    /// <para><b>What this cannot do.</b> Find a function that reads no globals,
    /// or one whose globals are themselves unresolved. Both return null rather
    /// than a nearest guess.</para>
    /// </summary>
    public static FingerprintMatch? LocateFunctionByDataFingerprint(
        ReadOnlySpan<byte> older, ReadOnlySpan<byte> newer, ulong oldFunction,
        IReadOnlyCollection<ulong> newTargets,
        Dictionary<ulong, List<(int Site, int DisplacementOffset)>> newIndex)
    {
        if (newTargets.Count == 0)
        {
            return null;
        }

        // Group by the function each reference sits in, NOT by proximity.
        //
        // Proximity was the first attempt and it is wrong: the references to
        // the spotTable straddle a function boundary, so a cluster spanning
        // 0x400 bytes begins inside getSpotTransform's PREDECESSOR, and walking
        // back from the cluster's first site returns that predecessor. The
        // length check caught it, but a technique that needs catching is a
        // technique that will one day not be caught.
        var byFunction = new Dictionary<int, HashSet<ulong>>();
        foreach (var target in newTargets)
        {
            if (!newIndex.TryGetValue(target, out var found))
            {
                continue;
            }
            foreach (var (site, _) in found)
            {
                var owner = FunctionStart(newer, site);
                if (!byFunction.TryGetValue(owner, out var targets))
                {
                    byFunction[owner] = targets = [];
                }
                targets.Add(target);
            }
        }
        if (byFunction.Count == 0)
        {
            return null;
        }

        // Most DISTINCT targets wins. A function reading two of the four many
        // times over is a worse explanation than one reading all four once.
        var best = -1;
        var bestCount = 0;
        foreach (var (owner, targets) in byFunction)
        {
            if (targets.Count > bestCount)
            {
                bestCount = targets.Count;
                best = owner;
            }
        }

        return best < 0
            ? null
            : new FingerprintMatch(
                oldFunction, (ulong)best, bestCount, newTargets.Count,
                FunctionEnd(older, (int)oldFunction) - (int)oldFunction,
                FunctionEnd(newer, best) - best);
    }


    /// <summary>
    /// Every distinct data address referenced from inside one function.
    ///
    /// This is the seed for <see cref="LocateFunctionByDataFingerprint"/>: take
    /// what the OLD function read, translate each address through what the data
    /// tiers have already resolved, and the new function is the one that reads
    /// the translated set.
    ///
    /// Returns empty for a function that references no globals, which is a
    /// refusal — those cannot be fingerprinted at all.
    /// </summary>
    public static List<ulong> ReferencedTargets(ReadOnlySpan<byte> image, ulong function)
    {
        var start = (int)function;
        if (start < 0 || start >= image.Length)
        {
            return [];
        }

        var end = FunctionEnd(image, start);
        var targets = new HashSet<ulong>();
        for (var i = start; i < end; i++)
        {
            if (DecodeRipRef(image, i) is not { } reference)
            {
                continue;
            }
            var displacement = BitConverter.ToInt32(image.Slice(i + reference.DisplacementOffset, 4));
            var target = i + reference.Length + (long)displacement;
            if (target >= 0 && target < image.Length)
            {
                targets.Add((ulong)target);
            }
        }
        return [.. targets];
    }

    /// <summary>
    /// Walk back to the start of the function containing <paramref name="site"/>.
    ///
    /// MSVC pads between functions with INT3, so a run of two marks the
    /// boundary. Two rather than one: a lone 0xCC also occurs inside instruction
    /// encodings and as immediate data, and stopping at one of those puts the
    /// "function start" in the middle of an instruction.
    /// </summary>
    private static int FunctionStart(ReadOnlySpan<byte> image, int site)
    {
        for (var i = site; i >= 2; i--)
        {
            if (image[i - 1] == 0xCC && image[i - 2] == 0xCC)
            {
                return i;
            }
        }
        return 0;
    }

    private static int FunctionEnd(ReadOnlySpan<byte> image, int start)
    {
        for (var i = start; i + 1 < image.Length; i++)
        {
            if (image[i] == 0xCC && image[i + 1] == 0xCC)
            {
                return i;
            }
        }
        return image.Length;
    }
}
