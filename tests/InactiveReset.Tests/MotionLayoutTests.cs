using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;

public sealed class MotionLayoutTests
{
    private static JsonObject Fixture()=>ProfileFixture.Read("motion-layout-66942337");
    private static MotionLayout Layout(){var j=Fixture();return MotionLayout.FromJson(j,j["executableSha256"]!.GetValue<string>());}

    [Fact] public void RecordedProfileHasAllFieldsAndDoesNotExposeMutableState()
    {
        var layout=Layout();Assert.Equal(104,layout.ProbeCount);
        Assert.Equal(0x159a0UL,layout.VehiclePhysics);Assert.Equal(0x2470UL,layout.PhysicsAggregateBody);
        Assert.Equal(0x230UL,layout.VehicleWheelCacheStride);Assert.Equal(0x778UL,layout.WheelResetMarker);
        var copy=layout.ToJson();copy["fields"]!["VehiclePhysics"]="0x17000";
        Assert.Equal(0x159a0UL,layout.VehiclePhysics);
    }

    [Theory]
    [InlineData("field")][InlineData("hash")][InlineData("overlap")][InlineData("probes")][InlineData("ranges")]
    [InlineData("missing-fields")][InlineData("missing-probes")][InlineData("missing-ranges")][InlineData("invalid-hex")]
    public void IncompleteOrInconsistentProfilesAreRefused(string change)
    {
        var j=Fixture();var hash=j["executableSha256"]!.GetValue<string>();
        switch(change){
            case "field":j["fields"]!.AsObject().Remove("VehiclePhysics");break;
            case "hash":j["executableSha256"]=new string('B',64);break;
            case "overlap":j["fields"]!["BodyOmega"]=j["fields"]!["BodyMatrix"]!.DeepClone();break;
            case "probes":j["probes"]!.AsArray().RemoveAt(0);break;
            case "ranges":j["codeRanges"]![0]![1]=int.MaxValue;break;
            case "missing-fields":j.Remove("fields");break;
            case "missing-probes":j.Remove("probes");break;
            case "missing-ranges":j.Remove("codeRanges");break;
            case "invalid-hex":j["fields"]!["VehiclePhysics"]="invalid";break;
        }
        Assert.Throws<GateException>(()=>MotionLayout.FromJson(j,hash));
    }

    [Fact] public void LiveCodeMustMatchEveryDiscoveredProbe()
    {
        var j=Fixture();var probes=j["probes"]!.AsArray();var layout=Layout();var reads=0;
        byte[] Read(ulong at,int count){reads++;return Convert.FromHexString(probes.First(p=>OffsetProfile.ParseHex(p!["rva"]!.GetValue<string>())==at&&p["bytes"]!.GetValue<string>().Length==count*2)!["bytes"]!.GetValue<string>());}
        layout.VerifyProbes(Read);Assert.Equal(layout.ProbeCount,reads);
        Assert.Throws<GateException>(()=>layout.VerifyProbes((at,count)=>new byte[count]));
    }

    [Theory][InlineData(0x140)][InlineData(0x1f8)][InlineData(0x240)]
    public void EngineSpeedFieldComesFromTheActualSetter(int field)
    {
        var code=new byte[]{0xf2,0x0f,0x11,0x89,0,0,0,0,0xc3};BitConverter.GetBytes(field).CopyTo(code,4);
        Assert.Equal((ulong)field,MotionLayout.DecodeEngineSetter(code));
    }

    [Theory][InlineData(-8)][InlineData(0)][InlineData(0x141)][InlineData(0x10008)]
    public void InvalidEngineFieldsAreRefused(int field)
    {
        var code=new byte[]{0xf2,0x0f,0x11,0x89,0,0,0,0,0xc3};BitConverter.GetBytes(field).CopyTo(code,4);
        Assert.Throws<GateException>(()=>MotionLayout.DecodeEngineSetter(code));
        code[8]=0x90;Assert.Throws<GateException>(()=>MotionLayout.DecodeEngineSetter(code));
    }

    [Theory][InlineData(0UL)][InlineData(3UL)]
    public void MissingOrUnalignedEngineIsRefusedBeforeMemoryAccess(ulong engine)=>
        Assert.Throws<GateException>(()=>Layout().ReadEngineSetter(null!,engine));

