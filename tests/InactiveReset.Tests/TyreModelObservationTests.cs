using System.Text.Json;
using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class TyreModelObservationTests
{
    internal sealed class Fixture
    {
        public readonly byte[] Memory;
        public readonly byte[] Image;
        public readonly MotionLayout Motion = SuspensionObservationTests.Motion();
        public readonly SuspensionObservationLayout Suspension = SuspensionObservationTests.Layout().Layout;
        public readonly TyreModelObservationLayout Layout;
        public readonly TyreModelTableLayout? TableLayout;
        public readonly Dictionary<string, int> Functions = [];
        public Fixture(bool tables = false)
        {
            Memory = new byte[tables ? 0x720000 : 0x600000]; Image = new byte[tables ? 0x40000 : 0x14000];
            var at = 0x1000;
            foreach (var function in TyreModelObservationLayout.Proofs()["functions"]!.AsObject())
            {
                Convert.FromHexString(function.Value!["bytes"]!.GetValue<string>()).CopyTo(Image, at);
                Functions.Add(function.Key, at); at += 0x5000;
            }
            Layout = TyreModelObservationLayout.Discover(Image, Motion);
            if (tables)
            {
                at = 0x18000;
                foreach (var function in TyreModelTableLayout.Proofs()["functions"]!.AsObject())
                {
                    Convert.FromHexString(function.Value!["bytes"]!.GetValue<string>()).CopyTo(Image, at);
                    Functions.Add(function.Key, at); at += 0x9000;
                }
                TableLayout = TyreModelTableLayout.Discover(Image, Motion);
            }
            U(0x100000 + Motion.VehiclePhysics, 0x200000);
            D(0x200000 + Suspension["PhysicsClock"], 10); D(0x200000 + Suspension["PhysicsStep"], 0.0025);
            D(0x100000 + Suspension["VehicleClock"], 10);
            U(0x200000 + Motion.PhysicsBodiesBegin, 0x300000); U(0x200000 + Motion.PhysicsBodiesEnd, 0x300060);
            for (var i = 0; i < 12; i++) U(0x300000UL + (ulong)i * 8, 0x400000UL + (ulong)i * 0x1000);
            for (var wheel = 0; wheel < 4; wheel++)
            {
                var tyre = Tyre(wheel); var body = tyre + (ulong)Layout["EmbeddedBody"];
                U(0x200000UL + (ulong)wheel * Motion.WheelStride + Motion.WheelTyre, tyre);
                U(0x200000UL + (ulong)wheel * Motion.WheelStride + Motion.WheelSpin, 0x400000UL + (ulong)wheel * 0x2000);
                BitConverter.GetBytes(0xdead0000U + (uint)wheel).CopyTo(Memory, (int)tyre + Layout["Control"]);
                for (var slot = 0; slot < 8; slot++)
                {
                    var start = tyre + (ulong)(Layout["Slots"] + slot * Layout["SlotStride"]);
                    for (var n = 0; n < 12; n++) BitConverter.GetBytes((ushort)(n == 11 ? 65535 : n + slot)).CopyTo(Memory, (int)start + n * 2);
                    for (var n = 0; n < 9; n++) D(start + 0x18 + (ulong)n * 8, wheel * 1000 + slot * 100 + n + 0.25);
                }
                for (var n = 0; n < 3; n++)
                {
                    D(body + Motion.BodyMatrix + (ulong)n * 32, 1);
                    D(body + Motion.BodyInertia + (ulong)n * 8, 2);
                    D(body + Motion.BodyInverseDiagonal + (ulong)n * 8, 0.5);
                    D(body + 0x10 + (ulong)n * 8, n + 1);
                    D(body + 0x128 + (ulong)n * 8, -n - 1);
                }
                if (tables)
                {
                    var a = Region(wheel, 0); var contacts = Region(wheel, 1); var b = Region(wheel, 2); var descriptors = Region(wheel, 3);
                    BitConverter.GetBytes(504).CopyTo(Memory, (int)tyre + 0x310); U(tyre + 0x318, contacts);
                    BitConverter.GetBytes(72).CopyTo(Memory, (int)tyre + 0xa08);
                    U(tyre + 0xa10, a); U(tyre + 0xa18, b);
                    BitConverter.GetBytes(648).CopyTo(Memory, (int)tyre + 0xa20); U(tyre + 0xa28, descriptors);
                    for (var region = 0; region < 3; region++)
                        for (var cell = 0; cell < (region == 1 ? 504 : 72); cell++)
                            for (var scalar = 0; scalar < 12; scalar++)
                                D(Region(wheel, region) + (ulong)(cell * (region == 1 ? 0x110 : 0x60) + scalar * 8),
                                    wheel * 1000000 + region * 100000 + cell * 100 + scalar + 0.5);
                    for (var group = 0; group < 72; group++)
                        for (var lane = 0; lane < 9; lane++)
                        {
                            var region = lane == 0 ? 0 : lane == 8 ? 2 : 1;
                            var index = region == 1 ? group * 7 + lane - 1 : group;
                            U(descriptors + (ulong)((group * 9 + lane) * 8), Region(wheel, region) + (ulong)(index * (region == 1 ? 0x110 : 0x60)));
                        }
                }
            }
        }
        public static ulong Tyre(int wheel) => 0x500000UL + (ulong)wheel * 0x4000;
        public static ulong Region(int wheel, int region) => 0x600000UL + (ulong)wheel * 0x40000 + new ulong[] { 0, 0x4000, 0x28000, 0x2a000 }[region];
        public void U(ulong at, ulong value) => BitConverter.GetBytes(value).CopyTo(Memory, (int)at);
        public void D(ulong at, double value) => BitConverter.GetBytes(value).CopyTo(Memory, (int)at);
        public byte[] Read(ulong at, int count) => Memory.AsSpan((int)at, count).ToArray();
        public TyreModelAcquisition Acquire(Func<ulong, int, byte[]>? read = null) =>
            TyreModelAcquisition.Acquire(read ?? Read, 0x100000, Motion, Suspension, Layout, TableLayout);
    }
    [Fact]
    public void DiscoveryRelocatesFunctionsAndRejectsMissingOrAmbiguousProofs()
    {
        var f = new Fixture(); Assert.Equal(3, f.Layout.ProbeCount);
        f.Layout.Verify((rva, n) => f.Image.AsSpan((int)rva, n).ToArray());
        Array.Copy(f.Image, f.Functions["modelUpdate"], f.Image, 0x10000, 96);
        Assert.Throws<GateException>(() => TyreModelObservationLayout.Discover(f.Image, f.Motion));
        f.Image.AsSpan(0x10000, 96).Clear(); f.Image[f.Functions["modelUpdate"]] ^= 1;
        Assert.Throws<GateException>(() => TyreModelObservationLayout.Discover(f.Image, f.Motion));
    }
    [Theory]
    [InlineData("modelUpdate")][InlineData("slotUpdate")][InlineData("wheelUpdate")]
    public void CompleteCodeIsRequiredBeyondDiscoveryPrefix(string role)
    {
        var f = new Fixture(); f.Image[f.Functions[role] + 128] ^= 1;
        Assert.Throws<GateException>(() => TyreModelObservationLayout.Discover(f.Image, f.Motion));
        Assert.Throws<GateException>(() => f.Layout.Verify((rva, n) => f.Image.AsSpan((int)rva, n).ToArray()));
    }
    [Fact]
    public void ObservationRefusesAContradictoryEmbeddedBodyLayout()
    {
        var f = new Fixture(); var json = f.Motion.ToJson();
        json["fields"]!["BodyInertia"] = "0x180";
        var changed = MotionLayout.FromJson(json, json["executableSha256"]!.GetValue<string>());
        Assert.Throws<GateException>(() => TyreModelObservationLayout.Discover(f.Image, changed));
    }
    [Fact]
    public void CaptureSeparatesIntegerWordsFromScalarsAndUsesOnlyLocalIds()
    {
        var result = new Fixture().Acquire().Decode();
        Assert.Equal(4, result.Wheels.Length); Assert.False(result.CompleteModelStateAvailable); Assert.False(result.TyreContactStateAvailable);
        Assert.Equal(10, result.Markers.PhysicsTime);
        for (var i = 0; i < 4; i++)
        {
            var wheel = result.Wheels[i]; Assert.Equal(i * 2, wheel.SpinBodyId); Assert.Equal(12 + i, wheel.EmbeddedBody.Id);
            Assert.Equal(0xdead0000U + (uint)i, wheel.ControlWord); Assert.Equal(5, wheel.IndexedSlot);
            Assert.Equal(new double[] { 1, 2, 3 }, wheel.Force); Assert.Equal(new double[] { -1, -2, -3 }, wheel.Torque);
            Assert.Equal(8, wheel.Slots.Length);
            Assert.All(wheel.Slots, s => { Assert.Equal(12, s.Words.Length); Assert.Equal(9, s.ScalarsByOffset.Count); Assert.Equal(65535, s.Words[11]); });
            Assert.Equal(i * 1000 + 708.25, wheel.Slots[7].ScalarsByOffset["0x58"]);
        }
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("5242880", json); Assert.DoesNotContain("Pointer", json); Assert.Contains("unresolved", json);
    }
    [Fact]
    public void StateIsBulkRereadAndDecodingUsesVerifiedLocalCopies()
    {
        var f = new Fixture(); var reads = new List<(ulong At, int Count)>(); var decoding = false;
        var acquisition = f.Acquire((at, count) =>
        { Assert.False(decoding); reads.Add((at, count)); return f.Read(at, count); });
        Assert.Equal(2, reads.Count(r => r.At == Fixture.Tyre(0) + 0xc && r.Count == 0x304));
        Assert.Equal(2, reads.Count(r => r.At == Fixture.Tyre(0) + 0xac0 + 0x10));
        f.D(Fixture.Tyre(0) + 0x10 + 0x18, double.NaN); decoding = true;
        Assert.Equal(0.25, acquisition.Decode().Wheels[0].Slots[0].ScalarsByOffset["0x18"]);
    }
    [Theory]
    [InlineData("inline")][InlineData("body")][InlineData("tyre-pointer")][InlineData("spin-pointer")]
    [InlineData("physics-pointer")][InlineData("clock")][InlineData("mode")]
    public void EveryAdvancingBlockStopsWithoutRetry(string role)
    {
        var f = new Fixture(); var reads = 0;
        var at = role switch
        {
            "inline" => Fixture.Tyre(0) + 0xc,
            "body" => Fixture.Tyre(0) + 0xac0 + 0x10,
            "tyre-pointer" => 0x200000 + f.Motion.WheelTyre,
            "spin-pointer" => 0x200000 + f.Motion.WheelSpin,
            "physics-pointer" => 0x100000 + f.Motion.VehiclePhysics,
            "clock" => 0x200000 + f.Suspension["PhysicsClock"],
            _ => 0x100000 + f.Suspension["Mode"]
        };
        var error = Assert.Throws<GateException>(() => f.Acquire((address, n) =>
        {
            var bytes = f.Read(address, n); if (address == at && ++reads == 2) bytes[0] ^= 1; return bytes;
        }));
        var change = Assert.IsType<TyreModelReadChange>(error.Data["tyreModelReadChange"]);
        Assert.Equal(0, change.FirstChangedOffset); Assert.Equal(1, change.ChangedBytes); Assert.Equal(2, reads);
        Assert.Null(error.Data["observationTiming"]);
    }
    [Theory]
    [InlineData("short")][InlineData("foreign-spin")][InlineData("duplicate-tyre")]
    [InlineData("overlapping-tyre")][InlineData("duplicate-body")][InlineData("mode")]
    [InlineData("nan-slot")][InlineData("nan-body")][InlineData("bad-matrix")][InlineData("bad-inertia")][InlineData("bad-step")]
    public void InvalidStateIsRefused(string failure)
    {
        var f = new Fixture(); var tyre = Fixture.Tyre(0); var body = tyre + 0xac0;
        if (failure == "foreign-spin") f.U(0x200000 + f.Motion.WheelSpin, 0x490000);
        if (failure == "duplicate-tyre") f.U(0x200000 + f.Motion.WheelStride + f.Motion.WheelTyre, tyre);
        if (failure == "overlapping-tyre") f.U(0x200000 + f.Motion.WheelStride + f.Motion.WheelTyre, tyre + 0x100);
        if (failure == "duplicate-body") f.U(0x300008, 0x400000);
        if (failure == "mode") f.Memory[0x100000 + f.Suspension["Mode"]] = 1;
        if (failure == "nan-slot") f.D(tyre + 0x28, double.NaN);
        if (failure == "nan-body") f.D(body + f.Motion.BodyVelocity, double.NaN);
        if (failure == "bad-matrix") f.D(body + f.Motion.BodyMatrix, -1);
        if (failure == "bad-inertia") f.D(body + f.Motion.BodyInertia, 0);
        if (failure == "bad-step") f.D(0x200000 + f.Suspension["PhysicsStep"], 0);
        Assert.Throws<GateException>(() => f.Acquire(failure == "short" ? (_, _) => [] : null).Decode());
    }
    [Theory]
    [InlineData(-1)][InlineData(0xc)][InlineData(0x10)][InlineData(0x30f)][InlineData(0xb98)]
    public void PausedPairRequiresRawStateIncludingUnexportedBytes(int offset)
    {
        var f = new Fixture(); var first = f.Acquire();
        if (offset >= 0) f.Memory[(int)Fixture.Tyre(0) + offset] ^= 1;
        var second = f.Acquire();
        if (offset < 0) first.RequireSame(second); else Assert.Throws<GateException>(() => first.RequireSame(second));
    }
    [Theory]
    [InlineData("decode")][InlineData("reread")][InlineData("pair")]
    public void TyreFailuresReleaseOwnedPause(string failure)
    {
        var f = new Fixture(); byte flag = 0; var writes = new List<byte>();
        var pause = new PhysicsPause(() => true, () => flag, v => { flag = v; writes.Add(v); });
        Assert.Throws<GateException>(() => OwnedPauseObservation.Run(pause, () =>
        {
            if (failure == "decode") f.D(Fixture.Tyre(0) + 0x28, double.NaN);
            var reads = 0;
            var first = f.Acquire((at, n) =>
            {
                var bytes = f.Read(at, n);
                if (failure == "reread" && at == Fixture.Tyre(0) + 0xc && ++reads == 2) bytes[0] ^= 1;
                return bytes;
            });
            if (failure == "pair") { f.Memory[(int)Fixture.Tyre(0) + 0xc] ^= 1; first.RequireSame(f.Acquire()); }
            return first.Decode();
        }, () => { }, _ => { }, CancellationToken.None));
        Assert.Equal(new byte[] { 1, 0 }, writes); Assert.Equal(0, flag);
    }
    [Fact]
    public void IdenticalNumericValuesAtDifferentNativeObjectsDoNotPassPausedPair()
    {
        var f = new Fixture(); var first = f.Acquire(); var replacement = 0x580000UL;
        Array.Copy(f.Memory, (int)Fixture.Tyre(0), f.Memory, (int)replacement, 0x2000);
        f.U(0x200000 + f.Motion.WheelTyre, replacement);
        var second = f.Acquire();
        Assert.Equal(JsonSerializer.Serialize(first.Decode()), JsonSerializer.Serialize(second.Decode()));
        Assert.Throws<GateException>(() => first.RequireSame(second));
    }
    [Fact]
    public void NewCommandsRefuseWindowArgumentsBeforeAttachment()
    {
        var args = new[] { new string('0', 64), "unused", "unused", "--window" };
        Assert.Throws<ArgumentException>(() => SuspensionProbe.TyreModelState(args));
        Assert.Throws<ArgumentException>(() => SuspensionProbe.PausedTyreModelState(args));
    }
}
