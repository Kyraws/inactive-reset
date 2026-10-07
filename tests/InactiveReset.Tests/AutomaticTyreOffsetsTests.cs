using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;
namespace InactiveReset.Tests;
public sealed class AutomaticTyreOffsetsTests
{
    private static string RepositoryRoot()
    {
        var directory=AppContext.BaseDirectory;
        while(directory is not null && !Directory.Exists(Path.Combine(directory,"offsets"))) directory=Path.GetDirectoryName(directory);
        return directory ?? throw new DirectoryNotFoundException("repository root missing");
    }
    [Fact]
    public void Anchors_follow_moved_code_and_operands_and_reject_missing_or_duplicate_code()
    {
        var anchor=JsonNode.Parse("""{"pattern":"48 8B 81 ?? ?? ?? ?? 90","operand":3,"width":4,"kind":"value","next":7}""")!.AsObject();
        var image=new byte[100];
        void Put(int at,int value){new byte[]{0x48,0x8b,0x81}.CopyTo(image,at);BitConverter.GetBytes(value).CopyTo(image,at+3);image[at+7]=0x90;}
        Assert.Throws<GateException>(()=>AutomaticTyreOffsets.ResolveAnchor(image,anchor,"physicsPointerOffset"));
        Put(40,0x17000);
        Assert.Equal(0x17000UL,AutomaticTyreOffsets.ResolveAnchor(image,anchor,"physicsPointerOffset").Value);
        Put(60,0x17000);
        Assert.Throws<GateException>(()=>AutomaticTyreOffsets.ResolveAnchor(image,anchor,"physicsPointerOffset"));
    }

    [Fact]
    public void Rip_targets_follow_relocation_and_selected_indices_require_the_module_base()
    {
        var anchor=JsonNode.Parse("""{"pattern":"48 8D 05 ?? ?? ?? ?? 90","operand":3,"width":4,"kind":"rip","next":7}""")!.AsObject();
        var image=new byte[100];new byte[]{0x48,0x8d,0x05}.CopyTo(image,20);image[27]=0x90;
        BitConverter.GetBytes(50-27).CopyTo(image,23);
        Assert.Equal(50UL,AutomaticTyreOffsets.ResolveAnchor(image,anchor,"inventoryRva").Value);
        anchor["baseOperand"]=3;anchor["baseNext"]=7;
        Assert.Throws<GateException>(()=>AutomaticTyreOffsets.ResolveAnchor(image,anchor,"selectedIndicesRva"));
    }

