// The class and its namespace share a name, so the type needs an alias here.
using ReanchorTool = InactiveReset.Reanchor.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The two tiers that survive a RECOMPILE.
///
/// Signature matching finds a function that moved; it cannot find one the
/// compiler regenerated, and reference voting fails with it because the
/// instructions it needs to locate live inside those regenerated functions.
/// The 1.4.1.3 patch did exactly that and left five required addresses
/// unresolved, <c>probe</c> among them.
///
/// What survives is coarser: how MANY instructions read an address and what
/// they look like, and which globals a function reads. Both are statistical, so
/// these tests pin the refusals as hard as the hits -- a tier that answers
/// confidently on thin evidence is worse than one that stays quiet.
/// </summary>
public sealed class ReferenceProfileTests
{
    /// <summary>
    /// Deterministic filler, so a failure is reproducible. It stands in for the
    /// rest of the module and must not accidentally contain the patterns under
    /// test.
    /// </summary>
    private static byte[] Filler(int length, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Write <c>mov rax,[rip+disp]</c> (48 8B 05) at <paramref name="at"/>,
    /// aimed at <paramref name="target"/>, preceded by <paramref name="lead"/>.
    ///
    /// The lead bytes are what the profile tier actually compares: they stand in
    /// for the instruction before the read, which usually survives a recompile
    /// even when the read's own registers do not.
    /// </summary>
    private static void WriteRead(byte[] image, int at, int target, byte[] lead)
    {
        lead.CopyTo(image, at - lead.Length);
        image[at] = 0x48;
        image[at + 1] = 0x8B;
        image[at + 2] = 0x05;
        BitConverter.GetBytes(target - (at + 7)).CopyTo(image, at + 3);
    }

    /// <summary>Bracket a function body with the INT3 padding MSVC emits.</summary>
    private static void Pad(byte[] image, int at)
    {
        image[at] = 0xCC;
        image[at + 1] = 0xCC;
    }

    // ---- reference profile --------------------------------------------------

    [Fact]
    public void An_address_read_by_recompiled_code_is_found_by_its_reference_profile()
    {
        // Five reads of one address, each with a distinct leading instruction.
        // In the new image every read has MOVED and the address has moved with
        // it, so nothing matches by position -- only by shape.
        var older = Filler(0x8000, seed: 11);
        var newer = Filler(0x8000, seed: 12);
        const int oldTarget = 0x6000;
        const int newTarget = 0x5AB0;

        byte[][] leads =
        [
            [0x3B, 0xC2, 0x0F, 0x4C, 0xD0],
            [0x08, 0x41, 0x89, 0x41, 0x08],
            [0x24, 0x54, 0x48, 0x63, 0xC9],
            [0x98, 0x48, 0xC1, 0xE0, 0x05],
            [0xE8, 0x01, 0x81, 0xF6, 0xFF],
        ];
        for (var i = 0; i < leads.Length; i++)
        {
            WriteRead(older, 0x1000 + i * 0x40, oldTarget, leads[i]);
            WriteRead(newer, 0x2200 + i * 0x60, newTarget, leads[i]);
        }

        var oldIndex = ReanchorTool.BuildReferenceIndex(older);
        var newIndex = ReanchorTool.BuildReferenceIndex(newer);

        var match = ReanchorTool.RemapDataByReferenceProfile(
            older, newer, oldTarget, oldIndex, newIndex);

        Assert.NotNull(match);
        Assert.Equal((ulong)newTarget, match!.NewRva);
        Assert.True(match.Unrivalled);
        Assert.Equal(5, match.OldSites);
    }

    [Fact]
    public void A_neighbour_read_by_the_same_code_does_not_outrank_the_answer()
    {
        // The real failure mode: garPosTable sits eight bytes from pitPosTable
        // and the same functions read both. The right answer must still win.
        var older = Filler(0x8000, seed: 21);
        var newer = Filler(0x8000, seed: 22);
        const int oldTarget = 0x6000;
        const int oldNeighbour = 0x6008;
        const int newTarget = 0x5AB0;
        const int newNeighbour = 0x5AB8;

        byte[][] leads =
        [
            [0x3B, 0xC2, 0x0F, 0x4C, 0xD0],
            [0x08, 0x41, 0x89, 0x41, 0x08],
            [0x24, 0x54, 0x48, 0x63, 0xC9],
        ];
        byte[][] neighbourLeads =
        [
            [0x11, 0x22, 0x33, 0x44, 0x55],
            [0x66, 0x77, 0x88, 0x99, 0xAA],
        ];

        for (var i = 0; i < leads.Length; i++)
        {
            WriteRead(older, 0x1000 + i * 0x40, oldTarget, leads[i]);
            WriteRead(newer, 0x2200 + i * 0x60, newTarget, leads[i]);
        }
        for (var i = 0; i < neighbourLeads.Length; i++)
        {
            WriteRead(older, 0x1400 + i * 0x40, oldNeighbour, neighbourLeads[i]);
            WriteRead(newer, 0x2600 + i * 0x60, newNeighbour, neighbourLeads[i]);
        }

        var match = ReanchorTool.RemapDataByReferenceProfile(
            older, newer, oldTarget,
            ReanchorTool.BuildReferenceIndex(older),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.NotNull(match);
        Assert.Equal((ulong)newTarget, match!.NewRva);
        Assert.NotEqual((ulong)newNeighbour, match.NewRva);
    }

    [Fact]
    public void An_address_with_too_few_references_is_refused()
    {
        // One reference is not a profile. Measured on the real patch,
        // trackLimits.config won on four references and derivedFlags.0 on
        // three -- thin enough that neither may be trusted alone, and below the
        // floor the tier must not answer at all.
        var older = Filler(0x8000, seed: 31);
        var newer = Filler(0x8000, seed: 32);

        WriteRead(older, 0x1000, 0x6000, [0x3B, 0xC2, 0x0F, 0x4C, 0xD0]);
        WriteRead(newer, 0x2200, 0x5AB0, [0x3B, 0xC2, 0x0F, 0x4C, 0xD0]);

        var match = ReanchorTool.RemapDataByReferenceProfile(
            older, newer, 0x6000,
            ReanchorTool.BuildReferenceIndex(older),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.Null(match);
    }

    [Fact]
    public void An_address_absent_from_the_old_image_is_refused()
    {
        var older = Filler(0x8000, seed: 41);
        var newer = Filler(0x8000, seed: 42);

        var match = ReanchorTool.RemapDataByReferenceProfile(
            older, newer, 0x6000,
            ReanchorTool.BuildReferenceIndex(older),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.Null(match);
    }

    // ---- function fingerprint -----------------------------------------------

    [Fact]
    public void A_recompiled_function_is_found_by_the_globals_it_reads()
    {
        // getSpotTransform in miniature: a function reading four globals, whose
        // bytes are entirely different in the new image. Only the SET of things
        // it reads is preserved.
        var older = Filler(0x8000, seed: 51);
        var newer = Filler(0x8000, seed: 52);

        int[] oldTargets = [0x6000, 0x6008, 0x6024, 0x6028];
        int[] newTargets = [0x5AB0, 0x5AB8, 0x5AD4, 0x5AD8];

        const int oldFunction = 0x1000;
        const int newFunction = 0x2200;
        Pad(older, oldFunction - 2);
        Pad(newer, newFunction - 2);

        for (var i = 0; i < oldTargets.Length; i++)
        {
            WriteRead(older, oldFunction + 0x20 + i * 0x30, oldTargets[i], [1, 2, 3, 4, 5]);
            WriteRead(newer, newFunction + 0x24 + i * 0x30, newTargets[i], [9, 8, 7, 6, 5]);
        }
        Pad(older, oldFunction + 0x200);
        Pad(newer, newFunction + 0x200);

        var match = ReanchorTool.LocateFunctionByDataFingerprint(
            older, newer, oldFunction,
            newTargets.Select(t => (ulong)t).ToArray(),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.NotNull(match);
        Assert.Equal((ulong)newFunction, match!.NewRva);
        Assert.Equal(4, match.TargetsFound);
        Assert.True(match.LengthPlausible);
    }

    [Fact]
    public void A_region_reading_only_some_of_the_globals_loses_to_one_reading_all()
    {
        var older = Filler(0x8000, seed: 61);
        var newer = Filler(0x8000, seed: 62);

        int[] newTargets = [0x5AB0, 0x5AB8, 0x5AD4, 0x5AD8];
        const int decoy = 0x1200;
        const int real = 0x2200;
        Pad(newer, decoy - 2);
        Pad(newer, real - 2);

        // The decoy reads two of the four, repeatedly -- more references, fewer
        // distinct targets. Distinct targets must win.
        for (var i = 0; i < 6; i++)
        {
            WriteRead(newer, decoy + 0x20 + i * 0x20, newTargets[i % 2], [1, 2, 3, 4, 5]);
        }
        for (var i = 0; i < newTargets.Length; i++)
        {
            WriteRead(newer, real + 0x20 + i * 0x30, newTargets[i], [9, 8, 7, 6, 5]);
        }

        var match = ReanchorTool.LocateFunctionByDataFingerprint(
            older, newer, 0x1000,
            newTargets.Select(t => (ulong)t).ToArray(),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.NotNull(match);
        Assert.Equal((ulong)real, match!.NewRva);
        Assert.Equal(4, match.TargetsFound);
    }

    [Fact]
    public void A_function_whose_globals_are_unresolved_is_refused()
    {
        // The ordering constraint, as a test: this tier cannot run before the
        // data tiers have produced seeds.
        var older = Filler(0x8000, seed: 71);
        var newer = Filler(0x8000, seed: 72);

        var match = ReanchorTool.LocateFunctionByDataFingerprint(
            older, newer, 0x1000, [], ReanchorTool.BuildReferenceIndex(newer));

        Assert.Null(match);
    }

    [Fact]
    public void A_wildly_different_length_is_reported_as_implausible()
    {
        // Landing in the wrong function is the failure this guards. The tier
        // still returns its answer -- refusing is the caller's decision -- but
        // it must not claim the length is consistent.
        var older = Filler(0x8000, seed: 81);
        var newer = Filler(0x8000, seed: 82);

        int[] newTargets = [0x5AB0, 0x5AB8];
        const int oldFunction = 0x1000;
        const int newFunction = 0x2200;

        Pad(older, oldFunction - 2);
        Pad(older, oldFunction + 0x40);       // short in the old image
        Pad(newer, newFunction - 2);

        for (var i = 0; i < newTargets.Length; i++)
        {
            WriteRead(newer, newFunction + 0x20 + i * 0x40, newTargets[i], [9, 8, 7, 6, 5]);
        }
        Pad(newer, newFunction + 0x600);      // and very long in the new one

        var match = ReanchorTool.LocateFunctionByDataFingerprint(
            older, newer, oldFunction,
            newTargets.Select(t => (ulong)t).ToArray(),
            ReanchorTool.BuildReferenceIndex(newer));

        Assert.NotNull(match);
        Assert.False(match!.LengthPlausible);
    }
}
