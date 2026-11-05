using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>Current-code proofs for a read-only inventory, never restoration offsets.</summary>
public sealed class ConstraintInventoryLayout
{
    private readonly Dictionary<string, ulong> _fields = [];
    private readonly List<(ulong Rva, byte[] Bytes)> _probes = [];
    private readonly Dictionary<ulong, string> _types = [];
    private readonly (int Start, int End)[] _code;
    public ulong ModuleBase { get; }
    public int ModuleSize { get; }
    public ulong this[string name] => _fields[name];
    public int ProbeCount => _probes.Count;
    public IReadOnlyDictionary<string, ulong> Fields => _fields;
    public string Identify(ulong vtableRva) => _types.GetValueOrDefault(vtableRva, "unresolved");
    internal ulong TypeVtable(string name) => _types.Single(t => t.Value == name).Key;
    private ConstraintInventoryLayout(byte[] image, ulong moduleBase)
    {
        ModuleBase = moduleBase; ModuleSize = image.Length;
        _code = AutomaticOffsets.ExecutableRanges(image).Select(r => (r.Start, r.End)).ToArray();
    }
    internal static JsonObject Anchors()
    {
        using var stream = typeof(ConstraintInventoryLayout).Assembly.GetManifestResourceStream(
            "InactiveReset.Reanchor.constraint-inventory-anchors.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    public ulong ModuleRva(ulong pointer, bool executable = false)
    {
        if (pointer < ModuleBase || pointer - ModuleBase >= (ulong)ModuleSize ||
            (executable && !_code.Any(r => pointer - ModuleBase >= (ulong)r.Start && pointer - ModuleBase < (ulong)r.End)))
            throw new GateException("Constraint inventory vtable/target is outside the verified module section.");
        return pointer - ModuleBase;
    }
    public static ConstraintInventoryLayout Discover(byte[] image, ulong moduleBase)
    {
        var layout = new ConstraintInventoryLayout(image, moduleBase); var anchors = Anchors();
        foreach (var group in new[] { "fields", "contracts" })
            foreach (var item in anchors[group]!.AsObject())
            {
                var found = CodeAnchor.Resolve(image, item.Value!.AsObject(), item.Key);
                layout._probes.Add(((ulong)found.At, image.AsSpan(found.At, found.Length).ToArray()));
                if (group == "fields") layout._fields.Add(item.Key, found.Value);
            }
        if (layout["ObjectsEnd"] != layout["ObjectsBegin"] + 8 ||
            layout["GroupsEnd"] != layout["GroupsBegin"] + 8 ||
            layout["GroupsBegin"] != layout["ObjectsBegin"] + 24 ||
            layout["GroupRows"] != 4 || layout["GroupObjects"] != 0x20 || layout["GroupObjectCount"] != 0x38)
            throw new GateException("Unsupported constraint inventory layout relationships.");
        var table = checked((int)layout["KnownVtableRva"]);
        if (table < 0 || table > image.Length - 0xa8) throw new GateException("Invalid constraint vtable extent.");
        layout._probes.Add(((ulong)table, image.AsSpan(table, 0xa8).ToArray()));
        foreach (var (name, slot) in new[] { ("KnownExportRva", 0x28), ("KnownPreSolveRva", 0xa0) })
        {
            var target = layout.ModuleRva(BitConverter.ToUInt64(image, table + slot), executable: true);
            var pattern = anchors["functions"]![name]!["pattern"]!.GetValue<string>().Split(' ');
            if (target + (ulong)pattern.Length > (ulong)image.Length || pattern.Where((p, i) =>
                p != "??" && image[(int)target + i] != Convert.ToByte(p, 16)).Any())
                throw new GateException("Known joint function code does not match its constructor/vtable proof.");
            layout._fields.Add(name, target);
            layout._probes.Add((target, image.AsSpan((int)target, pattern.Length).ToArray()));
        }
        layout._types.Add(layout["KnownVtableRva"], "IsoBallJoint_0");
        foreach (var type in anchors["types"]!.AsObject())
        {
            var tableProof = CodeAnchor.Resolve(image, type.Value!["VtableRva"]!.AsObject(), "VtableRva");
            var nameProof = CodeAnchor.Resolve(image, type.Value!["NameRva"]!.AsObject(), "NameRva");
            if (tableProof.At != nameProof.At || tableProof.Value + 0xa8 > (ulong)image.Length)
                throw new GateException("Joint type constructor proofs disagree.");
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(type.Key + "\0");
            if (nameProof.Value + (ulong)nameBytes.Length > (ulong)image.Length ||
                !image.AsSpan((int)nameProof.Value, nameBytes.Length).SequenceEqual(nameBytes))
                throw new GateException("Joint constructor type name changed.");
            layout._types.Add(tableProof.Value, type.Key);
            layout._probes.Add(((ulong)tableProof.At, image.AsSpan(tableProof.At, tableProof.Length).ToArray()));
            layout._probes.Add((nameProof.Value, nameBytes));
        }
        return layout;
    }
    public void Verify(Func<ulong, int, byte[]> read)
    {
        foreach (var probe in _probes)
            if (!read(probe.Rva, probe.Bytes.Length).AsSpan().SequenceEqual(probe.Bytes))
                throw new GateException("Constraint inventory code/vtable changed after discovery.");
    }
}
