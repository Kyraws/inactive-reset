using System.Text.Json;
using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;
using Fixture = InactiveReset.Tests.TyreModelObservationTests.Fixture;

namespace InactiveReset.Tests;

public sealed class TyreModelTableTests
{
    [Theory]
    [InlineData("allocate")][InlineData("advance")][InlineData("coefficients")]
    public void TableCodeRequiresExactFunctionsBeyondUniquePrefixes(string role)
    {
        var f = new Fixture(true); Assert.Equal(3, f.TableLayout!.ProbeCount);
        f.TableLayout.Verify((at, n) => f.Image.AsSpan((int)at, n).ToArray());
        f.Image[f.Functions[role] + 100] ^= 1;
        Assert.Throws<GateException>(() => TyreModelTableLayout.Discover(f.Image, f.Motion));
        Assert.Throws<GateException>(() => f.TableLayout.Verify((at, n) => f.Image.AsSpan((int)at, n).ToArray()));
    }
    [Fact]
    public void TableDiscoveryRejectsMissingOrAmbiguousPrefixes()
    {
        var f = new Fixture(true); Array.Copy(f.Image, f.Functions["allocate"], f.Image, 0x3f000, 96);
        Assert.Throws<GateException>(() => TyreModelTableLayout.Discover(f.Image, f.Motion));
        f.Image.AsSpan(0x3f000, 96).Clear(); f.Image[f.Functions["allocate"]] ^= 1;
        Assert.Throws<GateException>(() => TyreModelTableLayout.Discover(f.Image, f.Motion));
    }
    [Fact]
    public void CaptureMapsAll648TargetsAndExportsOnlyTypedPrefixes()
    {
        var f = new Fixture(true); var value = f.Acquire().Decode();
        Assert.False(value.CompleteModelStateAvailable); Assert.False(value.TyreContactStateAvailable);
        for (var wheel = 0; wheel < 4; wheel++)
        {
            var t = Assert.IsType<ObservedTyreModelTables>(value.Wheels[wheel].Tables);
            Assert.Equal(72, t.StateA.Length); Assert.Equal(504, t.ContactPrefixes.Length); Assert.Equal(72, t.StateB.Length);
            Assert.All(t.StateA.Concat(t.ContactPrefixes).Concat(t.StateB), row => Assert.Equal(12, row.Length));
            Assert.Equal(72, t.Descriptors.Length); Assert.All(t.Descriptors, group => Assert.Equal(9, group.Length));
            Assert.Equal(new ObservedTyreTableTarget("stateA", 71), t.Descriptors[71][0]);
            Assert.Equal(new ObservedTyreTableTarget("contact", 503), t.Descriptors[71][7]);
            Assert.Equal(new ObservedTyreTableTarget("stateB", 71), t.Descriptors[71][8]);
            Assert.Equal(wheel * 1000000 + 150311.5, t.ContactPrefixes[503][11]);
        }
        var json = JsonSerializer.Serialize(value); Assert.DoesNotContain("6291456", json); Assert.DoesNotContain("Pointer", json);
        Assert.Null(new Fixture().Acquire().Decode().Wheels[0].Tables);
    }
    [Theory]
    [InlineData("contact")][InlineData("state")][InlineData("descriptor-count")]
    [InlineData("alias")][InlineData("cross-wheel-alias")][InlineData("body-alias")][InlineData("short")]
    public void CountsAndRangesAreCheckedBeforeTableStateReads(string failure)
    {
        var f = new Fixture(true); var tyre = Fixture.Tyre(0); var reads = 0;
        if (failure == "contact") BitConverter.GetBytes(int.MaxValue).CopyTo(f.Memory, (int)tyre + 0x310);
        if (failure == "state") BitConverter.GetBytes(73).CopyTo(f.Memory, (int)tyre + 0xa08);
        if (failure == "descriptor-count") BitConverter.GetBytes(72).CopyTo(f.Memory, (int)tyre + 0xa20);
        if (failure == "alias") f.U(tyre + 0xa18, Fixture.Region(0, 0));
        if (failure == "cross-wheel-alias") f.U(tyre + 0xa10, Fixture.Region(1, 0));
        if (failure == "body-alias") f.U(tyre + 0xa10, 0x400000);
        Assert.Throws<GateException>(() => f.Acquire((at, n) =>
        {
            if (at >= 0x600000) reads++;
            if (failure == "short" && at == tyre + 0xa08) return [];
            return f.Read(at, n);
        }));
        Assert.Equal(0, reads);
    }
    [Fact]
    public void DescriptorPointersAreNeverDereferencedAndOffsetsMustMatchExactly()
    {
        var f = new Fixture(true); f.U(Fixture.Region(0, 3), 0xdead0000);
        var acquisition = f.Acquire((at, n) => { Assert.NotEqual(0xdead0000UL, at); return f.Read(at, n); });
        Assert.Throws<GateException>(() => acquisition.Decode());
        f.U(Fixture.Region(0, 3), Fixture.Region(0, 0) + 8);
        Assert.Throws<GateException>(() => f.Acquire().Decode());
    }
    [Fact]
    public void LastDescriptorAndSlotIndexAreBounded()
    {
        var f = new Fixture(true); var descriptor = Fixture.Region(0, 3) + 0x1440 - 8;
        f.U(descriptor, Fixture.Region(0, 2) + 72 * 0x60);
        Assert.Throws<GateException>(() => f.Acquire().Decode());
        f.U(descriptor, Fixture.Region(0, 2) + 71 * 0x60);
        BitConverter.GetBytes((ushort)72).CopyTo(f.Memory, (int)Fixture.Tyre(0) + 0x10);
        Assert.Throws<GateException>(() => f.Acquire().Decode());
        BitConverter.GetBytes((ushort)0).CopyTo(f.Memory, (int)Fixture.Tyre(0) + 0x10);
        BitConverter.GetBytes(ushort.MaxValue).CopyTo(f.Memory, (int)Fixture.Tyre(0) + 0x10 + 18);
        Assert.Equal(ushort.MaxValue, f.Acquire().Decode().Wheels[0].Slots[0].Words[9]);
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)]
    public void AllNumericPrefixesMustBeFiniteButUnclassifiedBytesAreNotDecoded(int region)
    {
        var f = new Fixture(true); var index = region == 1 ? 503 : 71;
        var at = Fixture.Region(0, region) + (ulong)(index * (region == 1 ? 0x110 : 0x60) + 0x58);
        f.D(at, double.NaN); Assert.Throws<GateException>(() => f.Acquire().Decode());
        f.D(at, 0); f.D(Fixture.Region(0, 1) + 0x100, double.NaN);
        Assert.NotNull(f.Acquire().Decode().Wheels[0].Tables);
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public void TablesAreBulkCopiedAndRereadBeforeLocalDecoding(int region)
    {
        var f = new Fixture(true); var calls = new List<(ulong At, int Count)>(); var decoding = false;
        var acquisition = f.Acquire((at, n) => { Assert.False(decoding); calls.Add((at, n)); return f.Read(at, n); });
        Assert.Equal(2, calls.Count(c => c.At == Fixture.Region(0, region)));
        f.D(Fixture.Region(0, 0), double.NaN); decoding = true;
        Assert.Equal(0.5, acquisition.Decode().Wheels[0].Tables!.StateA[0][0]);
    }
    [Theory]
    [InlineData("state")][InlineData("metadata")][InlineData("graph")][InlineData("header")]
    public void AnyRereadChangeStopsEvenIfItWouldNotChangeDecodedValues(string role)
    {
        var f = new Fixture(true); var count = 0;
        var at = role == "header" ? Fixture.Tyre(0) + 0xa08 : Fixture.Region(0, role == "graph" ? 3 : 1);
        var offset = role == "metadata" ? 0x100 : 0;
        var error = Assert.Throws<GateException>(() => f.Acquire((address, n) =>
        { var bytes = f.Read(address, n); if (address == at && ++count == 2) bytes[offset] ^= 1; return bytes; }));
        var change = Assert.IsType<TyreModelReadChange>(error.Data["tyreModelReadChange"]);
        Assert.Equal(offset, change.FirstChangedOffset); Assert.Equal(1, change.ChangedBytes);
        Assert.Null(error.Data["observationTiming"]);
    }
    [Fact]
    public void PausedPairRejectsUnexportedMetadataAndReplacedAllocations()
    {
        var f = new Fixture(true); var first = f.Acquire();
        f.Memory[(int)Fixture.Region(0, 1) + 0x100] ^= 1;
        var second = f.Acquire(); Assert.Equal(JsonSerializer.Serialize(first.Decode()), JsonSerializer.Serialize(second.Decode()));
        Assert.Throws<GateException>(() => first.RequireSame(second));
        f.Memory[(int)Fixture.Region(0, 1) + 0x100] ^= 1;
        var replacement = 0x710000UL;
        Array.Copy(f.Memory, (int)Fixture.Region(0, 0), f.Memory, (int)replacement, 0x1b00);
        f.U(Fixture.Tyre(0) + 0xa10, replacement);
        for (var group = 0; group < 72; group++) f.U(Fixture.Region(0, 3) + (ulong)(group * 9 * 8), replacement + (ulong)(group * 0x60));
        second = f.Acquire(); Assert.Equal(JsonSerializer.Serialize(first.Decode()), JsonSerializer.Serialize(second.Decode()));
        Assert.Throws<GateException>(() => first.RequireSame(second));
    }
    [Theory]
    [InlineData("graph")][InlineData("numeric")][InlineData("reread")]
    public void AllocatedTableFailuresReleaseTheOwnedPause(string failure)
    {
        var f = new Fixture(true); byte flag = 0; var writes = new List<byte>();
        var pause = new PhysicsPause(() => true, () => flag, v => { flag = v; writes.Add(v); });
        Assert.Throws<GateException>(() => OwnedPauseObservation.Run(pause, () =>
        {
            if (failure == "graph") f.U(Fixture.Region(0, 3), 0xdead0000);
            if (failure == "numeric") f.D(Fixture.Region(0, 2), double.NaN);
            var reads = 0;
            return f.Acquire((at, n) =>
            {
                var bytes = f.Read(at, n);
                if (failure == "reread" && at == Fixture.Region(0, 0) && ++reads == 2) bytes[0] ^= 1;
                return bytes;
            }).Decode();
        }, () => { }, _ => { }, CancellationToken.None));
        Assert.Equal(new byte[] { 1, 0 }, writes); Assert.Equal(0, flag);
    }
    [Fact]
    public void TableCommandsRejectWindowsBeforeAttachment()
    {
        var args = new[] { new string('0', 64), "unused", "unused", "--window" };
        Assert.Throws<ArgumentException>(() => SuspensionProbe.TyreTableState(args));
        Assert.Throws<ArgumentException>(() => SuspensionProbe.PausedTyreTableState(args));
    }
}
