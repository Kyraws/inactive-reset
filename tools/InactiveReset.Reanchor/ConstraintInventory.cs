using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public sealed record InventoriedJoint(int Id, ulong VtableRva, ulong RowExportRva, ulong PreSolveRva,
    string Type, int[]? BodyIds)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ObservedJointState? ObservedState { get; init; }
    public bool StateAvailable => ObservedState is not null;
}
public sealed record InventoriedSolverGroup(int Id, int RowCount, int[] JointIds);
public sealed record ConstraintReadChange(string Block, int ExpectedBytes, int ActualBytes, int? FirstChangedOffset, int ChangedBytes,
    bool RowDataOnly = false);
public sealed class ConstraintInventoryAcquisition
{
    private readonly Func<ConstraintInventory> _decode;
    private readonly (ulong At, byte[] Bytes, string Role)[] _blocks;
    internal ConstraintInventoryAcquisition(Func<ConstraintInventory> decode,
        (ulong At, byte[] Bytes, string Role)[] blocks) { _decode = decode; _blocks = blocks; }
    public ConstraintInventory Decode() => _decode();
    public void RequireSame(ConstraintInventoryAcquisition other)
    {
        if (_blocks.Length != other._blocks.Length)
            throw new GateException("Constraint topology changed during the owned pause.");
        for (var i = 0; i < _blocks.Length; i++)
            if (_blocks[i].At != other._blocks[i].At || _blocks[i].Role != other._blocks[i].Role ||
                !_blocks[i].Bytes.AsSpan().SequenceEqual(other._blocks[i].Bytes))
                throw new GateException($"Constraint block changed during the owned pause: {_blocks[i].Role}.");
    }
}
public sealed record ConstraintInventory(InventoriedJoint[] Joints, InventoriedSolverGroup[] Groups, int BodyCount)
{
    public bool ConstraintStateAvailable => false;
    public string Limitation => "Membership/vtables reread; no native lock or atomicity proof. Optional joint blocks are reread exactly; no complete solver/contact state or restoration payload.";

    public static ConstraintInventory Read(Func<ulong, int, byte[]> read, ulong vehicle,
        MotionLayout motion, ConstraintInventoryLayout layout, ConstraintJointStateLayout? stateLayout = null)
        => Acquire(read, vehicle, motion, layout, stateLayout).Decode();

