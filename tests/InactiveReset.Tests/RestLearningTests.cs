using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The guard on automatic learning.
///
/// This is the highest-stakes code added for self-calibration. A rejected sample
/// costs nothing; an accepted bad one is written down as truth and every later
/// placement at that checkpoint inherits it.
///
/// Both cases below are REAL placements from 2026-08-22, in the same car,
/// minutes apart. One is the model working; the other would have poisoned it.
/// </summary>
public sealed class RestLearningTests
{
    private static ContainerState Bmw() =>
        new(SlotIndex: 0, PitIndex: 6, GarageIndex: 0, ControlOwner: 1,
            VehicleLength: 5.023269f, VehicleWidth: 2.004824f, LateralSignSource: 8.60127f,
            RestOffsetPrimary: -2.550228f, RestOffsetSecondary: -0.143869f);

    /// <summary>
    /// A settled car. Note motion is 0.001, NOT zero: that is what a stationary
    /// car actually reads, and gating on bit-zero threw away good placements.
    /// </summary>
    private static PlacementOutcome Outcome(
        bool read = true, float motion = 0.001f, bool player = true, bool neutral = true) => new()
    {
        Completed = true,
        Message = "placed",
        Arrival = new ArrivalVerification
        {
            Checked = read,
            ControlIsPlayer = player,
            GearIsNeutral = neutral,
            LargestMotionComponent = motion,
        },
    };

    [Fact]
    public void AGoodPlacementIsLearnedFrom()
    {
        // The clean BMW placement: 2.4 mm, in the open, arrival verified.
        var reject = LearnedRest.RejectReason(
            Outcome(), Bmw(), errorForward: 0.000970f, errorLateral: 0.002160f,
            impliedCandidate: 0.01);

        Assert.Null(reject);
    }

    [Fact]
    public void AWrongClearanceCandidateIsRefused()
    {
        // The wall placement. The engine rejected the first nine candidates and
        // stepped out to the last one it is allowed, missing by 1.794 m. The
        // implied constants were D = 1.95, L = 1.80 — both garbage, and both
        // entirely plausible-looking in a file.
        var reject = LearnedRest.RejectReason(
            Outcome(), Bmw(), errorForward: -1.268600f, errorLateral: 1.268100f,
            impliedCandidate: 8.95);

        Assert.NotNull(reject);
        Assert.Contains("clearance-search step", reject);
        Assert.Contains("candidate 9", reject);
    }

    [Fact]
    public void AMissOffTheWholeStepsDoesNotBlameTheClearanceSearch()
    {
        // The Daytona miss from an unsigned L: 1.075 m, implying candidate
        // -3.81. The engine can only take WHOLE steps, so a fractional index is
        // evidence AGAINST the clearance search, not for it. The message used to
        // assert "candidate -3.8" as a finding and sent a diagnosis after the
        // banking of a track that had nothing to do with it.
        var reject = LearnedRest.RejectReason(
            Outcome(), Bmw(), errorForward: 0.000735f, errorLateral: -1.074646f,
            impliedCandidate: -3.81);

        Assert.NotNull(reject);
        Assert.Contains("does NOT lie on a whole step", reject);
        Assert.DoesNotContain("used search candidate", reject);
    }

    /// <summary>Genesis Magma Racing 2026 #17:LM. Lateral sign -1.</summary>
    private static ContainerState Genesis() =>
        new(SlotIndex: 0, PitIndex: 0, GarageIndex: 0, ControlOwner: 1,
            VehicleLength: 5.0982f, VehicleWidth: 1.99268f, LateralSignSource: -13.496212f);

    [Fact]
    public void ASpotSpecificSettleOnBankingIsLearnedFrom()
    {
        // Daytona day4, on the banking: 0.061 m, 59 mm of it lateral, arrival
        // verified, in the open. Real and repeatable, and learning is keyed per
        // CHECKPOINT so the correction can only ever apply back at this spot.
        //
        // The cap used to be a quarter step (0.050 m) and refused this, which
        // meant no banked spot could ever self-improve.
        Assert.Null(LearnedRest.RejectReason(
            Outcome(), Genesis(), errorForward: -0.013712f, errorLateral: 0.059064f,
            impliedCandidate: 0.2));
    }

    [Fact]
    public void AMissBetweenASettleAndAWholeStepIsStillRefused()
    {
        // The murky middle: too big to be a settle, not on a whole step. Nothing
        // measured has ever landed here, and a sample nobody can explain is not
        // one to write down as truth.
        var reject = LearnedRest.RejectReason(
            Outcome(), Genesis(), errorForward: 0.02f, errorLateral: 0.15f,
            impliedCandidate: 0.55);

        Assert.NotNull(reject);
        Assert.Contains("too large to be a calibration error", reject);
    }

