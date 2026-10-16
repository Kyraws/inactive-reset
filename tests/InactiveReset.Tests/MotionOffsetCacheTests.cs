using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;

public sealed class MotionOffsetCacheTests:IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"ir-motion-cache-"+Guid.NewGuid().ToString("N"));
    private readonly JsonObject _expected;
    private readonly string _hash;
    private readonly ProbeSpec _probe;
    private readonly Dictionary<(ulong,int),byte[]> _motionProbes;
    private string Cache=>Path.Combine(_directory,_hash+".auto.json");
    public MotionOffsetCacheTests()
    {
        var motion=ProfileFixture.Read("motion-layout-66942337");_hash=motion["executableSha256"]!.GetValue<string>();
        _expected=ProfileFixture.Read();_expected["build"]!["executableSha256"]=_hash;
        _expected["build"]!["automaticResolverVersion"]=AutomaticOffsets.ResolverVersion;
        _expected["build"]!["sizeOfImage"]=$"0x{motion["imageSize"]!.GetValue<int>():X}";
        _expected["motion"]=motion;Directory.CreateDirectory(_directory);
        _probe=OffsetProfile.Parse(_expected.ToJsonString(),"test").Probe;
        _motionProbes=motion["probes"]!.AsArray().GroupBy(p=>(OffsetProfile.ParseHex(p!["rva"]!.GetValue<string>()),p["bytes"]!.GetValue<string>().Length/2)).ToDictionary(g=>g.Key,g=>Convert.FromHexString(g.First()!["bytes"]!.GetValue<string>()));
    }
    private byte[] Read(ulong rva,int size)
    {
        if(rva==_probe.Rva.Value&&size==_probe.Bytes.Length)return _probe.Bytes;
        return _motionProbes[(rva,size)];
    }
    private OffsetProfile Load(out bool discovered,Func<ulong,int,byte[]>? read=null)=>AutomaticOffsets.LoadOrDiscover(
        _directory,_hash,"test",()=>new byte[0x1000],read??Read,out discovered,(_,_,_)=>_expected.DeepClone().AsObject());

    [Fact] public void VerifiedResetCacheIsReused()
    {
        Load(out var first);Assert.True(first);var loaded=Load(out var second);Assert.False(second);
        Assert.Equal(0x159a0UL,loaded.Motion!.VehiclePhysics);
    }
    [Theory][InlineData("field")][InlineData("probe")][InlineData("probe-rva")][InlineData("hash")][InlineData("version")][InlineData("image-size")]
    [InlineData("missing-fields")][InlineData("missing-probes")][InlineData("missing-ranges")]
    public void AlteredResetCacheIsRepairedFromDerivation(string change)
    {
        Load(out _);var changed=JsonNode.Parse(File.ReadAllText(Cache))!.AsObject();
        switch(change){
            case "field":changed["motion"]!["fields"]!["VehiclePhysics"]="0x17000";break;
            case "probe":changed["motion"]!["probes"]![0]!["bytes"]="00";break;
            case "probe-rva":changed["motion"]!["probes"]![0]!["rva"]="0x1A5A59D";break;
            case "hash":changed["motion"]!["executableSha256"]=new string('B',64);break;
            case "version":changed["motion"]!["resolverVersion"]=0;break;
            case "image-size":changed["motion"]!["imageSize"]=changed["motion"]!["imageSize"]!.GetValue<int>()+0x1000;break;
            case "missing-fields":changed["motion"]!.AsObject().Remove("fields");break;
            case "missing-probes":changed["motion"]!.AsObject().Remove("probes");break;
            case "missing-ranges":changed["motion"]!.AsObject().Remove("codeRanges");break;
        }
        File.WriteAllText(Cache,changed.ToJsonString());Load(out var discovered);Assert.True(discovered);
        Assert.True(JsonNode.DeepEquals(_expected,JsonNode.Parse(File.ReadAllText(Cache))));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void FailedLiveResetProbePreservesCacheAndRefusesUse(bool unreadable)
    {
        Load(out _);var original=File.ReadAllText(Cache);var target=OffsetProfile.ParseHex(_expected["motion"]!["probes"]![0]!["rva"]!.GetValue<string>());
        byte[] Changed(ulong rva,int size)=>rva!=target?Read(rva,size):unreadable?throw new MemoryAccessException("unreadable reset code"):new byte[size];
        if(unreadable)Assert.Throws<MemoryAccessException>(()=>Load(out _,Changed));else Assert.Throws<GateException>(()=>Load(out _,Changed));
        Assert.Equal(original,File.ReadAllText(Cache));
    }
    public void Dispose()=>Directory.Delete(_directory,true);
}
