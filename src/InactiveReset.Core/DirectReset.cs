using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace InactiveReset.Core;

public sealed record DirectResetResult(Vec3 Target, Vec3 Observed, double PauseMilliseconds,
    RollingMotionResult? Motion);

public static class DirectReset
{
    internal static void RequireCoherentObservation(Vec3 target,double initialSpeedKmh,TelemetrySnapshot sample,double elapsed)
    {
        var speed=sample.SpeedKmh;
        if(!target.IsFinite||!sample.Pose.Position.IsFinite||speed is null||!double.IsFinite(speed.Value)||
            !double.IsFinite(initialSpeedKmh)||initialSpeedKmh<0||!double.IsFinite(elapsed)||elapsed<=0)
            throw new GateException("Direct reset produced invalid resumed telemetry.");
        var p=sample.Pose.Position;
        var distance=Math.Sqrt(Math.Pow((double)p.X-target.X,2)+Math.Pow((double)p.Y-target.Y,2)+Math.Pow((double)p.Z-target.Z,2));
        // Allow normal acceleration and travel at any speed, but detect a jump or automatic pit recovery.
        var travel=5+elapsed*(Math.Max(initialSpeedKmh,speed.Value)/3.6+10);
        if(distance>travel)throw new GateException("Direct reset became unstable after resume; the car left the expected travel area.");
    }

    internal static void RequireDriver(JsonObject state)
    {
        if(state["MultiStintState"]?.GetValue<string>() != "DRIVING" ||
            state["playerVehicleLoaded"]?.GetValue<bool>() != true ||
            state["inControlOfVehicle"]?.GetValue<bool>() != true ||
            state["inMonitor"]?.GetValue<bool>() != false ||
            state["isReplayActive"]?.GetValue<bool>() != false ||
            string.IsNullOrWhiteSpace(state["PitState"]?.GetValue<string>()))
            throw new GateException("Direct reset requires active driving control outside the garage.");
    }

    // Discard the teleported partial lap; the next line crossing starts a timed lap.
    internal static Dictionary<string, byte[]> LapRepair() => new()
    {
        ["pitState"] = BitConverter.GetBytes(0), ["pitFlag"] = [0],
        ["sector"] = BitConverter.GetBytes(LapValidityController.LastSector),
        ["countLapFlag"] = BitConverter.GetBytes((int)LapCounting.LapOnly), ["lapCountsNext"] = [1],
    };

