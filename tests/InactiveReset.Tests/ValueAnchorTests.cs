// The class and its namespace share a name, so the type needs an alias here.
using ReanchorTool = InactiveReset.Reanchor.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// Value anchoring: re-derive a data address from the CONTENT at that address
/// rather than from the code referencing it.
///
/// This exists because signature and reference matching both fail when the
/// compiler regenerates the functions doing the referencing, which is what the
/// 1.4.1.3 patch did. Constants survive that; live state does not, and the
/// difference is what these pin.
/// </summary>
public sealed class ValueAnchorTests
{
    /// <summary>
    /// An image of pseudo-random filler, deterministic so a failure is
    /// reproducible. Filler stands in for the rest of the module: it must not
    /// accidentally contain the block being searched for.
    /// </summary>
    private static byte[] Filler(int length, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>The engine tunables, as they actually sit in the game.</summary>
    private static byte[] TunableBlock() =>
    [
        .. BitConverter.GetBytes(0.55f),
        .. BitConverter.GetBytes(0.1f),
        .. BitConverter.GetBytes(1.5f),
        .. BitConverter.GetBytes(25f),
        .. BitConverter.GetBytes(45f),
    ];

    [Fact]
    public void A_constant_block_is_found_at_its_new_address()
    {
        var block = TunableBlock();
        var older = Filler(0x8000, seed: 1);
        var newer = Filler(0x8000, seed: 2);

        const int oldAt = 0x1000;
        const int newAt = 0x1000 - 0x550;   // the block moved
        block.CopyTo(older, oldAt);
        block.CopyTo(newer, newAt);

        var match = ReanchorTool.RemapDataByValue(older, newer, oldAt);

        Assert.NotNull(match);
        Assert.Equal((ulong)newAt, match.NewRva);
        Assert.Equal(-0x550, match.Delta);
        Assert.True(match.Corroborated);
    }

    /// <summary>
    /// The case that was missed at first. yawOffsetDegrees is the LAST float in
    /// the block, so a window that only ever extends forwards runs off the
    /// constants into whatever follows -- which differs between two captures.
    /// </summary>
    [Fact]
    public void A_constant_at_the_end_of_a_block_is_found_too()
    {
        var block = TunableBlock();
        var older = Filler(0x8000, seed: 3);
        var newer = Filler(0x8000, seed: 4);

        const int oldStart = 0x2000;
        const int newStart = 0x2000 - 0x550;
        block.CopyTo(older, oldStart);
        block.CopyTo(newer, newStart);

        // The last float, 16 bytes into the block.
        var match = ReanchorTool.RemapDataByValue(older, newer, oldStart + 16);

        Assert.NotNull(match);
        Assert.Equal((ulong)(newStart + 16), match.NewRva);
    }

    /// <summary>
    /// Volatile runtime state has different content in two captures taken at
    /// different moments, so it identifies nothing and must return null rather
    /// than a guess.
    /// </summary>
    [Fact]
    public void Volatile_data_is_refused_rather_than_guessed()
    {
        var older = Filler(0x8000, seed: 5);
        var newer = Filler(0x8000, seed: 6);

        Assert.Null(ReanchorTool.RemapDataByValue(older, newer, 0x1000));
    }

    /// <summary>
    /// A block that appears twice in the new image cannot be told apart, so it
    /// is refused. Picking either one would be a coin toss reported as a fact.
    /// </summary>
    [Fact]
    public void An_ambiguous_block_is_refused()
    {
        var block = TunableBlock();
        var older = Filler(0x8000, seed: 7);
        var newer = Filler(0x8000, seed: 8);

        block.CopyTo(older, 0x1000);
        block.CopyTo(newer, 0x0800);
        block.CopyTo(newer, 0x3000);   // and again

        Assert.Null(ReanchorTool.RemapDataByValue(older, newer, 0x1000));
    }

    /// <summary>
    /// A run of zeroes matches everywhere. Demanding variety is what stops the
    /// technique from confidently resolving padding.
    /// </summary>
    [Fact]
    public void A_bland_region_is_refused()
    {
        var older = new byte[0x8000];
        var newer = new byte[0x8000];

        Assert.Null(ReanchorTool.RemapDataByValue(older, newer, 0x1000));
    }
}