    [Fact]
    public void TheFirstWholeStepIsRefusedEvenThoughItIsSmall()
    {
        // Candidate 1 is only 0.199 m -- the smallest clearance-search artifact
        // there is, and the one closest to passing for a settle. It must not.
        var reject = LearnedRest.RejectReason(
            Outcome(), Genesis(), errorForward: 0.0f, errorLateral: 0.199268f,
            impliedCandidate: 1.0);

        Assert.NotNull(reject);
        Assert.Contains("candidate 1", reject);
    }

    [Fact]
    public void ASettledCarWithNonZeroMotionIsStillLearnedFrom()
    {
        // The regression this guard already caused once. A stationary car reads
        // ~0.001, never bit-zero, so ArrivalVerification.Verified is true or
        // false almost at random. Two consecutive 3 mm placements were thrown
        // away before this was fixed.
        Assert.Null(LearnedRest.RejectReason(
            Outcome(motion: 0.0010045f), Bmw(), 0.000970f, 0.002160f, 0.01));
    }

    [Fact]
    public void ACarStillRollingIsRefused()
    {
        // Not settled yet, and the settle is the thing being measured.
        var reject = LearnedRest.RejectReason(
            Outcome(motion: 1.5f), Bmw(), 0.001f, 0.002f, 0.0);

        Assert.NotNull(reject);
        Assert.Contains("still moving", reject);
    }

    [Fact]
    public void AnUnreadableArrivalIsRefused()
    {
        var reject = LearnedRest.RejectReason(
            Outcome(read: false), Bmw(), 0.001f, 0.002f, 0.0);

        Assert.NotNull(reject);
        Assert.Contains("could not be read", reject);
    }

    [Fact]
    public void AnAbnormalPlacementPathIsRefused()
    {
        // Gear not neutral or control not with the player means this did not go
        // through the placement path, whatever the miss looks like.
        Assert.NotNull(LearnedRest.RejectReason(Outcome(neutral: false), Bmw(), 0.001f, 0.002f, 0.0));
        Assert.NotNull(LearnedRest.RejectReason(Outcome(player: false), Bmw(), 0.001f, 0.002f, 0.0));
    }

    [Fact]
    public void AFailedPlacementIsRefused()
    {
        var failed = new PlacementOutcome { Completed = false, Message = "gates failed" };
        var reject = LearnedRest.RejectReason(failed, Bmw(), 0f, 0f, 0.0);

        Assert.NotNull(reject);
        Assert.Contains("did not complete", reject);
    }

    [Fact]
    public void TheRejectionThresholdSitsWellBelowOneSearchStep()
    {
        // A step is 0.1 * width = 0.200 m for this car. Real calibration errors
        // have been under 10 mm. The threshold must reject a whole step with
        // room to spare, and accept an ordinary miss with room to spare.
        var step = 0.1f * Bmw().VehicleWidth;

        Assert.Null(LearnedRest.RejectReason(Outcome(), Bmw(), 0.02f, 0.02f, 0.1));
        Assert.NotNull(LearnedRest.RejectReason(Outcome(), Bmw(), step, 0f, 1.0));
    }

    [Fact]
    public void LearningAveragesRatherThanReplacing()
    {
        // One sample is mathematically sufficient, but the settle is physical
        // and terrain varies, so later samples should pull the value rather than
        // overwrite it.
        var first = LearnedRest.First("BUILD", "T", "V", "cp", 3.0f, 0.5f);
        var second = first.With(3.2f, 0.7f);

        Assert.Equal(2, second.SampleCount);
        Assert.Equal(3.1f, second.Forward, 5);
        Assert.Equal(0.6f, second.Lateral, 5);
    }

    [Fact]
    public void ALearnedValueFromAnotherBuildIsNotUsed()
    {
        // D moved 0.5 m across a single patch, so a learned value from another
        // build is not evidence about this one.
        var data = Directory.CreateTempSubdirectory("inactive-reset-learn-").FullName;
        try
        {
            var store = new LearnedRestStore(data);
            store.Save(LearnedRest.First("AAAA1111", "T", "V", "cp", 3.0f, 0.5f));

            Assert.NotNull(store.Load("AAAA1111", "T", "V", "cp"));
            Assert.Null(store.Load("BBBB2222", "T", "V", "cp"));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