    public static DirectResetResult Restore(GameSession game,Checkpoint checkpoint,string offsets,
        LmuSessionClient menu,bool restoreSpeed,Action<string> progress,CancellationToken cancellation)
    {
        var layout=game.RequireMotion();
        menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
        RequireDriver(menu.GameStateAsync(cancellation).GetAwaiter().GetResult());
        using var sdk=new SharedMemoryReader(SharedMemoryOffsets.Load(Path.Combine(offsets,"shared-memory.json")));
        var live=new LiveStateReader(game);var slot=live.ResolveSlotIndex();
        if(slot is <0 or >=104)throw new GateException("Invalid direct-reset player slot.");
        var vehicle=game.Offsets.Containers.ContainerAddress(game.ModuleBase,slot)+game.Offsets.Containers.VehicleDelta;
        var physics=game.Memory.ReadUInt64(vehicle+layout.VehiclePhysics);
        bool Same()=>!game.Process.HasExited&&live.ResolveSlotIndex()==slot&&live.ReadControlOwner(slot)==0&&
            physics!=0&&game.Memory.ReadUInt64(vehicle+layout.VehiclePhysics)==physics;
        TelemetrySnapshot Read(){cancellation.ThrowIfCancellationRequested();if(!Same())throw new GateException("Direct-reset session changed.");
            var s=sdk.Read()??throw new GateException("No player telemetry.");
            new SessionIdentity(s.TrackName,s.VehicleName).RequireMatches(checkpoint);
            if(restoreSpeed)RollingMotion.RequireIdentity(checkpoint,s);return s;}
        var first=Read();
        layout.ReadEngineSetter(game,game.Memory.ReadUInt64(physics+layout.PhysicsEngine));
        if(restoreSpeed)RollingMotion.RequireEligible(checkpoint,true,new(first.TrackName,first.VehicleName));
        Thread.Sleep(50);if(Read().ElapsedTime<=first.ElapsedTime)throw new GateException("Resume the game before direct reset.");
        using var hold=restoreSpeed?RollingBrakeHold.Prepare(menu,Same,progress,cancellation,onTrack:true):null;
        using var pause=PhysicsPause.Prepare(game,Same);
        var lapRepair=LapRepair();
        ulong LapAddress(string field)=>game.Offsets.Containers.FieldAddress(game.ModuleBase,slot,field);
        foreach(var (field,bytes) in lapRepair)game.Memory.RequireWritableDataPage(LapAddress(field),bytes.Length);
        DirectPosePlan? pose=null;GearMotionPlan? gears=null;double frozen=0;var duration=Stopwatch.StartNew();
        void Boundary(){cancellation.ThrowIfCancellationRequested();if(!pause.IsPaused||Read().ElapsedTime!=frozen)throw new GateException("Direct-reset pause boundary changed.");}
        void Paused(JsonObject state){RequireDriver(state);Boundary();}
        progress("Resetting directly to checkpoint.");
        try{
            RollingMotion.ApplyWhilePaused(pause,()=>{
                var stateTask=menu.GameStateAsync(cancellation);
                Thread.Sleep(25);frozen=Read().ElapsedTime;
                pose=DirectPosePlan.Prepare(game,slot,checkpoint.Pose,restoreSpeed?checkpoint.LocalVelocityMetresPerSecond:null);
                gears=GearMotionPlan.PrepareForReset(game,slot,pose,restoreSpeed?checkpoint.Gear:0);
                Paused(stateTask.GetAwaiter().GetResult());
                string State()=>JsonSerializer.Serialize(new{body=pose.ReadState(),gear=gears.ReadState()});
                var before=State();Thread.Sleep(25);Boundary();
                if(before!=State())throw new GateException("Direct-reset state is not stable while paused.");
                pose.Preflight();gears.Preflight();
            },()=>{pose!.Apply(Boundary);gears!.Apply(Boundary);
                foreach(var (field,bytes) in lapRepair){Boundary();game.Memory.WriteVerified(LapAddress(field),bytes);}
            },()=>{
                var stateTask=menu.GameStateAsync(cancellation);
                Boundary();pose!.VerifyApplied();gears!.VerifyApplied();
                foreach(var (field,bytes) in lapRepair)
                    if(!game.Memory.ReadBytes(LapAddress(field),bytes.Length).SequenceEqual(bytes))
                        throw new GateException($"Direct-reset lap repair did not verify: {field}.");
                Paused(stateTask.GetAwaiter().GetResult());
            });
        }catch(Exception ex){bool paused;try{paused=pause.IsPaused;}catch{paused=false;}
            throw new GateException((paused?"Direct reset failed; physics remains paused after a possible partial update. Restart LMU before another reset. ":"Direct reset failed. ")+ex.Message);}
        duration.Stop();hold?.Restore();
        progress("Pit state cleared; sector and out-lap state repaired. Cross the line to start a clean timed lap. Simulation resumed; observing the direct reset.");
        var wait=Stopwatch.StartNew();
        TelemetrySnapshot? resumed=null;
        do{
            var after=Read();
            if(after.ElapsedTime>frozen){
                RequireCoherentObservation(checkpoint.Pose.Position,restoreSpeed?checkpoint.SpeedKmh!.Value:0,after,after.ElapsedTime-frozen);
                if(resumed==null){
                    if(after.Gear!=(restoreSpeed?checkpoint.Gear:0))throw new GateException("Direct reset resumed without the requested gear.");
                    resumed=after;
                }
                if(after.ElapsedTime-frozen>=2){
                    RequireDriver(menu.GameStateAsync(cancellation).GetAwaiter().GetResult());
                    return new(checkpoint.Pose.Position,after.Pose.Position,duration.Elapsed.TotalMilliseconds,
                        restoreSpeed?new(checkpoint.SpeedKmh!.Value,resumed.SpeedKmh??0,checkpoint.Gear,resumed.Gear,gears!.ReadEngineRpm()):null);
                }
            }
            Thread.Sleep(20);
        }while(wait.ElapsedMilliseconds<6000);
        throw new GateException("Direct pose was written, but two seconds of resumed simulation could not be verified.");
    }
}