    [LocalDumpFact("66942337")]
    public void Discovery_follows_changed_globals_and_structure_operands_without_reusing_old_addresses()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        using var stream=typeof(AutomaticOffsets).Assembly.GetManifestResourceStream("InactiveReset.Core.discovery-tyres.json")!;
        var seeds=JsonNode.Parse(stream)!.AsObject();
        var patches=new List<(int At,int Width,int Value)>();
        foreach(var field in seeds)
        foreach(var node in field.Value!.AsArray())
        {
            var anchor=node!.AsObject();
            if(anchor["kind"]!.GetValue<string>()=="shape")continue;
            var resolved=AutomaticTyreOffsets.ResolveAnchor(image,anchor,field.Key);
            var delta=field.Key.EndsWith("Rva") ? 0x1000 : field.Key is "compoundOffset" or "freshFlagsOffset" or "uniformFlagOffset" ? 4 : 0x20;
            var value=(long)resolved.Value+delta;
            if(anchor["kind"]!.GetValue<string>()=="rip") value-=resolved.At+anchor["next"]!.GetValue<int>();
            patches.Add((resolved.At+anchor["operand"]!.GetValue<int>(),anchor["width"]!.GetValue<int>(),checked((int)value)));
        }
        foreach(var patch in patches)
        {
            if(patch.Width==1)image[patch.At]=checked((byte)patch.Value);
            else BitConverter.GetBytes(patch.Value).CopyTo(image,patch.At);
        }
        var profile=AutomaticTyreOffsets.Discover(image,new string('C',64));
        Assert.Equal(0x3B94EB8UL,OffsetProfile.ParseHex(profile["reset"]!["inventoryRva"]!.GetValue<string>()));
        Assert.Equal(0x159C0UL,OffsetProfile.ParseHex(profile["physicsPointerOffset"]!.GetValue<string>()));
        Assert.Equal(0x6D0UL,OffsetProfile.ParseHex(profile["reset"]!["wheelStride"]!.GetValue<string>()));
        // A changed operand in only one consumer must fail rather than pick one.
        var first=seeds["physicsPointerOffset"]![0]!.AsObject();
        var site=AutomaticTyreOffsets.ResolveAnchor(image,first,"physicsPointerOffset");
        BitConverter.GetBytes(0x17000).CopyTo(image,site.At+first["operand"]!.GetValue<int>());
        Assert.Throws<GateException>(()=>AutomaticTyreOffsets.Discover(image,new string('D',64)));
    }

    [LocalDumpFact("66942337")]
    public void All_discovered_fields_match_the_live_verified_profile_and_cache_rechecks_code()
    {
        var image=File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var hash=new string('A',64);
        var resolved=AutomaticTyreOffsets.Discover(image,hash);
        var expected=JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(),"offsets","tyre-physics-66942337.json")))!.AsObject();
        foreach(var field in expected)
        {
            if(field.Key is "executableSha256" or "probeRva" or "probeBytes" or "reset") continue;
            Assert.Equal(field.Value!.GetValue<string>().ToUpperInvariant(),resolved[field.Key]!.GetValue<string>().ToUpperInvariant());
        }
        foreach(var field in expected["reset"]!.AsObject())
        {
            if(field.Key is "probes")continue;
            if(field.Key=="temperatureOffsets")Assert.Equal(field.Value!.AsArray().Select(x=>x!.GetValue<string>().ToUpperInvariant()),resolved["reset"]![field.Key]!.AsArray().Select(x=>x!.GetValue<string>().ToUpperInvariant()));
            else Assert.Equal(field.Value!.GetValue<string>().ToUpperInvariant(),resolved["reset"]![field.Key]!.GetValue<string>().ToUpperInvariant());
        }
        var directory=Path.Combine(Path.GetTempPath(),"inactive-reset-tyres-"+Guid.NewGuid().ToString("N"));var captures=0;
        byte[] Capture(){captures++;return image;}
        byte[] Read(ulong at,int size)=>image.AsSpan((int)at,size).ToArray();
        try
        {
            AutomaticTyreOffsets.LoadOrDiscover(directory,hash,Capture,Read);
            AutomaticTyreOffsets.LoadOrDiscover(directory,hash,Capture,Read);
            Assert.Equal(1,captures);
            var cachePath=Path.Combine(directory,$"tyre-physics-{hash}.auto.json");
            var altered=JsonNode.Parse(File.ReadAllText(cachePath))!.AsObject();
            altered["physicsPointerOffset"]="0x17000";
            File.WriteAllText(cachePath,altered.ToJsonString());
            var repaired=AutomaticTyreOffsets.LoadOrDiscover(directory,hash,Capture,Read);
            Assert.Equal(2,captures);
            Assert.Equal(resolved["physicsPointerOffset"]!.GetValue<string>(),repaired["physicsPointerOffset"]!.GetValue<string>());
            altered=JsonNode.Parse(File.ReadAllText(cachePath))!.AsObject();
            altered["reset"]!["probes"]=new JsonArray();
            File.WriteAllText(cachePath,altered.ToJsonString());
            AutomaticTyreOffsets.LoadOrDiscover(directory,hash,Capture,Read);
            Assert.Equal(3,captures);
            var probe=resolved["reset"]!["probes"]![0]!;
            var rva=(int)OffsetProfile.ParseHex(probe["rva"]!.GetValue<string>());
            var original=image[rva];image[rva]^=0xff;
            Assert.Throws<GateException>(()=>AutomaticTyreOffsets.LoadOrDiscover(directory,hash,Capture,Read));
            image[rva]=original;
            AutomaticTyreOffsets.LoadOrDiscover(directory,new string('B',64),Capture,Read);
            Assert.Equal(5,captures);
        }
        finally{Directory.Delete(directory,true);}
    }
}
