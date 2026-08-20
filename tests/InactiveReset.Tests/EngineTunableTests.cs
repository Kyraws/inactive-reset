using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The range checks on the live-read engine tunables.
///
/// These matter more than they look. A stale address does not fault -- it
/// returns four perfectly ordinary-looking floats, and ordinary-looking floats
/// place the car somewhere ordinary-looking and wrong. That is the failure this
/// project has had twice. These tests are the only thing standing between a
/// relocated <c>.data</c> block and a silently mis-placed car.
/// </summary>
public sealed class EngineTunableTests
{
    /// <summary>The 1AC2F605 profile's real values.</summary>
    private static EngineModelSpec Spec() => new()
    {
        YawOffsetDegrees = new Rva(0x03B3621C, Confidence.Established),
        SearchStartFactorRva = new Rva(0x03B3620C, Confidence.Established),
        SearchStepFactorRva = new Rva(0x03B36210, Confidence.Established),
        SearchMaxFactorRva = new Rva(0x03B36214, Confidence.Established),

        FallbackYawOffsetRadians = 0.7853982f,
        FallbackSearchStartFactor = 0.55f,
        FallbackSearchStepFactor = 0.1f,
        FallbackSearchMaxFactor = 1.5f,

        YawOffsetDegreesMin = 0f,
        YawOffsetDegreesMax = 90f,
        SearchFactorMin = 0.001f,
        SearchFactorMax = 5f,
    };

    private static EngineTunables Validate(float? deg, float? start, float? step, float? max) =>
        EngineTunables.Validate(deg, start, step, max, Spec(), []);

    [Fact]
    public void LiveValuesInRangeAreUsedAndConvertedToRadians()
    {
        var t = Validate(45f, 0.55f, 0.1f, 1.5f);

        Assert.Equal(TunableSource.Live, t.Source);
        Assert.Empty(t.Rejections);
        // 45 deg -> 0.7853982 rad. The engine stores degrees; the model wants radians.
        Assert.Equal(0.7853982f, t.YawOffsetRadians, 6);
        Assert.Equal(0.55f, t.SearchStartFactor);
    }

    /// <summary>
    /// The retune that started all this. 35 -> 45 deg must be picked up with no
    /// profile change at all: that is the entire point of reading live.
    /// </summary>
    [Fact]
    public void ARetuneIsPickedUpWithoutTouchingTheProfile()
    {
        var before = Validate(35f, 0.2f, 0.1f, 1.5f);
        var after = Validate(45f, 0.55f, 0.1f, 1.5f);

        Assert.Equal(TunableSource.Live, before.Source);
        Assert.Equal(TunableSource.Live, after.Source);
        Assert.Equal(0.6108652f, before.YawOffsetRadians, 6);
        Assert.Equal(0.7853982f, after.YawOffsetRadians, 6);
        Assert.NotEqual(before.SearchStartFactor, after.SearchStartFactor);
    }

    [Theory]
    // Garbage of the kind a relocated block actually produces -- see the
    // neighbours of 0x018E93F4 in the 44C3EE9C dump: denormals and 1e+29.
    [InlineData(3.4028235e+38f, 0.55f, 0.1f, 1.5f)]
    [InlineData(1.4e-45f, 0.55f, 0.1f, 1.5f)]
    [InlineData(45f, 1.66e+22f, 0.1f, 1.5f)]
    [InlineData(45f, 0.55f, 0.1f, 4.15e+21f)]
    [InlineData(-30f, 0.55f, 0.1f, 1.5f)]
    [InlineData(180f, 0.55f, 0.1f, 1.5f)]
    public void OutOfRangeValuesFallBackAndSayWhy(float deg, float start, float step, float max)
    {
        var t = Validate(deg, start, step, max);

        Assert.Equal(TunableSource.Fallback, t.Source);
        Assert.NotEmpty(t.Rejections);
        Assert.Equal(0.7853982f, t.YawOffsetRadians, 6);
    }

    [Fact]
    public void NonFiniteValuesFallBack()
    {
        Assert.Equal(TunableSource.Fallback, Validate(float.NaN, 0.55f, 0.1f, 1.5f).Source);
        Assert.Equal(TunableSource.Fallback, Validate(45f, float.PositiveInfinity, 0.1f, 1.5f).Source);
    }

    /// <summary>
    /// Four individually plausible numbers can still be an incoherent SET, and an
    /// incoherent set means a wrong address just as surely as a huge one does.
    /// This is the check that catches a block which moved somewhere equally
    /// float-shaped.
    /// </summary>
    [Fact]
    public void IndividuallyPlausibleButIncoherentValuesFallBack()
    {
        // step >= max: the clearance search would never terminate as modelled.
        var badStep = Validate(45f, 0.55f, 2.0f, 1.5f);
        Assert.Equal(TunableSource.Fallback, badStep.Source);
        Assert.Contains(badStep.Rejections, r => r.Contains("not less than"));

        // start beyond max: the first candidate is already out of bounds.
        var badStart = Validate(45f, 3.0f, 0.1f, 1.5f);
        Assert.Equal(TunableSource.Fallback, badStart.Source);
        Assert.Contains(badStart.Rejections, r => r.Contains("exceeds max"));
    }

    [Fact]
    public void AnUnreadableValueFallsBack()
    {
        var t = EngineTunables.Validate(null, 0.55f, 0.1f, 1.5f, Spec(),
                                        ["yaw offset could not be read"]);

        Assert.Equal(TunableSource.Fallback, t.Source);
        Assert.Contains(t.Rejections, r => r.Contains("could not be read"));
    }

    /// <summary>
    /// A fallback that is not itself checked is just a second way to be
    /// confidently wrong. When the live read fails AND the snapshot is nonsense,
    /// the only honest answer is a refusal.
    /// </summary>
    [Fact]
    public void RefusesWhenLiveAndFallbackBothFail()
    {
        var spec = Spec() with { FallbackYawOffsetRadians = 99f };  // ~5672 deg

        var ex = Assert.Throws<StaleOffsetException>(
            () => EngineTunables.Validate(float.NaN, 0.55f, 0.1f, 1.5f, spec, []));

        Assert.Contains("does not describe the running build", ex.Message);
    }

    /// <summary>An address the reanchor could not resolve must never be read.</summary>
    [Fact]
    public void UnresolvedAddressIsNotTrusted()
    {
        var spec = Spec() with
        {
            YawOffsetDegrees = new Rva(0x03B3621C, Confidence.Unresolved, "stale"),
        };
        Assert.Equal(Confidence.Unresolved, spec.YawOffsetDegrees.Confidence);
        Assert.Throws<StaleOffsetException>(() => spec.YawOffsetDegrees.Require("yaw offset"));
    }
}
