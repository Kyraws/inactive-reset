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
            Checkpoint("Track", "Car"), CalibrationProfile.Placeholder("Track", "Car")));
        var identity = new SessionIdentity("Track", "Car");
        Assert.Equal(identity, SessionIdentity.RequireKnown(identity));
    }

    private static Checkpoint Checkpoint(string track, string vehicle) => new()
    {
        Name = "cp",
        TrackName = track,
        VehicleName = vehicle,
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
}
