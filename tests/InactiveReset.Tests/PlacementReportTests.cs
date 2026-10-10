using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The placement report is where an outcome becomes sentences a driver acts on.
///
/// These are the assertions that could not be written before it existed. The
/// sequence that produces a <see cref="PlacementOutcome"/> still needs a running
/// game; deciding what one MEANS does not, and that is the half where the two
/// front ends had already drifted apart.
/// </summary>
public sealed class PlacementReportTests
{
    [Theory]
    [InlineData(true, OutcomeSeverity.Good)]
    [InlineData(false, OutcomeSeverity.Warning)]
    public void Tyre_verification_is_reported_independently_of_placement(bool verified, OutcomeSeverity severity)
    {
        var outcome=Placed() with { TyreReset=new(verified,"four tyre result") };
        Assert.Equal(severity,Line(outcome,"Tyres").Severity);
        Assert.Equal("four tyre result",Line(outcome,"Tyres").Value);
    }
    /// <summary>A placement that succeeded and did everything it was asked to.</summary>
    private static PlacementOutcome Placed() => new()
    {
        Completed = true,
        Message = "placed",
        HorizontalErrorMetres = 0.5663f,
        VerticalErrorMetres = 0.0018f,
        PitStateCleared = false,
        PitWaitSkipped = true,
        SectorWrite = new RuleWriteResult("sector", 0x1234, 0, 2, Changed: true),
    };

    private static OutcomeLine Line(PlacementOutcome outcome, string label) =>
        Assert.Single(PlacementReport.For(outcome), l => l.Label == label);

    // ---- the sector write --------------------------------------------------

    /// <summary>
    /// The regression this whole module exists for. The web page reported the
    /// pit flag and never the sector, so the driver was shown the secondary
    /// guard and not the fix.
    /// </summary>
    [Fact]
    public void ReportsTheSectorWrite()
    {
        var line = Line(Placed(), "sector");

        Assert.Equal("0 -> 2", line.Value);
        Assert.Equal(OutcomeSeverity.Good, line.Severity);
        Assert.Contains("count as a lap", line.Sentence);
    }

    /// <summary>
    /// "Not requested" and "tried and failed" used to be the same value -- null
    /// -- so no front end could tell them apart. The CLI guessed by inspecting
    /// its own arguments; the page could not guess at all.
    /// </summary>
    [Fact]
    public void DistinguishesASectorFailureFromASectorLeftAlone()
    {
        var failed = Line(Placed() with { SectorWrite = null, SectorFailure = "profile has no sector field" },
                          "sector");
        var skipped = Line(Placed() with { SectorWrite = null }, "sector");

        Assert.Equal(OutcomeSeverity.Warning, failed.Severity);
        Assert.Contains("profile has no sector field", failed.Sentence);

        Assert.Equal(OutcomeSeverity.Normal, skipped.Severity);
        Assert.NotEqual(failed.Value, skipped.Value);
    }

    // ---- the pit-state wait ------------------------------------------------