    /// <summary>Copy and verify native bytes before decoding the immutable local copy.</summary>
    public static ConstraintInventoryAcquisition Acquire(Func<ulong, int, byte[]> read, ulong vehicle,
        MotionLayout motion, ConstraintInventoryLayout layout, ConstraintJointStateLayout? stateLayout = null)
    {
        var tracked = new List<(ulong At, byte[] Bytes, string Role)>();
        var stateBlocks = new Dictionary<int, byte[]>();
        var stateRequests = new List<(int Id, ulong At, int Size, string Role)>();
        byte[] Stable(ulong at, int count, string role)
        {
            var bytes = read(at, count);
            if (bytes.Length != count) throw new GateException("Incomplete constraint inventory read.");
            tracked.Add((at, bytes, role)); return bytes;
        }
        static void Pointer(ulong at, string role, ulong alignment = 8)
        {
            if (at < 0x10000 || at % alignment != 0 || at > 0x00007FFFFFFF0000)
                throw new GateException($"Invalid constraint inventory {role} pointer: 0x{at:X}.");
        }
        ulong[] Pointers(ulong at, int count, string role, ulong vectorAlignment = 8)
        {
            if (count == 0) return [];
            Pointer(at, role + " vector", vectorAlignment);
            var bytes = Stable(at, checked(count * 8), role + " members");
            var values = Enumerable.Range(0, count).Select(i => BitConverter.ToUInt64(bytes, i * 8)).ToArray();
            foreach (var value in values) Pointer(value, role + " member");
            if (values.Distinct().Count() != values.Length) throw new GateException("Duplicate constraint inventory membership.");
            return values;
        }
        var physics = BitConverter.ToUInt64(Stable(vehicle + motion.VehiclePhysics, 8, "vehicle physics pointer")); Pointer(physics, "physics");
        if (BitConverter.ToInt32(Stable(vehicle + layout["Mode"], 4, "vehicle telemetry mode")) != 0)
            throw new GateException("Constraint inventory requires physics-backed telemetry mode.");
        ulong[] Members(ulong begin, ulong end, int maximum, string role)
        {
            var first = BitConverter.ToUInt64(Stable(physics + begin, 8, role + " begin"));
            var last = BitConverter.ToUInt64(Stable(physics + end, 8, role + " end"));
            if (last < first || (last - first) % 8 != 0 || (last - first) / 8 > (ulong)maximum)
                throw new GateException("Constraint inventory vector exceeds its supported bound.");
            if (first != 0) Pointer(first, role + " begin");
            return Pointers(first, (int)((last - first) / 8), role);
        }
        var bodies = Members(motion.PhysicsBodiesBegin, motion.PhysicsBodiesEnd, 12, "linked bodies");
        if (bodies.Length != 12) throw new GateException("Constraint inventory requires the researched 12-body layout.");
        var bodyIds = bodies.Select((at, id) => (at, id)).ToDictionary(v => v.at, v => v.id);
        var objects = Members(layout["ObjectsBegin"], layout["ObjectsEnd"], 256, "joints");
        var groups = Members(layout["GroupsBegin"], layout["GroupsEnd"], 64, "solver groups");
        var objectIds = objects.Select((at, id) => (at, id)).ToDictionary(v => v.at, v => v.id);
        var joints = objects.Select((at, id) =>
        {
            var vtable = BitConverter.ToUInt64(Stable(at, 8, $"joint {id} vtable pointer")); Pointer(vtable, $"joint {id} vtable");
            var vtableRva = layout.ModuleRva(vtable);
            if (vtableRva + 0xa8 > (ulong)layout.ModuleSize) throw new GateException("Invalid joint vtable extent.");
            var table = Stable(vtable, 0xa8, $"joint {id} vtable slots");
            var export = layout.ModuleRva(BitConverter.ToUInt64(table, 0x28), executable: true);
            var preSolve = layout.ModuleRva(BitConverter.ToUInt64(table, 0xa0), executable: true);
            var known = vtableRva == layout["KnownVtableRva"];
            int[]? references = null;
            if (known)
            {
                if (export != layout["KnownExportRva"] || preSolve != layout["KnownPreSolveRva"])
                    throw new GateException("Known joint vtable targets changed.");
                var pointers = Pointers(at + 0x68, 2, $"joint {id} bodies");
                references = pointers.Select(p => bodyIds.TryGetValue(p, out var bodyId) ? bodyId
                    : throw new GateException("Known joint references a body outside linked membership.")).ToArray();
            }
            var type = layout.Identify(vtableRva);
            if (stateLayout is not null)
                stateRequests.Add((id, at, stateLayout.Size(type), $"joint {id} {type} state"));
            return new InventoriedJoint(id, vtableRva, export, preSolve, type, references);
        }).ToArray();
        var solvers = groups.Select((at, id) =>
        {
            var rows = BitConverter.ToInt32(Stable(at + layout["GroupRows"], 4, $"solver {id} row count"));
            var count = BitConverter.ToInt32(Stable(at + layout["GroupObjectCount"], 4, $"solver {id} joint count"));
            if (rows is < 0 or > 256 || count is < 0 or > 256)
                throw new GateException("Unsupported solver inventory counts.");
            var members = BitConverter.ToUInt64(Stable(at + layout["GroupObjects"], 8, $"solver {id} joint table pointer"));
            // Native construction places this table immediately after two-byte index pairs.
            var ids = Pointers(members, count, $"solver {id} joints", vectorAlignment: 2).Select(p => objectIds.TryGetValue(p, out var jointId) ? jointId
                : throw new GateException("Solver group references a joint outside object membership.")).ToArray();
            return new InventoriedSolverGroup(id, rows, ids);
        }).ToArray();
        // Keep topology discovery and decoding outside the dynamic state's two read passes.
        var stateTracked = new List<(ulong At, byte[] Bytes, string Role)>(stateRequests.Count);
        foreach (var request in stateRequests)
        {
            var block = read(request.At, request.Size);
            if (block.Length != request.Size) throw new GateException("Incomplete joint state block.");
            stateBlocks.Add(request.Id, block);
            stateTracked.Add((request.At, block, request.Role));
        }
        foreach (var item in stateTracked.Concat(tracked))
        {
            var actual = read(item.At, item.Bytes.Length);
            if (actual.AsSpan().SequenceEqual(item.Bytes)) continue;
            var different = Enumerable.Range(0, Math.Min(actual.Length, item.Bytes.Length))
                .Where(i => actual[i] != item.Bytes[i]).ToArray();
            int? first = different.Length > 0 ? different[0] : null;
            var request = stateRequests.FirstOrDefault(r => r.At == item.At && r.Size == item.Bytes.Length);
            var rowDataOnly = stateLayout is not null && request.At != 0 && actual.Length == item.Bytes.Length &&
                different.All(i => stateLayout.IsRowDataOffset(joints[request.Id].Type, i));
            var change = new ConstraintReadChange(item.Role, item.Bytes.Length, actual.Length, first,
                different.Length + Math.Abs(actual.Length - item.Bytes.Length), rowDataOnly);
            var error = new GateException($"Constraint reread refused: {item.Role}; bytes {actual.Length}/{item.Bytes.Length}; " +
                $"first changed offset {(first is { } offset ? $"0x{offset:X}" : "none (length mismatch)")}; changed bytes {change.ChangedBytes}.");
            error.Data["constraintReadChange"] = change;
            throw error;
        }
        return new(() =>
        {
            var decoded = stateLayout is null ? joints : joints.Select(j =>
            {
                var block = stateBlocks[j.Id];
                if (layout.ModuleRva(BitConverter.ToUInt64(block)) != j.VtableRva)
                    throw new GateException("Joint vtable changed during state read.");
                var offset = stateLayout.BodyOffset(j.Type);
                var references = Enumerable.Range(0, 2).Select(i =>
                {
                    var body = BitConverter.ToUInt64(block, offset + i * 8); Pointer(body, "joint body");
                    return bodyIds.TryGetValue(body, out var bodyId) ? bodyId
                        : throw new GateException("Joint references a body outside linked membership.");
                }).ToArray();
                return j with { BodyIds = references, ObservedState = stateLayout.Decode(j.Type, block) };
            }).ToArray();
            if (stateLayout is not null && (solvers.Any(g => g.RowCount != g.JointIds.Sum(id => decoded[id].ObservedState!.Rows.Length)) ||
                decoded.Any(j => solvers.Count(g => g.JointIds.Contains(j.Id)) != 1)))
                throw new GateException("Solver membership or row count disagrees with supported joint exports.");
            return new(decoded, solvers, bodies.Length);
        }, stateTracked.Concat(tracked).ToArray());
    }
}
