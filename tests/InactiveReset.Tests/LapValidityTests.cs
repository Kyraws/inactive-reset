using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The lap-validity rule, and the offsets it depends on.
///
/// The offsets are asserted against the shipped profile rather than duplicated
/// as constants here. They were verified byte-for-byte in
/// <c>LMU_runtime_44C3EE9C.bin</c> — the dump the 1AC2F605 profile was derived
/// from — by matching the exact instruction encodings that read and write them.
/// If a reanchor loses them, this fails loudly instead of the placement quietly
/// stopping fixing the out-lap.
/// </summary>
public sealed class LapValidityTests
{
    /// <summary>
    /// Loads the profile by filename, then checks the hash inside it. The
    /// filename is only a hint — <c>ForBuild</c> is right to treat the hash as
    /// the evidence, and so is this. Loading every *.json in the directory would
    /// not work: <c>shared-memory.json</c> lives there too and is not a build
    /// profile.
    /// </summary>
    private static ContainerSpec Containers()
    {
        var profile = OffsetProfile.Load(
            Path.Combine(RepositoryRoot(), "offsets", "1AC2F605.json"));
        Assert.StartsWith("1AC2F605", profile.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
        return profile.Containers;
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "offsets")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new DirectoryNotFoundException("could not find the repository root");
    }

    [Theory]
    [InlineData("countLapFlag", 0x1CFF0UL, "int32")]
    [InlineData("lapCountsNext", 0x1CFF4UL, "byte")]
    [InlineData("pitFlag", 0x8FD0UL, "byte")]
    [InlineData("pitState", 0x8FC0UL, "int32")]
    [InlineData("lapStartEt", 0x1CFF8UL, "double")]
    [InlineData("lapNumber", 0x1CEA0UL, "int32")]
    [InlineData("sector", 0x1CEA8UL, "int32")]
    public void ProfileCarriesTheLapValidityFields(string name, ulong offset, string type)
    {
        var field = Containers().Field(name);
        Assert.Equal(offset, field.Offset);
        Assert.Equal(type, field.Type);

        // All four are container-relative. Reading them from vehicle (+8) lands
        // on the neighbouring field and reports plausible nonsense, which is the
        // single most repeated mistake in this project's history.
        Assert.Equal(OffsetBase.Container, field.Base);
    }

    /// <summary>
    /// The latch and the pit flag sit 4 bytes apart in the same object, which is
    /// exactly close enough to confuse. Pin the relationship rather than the
    /// literals alone.
    /// </summary>
    [Fact]
    public void TheLatchImmediatelyFollowsCountLapFlag()
    {
        var containers = Containers();
        Assert.Equal(
            containers.Field("countLapFlag").Offset + 4,
            containers.Field("lapCountsNext").Offset);
    }

    /// <summary>
    /// The engine promotes at the line and then demotes again if the pit flag is
    /// still set. A prediction made from the latch alone is wrong in precisely
    /// the case this whole feature exists for, so the demotion has to be part of
    /// the prediction.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]    // latch set, not in pits -> timed
    [InlineData(true, true, false)]    // latch set, STILL IN PITS -> demoted
    [InlineData(false, false, false)]  // latch clear -> not timed either way
    [InlineData(false, true, false)]
    public void NextLapPredictionAppliesTheOutLapDemotion(
        bool latch, bool pitFlag, bool expected)
    {
        var state = State(countLapFlag: 1, latch: latch, pitFlag: pitFlag);
        Assert.Equal(expected, state.NextLapWillBeTimed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void OnlyCountLapAndTimeProducesALapTime(int flag, bool timed)
    {
        Assert.Equal(timed, State(flag, latch: true, pitFlag: false).CurrentLapIsTimed);
    }

    [Fact]
    public void CountLapFlagMapsOntoTheEnginesEnum()
    {
        Assert.Equal(0, (int)LapCounting.Neither);
        Assert.Equal(1, (int)LapCounting.LapOnly);
        Assert.Equal(2, (int)LapCounting.LapAndTime);
    }

    /// <summary>
    /// The case the driver actually hits: placed, latch armed by Slot_Reset, but
    /// the pit flag never cleared. The summary has to name the pit flag — "not
    /// timed" alone gives no way to act.
    /// </summary>
    [Fact]
    public void SummaryNamesThePitFlagWhenItIsWhatIsHoldingTheLapBack()
    {
        var state = State(countLapFlag: 1, latch: true, pitFlag: true);
        Assert.Contains("pit flag", state.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static LapValidityState State(int countLapFlag, bool latch, bool pitFlag) => new()
    {
        SlotIndex = 0,
        CountLapFlag = countLapFlag,
        LapCountsNext = latch,
        PitFlag = pitFlag,
        PitState = 0,
        LapStartEt = 0,
        LapNumber = 0,
        Sector = 0,
        CountLapFlagAddress = 0,
        LapCountsNextAddress = 0,
        PitFlagAddress = 0,
    };
}
