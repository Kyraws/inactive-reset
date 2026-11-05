using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public sealed class TyreModelObservationLayout
{
    private readonly List<(ulong Rva, byte[] Bytes)> _probes = [];
    private readonly Dictionary<string, int> _fields = [];
    public int this[string name] => _fields[name];
    public int ProbeCount => _probes.Count;
    internal static JsonObject Proofs()
    {
        using var stream = typeof(TyreModelObservationLayout).Assembly.GetManifestResourceStream(
            "InactiveReset.Reanchor.tyre-model-observation-proofs.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    public static TyreModelObservationLayout Discover(byte[] image, MotionLayout motion)
    {
        var result = new TyreModelObservationLayout(); var proofs = Proofs();
        foreach (var field in proofs["fields"]!.AsObject()) result._fields.Add(field.Key, field.Value!.GetValue<int>());
        foreach (var function in proofs["functions"]!.AsObject())
        {
            var found = CodeAnchor.Resolve(image, function.Value!.AsObject(), "tyre model " + function.Key);
            var bytes = Convert.FromHexString(function.Value!["bytes"]!.GetValue<string>());
            if (found.At > image.Length - bytes.Length || !image.AsSpan(found.At, bytes.Length).SequenceEqual(bytes))
                throw new GateException("Unsupported tyre model observation code: " + function.Key);
            result._probes.Add(((ulong)found.At, bytes));
        }
        if ((ulong)result["WheelBase"] != motion.WheelRecordBase || (ulong)result["WheelStride"] != motion.WheelStride ||
            (ulong)(result["WheelBase"] + result["WheelTyreLocal"]) != motion.WheelTyre ||
            (ulong)(result["WheelBase"] + result["WheelSpinLocal"]) != motion.WheelSpin ||
            (ulong)result["EmbeddedBody"] != motion.TyreContactBody ||
            result["Control"] + 4 != result["Slots"] || result["WordCount"] * 2 != result["ScalarStart"] ||
            result["ScalarStart"] + result["ScalarCount"] * 8 != result["SlotStride"] ||
            result["Slots"] + result["SlotCount"] * result["SlotStride"] != (int)motion.TyreCount ||
            result["SlotCount"] != 8 || result["SlotMask"] != 7 || result["SlotShift"] != 16 || result["ControlMask"] != 0x7ffff)
            throw new GateException("Tyre model layout disagrees with the verified motion layout.");
        // The exact model/slot code above uses this embedded-body convention.
        if (motion.BodyPosition != 0x40 || motion.BodyVelocity != 0x28 || motion.BodyMatrix != 0x90 ||
            motion.BodyOmega != 0xe0 || motion.BodyMomentum != 0x110 || motion.BodyInertia != 0x140 || motion.BodyInverseDiagonal != 0xf8)
            throw new GateException("Tyre model embedded-body fields disagree with the verified motion layout.");
        return result;
    }
    public void Verify(Func<ulong, int, byte[]> readCode)
    {
        foreach (var p in _probes)
            if (!readCode(p.Rva, p.Bytes.Length).AsSpan().SequenceEqual(p.Bytes))
                throw new GateException("Tyre model observation code changed after discovery.");
    }
}

public sealed record ObservedTyreModelSlot(int Id, ushort[] Words, Dictionary<string, double> ScalarsByOffset);
public sealed record TyreModelReadChange(string Block, int ExpectedBytes, int ActualBytes, int? FirstChangedOffset, int ChangedBytes);
public sealed record ObservedTyreModelWheel(int WheelId, int SpinBodyId, uint ControlWord, int IndexedSlot,
    ObservedTyreModelSlot[] Slots, ObservedBody EmbeddedBody, double[] Force, double[] Torque)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ObservedTyreModelTables? Tables { get; init; }
}
public sealed record TyreModelObservation(ObservedTyreModelWheel[] Wheels, ObservedUpdateMarkers Markers)
{
    public bool CompleteModelStateAvailable => false;
    public bool TyreContactStateAvailable => false;
    public string Limitation => Wheels.Any(w => w.Tables is not null)
        ? "Bounded embedded-body, inline slots and allocated numeric cell prefixes/local graph only. No complete contact/model history, validated physical units, atomicity proof or restoration payload. Body IDs 0..11 are linked; 12..15 identify embedded bodies by wheel."
        : "Bounded embedded-body and inline slot/control observation only. Slot words/scalars have unresolved units; no allocated contact table, complete model state, atomicity proof or restoration payload. Body IDs 0..11 are linked; 12..15 identify embedded bodies by wheel.";
}

public sealed class TyreModelAcquisition
{
    private readonly Func<TyreModelObservation> _decode;
    private readonly (ulong At, byte[] Bytes, string Role)[] _blocks;
    internal TyreModelAcquisition(Func<TyreModelObservation> decode, (ulong At, byte[] Bytes, string Role)[] blocks)
    { _decode = decode; _blocks = blocks; }
    public TyreModelObservation Decode() => _decode();
    public void RequireSame(TyreModelAcquisition other)
    {
        if (_blocks.Length != other._blocks.Length) throw new GateException("Tyre model topology changed during pause.");
        for (var i = 0; i < _blocks.Length; i++)
            if (_blocks[i].At != other._blocks[i].At || _blocks[i].Role != other._blocks[i].Role ||
                !_blocks[i].Bytes.AsSpan().SequenceEqual(other._blocks[i].Bytes))
                throw new GateException("Tyre model block changed during pause: " + _blocks[i].Role);
    }
    public static TyreModelAcquisition Acquire(Func<ulong, int, byte[]> read, ulong vehicle,
        MotionLayout m, SuspensionObservationLayout suspension, TyreModelObservationLayout layout, TyreModelTableLayout? tables = null)
    {
        var topology = new List<(ulong At, byte[] Bytes, string Role)>();
        var state = new List<(ulong At, byte[] Bytes, string Role)>();
        static void Pointer(ulong at)
        {
            if (at < 0x10000 || at % 8 != 0 || at > 0x00007ffffffe0000)
                throw new GateException("Invalid tyre model observation pointer.");
        }
        byte[] Bytes(ulong at, int count)
        {
            var bytes = read(at, count);
            if (bytes.Length != count) throw new GateException("Incomplete tyre model observation read.");
            return bytes;
        }
        byte[] Stable(ulong at, int count, string role)
        { var bytes = Bytes(at, count); topology.Add((at, bytes, role)); return bytes; }
        var physics = BitConverter.ToUInt64(Stable(vehicle + m.VehiclePhysics, 8, "physics pointer")); Pointer(physics);
        if (BitConverter.ToInt32(Stable(vehicle + suspension["Mode"], 4, "telemetry mode")) != 0)
            throw new GateException("Tyre model observation requires physics-backed telemetry.");
        var clock = Stable(physics + suspension["PhysicsClock"], 16, "physics clock/step");
        var vehicleClock = Stable(vehicle + suspension["VehicleClock"], 8, "vehicle clock");
        var counter = Stable(vehicle + suspension["CompletionCounter"], 1, "completion counter");
        var begin = BitConverter.ToUInt64(Stable(physics + m.PhysicsBodiesBegin, 8, "linked bodies begin")); Pointer(begin);
        var end = BitConverter.ToUInt64(Stable(physics + m.PhysicsBodiesEnd, 8, "linked bodies end"));
        if (end < begin || end - begin != 12 * 8) throw new GateException("Tyre model observation requires twelve linked bodies.");
        var members = Stable(begin, 12 * 8, "linked membership");
        var bodies = Enumerable.Range(0, 12).Select(i => BitConverter.ToUInt64(members, i * 8)).ToArray();
        foreach (var body in bodies) Pointer(body);
        if (bodies.Distinct().Count() != 12) throw new GateException("Duplicate tyre model linked membership.");
        var tyres = new ulong[4]; var spins = new int[4];
        for (var i = 0; i < 4; i++)
        {
            var wheel = physics + (ulong)i * m.WheelStride;
            tyres[i] = BitConverter.ToUInt64(Stable(wheel + m.WheelTyre, 8, $"wheel {i} tyre pointer")); Pointer(tyres[i]);
            var spin = BitConverter.ToUInt64(Stable(wheel + m.WheelSpin, 8, $"wheel {i} spin pointer")); Pointer(spin);
            spins[i] = Array.IndexOf(bodies, spin);
            if (spins[i] < 0) throw new GateException("Tyre model wheel references a foreign spin body.");
        }
        if (tyres.Distinct().Count() != 4 || spins.Distinct().Count() != 4)
            throw new GateException("Tyre model wheel membership overlaps.");
        var bodyEnd = new[] { m.BodyPosition + 24, m.BodyVelocity + 24, m.BodyMatrix + 72,
            m.BodyOmega + 24, m.BodyMomentum + 24, m.BodyInertia + 24, m.BodyInverseDiagonal + 24, 0x140UL }.Max();
        if (bodyEnd > 4096 || new[] { m.BodyPosition, m.BodyVelocity, m.BodyMatrix, m.BodyOmega,
                m.BodyMomentum, m.BodyInertia, m.BodyInverseDiagonal }.Any(offset => offset < 0x10))
            throw new GateException("Unsupported tyre embedded-body observation extent.");
        var modelEnd = (ulong)layout["EmbeddedBody"] + bodyEnd;
        for (var i = 0; i < 4; i++)
            if (bodies.Any(b => b < tyres[i] + modelEnd && b + bodyEnd > tyres[i]) ||
                tyres.Where((_, j) => j != i).Any(t => t < tyres[i] + modelEnd && t + modelEnd > tyres[i]))
                throw new GateException("Tyre model allocations overlap captured membership.");
        var tablePointers = new ulong[4][]; var descriptors = new byte[4][]; var tableBuffers = new byte[4][][];
        if (tables is not null)
        {
            var ranges = new List<(ulong At, int Count)>();
            for (var i = 0; i < 4; i++)
            {
                var contact = Stable(tyres[i] + (ulong)tables["contactCount"], 16, $"wheel {i} contact header");
                var header = Stable(tyres[i] + (ulong)tables["stateCount"], 40, $"wheel {i} table header");
                if (BitConverter.ToInt32(contact) != tables["contactRecords"] || BitConverter.ToInt32(header) != tables["stateEntries"] ||
                    BitConverter.ToInt32(header, 24) != tables["descriptorPointers"])
                    throw new GateException("Tyre table allocation counts disagree with the verified layout.");
                tablePointers[i] = [BitConverter.ToUInt64(header, 8), BitConverter.ToUInt64(contact, 8),
                    BitConverter.ToUInt64(header, 16), BitConverter.ToUInt64(header, 32)];
                var sizes = new[] { tables["stateAllocation"], tables["contactAllocation"], tables["stateAllocation"], tables["descriptorAllocation"] };
                for (var region = 0; region < 4; region++)
                {
                    var at = tablePointers[i][region]; var count = sizes[region]; Pointer(at);
                    if (at > 0x00007fffffff0000UL - (ulong)count) throw new GateException("Tyre table allocation exceeds supported address range.");
                    ranges.Add((at, count));
                }
            }
            foreach (var range in ranges)
            {
                bool Overlaps(ulong at, ulong count) => range.At < at + count && at < range.At + (ulong)range.Count;
                if (ranges.Count(r => Overlaps(r.At, (ulong)r.Count)) != 1 ||
                    topology.Any(t => Overlaps(t.At, (ulong)t.Bytes.Length)) ||
                    bodies.Any(b => Overlaps(b, bodyEnd)) ||
                    tyres.Any(t => Overlaps(t, Math.Max(modelEnd, (ulong)tables["modelPointer"] + 8))))
                    throw new GateException("Tyre table allocations overlap observed state or membership.");
            }
            for (var i = 0; i < 4; i++)
                descriptors[i] = Stable(tablePointers[i][3], tables["descriptorAllocation"], $"wheel {i} descriptor graph");
        }
        var headers = new byte[4][]; var embedded = new byte[4][];
        for (var i = 0; i < 4; i++)
        {
            var at = tyres[i] + (ulong)layout["Control"];
            headers[i] = Bytes(at, layout["Slots"] + layout["SlotCount"] * layout["SlotStride"] - layout["Control"]);
            state.Add((at, headers[i], $"wheel {i} inline control/slots"));
            at = tyres[i] + (ulong)layout["EmbeddedBody"] + 0x10;
            embedded[i] = Bytes(at, checked((int)bodyEnd - 0x10));
            state.Add((at, embedded[i], $"wheel {i} embedded body"));
            if (tables is not null)
            {
                tableBuffers[i] = new byte[3][];
                for (var region = 0; region < 3; region++)
                {
                    at = tablePointers[i][region]; var size = region == 1 ? tables["contactAllocation"] : tables["stateAllocation"];
                    tableBuffers[i][region] = Bytes(at, size);
                    state.Add((at, tableBuffers[i][region], $"wheel {i} allocated region {region}"));
                }
            }
        }
        var blocks = state.Concat(topology).ToArray();
        foreach (var block in blocks)
        {
            var actual = Bytes(block.At, block.Bytes.Length);
            if (actual.AsSpan().SequenceEqual(block.Bytes)) continue;
            var offsets = Enumerable.Range(0, Math.Min(actual.Length, block.Bytes.Length)).Where(i => actual[i] != block.Bytes[i]).ToArray();
            var error = new GateException("Tyre model reread refused: " + block.Role);
            error.Data["tyreModelReadChange"] = new TyreModelReadChange(block.Role, block.Bytes.Length, actual.Length,
                offsets.Length == 0 ? null : offsets[0], offsets.Length + Math.Abs(actual.Length - block.Bytes.Length));
            throw error;
        }
        return new(() =>
        {
            double Scalar(byte[] bytes, int at)
            {
                var value = BitConverter.ToDouble(bytes, at);
                if (!double.IsFinite(value)) throw new GateException("Nonfinite tyre model observation scalar.");
                return value;
            }
            double[] Vector(byte[] bytes, ulong offset, int count) => Enumerable.Range(0, count)
                .Select(i => Scalar(bytes, checked((int)offset - 0x10 + i * 8))).ToArray();
            var markers = new ObservedUpdateMarkers(Scalar(clock, 0), Scalar(clock, 8), Scalar(vehicleClock, 0), counter[0]);
            if (markers.PhysicsStep <= 0) throw new GateException("Invalid tyre model timestep.");
            var wheels = Enumerable.Range(0, 4).Select(i =>
            {
                var header = headers[i]; var block = embedded[i]; var control = BitConverter.ToUInt32(header);
                var slots = Enumerable.Range(0, layout["SlotCount"]).Select(id =>
                {
                    var at = layout["Slots"] - layout["Control"] + id * layout["SlotStride"];
                    return new ObservedTyreModelSlot(id,
                        Enumerable.Range(0, layout["WordCount"]).Select(n => BitConverter.ToUInt16(header, at + n * 2)).ToArray(),
                        Enumerable.Range(0, layout["ScalarCount"]).ToDictionary(n => $"0x{layout["ScalarStart"] + n * 8:X}",
                            n => Scalar(header, at + layout["ScalarStart"] + n * 8)));
                }).ToArray();
                var matrix = Vector(block, m.BodyMatrix, 9); DirectPosePlan.RequireRotation(matrix);
                var inertia = Vector(block, m.BodyInertia, 3); var inverse = Vector(block, m.BodyInverseDiagonal, 3);
                if (inertia.Any(n => n <= 0) || inverse.Any(n => n <= 0)) throw new GateException("Invalid tyre embedded-body inertia.");
                if (tables is not null && slots.Any(slot => slot.Words.Take(9).Any(index => index >= tables["stateEntries"])))
                    throw new GateException("Tyre slot index is outside the verified descriptor groups.");
                return new ObservedTyreModelWheel(i, spins[i], control, (int)((control >> layout["SlotShift"]) & (uint)layout["SlotMask"]),
                    slots, new ObservedBody(12 + i, Vector(block, m.BodyPosition, 3), matrix, Vector(block, m.BodyVelocity, 3),
                        Vector(block, m.BodyOmega, 3), Vector(block, m.BodyMomentum, 3), inertia, inverse),
                    Vector(block, 0x10, 3), Vector(block, 0x128, 3))
                    { Tables = tables?.Decode(tablePointers[i], descriptors[i], tableBuffers[i]) };
            }).ToArray();
            return new TyreModelObservation(wheels, markers);
        }, blocks);
    }
}
