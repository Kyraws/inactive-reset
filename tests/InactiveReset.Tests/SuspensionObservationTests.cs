using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class SuspensionObservationTests
{
    [Theory][InlineData(1, 2, 1)][InlineData(double.NaN, 1, 1)][InlineData(1, 1, 26)]
    [InlineData(1, 1, -1)][InlineData(1, 1, double.PositiveInfinity)]
    public void TornOrOverlongTimeBracketsAreRefused(double before, double after, double milliseconds) =>
        Assert.Throws<GateException>(() => SuspensionProbe.RequireTimeBracket(before, after, milliseconds));

    [Fact] public void EqualSdkTimeIsAcceptedWithoutClaimingAtomicity() => SuspensionProbe.RequireTimeBracket(1, 1, 5);

    [Fact] public void OnlyTimingFailuresAreEligibleForWindowRetry()
    {
        var error = Assert.Throws<GateException>(() => SuspensionProbe.RequireTimeBracket(1, 2, 5));
        Assert.Equal(true, error.Data["observationTiming"]);
        Assert.Null(new GateException("player context changed").Data["observationTiming"]);
    }

    [Fact] public void WindowTimeRestartStopsInsteadOfRetrying()
    {
        SuspensionProbe.RequireTimeProgress(1, 1);
        SuspensionProbe.RequireTimeProgress(1, 2);
        var error = Assert.Throws<GateException>(() => SuspensionProbe.RequireTimeProgress(2, 1));
        Assert.Null(error.Data["observationTiming"]);
    }
    internal static MotionLayout Motion()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offsets", "motion-layout-66942337.json")))!.AsObject();
        return MotionLayout.FromJson(json, json["executableSha256"]!.GetValue<string>());
    }
    internal static (byte[] Image, SuspensionObservationLayout Layout) Layout()
    {
        var anchors = SuspensionObservationLayout.Anchors(); var image = new byte[0x40000];
        foreach (var group in new[] { "fields", "contracts" })
            foreach (var item in anchors[group]!.AsObject())
            {
                var anchor = item.Value!; var at = (int)(OffsetProfile.ParseHex(anchor["sourceRva"]!.GetValue<string>()) & 0x3ffff);
                var pattern = anchor["pattern"]!.GetValue<string>().Split(' ');
                for (var i = 0; i < pattern.Length; i++) if (pattern[i] != "??") image[at + i] = Convert.ToByte(pattern[i], 16);
            }
        var values = new Dictionary<string, int> { ["Cursor"] = 0x878, ["Stride"] = 0x6b0, ["Mode"] = 0x6f88,
            ["SuspensionGate"] = 0x18f52, ["TyreGate"] = 0x18f53, ["ThirdGate"] = 0x18f4a, ["RideGate"] = 0x18f48,
            ["RideTable"] = 0x38f8, ["Deflection"] = 0x900, ["Force"] = 0x940, ["DeflectionLocal"] = 0x2e8,
            ["VelocityLocal"] = 0x2f0, ["ReferenceLengthLocal"] = 0x2f8, ["ForceLocal"] = 0x328,
            ["DisabledFromCursor"] = 0xa0, ["SpinMode"] = 0x919, ["Owner"] = 0x4bc0,
            ["PhysicsClock"] = 0xa8, ["PhysicsStep"] = 0xb0, ["VehicleClock"] = 0x159e0, ["CompletionCounter"] = 0x18f4e };
        foreach (var item in anchors["fields"]!.AsObject())
        {
            var a = item.Value!; var at = (int)(OffsetProfile.ParseHex(a["sourceRva"]!.GetValue<string>()) & 0x3ffff);
            BitConverter.GetBytes(values[item.Key]).CopyTo(image, at + a["operand"]!.GetValue<int>());
        }
        return (image, SuspensionObservationLayout.Discover(image, Motion()));
    }
    [Fact] public void DiscoveryBindsCodeAndRejectsMissingOrDuplicateEvidence()
    {
        var (image, layout) = Layout(); Assert.Equal(29, layout.ProbeCount);
        layout.Verify((at, count) => image.AsSpan((int)at, count).ToArray());
        var a = SuspensionObservationLayout.Anchors()["fields"]!["Cursor"]!;
        var site = (int)(OffsetProfile.ParseHex(a["sourceRva"]!.GetValue<string>()) & 0x3ffff);
        image.AsSpan(site, 24).CopyTo(image.AsSpan(0x100, 24));
        Assert.Throws<GateException>(() => SuspensionObservationLayout.Discover(image, Motion()));
        image[site] ^= 1;
        Assert.Throws<GateException>(() => layout.Verify((at, count) => image.AsSpan((int)at, count).ToArray()));
        image.AsSpan(0x100, 24).Clear();
        Assert.Throws<GateException>(() => SuspensionObservationLayout.Discover(image, Motion()));
    }
    [Fact] public void DisagreeingProducerAndConsumerOffsetsAreRefused()
    {
        var (image, _) = Layout(); var a = SuspensionObservationLayout.Anchors()["fields"]!["Deflection"]!;
        var site = (int)(OffsetProfile.ParseHex(a["sourceRva"]!.GetValue<string>()) & 0x3ffff);
        BitConverter.GetBytes(0x910).CopyTo(image, site + a["operand"]!.GetValue<int>());
        Assert.Throws<GateException>(() => SuspensionObservationLayout.Discover(image, Motion()));
    }
    private static byte[] Memory(MotionLayout m, SuspensionObservationLayout s)
    {
        var memory = new byte[0x500000];
        void U(ulong at, ulong v) => BitConverter.GetBytes(v).CopyTo(memory, (int)at);
        void D(ulong at, double v) => BitConverter.GetBytes(v).CopyTo(memory, (int)at);
        U(0x100000 + m.VehiclePhysics, 0x200000);
        U(0x200000 + s["Owner"], 0x120000);
        D(0x200000 + s["PhysicsClock"], 1); D(0x200000 + s["PhysicsStep"], 0.0025);
        D(0x100000 + s["VehicleClock"], 1);
        U(0x200000 + m.PhysicsBodiesBegin, 0x300000); U(0x200000 + m.PhysicsBodiesEnd, 0x300060);
        U(0x200000 + m.PhysicsChassisBegin, 0x300000); U(0x200000 + m.PhysicsChassisEnd, 0x300010);
        foreach (var name in new[] { "SuspensionGate", "TyreGate", "ThirdGate", "RideGate" })
            memory[(name is "SuspensionGate" or "TyreGate" ? 0x120000 : 0x100000) + (int)s[name]] = 1;
        for (var i = 0; i < 12; i++) U(0x300000 + (ulong)i * 8, 0x400000 + (ulong)i * 0x1000);
        foreach (var body in Enumerable.Range(0, 12).Select(i => 0x400000UL + (ulong)i * 0x1000).Append(0x200000 + m.PhysicsAggregateBody))
        {
            for (var i = 0; i < 3; i++) { D(body + m.BodyMatrix + (ulong)i * 32, 1); D(body + m.BodyInertia + (ulong)i * 8, 2); D(body + m.BodyInverseDiagonal + (ulong)i * 8, 0.5); }
        }
        for (var i = 0; i < 4; i++)
        {
            var wheel = 0x200000UL + (ulong)i * m.WheelStride;
            U(wheel + m.WheelSpin, 0x400000 + (ulong)i * 0x2000); U(wheel + m.WheelUpright, 0x401000 + (ulong)i * 0x2000);
            U(wheel + s["Cursor"] - 0x78, 0x400000); U(wheel + s["Cursor"] + 8, 0x401000);
            D(wheel + s["Deflection"], 0.02);
        }
        return memory;
    }
    [Fact] public void SnapshotUsesLocalBodyIdsAndRefusesTopologyChanges()
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s);
        byte[] Read(ulong at, int count) => memory.AsSpan((int)at, count).ToArray();
        var snapshot = SuspensionObservation.Read(Read, 0x100000, m, s);
        Assert.Equal(12, snapshot.Bodies.Length); Assert.Equal(4, snapshot.Wheels.Length);
        Assert.Equal(0, snapshot.Wheels[0].A.Anchors[0].BodyId); Assert.Equal(1, snapshot.Wheels[0].B.Anchors[0].BodyId);
        Assert.Equal(0.02, snapshot.Wheels[0].Deflection); Assert.False(snapshot.ConstraintStateAvailable);
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, snapshot.OutputGates);
        var reads = 0;
        byte[] Changed(ulong at, int count)
        {
            if (at == 0x100000 + m.VehiclePhysics && ++reads == 2) return BitConverter.GetBytes(0x210000UL);
            return Read(at, count);
        }
        Assert.Throws<GateException>(() => SuspensionObservation.Read(Changed, 0x100000, m, s));
    }

    [Fact] public void PausedPairIgnoresReadClocksButRejectsStateAndPhaseChanges()
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s);
        byte[] Read(ulong at, int count) => memory.AsSpan((int)at, count).ToArray();
        var first = SuspensionObservation.Read(Read, 0x100000, m, s);
        var second = SuspensionObservation.Read(Read, 0x100000, m, s);
        SuspensionProbe.RequirePausedPair(1, 1, first, second);
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(1, 2, first, second));
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(double.NaN, double.NaN, first, second));
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(1, 1, first, second with { ChangedBodyIds = [0] }));
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(1, 1, first,
            second with { UpdateAfter = second.UpdateAfter! with { CompletionCounter = 2 } }));
        var wheels = second.Wheels.ToArray(); wheels[0] = wheels[0] with { Deflection = 0.03 };
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(1, 1, first, second with { Wheels = wheels }));
        var bodies = second.Bodies.ToArray(); bodies[0] = bodies[0] with { Velocity = [1, 0, 0] };
        Assert.Throws<GateException>(() => SuspensionProbe.RequirePausedPair(1, 1, first, second with { Bodies = bodies }));
    }

    [Fact] public void BodyStateUsesOneBulkReadAndOneVerificationRead()
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s);
        var reads = new List<(ulong Address, int Count)>();
        byte[] Read(ulong at, int count)
        { reads.Add((at, count)); return memory.AsSpan((int)at, count).ToArray(); }
        var snapshot = SuspensionObservation.Read(Read, 0x100000, m, s);
        for (var i = 0; i < 12; i++)
        {
            var start = 0x400000UL + (ulong)i * 0x1000;
            var bodyReads = reads.Where(r => r.Address >= start && r.Address < start + 0x1000).ToArray();
            Assert.Equal(2, bodyReads.Length); Assert.Equal(bodyReads[0], bodyReads[1]);
            Assert.InRange(bodyReads[0].Count, 72, 4096);
            Assert.True(snapshot.Bodies[i].ReadCompletedTimestamp >= snapshot.Bodies[i].ReadStartedTimestamp);
            Assert.True(snapshot.Bodies[i].ReadStartedTimestamp > 0);
        }
        for (var i = 0; i < 4; i++)
        {
            var start = 0x200000UL + (ulong)i * m.WheelStride + m.WheelRecordBase;
            Assert.Single(reads, r => r.Address == start && r.Count == (int)m.WheelStride);
        }
        Assert.Equal("bulk-body-and-wheel-v4-markers", snapshot.Acquisition);
        Assert.True(snapshot.BodyBlocksUnchanged);
    }

    [Fact] public void BodyRereadReportsChangesEvenWithUnchangedTopology()
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s); var reads = 0;
        byte[] Read(ulong at, int count)
        {
            if (at >= 0x400000 && at < 0x401000 && ++reads == 2)
                BitConverter.GetBytes(1d).CopyTo(memory, (int)(0x400000 + m.BodyPosition));
            return memory.AsSpan((int)at, count).ToArray();
        }
        var result = SuspensionObservation.Read(Read, 0x100000, m, s);
        Assert.False(result.BodyBlocksUnchanged); Assert.Equal(new[] { 0 }, result.ChangedBodyIds);
        Assert.True(result.UpdateMarkersUnchanged); Assert.True(result.NativeUpdateObserved);
        Assert.Contains("not coherent", result.Coherence);
        Assert.Equal(0, result.Bodies[0].Position[0]);
    }

    [Fact] public void CompletedUpdateMarkerCanChangeWithoutBodyBlockDifferences()
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s); var reads = 0;
        byte[] Read(ulong at, int count)
        {
            if (at == 0x100000 + s["CompletionCounter"] && ++reads == 2) return new byte[] { 1 };
            return memory.AsSpan((int)at, count).ToArray();
        }
        var snapshot = SuspensionObservation.Read(Read, 0x100000, m, s);
        Assert.True(snapshot.BodyBlocksUnchanged); Assert.False(snapshot.UpdateMarkersUnchanged);
        Assert.True(snapshot.NativeUpdateObserved); Assert.Contains("not coherent", snapshot.Coherence);
    }
    [Theory][InlineData("unknown-body")][InlineData("nonfinite")][InlineData("mode")][InlineData("duplicate")]
    public void InvalidNativeStateIsRefused(string change)
    {
        var m = Motion(); var (_, s) = Layout(); var memory = Memory(m, s);
        switch (change)
        {
            case "unknown-body": BitConverter.GetBytes(0x480000UL).CopyTo(memory, (int)(0x200000 + s["Cursor"] - 0x78)); break;
            case "nonfinite": BitConverter.GetBytes(double.NaN).CopyTo(memory, (int)(0x400000 + m.BodyVelocity)); break;
            case "mode": BitConverter.GetBytes(1).CopyTo(memory, (int)(0x100000 + s["Mode"])); break;
            case "duplicate": BitConverter.GetBytes(0x400000UL).CopyTo(memory, 0x300008); break;
        }
        Assert.Throws<GateException>(() => SuspensionObservation.Read((at, count) => memory.AsSpan((int)at, count).ToArray(), 0x100000, m, s));
    }
}
