using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class RollingMotionTests
{
    [Fact]
    public void CaptureEligibilityRefusesUnknownStationaryDriftingAndInvalidValues()
    {
        Assert.True(RollingMotion.EligibleCapture(new(0, 0, -2)));
        foreach (var v in new Vec3?[] { null, new(0, 0, 0), new(0.1f, 0, -2), new(0, 0.1f, -2), new(float.NaN, 0, -2) })
            Assert.False(RollingMotion.EligibleCapture(v));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(60.1)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(350)]
    public void RecordedSpeedsUseConsistentBodyAndWheelPayloads(double speedKmh)
    {
        var local = new Vec3(0, 0, (float)(-speedKmh / 3.6));
        var pose = new RecordedPose(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        Assert.True(RollingMotion.EligibleCapture(local));
        var payload = RollingMotion.BuildPayload(local, pose);
        Assert.Equal(local.Z, BitConverter.ToSingle(payload, 8));
        var body = BodyVelocityPlan.ShiftVelocity([0, 0, 0], [0, 0, local.Z]);
        Assert.Equal((double)local.Z, BitConverter.ToDouble(body, 16));
        var wheel = WheelRotationPlan.Calculate(local.Z, 0.4, [1, 0, 0, 0, 1, 0, 0, 0, 1], [2, 3, 4], [0, 0, 0]);
        Assert.Equal(local.Z / 0.4, wheel.Rate, 10);
        Assert.Equal(wheel.Rate * 2, wheel.Momentum[0], 10);
    }

    [Fact]
    public void SmallVerticalComponentScalesWithForwardSpeedWithoutAllowingSideslipOrBouncing()
    {
        Assert.True(RollingMotion.EligibleCapture(new(0.010424137f, 0.07474714f, -16.163006f)));
        Assert.False(RollingMotion.EligibleCapture(new(0.06f, 0.07474714f, -16.163006f)));
        Assert.False(RollingMotion.EligibleCapture(new(0, 0.09f, -16.163006f)));
        Assert.False(RollingMotion.EligibleCapture(new(0, 0.06f, -2)));
    }

    [Fact]
    public void NonFiniteAndOverflowingVelocityCalculationsAreRefused()
    {
        var local = new Vec3(0, 0, float.PositiveInfinity);
        var pose = new RecordedPose(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        Assert.False(RollingMotion.EligibleCapture(local));
        Assert.Throws<CheckpointException>(() => RollingMotion.BuildPayload(local, pose));
        Assert.Throws<GateException>(() => BodyVelocityPlan.ShiftVelocity([0, 0, 0], [0, 0, double.PositiveInfinity]));
        foreach (var velocity in new[] { double.PositiveInfinity, double.MaxValue, double.MaxValue / 10 })
            Assert.Throws<GateException>(() => WheelRotationPlan.Calculate(velocity, 0.4,
                [1, 0, 0, 0, 1, 0, 0, 0, 1], [100, 3, 4], [0, 0, 0]));
    }

    [Fact]
    public void EligibilityRequiresVerifiedLayoutAndCaptureIdentity()
    {
        var checkpoint = new Checkpoint { Name = "speed", TrackName = "Track", VehicleName = "Car", Pose = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), LocalVelocityMetresPerSecond = new(0, 0, -2) };
        RollingMotion.RequireEligible(checkpoint, true, new("Track", "Car"));
        Assert.Throws<GateException>(() => RollingMotion.RequireEligible(checkpoint, false, new("Track", "Car")));
        var mismatch = Assert.Throws<GateException>(() => RollingMotion.RequireEligible(checkpoint, true, new("Track", "Other car")));
        Assert.Contains("captured in Car", mismatch.Message);
        Assert.Contains("currently driving Other car", mismatch.Message);
        Assert.Throws<CheckpointException>(() => RollingMotion.RequireEligible(checkpoint, true, new("Other track", "Car")));
    }

    [Fact]
    public void ReportDistinguishesRequestedAndObservedSpeed()
    {
        var outcome = new PlacementOutcome { Completed = true, Message = "placed", SpeedRestorationAttempted = true, RollingMotion = new(8.94, 8.54) };
        var line = Assert.Single(PlacementReport.For(outcome), l => l.Label == "Rolling start");
        Assert.Contains("requested", line.Value); Assert.Contains("observed", line.Value);
        Assert.Contains("gear is not restored", line.Sentence);
    }

    [Fact]
    public void ReportShowsActualGearAndEngineRpmForSynchronizedRestore()
    {
        var outcome = new PlacementOutcome { Completed = true, Message = "placed", SpeedRestorationAttempted = true,
            RollingMotion = new(300, 290, 6, 6, 7100) };
        var lines = PlacementReport.For(outcome);
        var gear = Assert.Single(lines, l => l.Label == "Restored gear");
        Assert.Contains("requested 6; observed 6", gear.Value);
        Assert.Contains("7100", gear.Sentence);
        Assert.DoesNotContain(lines, l => l.Sentence?.Contains("gear is not restored") == true);
    }

    [Fact]
    public void SuccessfulSequenceResumesOnlyAfterFullVerification()
    {
        byte flag = 0; var steps = new List<string>();
        var pause = new PhysicsPause(() => true, () => flag, v => { flag = v; steps.Add(v == 1 ? "pause" : "resume"); });
        RollingMotion.ApplyWhilePaused(pause, () => steps.Add("prepare"), () => steps.Add("apply"), () => steps.Add("verify"));
        Assert.Equal(new[] { "pause", "prepare", "apply", "verify", "resume" }, steps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedMotionOrVerificationLeavesPhysicsPaused(bool verifyFails)
    {
        byte flag = 0;
        var pause = new PhysicsPause(() => true, () => flag, v => flag = v);
        Assert.Throws<GateException>(() => RollingMotion.ApplyWhilePaused(pause, () => { },
            () => { if (!verifyFails) throw new GateException("partial write"); },
            () => { if (verifyFails) throw new GateException("verification failed"); }));
        Assert.Equal(1, flag);
    }

    [Fact]
    public void FailedPreparationResumesBecauseMotionWasNeverAttempted()
    {
        byte flag = 0; var applied = false;
        var pause = new PhysicsPause(() => true, () => flag, v => flag = v);
        Assert.Throws<OperationCanceledException>(() => RollingMotion.ApplyWhilePaused(pause,
            () => throw new OperationCanceledException(), () => applied = true, () => { }));
        Assert.Equal(0, flag); Assert.False(applied);
    }

    [Theory]
    [InlineData(0, 3000)]
    [InlineData(3000, 3500)]
    [InlineData(9600, -1)]
    public void HandoffWaitRequiresDelayAndSustainedReadiness(int readyAt, int expected)
    {
        long now = 0;
        void Run() => RollingMotion.WaitForHandoff(() => now >= readyAt, () => now, () => now += 50, default);
        if (expected < 0) Assert.Throws<GateException>(Run);
        else { Run(); Assert.Equal(expected, now); }
    }

    [Fact]
    public void LostReadinessRestartsStableWindowAndCancellationStopsWaiting()
    {
        long now = 0;
        RollingMotion.WaitForHandoff(() => now < 2900 || now >= 3100, () => now, () => now += 50, default);
        Assert.Equal(3600, now);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => RollingMotion.WaitForHandoff(
            () => throw new Exception("Must not sample after cancellation"), () => now, () => now += 50, cancel.Token));
    }

    [Fact]
    public void GarageHandoffAllowsCreepButRejectsInheritedBraking()
    {
        var creeping = new WheelMotionTelemetry(1, 0, 0, 0, 0, [2, 2, 2, 2], [0.3, 0.3, 0.3, 0.3], [0, 0, 0, 0]);
        WheelRotationPlan.RequirePedalsReleased(creeping);
        Assert.Throws<GateException>(() => WheelRotationPlan.RequireReleased(creeping));
        Assert.Throws<GateException>(() => WheelRotationPlan.RequirePedalsReleased(creeping with { FilteredBrake = 1 }));
        Assert.Throws<GateException>(() => WheelRotationPlan.RequirePedalsReleased(creeping with { Brake = 0.2 }));
    }

    [Fact]
    public void DriverReadinessRequiresActualDrivingControlAndPermitsOnlyKnownPitStates()
    {
        var state = System.Text.Json.Nodes.JsonNode.Parse("""
            {"MultiStintState":"DRIVING","playerVehicleLoaded":true,"inControlOfVehicle":true,
             "inMonitor":false,"isReplayActive":false,"PitState":"EXITING"}
            """)!.AsObject();
        Assert.True(RollingMotion.DriverReady(state));
        state["inControlOfVehicle"] = false; Assert.False(RollingMotion.DriverReady(state));
        state["inControlOfVehicle"] = true;
        state["PitState"] = "SETUP"; Assert.False(RollingMotion.DriverReady(state));
        state["PitState"] = "NONE"; Assert.True(RollingMotion.DriverReady(state));
        state["MultiStintState"] = "PAUSED"; Assert.False(RollingMotion.DriverReady(state));
        state["MultiStintState"] = "DRIVING";
        state["isReplayActive"] = true; Assert.False(RollingMotion.DriverReady(state));
    }
}
