namespace InactiveReset.Core;

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
        int context = 24)
    {
        if (!oldIndex.TryGetValue(target, out var sites) || sites.Count == 0)
        {
            return null;
        }

        var votes = new Dictionary<ulong, int>();
        foreach (var (site, displacementOffset) in sites)
        {
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
            if (Reanchor.DecodeRipRef(newer, newSite) is not { } reference)
            {
                continue;
            }
            var displacement = BitConverter.ToInt32(newer.Slice(newSite + reference.DisplacementOffset, 4));
            var resolved = (ulong)(newSite + reference.Length + displacement);
            votes[resolved] = votes.GetValueOrDefault(resolved) + 1;
        }

        if (votes.Count == 0)
        {
            return null;
        }

        var best = votes.OrderByDescending(v => v.Value).First();
        return new DataMatch(target, best.Key, best.Value, sites.Count, votes.Count);
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
}
