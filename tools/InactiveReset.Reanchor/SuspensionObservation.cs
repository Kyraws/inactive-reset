using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public sealed record ObservedBody(int Id, double[] Position, double[] Matrix, double[] Velocity,
    double[] Omega, double[] Momentum, double[] Inertia, double[] InverseDiagonal)
{
    public long ReadStartedTimestamp { get; init; }
    public long ReadCompletedTimestamp { get; init; }
}
public sealed record ObservedAnchor(int BodyId, double Weight, double[] LocalPosition);
public sealed record ObservedEndpoint(ObservedAnchor[] Anchors, double ExtraParameter);
public sealed record ObservedUpdateMarkers(double PhysicsTime, double PhysicsStep, double VehicleTime, byte CompletionCounter);
public sealed record ObservedWheel(int SpinBodyId, int UprightBodyId, byte[] GeometryFlags, ObservedEndpoint A,
    ObservedEndpoint B, double Deflection, double DeflectionVelocity, double PushrodForce,
    double ReferenceLength, double RotationRate, double RideHeight)
{
    public long ReadStartedTimestamp { get; init; }
    public long ReadCompletedTimestamp { get; init; }
}
public sealed record SuspensionObservation(int Mode, byte[] OutputGates, int[] ChassisBodyIds,
    ObservedBody Aggregate, double[] OriginOffset, ObservedBody[] Bodies, ObservedWheel[] Wheels)
{
    public string Coherence => NativeUpdateObserved
        ? "native changes observed inside SDK time bracket; not coherent"
        : "body rereads and update markers unchanged; not an atomicity proof";
    public string Acquisition => "bulk-body-and-wheel-v4-markers";
    public ObservedUpdateMarkers? UpdateBefore { get; init; }
    public ObservedUpdateMarkers? UpdateAfter { get; init; }
    public bool UpdateMarkersUnchanged => UpdateBefore is not null && UpdateBefore == UpdateAfter;
    public bool NativeUpdateObserved => !BodyBlocksUnchanged || !UpdateMarkersUnchanged;
    public int[] ChangedBodyIds { get; init; } = [];
    public bool BodyBlocksUnchanged => ChangedBodyIds.Length == 0;
    public long BodyVerificationCompletedTimestamp { get; init; }
    public bool ConstraintStateAvailable => false;
    public bool TyreContactStateAvailable => false;

    public static SuspensionObservation Read(Func<ulong, int, byte[]> read, ulong vehicle,
        MotionLayout m, SuspensionObservationLayout s)
    {
        byte[] Bytes(ulong address, int count)
        {
            var bytes = read(address, count);
            if (bytes.Length != count) throw new GateException("Incomplete native observation read.");
            return bytes;
        }
        static void ValidPointer(ulong p)
        {
            if (p < 0x10000 || p % 8 != 0 || p > 0x00007FFFFFFF0000)
                throw new GateException("Invalid native observation pointer.");
        }
        double[] Doubles(ulong at, int count)
        {
            var bytes = Bytes(at, count * 8);
            var values = Enumerable.Range(0, count).Select(i => BitConverter.ToDouble(bytes, i * 8)).ToArray();
            if (values.Any(v => !double.IsFinite(v))) throw new GateException("Nonfinite native observation.");
            return values;
        }
        var topology = new List<(ulong Address, byte[] Bytes)>();
        var bodyBlocks = new List<(int Id, ulong Address, byte[] Bytes)>();
        byte[] Stable(ulong at, int count)
        {
            var bytes = Bytes(at, count); topology.Add((at, bytes)); return bytes;
        }
        var physics = BitConverter.ToUInt64(Stable(vehicle + m.VehiclePhysics, 8)); ValidPointer(physics);
        ObservedUpdateMarkers Markers()
        {
            var clock = Doubles(physics + s["PhysicsClock"], 2);
            if (clock[1] <= 0) throw new GateException("Invalid native physics timestep.");
            return new(clock[0], clock[1], Doubles(vehicle + s["VehicleClock"], 1)[0],
                Bytes(vehicle + s["CompletionCounter"], 1)[0]);
        }
        var updateBefore = Markers();
        var mode = BitConverter.ToInt32(Stable(vehicle + s["Mode"], 4));
        if (mode != 0) throw new GateException("Native suspension observation requires the physics-backed telemetry mode.");
        var owner = BitConverter.ToUInt64(Stable(physics + s["Owner"], 8)); ValidPointer(owner);
        var gates = new[] { "SuspensionGate", "TyreGate", "ThirdGate", "RideGate" }
            .Select(name => Stable((name is "SuspensionGate" or "TyreGate" ? owner : vehicle) + s[name], 1)[0]).ToArray();
        ulong[] Members(ulong beginOffset, ulong endOffset)
        {
            var begin = BitConverter.ToUInt64(Stable(physics + beginOffset, 8));
            var end = BitConverter.ToUInt64(Stable(physics + endOffset, 8));
            ValidPointer(begin);
            if (end < begin || (end - begin) % 8 != 0 || end - begin is < 8 or > 96)
                throw new GateException("Unsupported native observation membership.");
            var bytes = Stable(begin, checked((int)(end - begin)));
            var members = Enumerable.Range(0, bytes.Length / 8).Select(i => BitConverter.ToUInt64(bytes, i * 8)).ToArray();
            foreach (var member in members) ValidPointer(member);
            if (members.Distinct().Count() != members.Length) throw new GateException("Duplicate native body membership.");
            return members;
        }
        var bodies = Members(m.PhysicsBodiesBegin, m.PhysicsBodiesEnd);
        if (bodies.Length != 12) throw new GateException("Observation requires the researched 12-body layout.");
        var ids = bodies.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        int Id(ulong pointer) => ids.TryGetValue(pointer, out var id) ? id
            : throw new GateException("Attachment or chassis body is not a linked member.");
        var chassis = Members(m.PhysicsChassisBegin, m.PhysicsChassisEnd).Select(Id).ToArray();
        ObservedBody Body(ulong at, int id)
        {
            var fields = new[] { (m.BodyPosition, 3), (m.BodyMatrix, 9), (m.BodyVelocity, 3),
                (m.BodyOmega, 3), (m.BodyMomentum, 3), (m.BodyInertia, 3), (m.BodyInverseDiagonal, 3) };
            var first = fields.Min(f => f.Item1); var end = fields.Max(f => f.Item1 + (ulong)f.Item2 * 8);
            if (end - first > 4096) throw new GateException("Native body observation span exceeds its bound.");
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var bytes = Bytes(at + first, checked((int)(end - first)));
            var completed = System.Diagnostics.Stopwatch.GetTimestamp();
            bodyBlocks.Add((id, at + first, bytes));
            double[] Values(ulong offset, int count)
            {
                var values = Enumerable.Range(0, count).Select(i => BitConverter.ToDouble(bytes, checked((int)(offset - first)) + i * 8)).ToArray();
                if (values.Any(v => !double.IsFinite(v))) throw new GateException("Nonfinite native body observation.");
                return values;
            }
            var matrix = Values(m.BodyMatrix, 9); DirectPosePlan.RequireRotation(matrix);
            var inertia = Values(m.BodyInertia, 3);
            var inverse = Values(m.BodyInverseDiagonal, 3);
            if (inertia.Any(v => v <= 0) || inverse.Any(v => v <= 0))
                throw new GateException("Invalid observed inertia.");
            return new(id, Values(m.BodyPosition, 3), matrix, Values(m.BodyVelocity, 3),
                Values(m.BodyOmega, 3), Values(m.BodyMomentum, 3), inertia, inverse)
                { ReadStartedTimestamp = started, ReadCompletedTimestamp = completed };
        }
        ObservedEndpoint Endpoint(ulong at, byte[] bytes)
        {
            topology.Add((at, bytes));
            double Number(int offset)
            {
                var value = BitConverter.ToDouble(bytes, offset);
                if (!double.IsFinite(value)) throw new GateException("Nonfinite attachment descriptor.");
                return value;
            }
            var secondary = BitConverter.ToUInt64(bytes, 0x28);
            var anchors = new List<ObservedAnchor>();
            foreach (var offset in new[] { 0, 0x28, 0x50 })
            {
                if (offset != 0 && secondary == 0) break;
                var pointer = BitConverter.ToUInt64(bytes, offset);
                if (offset != 0 && pointer == 0) break;
                anchors.Add(new(Id(pointer), secondary == 0 ? 1 : Number(offset + 8),
                    [Number(offset + 0x10), Number(offset + 0x18), Number(offset + 0x20)]));
            }
            return new(anchors.ToArray(), Number(0x78));
        }
        var wheels = Enumerable.Range(0, 4).Select(i =>
        {
            var wheel = physics + (ulong)i * m.WheelStride;
            var blockStart = wheel + m.WheelRecordBase;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var bytes = Bytes(blockStart, checked((int)m.WheelStride));
            var completed = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] Slice(ulong at, int count)
            {
                if (at < blockStart || at - blockStart + (ulong)count > (ulong)bytes.Length)
                    throw new GateException("Native wheel observation field is outside its block.");
                return bytes.AsSpan(checked((int)(at - blockStart)), count).ToArray();
            }
            byte[] Tracked(ulong offset, int count)
            { var value = Slice(wheel + offset, count); topology.Add((wheel + offset, value)); return value; }
            double Number(ulong offset)
            {
                var value = BitConverter.ToDouble(Slice(wheel + offset, 8));
                if (!double.IsFinite(value)) throw new GateException("Nonfinite native wheel observation.");
                return value;
            }
            var spin = Id(BitConverter.ToUInt64(Tracked(m.WheelSpin, 8)));
            var upright = Id(BitConverter.ToUInt64(Tracked(m.WheelUpright, 8)));
            if (spin == upright) throw new GateException("Wheel spin/upright membership overlaps.");
            byte[] flags = [Tracked(s["Cursor"] + s["DisabledFromCursor"], 1)[0], Tracked(s["SpinMode"], 1)[0]];
            return new ObservedWheel(spin, upright, flags,
                Endpoint(wheel + s["Cursor"] - 0x78, Slice(wheel + s["Cursor"] - 0x78, 0x80)),
                Endpoint(wheel + s["Cursor"] + 8, Slice(wheel + s["Cursor"] + 8, 0x80)),
                Number(s["Deflection"]), Number(m.WheelRecordBase + s["VelocityLocal"]), Number(s["Force"]),
                Number(m.WheelRecordBase + s["ReferenceLengthLocal"]), Number(m.WheelRate),
                Doubles(physics + s["RideTable"] + (ulong)i * 8, 1)[0])
                { ReadStartedTimestamp = started, ReadCompletedTimestamp = completed };
        }).ToArray();
        if (wheels.SelectMany(w => new[] { w.SpinBodyId, w.UprightBodyId }).Distinct().Count() != 8)
            throw new GateException("Wheel body membership overlaps.");
        var result = new SuspensionObservation(mode, gates, chassis, Body(physics + m.PhysicsAggregateBody, -1),
            Doubles(physics + m.PhysicsOriginOffset, 3), bodies.Select((at, i) => Body(at, i)).ToArray(), wheels);
        var changed = bodyBlocks.Where(block => !Bytes(block.Address, block.Bytes.Length).AsSpan().SequenceEqual(block.Bytes))
            .Select(block => block.Id).ToArray();
        result = result with { ChangedBodyIds = changed, BodyVerificationCompletedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() };
        foreach (var item in topology)
            if (!Bytes(item.Address, item.Bytes.Length).AsSpan().SequenceEqual(item.Bytes))
                throw new GateException("Native topology, mode or output gates changed during observation.");
        return result with { UpdateBefore = updateBefore, UpdateAfter = Markers() };
    }
}