    [Fact]
    public void Never_calls_an_uncleared_pit_state_clear()
    {
        var uncleared = Placed() with
        {
            PitWaitSkipped = false,
            PitStateCleared = false,
            DistanceToClearMetres = 0f,
        };

        var line = Line(uncleared, "pit state");

        Assert.Equal("STILL SET", line.Value);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.DoesNotContain("clear", line.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Skipped and cleared are different claims: one says there was nothing to
    /// wait for, the other says something happened.
    /// </summary>
    [Fact]
    public void DistinguishesASkippedPitWaitFromAClearedOne()
    {
        var skipped = Line(Placed(), "pit state");
        var cleared = Line(
            Placed() with { PitWaitSkipped = false, PitStateCleared = true },
            "pit state");

        Assert.NotEqual(skipped.Value, cleared.Value);
        Assert.Equal(OutcomeSeverity.Good, skipped.Severity);
        Assert.Equal(OutcomeSeverity.Good, cleared.Severity);
    }

    // ---- failure -----------------------------------------------------------

    /// <summary>
    /// A failed placement still reports. The message carries what happened,
    /// including that the borrowed bytes went back, which is the fact that most
    /// needs to survive a failure.
    /// </summary>
    [Fact]
    public void ReportsAFailedPlacementWithItsReason()
    {
        var outcome = new PlacementOutcome
        {
            Completed = false,
            Message = "timed out waiting for Drive; the bytes were restored",
        };

        var line = Assert.Single(PlacementReport.For(outcome));

        Assert.Equal("FAILED", line.Value);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.Contains("the bytes were restored", line.Sentence);
    }

    // ---- a large horizontal miss -------------------------------------------

    /// <summary>
    /// A large miss is a defect and the report must name the causes worth
    /// checking, not reassure. It used to call the miss expected and blame a
    /// documented heading error, which is how an unsigned L went a whole session
    /// missing by 1.07 m without anyone reading it as broken.
    /// See docs/HEADING_BUG.md.
    /// </summary>
    [Fact]
    public void ExplainsALargeHorizontalError()
    {
        var line = Line(Placed(), "horizontal error");

        Assert.Equal(OutcomeSeverity.Normal, line.Severity);
        Assert.Contains("a real miss", line.Sentence);
        Assert.Contains("clearance search", line.Sentence);
        Assert.Contains("a different car", line.Sentence);
        Assert.DoesNotContain("expected, not a failed placement", line.Sentence);
    }

    [Fact]
    public void CallsAnAccuratePlacementGood()
    {
        var line = Line(Placed() with { HorizontalErrorMetres = 0.002f }, "horizontal error");

        Assert.Equal(OutcomeSeverity.Good, line.Severity);
        Assert.Null(line.Sentence);
    }

    // ---- the pit-speeding penalty ------------------------------------------

    [Fact]
    public void ReportsAFailureToDisableThePitSpeedingPenalty()
    {
        var outcome = Placed() with { PitSpeedingPenaltyFailure = "attached read-only" };

        var line = PlacementReport.ForPenalty(outcome);

        Assert.NotNull(line);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.Contains("stop/go", line.Sentence);
    }

    [Fact]
    public void ReportsTemporaryFlagRulesRestoredAfterClear()
    {
        var outcome = Placed() with
        {
            PitWaitSkipped = false,
            PitStateCleared = true,
            PitSpeedingPenalty = new RuleWriteResult("Pit-speeding gate", 0x1234, 3, 0, Changed: true),
        };

        var line = PlacementReport.ForPenalty(outcome);

        Assert.NotNull(line);
        Assert.Equal("3 -> 0 -> 3", line.Value);
        Assert.Equal(OutcomeSeverity.Good, line.Severity);
    }

    [Fact]
    public void WarnsWhenOriginalRulesReturnBeforePitStateClears()
    {
        var outcome = Placed() with
        {
            PitWaitSkipped = false,
            PitStateCleared = false,
            PitSpeedingPenalty = new RuleWriteResult("Pit-speeding gate", 0x1234, 2, 0, Changed: true),
        };

        var line = PlacementReport.ForPenalty(outcome);

        Assert.NotNull(line);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.Contains("limiter", line.Sentence);
    }

    [Fact]
    public void GarageCleanupDoesNotClaimPitStateCleared()
    {
        var outcome = Placed() with { ReturnedToGarage = true, PitWaitSkipped = false,
            PitSpeedingPenalty = new RuleWriteResult("Flag Rules", 0x1234, 3, 0, true) };
        var line = PlacementReport.ForPenalty(outcome)!;
        Assert.Equal(OutcomeSeverity.Good, line.Severity);
        Assert.Contains("garage", line.Sentence);
        Assert.Equal("returned to garage", PlacementReport.For(outcome).Single(l => l.Label == "pit state").Value);
    }

    [Fact]
    public void SessionChangeDoesNotClaimSavedRulesWereRestored()
    {
        var outcome = Placed() with { SessionEnded = true,
            PitSpeedingPenalty = new RuleWriteResult("Flag Rules", 0x1234, 3, 0, true) };
        var line = PlacementReport.ForPenalty(outcome)!;
        Assert.Equal("session ended", line.Value);
        Assert.Contains("not applied", line.Sentence);
    }

    /// <summary>
    /// Nothing to say when the penalty was neither written nor attempted, so
    /// nothing is said. A report full of "not applicable" trains the reader to
    /// skim it.
    /// </summary>
    [Fact]
    public void SaysNothingAboutThePenaltyWhenThereIsNothingToSay()
    {
        Assert.Null(PlacementReport.ForPenalty(Placed()));
    }

    // ---- the contract both front ends rely on ------------------------------

    /// <summary>
    /// Both front ends render whatever arrives, without inspecting it. A line
    /// with no label or no value would render as an empty row in the UI and a
    /// ragged column in the CLI.
    /// </summary>
    [Fact]
    public void EveryLineIsRenderable()
    {
        foreach (var line in PlacementReport.For(Placed()))
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Label));
            Assert.False(string.IsNullOrWhiteSpace(line.Value));
            Assert.NotEqual("", line.Sentence);
        }
    }
}
