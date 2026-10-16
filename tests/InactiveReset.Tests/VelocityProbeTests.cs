using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class VelocityProbeTests
{
    private static readonly RecordedPose Pose = new(new(0, 0, 0), new(0, 0, 1), new(0, 1, 0), new(-1, 0, 0));
    private static TelemetrySnapshot Snapshot(Vec3? velocity = null, int gear = 0) =>
        new("Test track", "Test car", Pose, 0, gear, 1, LocalVelocityMetresPerSecond: velocity);
    private static ArrivalState Rest(int owner = 0, int gear = 0, int pit = 0, float[]? motion = null) =>
        new() { Valid = true, ControlOwner = owner, Gear = gear, PitState = pit, MotionState = motion ?? new float[9] };

    [Fact]
    public void PayloadRotatesLocalVelocityAndExcludesAngularVelocity()
    {
        var payload = VelocityProbe.BuildPayload(new(0, 0, -1), Pose);
        Assert.Equal(24, payload.Length);
        var values = Enumerable.Range(0, 6).Select(i => BitConverter.ToSingle(payload, i * 4)).ToArray();
        Assert.Equal(new float[] { -1, 0, 0, 0, 0, -1 }, values);
        Assert.Throws<CheckpointException>(() => VelocityProbe.BuildPayload(new(0, 0, float.PositiveInfinity), Pose));
        Assert.Throws<GateException>(() => VelocityProbe.BuildPayload(new(0, 0, 0), Pose));
        Assert.Throws<CheckpointException>(() => VelocityProbe.BuildPayload(new(float.NaN, 0, 1), Pose));
        Assert.Throws<GateException>(() => VelocityProbe.BuildPayload(new(0, 0, 1), Pose with { Row0 = new(0, 0, 0) }));
    }

    [Fact]
    public void ProbeRefusesMovingUnknownGarageGearAndPitState()
    {
        var stopped = Snapshot(new(0, 0, 0));
        VelocityProbe.RequireStationary(Rest(), stopped);
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(owner: 1), stopped));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(gear: 1), stopped));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(pit: 1), stopped));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(), Snapshot()));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(), Snapshot(new(0, 0, 1))));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(), Snapshot(new(0, 0, 0), gear: 1)));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(motion: [float.NaN]), stopped));
        var moving = new float[9]; moving[0] = 1;
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(motion: moving), stopped));
        moving[0] = float.NaN;
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(Rest(motion: moving), stopped));
        Assert.Throws<GateException>(() => VelocityProbe.RequireStationary(new ArrivalState(), stopped));
    }

    [Fact]
    public void ExperimentalSpeedRequiresTheCaptureCarEvenThoughOrdinaryPlacementAllowsOtherCars()
    {
        var checkpoint = new Checkpoint { Name = "test", TrackName = "Test track", VehicleName = "Test car", Pose = Pose };
        VelocityProbe.RequireIdentity(checkpoint, Snapshot());
        Assert.Throws<GateException>(() => VelocityProbe.RequireIdentity(checkpoint, Snapshot() with { VehicleName = "Other car" }));
        Assert.Throws<CheckpointException>(() => VelocityProbe.RequireIdentity(checkpoint, Snapshot() with { TrackName = "Other track" }));
    }
    [Fact]
    public void PausedBoundaryRequiresFrozenTimeAndExplicitPlayerPauseState()
    {
        var paused = JsonNode.Parse("""{"MultiStintState":"PAUSED","playerVehicleLoaded":true,"inControlOfVehicle":true,"inMonitor":false,"isReplayActive":false,"PitState":"NONE"}""")!.AsObject();
        VelocityProbe.RequirePaused(paused, 100, 100);
        foreach (var time in new[] { 100.01, 99.99, double.NaN, double.PositiveInfinity })
            Assert.Throws<GateException>(() => VelocityProbe.RequirePaused(paused, 100, time));
        Assert.Throws<GateException>(() => VelocityProbe.RequirePaused(paused, double.NaN, 100));
        foreach (var field in new[] { "MultiStintState", "playerVehicleLoaded", "inControlOfVehicle", "inMonitor", "isReplayActive", "PitState" })
        {
            var missing = paused.DeepClone().AsObject(); missing.Remove(field);
            Assert.Throws<GateException>(() => VelocityProbe.RequirePaused(missing, 100, 100));
            var changed = paused.DeepClone().AsObject();
            changed[field] = field switch
            {
                "MultiStintState" => JsonValue.Create("DRIVING"),
                "PitState" => JsonValue.Create("SETUP"),
                "inMonitor" or "isReplayActive" => JsonValue.Create(true),
                _ => JsonValue.Create(false),
            };
            Assert.Throws<GateException>(() => VelocityProbe.RequirePaused(changed, 100, 100));
        }
    }

    [Theory]
    [InlineData("--bodies", "--wheels")]
    [InlineData("--bodies", "--paused-wheels")]
    [InlineData("--wheels", "--paused-wheels")]
    [InlineData("--auto-wheels", "--bodies")]
    [InlineData("--auto-wheels", "--paused-wheels")]
    public void MultipleProbeModesAreRejectedBeforeAttachment(string first, string second) =>
        Assert.Throws<ArgumentException>(() => VelocityProbe.Run([new string('A', 64), "unused.json", first, second]));

    [Fact]
    public void AutomaticFreezeRequiresNativeFlagEvenWhenMenuStillReportsDriving()
    {
        var state = JsonNode.Parse("""{"MultiStintState":"DRIVING","playerVehicleLoaded":true,"inControlOfVehicle":true,"inMonitor":false,"isReplayActive":false,"PitState":"NONE"}""")!.AsObject();
        VelocityProbe.RequirePhysicsPaused(state, 100, 100, true);
        Assert.Throws<GateException>(() => VelocityProbe.RequirePhysicsPaused(state, 100, 100, false));
        Assert.Throws<GateException>(() => VelocityProbe.RequirePhysicsPaused(state, 100, 100.01, true));
        state["MultiStintState"] = "PAUSED";
        Assert.Throws<GateException>(() => VelocityProbe.RequirePhysicsPaused(state, 100, 100, true));
    }

    [Fact]
    public void PlacementExitRequiresExplicitAllowanceAndKeepsOtherPitStatesBlocked()
    {
        var stopped = Snapshot(new(0, 0, 0));
        Assert.Throws<GateException>(() => RollingMotion.RequireStationary(Rest(pit: 5), stopped));
        RollingMotion.RequireStationary(Rest(pit: 5), stopped, placementExit: true);
        Assert.Throws<GateException>(() => RollingMotion.RequireStationary(Rest(pit: 4), stopped, placementExit: true));
        Assert.Throws<GateException>(() => RollingMotion.RequireStationary(Rest(pit: 6), stopped, placementExit: true));
        var state = JsonNode.Parse("""{"MultiStintState":"DRIVING","playerVehicleLoaded":true,"inControlOfVehicle":true,"inMonitor":false,"isReplayActive":false,"PitState":"EXITING"}""")!.AsObject();
        Assert.Throws<GateException>(() => RollingMotion.RequirePhysicsPaused(state, 100, 100, true));
        RollingMotion.RequirePhysicsPaused(state, 100, 100, true, placementExit: true);
        state["PitState"] = "SETUP";
        Assert.Throws<GateException>(() => RollingMotion.RequirePhysicsPaused(state, 100, 100, true, placementExit: true));
    }

}
