using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public sealed record ObservedTyreTableTarget(string Region, int Index);
public sealed record ObservedTyreModelTables(double[][] StateA, double[][] ContactPrefixes, double[][] StateB,
    ObservedTyreTableTarget[][] Descriptors)
{
    public string ScalarLayout => "Twelve doubles per row, offsets 0x0..0x58 in steps of eight; unresolved physical units. Contact rows are numeric prefixes only.";
}

public sealed class TyreModelTableLayout
{
    private readonly Dictionary<string, int> _values = [];
    private readonly List<(ulong Rva, byte[] Bytes)> _probes = [];
    public int this[string key] => _values[key];
    public int ProbeCount => _probes.Count;
    internal static JsonObject Proofs()
    {
        using var stream = typeof(TyreModelTableLayout).Assembly.GetManifestResourceStream("InactiveReset.Reanchor.tyre-model-table-proofs.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    public static TyreModelTableLayout Discover(byte[] image, MotionLayout motion)
    {
        var proofs = Proofs(); var result = new TyreModelTableLayout();
        foreach (var group in new[] { "fields", "expectedCounts", "bytes" })
            foreach (var field in proofs[group]!.AsObject()) result._values.Add(field.Key, field.Value!.GetValue<int>());
        result._values.Add("prefixDoubleCount", proofs["prefixDoubleCount"]!.GetValue<int>());
        foreach (var function in proofs["functions"]!.AsObject())
        {
            var found = CodeAnchor.Resolve(image, function.Value!.AsObject(), "tyre table " + function.Key);
            var bytes = Convert.FromHexString(function.Value!["bytes"]!.GetValue<string>());
            if (found.At > image.Length - bytes.Length || !image.AsSpan(found.At, bytes.Length).SequenceEqual(bytes))
                throw new GateException("Unsupported tyre table code: " + function.Key);
            result._probes.Add(((ulong)found.At, bytes));
        }
        if ((ulong)result["contactCount"] != motion.TyreCount || (ulong)result["contactPointer"] != motion.TyreRecords ||
            (ulong)result["contactStride"] != motion.TyreRecordStride || result["contactPointer"] != result["contactCount"] + 8 ||
            result["stateAPointer"] != result["stateCount"] + 8 || result["stateBPointer"] != result["stateCount"] + 16 ||
            result["descriptorPointerCount"] != result["stateCount"] + 24 || result["descriptorPointer"] != result["stateCount"] + 32 ||
            result["stateEntries"] != 72 || result["contactRecords"] != 7 * result["stateEntries"] ||
            result["descriptorPointers"] != 9 * result["stateEntries"] || result["descriptorGroupStride"] != 9 * 8 ||
            result["stateAllocation"] != result["stateEntries"] * result["stateStride"] ||
            result["contactAllocation"] != result["contactRecords"] * result["contactStride"] ||
            result["descriptorAllocation"] != result["descriptorPointers"] * 8 ||
            result["prefixDoubleCount"] != 12 || result["stateStride"] != result["prefixDoubleCount"] * 8)
            throw new GateException("Tyre table layout disagrees with the verified counts/strides.");
        return result;
    }
    public void Verify(Func<ulong, int, byte[]> readCode)
    {
        foreach (var p in _probes)
            if (!readCode(p.Rva, p.Bytes.Length).AsSpan().SequenceEqual(p.Bytes))
                throw new GateException("Tyre table code changed after discovery.");
    }
    internal ObservedTyreModelTables Decode(ulong[] pointers, byte[] descriptors, byte[][] buffers)
    {
        var targets = Enumerable.Range(0, this["stateEntries"]).Select(group => Enumerable.Range(0, 9).Select(lane =>
        {
            var region = lane == 0 ? 0 : lane == 8 ? 2 : 1;
            var index = region == 1 ? group * 7 + lane - 1 : group;
            var stride = region == 1 ? this["contactStride"] : this["stateStride"];
            if (BitConverter.ToUInt64(descriptors, (group * 9 + lane) * 8) != pointers[region] + (ulong)(index * stride))
                throw new GateException($"Tyre descriptor target disagrees with local graph: group {group}, lane {lane}.");
            return new ObservedTyreTableTarget(region == 0 ? "stateA" : region == 1 ? "contact" : "stateB", index);
        }).ToArray()).ToArray();
        double[][] Values(byte[] bytes, int count, int stride) => Enumerable.Range(0, count).Select(index =>
            Enumerable.Range(0, this["prefixDoubleCount"]).Select(n =>
            {
                var value = BitConverter.ToDouble(bytes, index * stride + n * 8);
                if (!double.IsFinite(value)) throw new GateException("Nonfinite allocated tyre cell scalar.");
                return value;
            }).ToArray()).ToArray();
        return new(Values(buffers[0], this["stateEntries"], this["stateStride"]),
            Values(buffers[1], this["contactRecords"], this["contactStride"]),
            Values(buffers[2], this["stateEntries"], this["stateStride"]), targets);
    }
}
