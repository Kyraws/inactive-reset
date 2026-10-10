using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LapValidityTests
{
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
