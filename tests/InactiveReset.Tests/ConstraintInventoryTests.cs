using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class ConstraintInventoryTests
{
    private const ulong Module = 0x10000000;
    private static (byte[] Image, ConstraintInventoryLayout Layout, MotionLayout Motion) Layout(bool jointState = false)
    {
        var anchors = ConstraintInventoryLayout.Anchors(); var image = new byte[0x80000];
        int Site(JsonNode a) => (int)(OffsetProfile.ParseHex(a["sourceRva"]!.GetValue<string>()) & 0x3ffff);
        foreach (var group in new[] { "fields", "contracts", "functions" })
            foreach (var item in anchors[group]!.AsObject())
            {
                var a = item.Value!; var at = Site(a); var pattern = a["pattern"]!.GetValue<string>().Split(' ');
                for (var i = 0; i < pattern.Length; i++) if (pattern[i] != "??") image[at + i] = Convert.ToByte(pattern[i], 16);
            }
        var typeIndex=0;
        foreach(var type in anchors["types"]!.AsObject())
        {
            foreach(var proof in type.Value!.AsObject())
            {
                var a=proof.Value!; var at=Site(a); var pattern=a["pattern"]!.GetValue<string>().Split(' ');
                for(var i=0;i<pattern.Length;i++) if(pattern[i]!="??") image[at+i]=Convert.ToByte(pattern[i],16);
            }
            typeIndex++;
        }
        var values = new Dictionary<string, int> { ["Mode"] = 0x6f88, ["ObjectsBegin"] = 0x2410, ["ObjectsEnd"] = 0x2418,
            ["GroupsBegin"] = 0x2428, ["GroupsEnd"] = 0x2430, ["GroupRows"] = 4,
            ["GroupObjectCount"] = 0x38, ["GroupObjects"] = 0x20, ["KnownVtableRva"] = 0x31000 };
        foreach (var item in anchors["fields"]!.AsObject())
        {
            var a = item.Value!; var at = Site(a); var value = values[item.Key];
            if (a["kind"]!.GetValue<string>() == "rip") value -= at + a["next"]!.GetValue<int>();
            var offset = at + a["operand"]!.GetValue<int>();
            if (a["width"]!.GetValue<int>() == 1) image[offset] = checked((byte)value);
            else BitConverter.GetBytes(value).CopyTo(image, offset);
        }
        foreach (var (name, slot) in new[] { ("KnownExportRva", 0x28), ("KnownPreSolveRva", 0xa0) })
            BitConverter.GetBytes(Module + (ulong)Site(anchors["functions"]![name]!)).CopyTo(image, 0x31000 + slot);
        typeIndex=0;
        foreach(var type in anchors["types"]!.AsObject())
        {
            foreach(var proof in type.Value!.AsObject())
            {
                var a=proof.Value!; var at=Site(a);
                var target=proof.Key=="NameRva" ? 0x38000+typeIndex*0x100 : 0x31400+typeIndex*0x200;
                BitConverter.GetBytes(target-at-a["next"]!.GetValue<int>()).CopyTo(image,at+a["operand"]!.GetValue<int>());
            }
            System.Text.Encoding.ASCII.GetBytes(type.Key+"\0").CopyTo(image,0x38000+typeIndex*0x100);
            Array.Copy(image,0x31000,image,0x31400+typeIndex*0x200,0xa8);
            typeIndex++;
        }
        if (jointState)
        {
            var at = 0x40000; typeIndex = 0;
            foreach (var type in ConstraintJointStateLayout.Proofs()["types"]!.AsObject())
            {
                foreach (var function in type.Value!["functions"]!.AsObject())
                {
                    var bytes = Convert.FromHexString(function.Value!["bytes"]!.GetValue<string>());
                    bytes.CopyTo(image, at);
                    BitConverter.GetBytes(Module + (ulong)at).CopyTo(image,
                        0x31400 + typeIndex * 0x200 + function.Value["slot"]!.GetValue<int>());
                    at += bytes.Length + 16;
                }
                typeIndex++;
            }
        }
        var json=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"offsets","motion-layout-66942337.json")))!.AsObject();
        return (image, ConstraintInventoryLayout.Discover(image, Module), MotionLayout.FromJson(json,json["executableSha256"]!.GetValue<string>()));
    }
    private sealed class Fixture
    {
        public readonly byte[] Memory = new byte[0x500000];
        public readonly byte[] Image;
        public readonly ConstraintInventoryLayout Layout;
        public readonly MotionLayout Motion;
        public Fixture(bool jointState = false)
        {
            (Image, Layout, Motion) = ConstraintInventoryTests.Layout(jointState);
            U(0x100000 + Motion.VehiclePhysics, 0x200000);
            U(0x200000 + Motion.PhysicsBodiesBegin, 0x300000); U(0x200000 + Motion.PhysicsBodiesEnd, 0x300060);
            for (var i=0;i<12;i++) U(0x300000+(ulong)i*8,0x480000+(ulong)i*0x1000);
            U(0x200000+Layout["ObjectsBegin"],0x300100); U(0x200000+Layout["ObjectsEnd"],0x300110);
            U(0x300100,0x400000); U(0x300108,0x401000);
            U(0x200000+Layout["GroupsBegin"],0x300200); U(0x200000+Layout["GroupsEnd"],0x300208); U(0x300200,0x450000);
            U(0x400000,Module+Layout["KnownVtableRva"]); U(0x400068,0x480000); U(0x400070,0x481000);
            Array.Copy(Image,0x31000,Image,0x31200,0xa8); U(0x401000,Module+0x31200);
            I(0x450000+Layout["GroupRows"],3); I(0x450000+Layout["GroupObjectCount"],2);
            U(0x450000+Layout["GroupObjects"],0x300300); U(0x300300,0x400000); U(0x300308,0x401000);
        }
        public void U(ulong at,ulong value)=>BitConverter.GetBytes(value).CopyTo(Memory,(int)at);
        public void I(ulong at,int value)=>BitConverter.GetBytes(value).CopyTo(Memory,(int)at);
        public byte[] Read(ulong at,int count) => at>=Module
            ? Image.AsSpan(checked((int)(at-Module)),count).ToArray() : Memory.AsSpan((int)at,count).ToArray();
        public ConstraintInventory Observe(Func<ulong,int,byte[]>? read=null)=>ConstraintInventory.Read(read??Read,0x100000,Motion,Layout);
    }
    [Fact] public void InventoryMapsLocalIdsAndKeepsUnknownTypesUnavailable()
    {
        var f=new Fixture(); var value=f.Observe();
        Assert.Equal(12,value.BodyCount); Assert.Equal(2,value.Joints.Length);
        Assert.Equal("IsoBallJoint_0",value.Joints[0].Type); Assert.Equal(new[]{0,1},value.Joints[0].BodyIds);
        Assert.Equal("unresolved",value.Joints[1].Type); Assert.Null(value.Joints[1].BodyIds);
        Assert.Equal(new[]{0,1},Assert.Single(value.Groups).JointIds); Assert.Equal(3,value.Groups[0].RowCount);
        Assert.False(value.ConstraintStateAvailable); Assert.All(value.Joints,j=>Assert.False(j.StateAvailable));
        Assert.Contains("atomicity",value.Limitation);
    }
    [Fact] public void CodeProofBindsConstructorVtableAndFunctionBytes()
    {
        var (image,layout,_) = Layout(); layout.Verify((at,count)=>image.AsSpan((int)at,count).ToArray());
        image[(int)layout["KnownExportRva"]]^=1;
        Assert.Throws<GateException>(()=>layout.Verify((at,count)=>image.AsSpan((int)at,count).ToArray()));
        Assert.Throws<GateException>(()=>ConstraintInventoryLayout.Discover(image,Module));
    }
    [Fact] public void MissingAndDuplicateSearchContractsRefuseDiscovery()
    {
        var (image,_,_) = Layout(); var a=ConstraintInventoryLayout.Anchors()["fields"]!["ObjectsBegin"]!;
        var at=(int)(OffsetProfile.ParseHex(a["sourceRva"]!.GetValue<string>())&0x3ffff);
        image.AsSpan(at,35).CopyTo(image.AsSpan(0x100,35));
        Assert.Throws<GateException>(()=>ConstraintInventoryLayout.Discover(image,Module));
        image.AsSpan(0x100,35).Clear(); image[at]^=1;
        Assert.Throws<GateException>(()=>ConstraintInventoryLayout.Discover(image,Module));
    }
    [Theory][InlineData("duplicate")][InlineData("oversize")][InlineData("unaligned")]
    [InlineData("group-member")][InlineData("body-member")][InlineData("negative-count")]
    [InlineData("large-rows")][InlineData("external-vtable")][InlineData("external-function")]
    [InlineData("fallback-mode")]
    public void InvalidTopologyAndCountsAreRefused(string failure)
    {
        var f=new Fixture();
        switch(failure)
        {
            case "duplicate": f.U(0x300108,0x400000); break;
            case "oversize": f.U(0x200000+f.Layout["ObjectsEnd"],0x301000); break;
            case "unaligned": f.U(0x300100,0x400001); break;
            case "group-member": f.U(0x300308,0x402000); break;
            case "body-member": f.U(0x400070,0x490000); break;
            case "negative-count": f.I(0x450000+f.Layout["GroupObjectCount"],-1); break;
            case "large-rows": f.I(0x450000+f.Layout["GroupRows"],257); break;
            case "external-vtable": f.U(0x401000,Module-8); break;
            case "external-function": BitConverter.GetBytes(Module-8).CopyTo(f.Image,0x31228); break;
            case "fallback-mode": f.I(0x100000+f.Layout["Mode"],1); break;
        }
        Assert.Throws<GateException>(()=>f.Observe());
    }
    [Fact] public void MembershipChangesAndShortReadsAreRefused()
    {
        var f=new Fixture(); var calls=0;
        byte[] Changed(ulong at,int count)
        {
            if(at==0x300100 && ++calls==2) f.U(0x300100,0x402000);
            return f.Read(at,count);
        }
        Assert.Throws<GateException>(()=>f.Observe(Changed));
        Assert.Throws<GateException>(()=>new Fixture().Observe((_,_)=>[]));
    }
    [Fact] public void EmptyObjectAndGroupVectorsAreExplicitEmptyInventories()
    {
        var f=new Fixture();
        foreach(var name in new[]{"ObjectsBegin","ObjectsEnd","GroupsBegin","GroupsEnd"}) f.U(0x200000+f.Layout[name],0);
        var value=f.Observe(); Assert.Empty(value.Joints); Assert.Empty(value.Groups);
    }
    [Theory][InlineData(0x300302)][InlineData(0x300304)]
    public void PackedSolverObjectTablesNeedOnlyTwoByteAlignment(int address)
    {
        var f=new Fixture();
        f.U(0x450000+f.Layout["GroupObjects"],(ulong)address);
        f.U((ulong)address,0x400000); f.U((ulong)address+8,0x401000);
        Assert.Equal(new[]{0,1},Assert.Single(f.Observe().Groups).JointIds);
    }
    [Fact] public void OddSolverObjectTableIsRefused()
    {
        var f=new Fixture(); f.U(0x450000+f.Layout["GroupObjects"],0x300301);
        Assert.Throws<GateException>(()=>f.Observe());
    }
    [Fact] public void ProvedTypeNamesDoNotEnableUnprovedBodyOrStateReads()
    {
        var f=new Fixture(); f.U(0x401000,Module+0x31400);
        var joint=f.Observe().Joints[1]; Assert.Equal("IsoBallJoint_2",joint.Type);
        Assert.Null(joint.BodyIds); Assert.False(joint.StateAvailable);
        f.Image[0x38000]^=1;
        Assert.Throws<GateException>(()=>f.Layout.Verify((at,count)=>f.Read(Module+at,count)));
        Assert.Throws<GateException>(()=>ConstraintInventoryLayout.Discover(f.Image,Module));
    }

    private static (Fixture Fixture, ConstraintJointStateLayout State) StateFixture()
    {
        var f = new Fixture(jointState: true);
        var specs = ConstraintJointStateLayout.Proofs()["types"]!.AsObject().ToArray();
        f.U(0x200000 + f.Layout["ObjectsEnd"], 0x3001f0);
        f.U(0x200000 + f.Layout["GroupsEnd"], 0x300218);
        for (var id = 0; id < 30; id++)
        {
            var type = id < 5 ? 0 : id < 9 ? 1 : id < 29 ? 2 : 3;
            var spec = specs[type].Value!; var at = 0x400000UL + (ulong)id * 0x1000;
            f.U(0x300100UL + (ulong)id * 8, at); f.U(at, Module + (ulong)(0x31400 + type * 0x200));
            var body = (ulong)spec["bodies"]!.GetValue<int>(); f.U(at + body, 0x480000); f.U(at + body + 8, 0x481000);
            for (var row = 0; row < spec["rows"]!.GetValue<int>(); row++)
            {
                var tuple = at + (ulong)(spec["tuple"]!.GetValue<int>() + row * spec["stride"]!.GetValue<int>());
                f.U(tuple + 0x10, BitConverter.DoubleToUInt64Bits(1.25 + id + row));
                f.U(tuple + 0x18, BitConverter.DoubleToUInt64Bits(double.NegativeInfinity));
                f.U(tuple + 0x20, BitConverter.DoubleToUInt64Bits(double.PositiveInfinity));
            }
        }
        int[][] groups = [[0,29], [1,2,5,6,9,10,11,12,13,14,15,16,17,18], [3,4,7,8,19,20,21,22,23,24,25,26,27,28]];
        for (var id = 0; id < 3; id++)
        {
            var group = 0x450000UL + (ulong)id * 0x1000; var table = 0x301002UL + (ulong)id * 0x100;
            f.U(0x300200UL + (ulong)id * 8, group); f.I(group + f.Layout["GroupRows"], id == 0 ? 6 : 20);
            f.I(group + f.Layout["GroupObjectCount"], groups[id].Length); f.U(group + f.Layout["GroupObjects"], table);
            for (var j = 0; j < groups[id].Length; j++) f.U(table + (ulong)j * 8, 0x400000UL + (ulong)groups[id][j] * 0x1000);
        }
        return (f, ConstraintJointStateLayout.Discover(f.Image, f.Layout));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0x50)]
    [InlineData(0x118)]
    [InlineData(0x10)]
    public void PausedPairRequiresEveryRawJointByteIncludingUnexportedCallback(int changedOffset)
    {
        var (f, state) = StateFixture();
        var first = ConstraintInventory.Acquire(f.Read, 0x100000, f.Motion, f.Layout, state);
        if (changedOffset >= 0) f.Memory[0x400000 + changedOffset] ^= 1;
        var second = ConstraintInventory.Acquire(f.Read, 0x100000, f.Motion, f.Layout, state);
        if (changedOffset < 0) first.RequireSame(second);
        else Assert.Throws<GateException>(() => first.RequireSame(second));
    }

    [Fact]
    public void PausedPairRejectsChangedMembershipEvenWithSameDecodedBodyIds()
    {
        var (f, state) = StateFixture();
        var first = ConstraintInventory.Acquire(f.Read, 0x100000, f.Motion, f.Layout, state);
        // Reverse two same-type entries; both captures remain individually valid.
        var a = BitConverter.ToUInt64(f.Memory, 0x300100);
        var b = BitConverter.ToUInt64(f.Memory, 0x300108);
        f.U(0x300100, b); f.U(0x300108, a);
        var second = ConstraintInventory.Acquire(f.Read, 0x100000, f.Motion, f.Layout, state);
        Assert.Throws<GateException>(() => first.RequireSame(second));
    }

    [Fact] public void StateCapturesFourTypesAnd46RowsWithoutReusablePointers()
    {
        var (f, state) = StateFixture(); state.Verify((rva, n) => f.Read(Module + rva, n));
        var result = ConstraintInventory.Read(f.Read, 0x100000, f.Motion, f.Layout, state);
        Assert.Equal(24, state.ProbeCount); Assert.Equal(46, result.Joints.Sum(j => j.ObservedState!.Rows.Length));
        Assert.All(result.Joints, j => { Assert.True(j.StateAvailable); Assert.Equal(new[] { 0, 1 }, j.BodyIds); });
        Assert.Equal(1.25, result.Joints[0].ObservedState!.Rows[0].Correction);
        Assert.Equal(32.25, result.Joints[29].ObservedState!.Rows[2].Correction);
        Assert.Equal("-Infinity", result.Joints[0].ObservedState!.Rows[0].Lower);
        Assert.Equal("Infinity", result.Joints[29].ObservedState!.Rows[0].Upper);
        Assert.Equal(12, result.Joints[9].ObservedState!.Rows[0].Coefficients.Length);
        Assert.Equal(6, result.Joints[5].ObservedState!.Rows[0].Coefficients.Length);
        Assert.False(result.ConstraintStateAvailable);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("Infinity", json); Assert.DoesNotContain("4194304", json);
    }
    [Theory][InlineData("initialize")][InlineData("export")][InlineData("set")][InlineData("get")][InlineData("preSolve")]
    public void StateRequiresExactCurrentVtableFunctionProofs(string role)
    {
        var (f, state) = StateFixture();
        var slot = ConstraintJointStateLayout.Proofs()["types"]!["IsoBallJoint_2"]!["functions"]![role]!["slot"]!.GetValue<int>();
        var at = BitConverter.ToUInt64(f.Image, 0x31400 + slot) - Module; f.Image[(int)at] ^= 1;
        Assert.Throws<GateException>(() => ConstraintJointStateLayout.Discover(f.Image, f.Layout));
        Assert.Throws<GateException>(() => state.Verify((rva, n) => f.Read(Module + rva, n)));
    }
    [Theory][InlineData("foreign-body")][InlineData("unknown")][InlineData("rows")][InlineData("membership")]
    [InlineData("nan")][InlineData("infinite-correction")][InlineData("limits")][InlineData("short")][InlineData("changed")]
    public void InvalidOrAdvancingStateIsRefused(string failure)
    {
        var (f, state) = StateFixture(); var calls = 0;
        switch (failure)
        {
            case "foreign-body": f.U(0x400098, 0x490000); break;
            case "unknown": f.U(0x400000, Module + 0x31200); break;
            case "rows": f.I(0x450004, 7); break;
            case "membership": f.U(0x301102, 0x400000); break;
            case "nan": f.U(0x400118, BitConverter.DoubleToUInt64Bits(double.NaN)); break;
            case "infinite-correction": f.U(0x400118, BitConverter.DoubleToUInt64Bits(double.PositiveInfinity)); break;
            case "limits": f.U(0x400120, BitConverter.DoubleToUInt64Bits(double.PositiveInfinity)); f.U(0x400128, 0); break;
        }
        byte[] Read(ulong at, int n)
        {
            if (at == 0x400000 && n == 0x378)
            {
                if (failure == "short") return [];
                if (failure == "changed" && ++calls == 2) f.U(0x400118, 0);
            }
            return f.Read(at, n);
        }
        Assert.Throws<GateException>(() => ConstraintInventory.Read(Read, 0x100000, f.Motion, f.Layout, state));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void StateRereadRefusalIdentifiesBlockAndOffsetWithoutPointers(bool truncated)
    {
        var (f, state) = StateFixture(); var calls = 0;
        byte[] Read(ulong at, int n)
        {
            var bytes = f.Read(at, n);
            if (at == 0x400000 && n == 0x378 && ++calls == 2)
            {
                if (truncated) return bytes[..^8];
                bytes[0x118] ^= 1;
            }
            return bytes;
        }
        var error = Assert.Throws<GateException>(() => ConstraintInventory.Read(Read, 0x100000, f.Motion, f.Layout, state));
        var change = Assert.IsType<ConstraintReadChange>(error.Data["constraintReadChange"]);
        Assert.Equal("joint 0 IsoBallJoint_2 state", change.Block);
        Assert.Equal(0x378, change.ExpectedBytes);
        Assert.Equal(truncated ? 0x370 : 0x378, change.ActualBytes);
        Assert.Equal(truncated ? (int?)null : 0x118, change.FirstChangedOffset);
        Assert.Equal(truncated ? 8 : 1, change.ChangedBytes);
        Assert.DoesNotContain("400000", error.Message);
    }
    [Fact] public void AcquiredStateDecodesFromVerifiedCopiesWithoutFurtherNativeReads()
    {
        var (f, state) = StateFixture(); var decoding = false;
        byte[] Read(ulong at, int n)
        {
            Assert.False(decoding);
            return f.Read(at, n);
        }
        var acquisition = ConstraintInventory.Acquire(Read, 0x100000, f.Motion, f.Layout, state);
        f.U(0x400118, BitConverter.DoubleToUInt64Bits(double.NaN));
        decoding = true;
        var result = acquisition.Decode();
        Assert.Equal(1.25, result.Joints[0].ObservedState!.Rows[0].Correction);
        Assert.Equal(46, result.Joints.Sum(j => j.ObservedState!.Rows.Length));
    }
    [Fact] public void JointStateBatchStartsAfterSolverTopologyDiscovery()
    {
        var (f, state) = StateFixture(); var topologyReads = 0;
        byte[] Read(ulong at, int n)
        {
            if (at == 0x301202 && ++topologyReads == 1)
                f.U(0x400118, BitConverter.DoubleToUInt64Bits(2.25));
            return f.Read(at, n);
        }
        var result = ConstraintInventory.Read(Read, 0x100000, f.Motion, f.Layout, state);
        Assert.Equal(2.25, result.Joints[0].ObservedState!.Rows[0].Correction);
        Assert.Equal(2, topologyReads);
    }
    [Theory][InlineData(0xcf, true)][InlineData(0x118, true)][InlineData(0x98, false)]
    [InlineData(0x4e, false)][InlineData(0x10, false)]
    public void OnlyNumericRowChangesQualifyForFreshBoundedAttempts(int offset, bool rowDataOnly)
    {
        var (f, state) = StateFixture(); var calls = 0;
        byte[] Read(ulong at, int n)
        {
            var bytes = f.Read(at, n);
            if (at == 0x400000 && n == 0x378 && ++calls == 2) bytes[offset] ^= 1;
            return bytes;
        }
        var error = Assert.Throws<GateException>(() => ConstraintInventory.Read(Read, 0x100000, f.Motion, f.Layout, state));
        Assert.Equal(rowDataOnly, Assert.IsType<ConstraintReadChange>(error.Data["constraintReadChange"]).RowDataOnly);
    }
    [Fact] public void NativeVerificationFinishesBeforeJointDecoding()
    {
        var (f, state) = StateFixture(); var calls = 0;
        f.U(0x400118, BitConverter.DoubleToUInt64Bits(double.NaN));
        byte[] Read(ulong at, int n)
        {
            var bytes = f.Read(at, n);
            if (at == 0x400000 && n == 0x378 && ++calls == 2) bytes[0xcf] ^= 1;
            return bytes;
        }
        var error = Assert.Throws<GateException>(() => ConstraintInventory.Read(Read, 0x100000, f.Motion, f.Layout, state));
        Assert.IsType<ConstraintReadChange>(error.Data["constraintReadChange"]);
        Assert.Equal(2, calls);
    }
}
