using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The derived rest model, pinned to the four cars it was measured on.
///
/// All measured at Circuit de Barcelona on build 0F6DCAC1, 2026-08-22. The first
/// three fitted the two settle coefficients; the FOURTH did not — the BMW was
/// predicted blind, before it was ever placed, and came in at 2.4 mm. That is
/// the row that makes this a model rather than a curve fit, so if it ever fails,
/// the model is wrong and no amount of refitting the other three will save it.
/// </summary>
public sealed class RestModelTests
{
    private static ContainerState Car(float length, float width, float primary, float secondary) =>
        new(SlotIndex: 0, PitIndex: 0, GarageIndex: 0, ControlOwner: 1,
            VehicleLength: length, VehicleWidth: width, LateralSignSource: 8.60127f,
            RestOffsetPrimary: primary, RestOffsetSecondary: secondary);

    // length, width, [v+0xB4], [v+0x9C], measured D, measured L
    public static readonly TheoryData<string, float, float, float, float, float, float> Measured = new()
    {
        { "Oreca 07 LMP2",  4.686768f, 1.902582f, -2.297890f, -0.202666f, 2.990632f, 0.498820f },
        { "Ferrari 296 GT3", 4.745703f, 2.031858f, -2.319527f, -0.232059f, 3.049423f, 0.503253f },
        { "Ferrari 499P",   5.023269f, 2.004829f, -2.533145f, -0.170900f, 3.231970f, 0.536077f },
        { "BMW M Hybrid V8", 5.023269f, 2.004824f, -2.550228f, -0.143869f, 3.221780f, 0.536622f },
    };

    [Theory]
    [MemberData(nameof(Measured))]
    public void DerivesTheMeasuredConstants(
        string car, float length, float width, float primary, float secondary,
        float measuredD, float measuredL)
    {
        var derived = RestModel.Derive(Car(length, width, primary, secondary));

        Assert.NotNull(derived);
        Assert.Equal(RestSource.Derived, derived.Value.Source);

        // 5 mm. The fitted cars land inside 1.7 mm and the blind one inside
        // 2.4 mm; the bar is set where a real regression trips it but ordinary
        // terrain variation does not.
        Assert.True(MathF.Abs(derived.Value.Forward - measuredD) < 0.005f,
            $"{car}: D {derived.Value.Forward:F6} vs measured {measuredD:F6}");
        Assert.True(MathF.Abs(derived.Value.Lateral - measuredL) < 0.005f,
            $"{car}: L {derived.Value.Lateral:F6} vs measured {measuredL:F6}");
    }

    [Fact]
    public void RefusesToDeriveWhenTheProfileLacksTheEngineFields()
    {
        // Older profiles have no restOffsetPrimary/Secondary, so they read as
        // zero. Deriving from zero would silently produce a plausible-looking
        // D of about half a metre and place the car a metre out.
        var old = Car(4.7457f, 2.03186f, 0f, 0f);

        Assert.False(old.HasEnginePlacementDistance);
        Assert.Null(RestModel.Derive(old));
    }

    [Fact]
    public void ThePlacementDistanceIsTheNegatedSum()
    {
        // ApplyVehicleTransform SUBTRACTS the offset, so the two stored floats
        // are negative and the forward distance is their negated sum. Getting
        // this sign wrong places the car the same distance behind the target.
        var car = Car(4.745703f, 2.031858f, -2.319527f, -0.232059f);
        Assert.Equal(2.551586f, car.EnginePlacementDistance, 5);
    }
}
