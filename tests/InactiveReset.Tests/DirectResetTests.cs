using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;
public sealed class DirectResetTests
{
 private static JsonObject Driving()=>JsonNode.Parse("""{"MultiStintState":"DRIVING","playerVehicleLoaded":true,"inControlOfVehicle":true,"inMonitor":false,"isReplayActive":false,"PitState":"NONE"}""")!.AsObject();
 private static TelemetrySnapshot Sample(Vec3 position,double speed)=>new("track","car",new(position,new(1,0,0),new(0,1,0),new(0,0,1)),0,1,2,LocalVelocityMetresPerSecond:new(0,0,(float)(-speed/3.6)));
 [Theory]
 [InlineData(40,20)]
 [InlineData(2.15,1.2)]
 [InlineData(300,165)]
 public void ObservationAllowsNormalTravelWithoutASpeedCeiling(double speed,float distance)=>DirectReset.RequireCoherentObservation(new(0,0,0),speed,Sample(new(0,0,distance),speed),2);
 [Fact] public void RecordedExplosionIsRejectedAfterTheFirstGoodFrame(){
  var target=new Vec3(-258.4777f,0.44833857f,15.323374f);
  DirectReset.RequireCoherentObservation(target,39.85813,Sample(new(-258.4765f,0.4488452f,15.461629f),39.81549),0.02);
  Assert.Throws<GateException>(()=>DirectReset.RequireCoherentObservation(target,39.85813,Sample(new(-192.31662f,-7.979705f,433.67322f),334.445744),2.17));
  Assert.Throws<GateException>(()=>DirectReset.RequireCoherentObservation(target,39.85813,Sample(new(-269.52f,-2.1f,-407.93f),0),4));}
 [Fact] public void NonFiniteResumedTelemetryIsRejected()=>Assert.Throws<GateException>(()=>DirectReset.RequireCoherentObservation(new(0,0,0),40,Sample(new(float.NaN,0,0),40),1));
 [Fact] public void BrakeHoldHasSeparateGarageAndOnTrackPreparationGates(){var state=Driving();RollingBrakeHold.RequireState(state,true);Assert.Throws<GateException>(()=>RollingBrakeHold.RequireState(state,false));state["inMonitor"]=true;state["MultiStintState"]="MONITOR_MENU";RollingBrakeHold.RequireState(state,false);Assert.Throws<GateException>(()=>RollingBrakeHold.RequireState(state,true));}
 [Fact] public void DirectReportDoesNotClaimAGarageArrivalOrPositionMissWhileRolling(){var outcome=new PlacementOutcome{Completed=true,Message="reset",DirectResetPauseMilliseconds=751,RollingMotion=new(40,39,1,1,2600)};var lines=PlacementReport.For(outcome);Assert.Contains(lines,l=>l.Label=="result"&&l.Value=="reset on track");Assert.Contains(lines,l=>l.Label=="Physics pause"&&l.Value=="751 ms");Assert.DoesNotContain(lines,l=>l.Label is "arrival" or "pit state" or "horizontal error");}
 [Fact] public void ActiveOnTrackPlayerIsAccepted()=>DirectReset.RequireDriver(Driving());
 [Theory]
 [InlineData("inMonitor",true)]
 [InlineData("isReplayActive",true)]
 [InlineData("inControlOfVehicle",false)]
 [InlineData("playerVehicleLoaded",false)]
 public void InactivePlayerMonitorAndReplayAreRefused(string field,bool value){var state=Driving();state[field]=value;Assert.Throws<GateException>(()=>DirectReset.RequireDriver(state));}
 [Theory]
 [InlineData("PAUSED")]
 [InlineData("MONITOR_MENU")]
 public void MenusAreRefused(string menu){var state=Driving();state["MultiStintState"]=menu;Assert.Throws<GateException>(()=>DirectReset.RequireDriver(state));}
 [Theory]
 [InlineData("EXITING")]
 [InlineData("ENTERING")]
 [InlineData("STOPPED")]
 public void DriverWithInheritedPitStateCanReset(string pit){var state=Driving();state["PitState"]=pit;DirectReset.RequireDriver(state);RollingBrakeHold.RequireState(state,true);}
 [Fact] public void MissingPitStateIsRefused(){var state=Driving();state.Remove("PitState");Assert.Throws<GateException>(()=>DirectReset.RequireDriver(state));}
 [Fact] public void ResetDiscardsPartialLapAndArmsNextTimedLap(){var repair=DirectReset.LapRepair();Assert.Equal(0,BitConverter.ToInt32(repair["pitState"]));Assert.Equal(new byte[]{0},repair["pitFlag"]);Assert.Equal(2,BitConverter.ToInt32(repair["sector"]));Assert.Equal(1,BitConverter.ToInt32(repair["countLapFlag"]));Assert.Equal(new byte[]{1},repair["lapCountsNext"]);}
}