    [Fact] public void SignedMemoryDisplacementsNeverBecomePositiveOffsets()
    {
        var a=JsonNode.Parse("""{"pattern":"48 8B 41 ?? C3","operand":3,"width":1,"kind":"value","next":4,"signed":true}""")!.AsObject();
        var image=new byte[]{0x48,0x8b,0x41,0x30,0xc3};
        Assert.Equal(0x30UL,CodeAnchor.Resolve(image,a,"EngineSetterSlot").Value);
        image[3]=0xf0;Assert.Throws<GateException>(()=>CodeAnchor.Resolve(image,a,"EngineSetterSlot"));
    }

    [Fact] public void AnUnavailableFeatureFailsBeforeAnyProcessMemoryAccess()
    {
        using var process=Process.GetCurrentProcess();
        var offsets=OffsetProfile.Parse(ProfileFixture.Read().ToJsonString(),"fixture");
        var session=new GameSession{Process=process,ModuleBase=0,ExecutableSha256=offsets.ExecutableSha256,Offsets=offsets,Memory=null!,Gates=[]};
        var error=Assert.Throws<GateException>(()=>session.RequireMotion());Assert.Contains("unavailable",error.Message);
    }

    private static List<(int At,int Width,int Value)> Patches(byte[] image,Func<string,ulong,ulong> change)
    {
        var patches=new List<(int,int,int)>();
        foreach(var field in MotionLayout.Seeds)
        foreach(var node in field.Value!.AsArray()){
            var a=node!.AsObject();var found=CodeAnchor.Resolve(image,a,field.Key);var value=change(field.Key,found.Value);
            if(a["kind"]!.GetValue<string>()=="rip")value=unchecked(value-(ulong)(found.At+a["next"]!.GetValue<int>()));
            patches.Add((found.At+a["operand"]!.GetValue<int>(),a["width"]!.GetValue<int>(),unchecked((int)value)));
        }
        return patches;
    }
    private static void Apply(byte[] image,IEnumerable<(int At,int Width,int Value)> patches)
    {foreach(var p in patches)if(p.Width==1)image[p.At]=checked((byte)p.Value);else BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(p.At,4),p.Value);}

    [LocalDumpFact("66942337")]
    public void NativeDiscoveryMatchesRecordedValuesWithoutAFixedHashGate()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));var hash=new string('B',64);
        var discovered=MotionLayout.Discover(image,hash).ToJson();
        Assert.True(JsonNode.DeepEquals(Fixture()["fields"],discovered["fields"]));
        Assert.Equal(hash,discovered["executableSha256"]!.GetValue<string>());
    }

    [LocalDumpFact("66942337")]
    public void AllGlobalReferencesFollowRelocationAndDisagreementIsRefused()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var patches=Patches(image,(name,value)=>name.EndsWith("Rva")?value+0x1000:value);Apply(image,patches);
        var layout=MotionLayout.Discover(image,new string('C',64));
        Assert.Equal(0x1a5b59dUL,layout.PauseFlagRva);Assert.Equal(0x3b8bc40UL,layout.CollisionBeginRva);
        var a=MotionLayout.Seeds["PauseFlagRva"]![0]!.AsObject();var found=CodeAnchor.Resolve(image,a,"PauseFlagRva");
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(found.At+a["operand"]!.GetValue<int>(),4),0);
        Assert.Throws<GateException>(()=>MotionLayout.Discover(image,new string('D',64)));
    }

    [LocalDumpFact("66942337")]
    public void CoherentBodyLayoutChangesAreDerivedFromConsumers()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var related=new HashSet<string>{"PhysicsAggregatePosition","PhysicsAggregateMatrix","VehicleCollisionPosition","VehicleCollisionVelocity"};
        Apply(image,Patches(image,(name,value)=>name.StartsWith("Body")||related.Contains(name)?value+8:value));
        var layout=MotionLayout.Discover(image,new string('E',64));
        Assert.Equal(0x48UL,layout.BodyPosition);Assert.Equal(0x68UL,layout.BodyInverseInertia);
        Assert.Equal(0x2470UL,layout.PhysicsAggregateBody);Assert.Equal(0x24b8UL,layout.PhysicsAggregatePosition);
    }

    [LocalDumpFact("66942337")]
    public void MissingOrAmbiguousResetCodeDisablesOnlyResetDiscovery()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var a=MotionLayout.Seeds["BodyInverseInertia"]![0]!.AsObject();var found=CodeAnchor.Resolve(image,a,"BodyInverseInertia");
        image[found.At]^=0xff;
        var profile=AutomaticOffsets.Discover(image,new string('F',64),"changed");
        Assert.Null(profile["motion"]);Assert.NotNull(profile["motionUnavailableReason"]);
        Assert.NotNull(OffsetProfile.Parse(profile.ToJsonString(),"test").Containers);
    }

    [LocalDumpFact("66942337")]
    public void DuplicateNativeConsumerRefusesResetButKeepsPlacementProfile()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var a=MotionLayout.Seeds["BodyInverseInertia"]![0]!.AsObject();var f=CodeAnchor.Resolve(image,a,"BodyInverseInertia");
        var destination=AutomaticOffsets.ExecutableRanges(image).First().Start;
        Assert.NotEqual(f.At,destination);image.AsSpan(f.At,f.Length).CopyTo(image.AsSpan(destination));
        var profile=AutomaticOffsets.Discover(image,new string('B',64),"duplicate");
        Assert.Null(profile["motion"]);Assert.Contains("ambiguous",profile["motionUnavailableReason"]!.GetValue<string>());
        Assert.NotNull(OffsetProfile.Parse(profile.ToJsonString(),"test").Containers);
    }

    [LocalDumpFact("66942337")]
    public void MovedCodeIsResolvedAtItsNewLocations()
    {
        var original=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var locations=new List<(int Start,int End)>();var rip=new List<(int At,int Next,int Operand,ulong Value)>();
        var anchors=MotionLayout.Seeds.SelectMany(field=>field.Value!.AsArray().Select(node=>(Name:field.Key,Anchor:node!.AsObject())))
            .Concat(MotionLayout.Contracts.Select(c=>(Name:c.Key,Anchor:c.Value!.AsObject())));
        foreach(var entry in anchors){
            var a=entry.Anchor;var f=CodeAnchor.Resolve(original,a,entry.Name);locations.Add((f.At,f.At+f.Length));
            if(a["kind"]!.GetValue<string>()=="rip")rip.Add((f.At,a["next"]!.GetValue<int>(),a["operand"]!.GetValue<int>(),f.Value));
        }
        var merged=new List<(int Start,int End)>();
        foreach(var r in locations.OrderBy(r=>r.Start)){
            if(merged.Count>0&&r.Start<=merged[^1].End){var prior=merged[^1];merged[^1]=(prior.Start,Math.Max(prior.End,r.End));}
            else merged.Add(r);
        }
        var image=new byte[original.Length+0x10000];original.CopyTo(image,0);
        var mappings=new List<(int Start,int End,int New)>();var cursor=original.Length;
        foreach(var r in merged){original.AsSpan(r.Start,r.End-r.Start).CopyTo(image.AsSpan(cursor));image.AsSpan(r.Start,r.End-r.Start).Clear();mappings.Add((r.Start,r.End,cursor));cursor+=r.End-r.Start+16;}
        Assert.True(cursor<image.Length);
        int Move(int at){var r=mappings.Single(r=>at>=r.Start&&at<r.End);return r.New+at-r.Start;}
        foreach(var r in rip)BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(Move(r.At)+r.Operand,4),checked((int)((long)r.Value-Move(r.At)-r.Next)));
        var pe=BitConverter.ToInt32(image,0x3c);var count=BitConverter.ToUInt16(image,pe+6);var optional=BitConverter.ToUInt16(image,pe+20);var header=pe+24+optional+count*40;
        Assert.True(header+40<0x1000);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(pe+6,2),(ushort)(count+1));
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(pe+24+56,4),image.Length);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(header+8,4),0x10000);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(header+12,4),original.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(header+36,4),0x60000020);
        var layout=MotionLayout.Discover(image,new string('A',64)).ToJson();
        Assert.True(JsonNode.DeepEquals(Fixture()["fields"],layout["fields"]));
        Assert.All(layout["probes"]!.AsArray(),p=>Assert.True(OffsetProfile.ParseHex(p!["rva"]!.GetValue<string>())>=(ulong)original.Length));
    }
}
