using System.Globalization;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

public sealed record ObservedConstraintRow(double[] Coefficients, double[] WeightedCoefficients,
    double Input, double Regularization, double Correction, string Lower, string Upper, double InverseDiagonal);
public sealed record ObservedJointState(string RelationshipKind, double[][] LocalRelationships,
    string FlagsHex, Dictionary<string, string> ParametersByOffset, ObservedConstraintRow[] Rows);

/// <summary>Exact current-code-bound observation only. Never invokes a native accessor.</summary>
public sealed class ConstraintJointStateLayout
{
    private readonly Dictionary<string, JsonObject> _types = [];
    private readonly List<(ulong Rva, byte[] Bytes)> _probes = [];
    public int ProbeCount => _probes.Count;
    internal static JsonObject Proofs()
    {
        using var stream = typeof(ConstraintJointStateLayout).Assembly.GetManifestResourceStream(
            "InactiveReset.Reanchor.constraint-state-proofs.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    public static ConstraintJointStateLayout Discover(byte[] image, ConstraintInventoryLayout inventory)
    {
        var result = new ConstraintJointStateLayout();
        foreach (var type in Proofs()["types"]!.AsObject())
        {
            var table = inventory.TypeVtable(type.Key);
            result._probes.Add((table, image.AsSpan(checked((int)table), 0xa8).ToArray()));
            foreach (var function in type.Value!["functions"]!.AsObject())
            {
                var spec = function.Value!;
                var pointer = BitConverter.ToUInt64(image, checked((int)table) + spec["slot"]!.GetValue<int>());
                var rva = inventory.ModuleRva(pointer, executable: true);
                var bytes = Convert.FromHexString(spec["bytes"]!.GetValue<string>());
                if (rva + (ulong)bytes.Length > (ulong)image.Length ||
                    !image.AsSpan((int)rva, bytes.Length).SequenceEqual(bytes))
                    throw new GateException($"Unsupported {type.Key} {function.Key} state code.");
                result._probes.Add((rva, bytes));
            }
            result._types.Add(type.Key, type.Value!.AsObject());
        }
        return result;
    }
    public void Verify(Func<ulong, int, byte[]> read)
    {
        foreach (var p in _probes)
            if (!read(p.Rva, p.Bytes.Length).AsSpan().SequenceEqual(p.Bytes))
                throw new GateException("Constraint state code/vtable changed after discovery.");
    }
    internal int Size(string type) => Spec(type)["size"]!.GetValue<int>();
    internal int BodyOffset(string type) => Spec(type)["bodies"]!.GetValue<int>();
    internal bool IsRowDataOffset(string type, int offset)
    {
        var s = Spec(type);
        for (var row = 0; row < s["rows"]!.GetValue<int>(); row++)
        {
            var stride = row * s["stride"]!.GetValue<int>();
            foreach (var (name, length) in new[] { ("coefficients", s["components"]!.GetValue<int>() * 8),
                ("tuple", 0x30), ("weighted", s["components"]!.GetValue<int>() * 8) })
            {
                var start = s[name]!.GetValue<int>() + stride;
                if (offset >= start && offset < start + length) return true;
            }
        }
        return false;
    }
    private JsonObject Spec(string type) => _types.TryGetValue(type, out var value) ? value
        : throw new GateException($"Constraint state unsupported for joint type {type}.");
    internal ObservedJointState Decode(string type, byte[] bytes)
    {
        var s = Spec(type);
        int I(string key) => s[key]!.GetValue<int>();
        if (bytes.Length != I("size")) throw new GateException("Incomplete joint state block.");
        double Scalar(int at, bool infinity = false)
        {
            var value = BitConverter.ToDouble(bytes, at);
            if (double.IsNaN(value) || (!infinity && !double.IsFinite(value)))
                throw new GateException("Invalid joint state scalar.");
            return value;
        }
        string Number(int at) => Scalar(at, infinity: true).ToString("R", CultureInfo.InvariantCulture);
        double[] Vector(int at, int count) => Enumerable.Range(0, count).Select(i => Scalar(at + i * 8)).ToArray();
        var rows = Enumerable.Range(0, I("rows")).Select(i =>
        {
            var stride = i * I("stride"); var tuple = I("tuple") + stride;
            if (Scalar(tuple + 0x18, true) > Scalar(tuple + 0x20, true))
                throw new GateException("Inverted joint row limits.");
            return new ObservedConstraintRow(Vector(I("coefficients") + stride, I("components")),
                Vector(I("weighted") + stride, I("components")), Scalar(tuple), Scalar(tuple + 8),
                Scalar(tuple + 0x10), Number(tuple + 0x18), Number(tuple + 0x20), Scalar(tuple + 0x28));
        }).ToArray();
        var relationships = s["relationships"]!.AsArray().Select(n => Vector(n!.GetValue<int>(), 3)).ToArray();
        var parameters = s["parameters"]!.AsArray().ToDictionary(n => $"0x{n!.GetValue<int>():x}", n => Number(n!.GetValue<int>()));
        return new(type is "IsoBallJoint_2" or "Bar_2" ? "body-local anchors" : "body-local directions",
            relationships, Convert.ToHexString(bytes.AsSpan(I("flags"), 8)), parameters, rows);
    }
}
