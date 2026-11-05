using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>Observation-only instruction proofs; never used by a restoration plan.</summary>
public sealed class SuspensionObservationLayout
{
    private readonly Dictionary<string, ulong> _fields = [];
    private readonly List<(ulong Rva, byte[] Bytes)> _probes = [];
    public ulong this[string name] => _fields[name];
    public int ProbeCount => _probes.Count;
    public IReadOnlyDictionary<string, ulong> Fields => _fields;
    internal static JsonObject Anchors()
    {
        using var stream = typeof(SuspensionObservationLayout).Assembly.GetManifestResourceStream(
            "InactiveReset.Reanchor.suspension-observation-anchors.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    public static SuspensionObservationLayout Discover(byte[] mappedImage, MotionLayout motion)
    {
        var result = new SuspensionObservationLayout();
        var anchors = Anchors();
        foreach (var group in new[] { "fields", "contracts" })
            foreach (var item in anchors[group]!.AsObject())
            {
                var found = CodeAnchor.Resolve(mappedImage, item.Value!.AsObject(), item.Key);
                result._probes.Add(((ulong)found.At, mappedImage.AsSpan(found.At, found.Length).ToArray()));
                if (group == "fields") result._fields.Add(item.Key, found.Value);
            }
        if (result["Stride"] != motion.WheelStride || result["Cursor"] < 0x78 ||
            result["Cursor"] + 0x88 > motion.WheelRecordBase + motion.WheelStride ||
            result["Cursor"] - 0x78 < motion.WheelRecordBase ||
            result["Deflection"] != motion.WheelRecordBase + result["DeflectionLocal"] ||
            result["Force"] != motion.WheelRecordBase + result["ForceLocal"] ||
            result["VelocityLocal"] != result["DeflectionLocal"] + 8 ||
            result["ReferenceLengthLocal"] != result["DeflectionLocal"] + 16 ||
            result["PhysicsStep"] != result["PhysicsClock"] + 8)
            throw new GateException("Unsupported suspension observation layout relationships.");
        foreach (var field in new[] { result["Cursor"] + result["DisabledFromCursor"], result["SpinMode"],
            result["Force"], motion.WheelRecordBase + result["ReferenceLengthLocal"] })
            if (field < motion.WheelRecordBase || field + 8 > motion.WheelRecordBase + motion.WheelStride)
                throw new GateException("Suspension observation field is outside its wheel record.");
        return result;
    }

    public void Verify(Func<ulong, int, byte[]> readCode)
    {
        foreach (var probe in _probes)
            if (!readCode(probe.Rva, probe.Bytes.Length).AsSpan().SequenceEqual(probe.Bytes))
                throw new GateException("Suspension observation code changed after discovery.");
    }
}
