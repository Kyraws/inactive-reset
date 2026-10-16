using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;
public sealed class DirectPosePlanTests
{
 private static readonly double[] Identity=[1,0,0,0,1,0,0,0,1];
 private static readonly double[] QuarterTurn=[0,0,1,0,1,0,-1,0,0];
 private static DirectPosePlan.BodyState Body()=>new([2,0,0],Identity.ToArray(),[3,0,0],[2,0,0],[0.5,0.25,0.125]);
 private static void Equal(double[] expected,double[] actual){Assert.Equal(expected.Length,actual.Length);for(var i=0;i<expected.Length;i++)Assert.Equal(expected[i],actual[i],10);}
 [Fact] public void BulkContactReadOnlySelectsTheTwoNativeFlagsInEachRecord(){
  var records=new byte[3*0x110];records[0x5f]=9;records[0x61]=9;records[0x66]=9;records[0x68]=9;
  records[0x60]=1;records[0x110+0x67]=255;records[2*0x110+0x60]=1;records[2*0x110+0x67]=2;
  var original=records.ToArray();
  Assert.Equal(new[]{0x60,0x177,0x280,0x287},DirectPosePlan.ContactResetOffsets(records,3,0x110,0x60,0x67));
  Assert.Equal(original,records);}
 [Fact] public void BulkContactReadHandlesTheFullResearchedTyreLayout(){
  var records=new byte[504*0x110];records[0x67]=1;records[503*0x110+0x60]=1;
  Assert.Equal(new[]{0x67,503*0x110+0x60},DirectPosePlan.ContactResetOffsets(records,504,0x110,0x60,0x67));
  Assert.Empty(DirectPosePlan.ContactResetOffsets([],0,0x110,0x60,0x67));}
 [Fact] public void BulkContactReadRefusesTruncatedOrMiscountedRecords(){
  Assert.Throws<GateException>(()=>DirectPosePlan.ContactResetOffsets(new byte[0x10f],1,0x110,0x60,0x67));
  Assert.Throws<GateException>(()=>DirectPosePlan.ContactResetOffsets(new byte[0x111],1,0x110,0x60,0x67));
  Assert.Throws<GateException>(()=>DirectPosePlan.ContactResetOffsets([],4097,0x110,0x60,0x67));}
 [Fact] public void NativeCacheRoundingIsAcceptedButPersistentBodyWritesRemainExact(){
  var target=BitConverter.GetBytes(0.9999906420707703);var normalized=BitConverter.GetBytes(0.9999906959782544);
  Assert.True(DirectPosePlan.CacheMatches(target,normalized,8));Assert.False(DirectPosePlan.CacheMatches(target,normalized,0));
  Assert.False(DirectPosePlan.CacheMatches(target,BitConverter.GetBytes(0.98),8));
  Assert.True(DirectPosePlan.CacheMatches(BitConverter.GetBytes(0.99999064f),BitConverter.GetBytes(0.9999907f),4));
  Assert.False(DirectPosePlan.CacheMatches(target,BitConverter.GetBytes(double.NaN),8));
  Assert.False(DirectPosePlan.CacheMatches(BitConverter.GetBytes(15.323374),BitConverter.GetBytes(831.40094),8));}
 [Theory]
 [InlineData(0,0,0)]
 [InlineData(0.2,2.8,-0.1)]
 [InlineData(-0.3,-1.2,0.4)]
 public void CachedEulerMatchesNativePitchYawRollConvention(double pitch,double yaw,double roll){
  var cp=Math.Cos(pitch);var sp=Math.Sin(pitch);var cy=Math.Cos(yaw);var sy=Math.Sin(yaw);var cr=Math.Cos(roll);var sr=Math.Sin(roll);
  double[] matrix=[cy*cr+sy*sp*sr,-cy*sr+sy*sp*cr,sy*cp,cp*sr,cp*cr,-sp,-sy*cr+cy*sp*sr,sy*sr+cy*sp*cr,cy*cp];
  Equal([pitch,yaw,roll],DirectPosePlan.Euler(matrix));}
 [Fact] public void SeparateCollisionBodyUsesSdkOriginAndTargetMotion(){
  var collision=DirectPosePlan.CollisionBody([100,0.45,15],QuarterTurn,[0,0,-11],[0.5,0.25,0.125]);
  Equal([100,0.45,15],collision.Position);Equal(QuarterTurn,collision.Matrix);Equal([0,0,-11],collision.Velocity);
  Equal([0,0,0],collision.Omega);Equal([0,0,0],collision.Momentum);Equal([0.125,0,0.25,0,0,0.5],collision.InverseInertia);}
 [Fact] public void IdentityTranslationPreservesVelocityAndAngularMomentum(){var b=Body();var result=DirectPosePlan.Transform(b,[1,0,0],Identity,[11,4,5],Identity);
  Equal([12,4,5],result.Position);Equal(b.Matrix,result.Matrix);Equal(b.Velocity,result.Velocity);Equal(b.Momentum,result.Momentum);Equal([1,0,0],result.Omega);
  Equal([2,0,0],b.Position);}
 [Fact] public void QuarterTurnRotatesAllDynamicVectorsAndTheInverseInertia(){var result=DirectPosePlan.Transform(Body(),[1,0,0],Identity,[10,0,10],QuarterTurn);
  Equal([10,0,9],result.Position);Equal([0,0,-3],result.Velocity);Equal([0,0,-2],result.Momentum);Equal([0,0,-1],result.Omega);
  Equal([0.125,0,0.25,0,0,0.5],result.InverseInertia);Assert.Equal(1,result.Omega.Zip(result.Momentum,(w,l)=>w*l).Sum()/2,10);}
 [Fact] public void TargetOriginUsesTheLoadedCarsOffsetInTheTargetOrientation(){var p=new RecordedPose(new(100,12,50),new(0,0,1),new(0,1,0),new(-1,0,0));
  Equal([97,10,51],DirectPosePlan.AggregatePosition(p,[1,2,3]));}
 [Fact] public void LinkedBodiesKeepTheirRelativeSeparationAndOrientation(){var a=Body();var b=a with{Position=[2,3,4],Matrix=[0,-1,0,1,0,0,0,0,1]};
  var ta=DirectPosePlan.Transform(a,[0,0,0],Identity,[100,10,-20],QuarterTurn);var tb=DirectPosePlan.Transform(b,[0,0,0],Identity,[100,10,-20],QuarterTurn);
  Assert.Equal(5,Math.Sqrt(ta.Position.Zip(tb.Position,(x,y)=>(x-y)*(x-y)).Sum()),10);DirectPosePlan.RequireRotation(tb.Matrix);Equal([0,0,1,1,0,0,0,1,0],tb.Matrix);}
 [Fact] public void NonOrthonormalAndReflectedOrientationsAreRefused(){Assert.Throws<GateException>(()=>DirectPosePlan.RequireRotation([1,0.1,0,0,1,0,0,0,1]));Assert.Throws<GateException>(()=>DirectPosePlan.RequireRotation([-1,0,0,0,1,0,0,0,1]));}
 [Fact] public void NonFiniteStateAndInvalidInverseInertiaAreRefused(){var b=Body();Assert.Throws<GateException>(()=>DirectPosePlan.Transform(b with{Position=[double.NaN,0,0]},[0,0,0],Identity,[0,0,0],Identity));Assert.Throws<GateException>(()=>DirectPosePlan.Transform(b with{InverseDiagonal=[0,1,1]},[0,0,0],Identity,[0,0,0],Identity));}
 [Fact] public void RotationThenInverseRecoversTheOriginalBodyState(){var b=Body();var first=DirectPosePlan.Transform(b,[1,2,3],Identity,[10,-4,8],QuarterTurn);
  var inverse=DirectPosePlan.Transform(new(first.Position,first.Matrix,first.Velocity,first.Momentum,b.InverseDiagonal),[10,-4,8],QuarterTurn,[1,2,3],Identity);
  Equal(b.Position,inverse.Position);Equal(b.Matrix,inverse.Matrix);Equal(b.Velocity,inverse.Velocity);Equal(b.Momentum,inverse.Momentum);}
}
