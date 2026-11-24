using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// Track geometry can be shared; calibration must belong to the loaded car.
/// </summary>
public class SessionIdentityTests
{
    [Fact]
    public void MissingLiveIdentityCannotBecomeCheckpointIdentity()
    {
        Assert.Throws<GateException>(() => SessionIdentity.RequireKnown(null));
        Assert.Throws<GateException>(() => SessionIdentity.RequireKnown(new("", "Car")));
        Assert.Throws<GateException>(() => SessionIdentity.RequireKnown(new("Track", " ")));
        Assert.Throws<GateException>(() => new PlacementService(null!).Plan(
            Checkpoint("Track", "Car"), "unused", null));
        var identity = new SessionIdentity("Track", "Car");
        Assert.Equal(identity, SessionIdentity.RequireKnown(identity));
    }

    private static Checkpoint Checkpoint(string track, string vehicle) => new()
    {
        Name = "cp",
        TrackName = track,
        VehicleName = vehicle,
        ExecutableSha256 = "build",
        Gear = 2,
        LocalVelocityMetresPerSecond = new(0, 0, -10),
        Pose = new RecordedPose(
            new Vec3(0f, 0f, 0f), new Vec3(1f, 0f, 0f), new Vec3(0f, 1f, 0f), new Vec3(0f, 0f, 1f)),
    };

    [Fact]
    public void MatchingSessionIsAccepted() =>
        new SessionIdentity("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM")
            .RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM"));

    [Fact]
    public void CaseDiffersButStillMatches() =>
        new SessionIdentity("circuit de barcelona", "bmw m team wrt 2026 #15:lm")
            .RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM"));

    [Fact]
    public void DifferentVehicleCanUseTheSameTrackPose()
    {
        var identity = new SessionIdentity("Circuit de Barcelona", "Genesis Magma Racing 2026 #17:LM");
        identity.RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM"));
        identity.RequireCalibration(CalibrationProfile.Placeholder(identity.TrackName, identity.VehicleName));
        Assert.Throws<CalibrationException>(() => identity.RequireCalibration(
            CalibrationProfile.Placeholder(identity.TrackName, "BMW M Team WRT 2026 #15:LM")));
    }

    [Fact]
    public void DifferentTrackIsRefused() =>
        Assert.Throws<CheckpointException>(() =>
            new SessionIdentity("Daytona International Speedway Road Course", "BMW M Team WRT 2026 #15:LM")
                .RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM")));

    [Theory]
    [InlineData("TF Sport 2026 #2:LM", "13_26_13AU64749108", "Corvette GT3", true)]
    [InlineData("Team WRT 2024 #31:LM", "The Bend Team WRT 2025 #31:LM", "BMW M4 GT3", true)]
    [InlineData("Peugeot TotalEnergies 2024 #93:QA", "Peugeot TotalEnergies 2024 #93:LM", "Peugeot 9X8", false)]
    [InlineData("Cadillac WTR 2025 #101:LM", "12_26_JOTA50003117", "Cadillac V-Series.R", false)]
    [InlineData("AF Corse 2025 #51:ELMS 1.29", "51_26_AFCO83755194.veh", "Ferrari 296 GT3", false)]
    [InlineData("Vector Sport 2024 #10:LM", "Vector Sport #10:ELMS25", "Oreca 07 WEC", false)]
    [InlineData("old capture livery", "OLD CAPTURE LIVERY", null, true)]
    [InlineData("Corvette GT3 unknown livery", "13_26_13AU64749108", null, false)]
    public void RecordedAndRollingPlacementUseTheSameVehicleDecision(string captured, string loaded, string? model, bool compatible)
    {
        Assert.Equal(model, VehicleCatalog.Model(captured));
        var checkpoint = Checkpoint("Track", captured);
        var selection = new TimelineSelection(checkpoint, "segment", 1, 2, 36, null);
        var identity = new SessionIdentity("Track", loaded);
        if (compatible)
        {
            selection.RequireContext("build", identity);
            RollingMotion.RequireEligible(checkpoint, true, identity);
            Assert.Throws<GateException>(() => selection.RequireContext("other build", identity));
        }
        else
        {
            Assert.Throws<GateException>(() => selection.RequireContext("build", identity));
            Assert.Throws<GateException>(() => RollingMotion.RequireEligible(checkpoint, true, identity));
        }
    }
}
