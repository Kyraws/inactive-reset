using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The guard that stops one car's constants being placed onto another.
///
/// Without it, a checkpoint captured in car A and placed while car B was loaded
/// produced a ~1 m miss that read as a property of the TRACK, because the report
/// printed the checkpoint's vehicle name rather than the one in the garage.
/// </summary>
public class SessionIdentityTests
{
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
    public void DifferentVehicleIsRefused()
    {
        var ex = Assert.Throws<CheckpointException>(() =>
            new SessionIdentity("Circuit de Barcelona", "Genesis Magma Racing 2026 #17:LM")
                .RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM")));
        Assert.Contains("Genesis", ex.Message);
        Assert.Contains("BMW", ex.Message);
    }

    [Fact]
    public void DifferentTrackIsRefused() =>
        Assert.Throws<CheckpointException>(() =>
            new SessionIdentity("Daytona International Speedway Road Course", "BMW M Team WRT 2026 #15:LM")
                .RequireMatches(Checkpoint("Circuit de Barcelona", "BMW M Team WRT 2026 #15:LM")));
}
