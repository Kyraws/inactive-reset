using System.Buffers.Binary;
using System.Text.Json.Nodes;
namespace InactiveReset.Core;

internal static class CodeAnchor
{
    internal static (ulong Value, int At, int Length) Resolve(byte[] image, JsonObject anchor, string name,
        int origin = 0, int? moduleSize = null)
    {
        var pattern = anchor["pattern"]!.GetValue<string>().Split(' ').Select(t => t == "??" ? (byte?)null : Convert.ToByte(t, 16)).ToArray();
        var prefix = pattern.TakeWhile(b => b is not null).Select(b => b!.Value).ToArray();
        if (prefix.Length == 0) throw new GateException("offset discovery: invalid anchor");
        var match = -1;
        foreach (var range in AutomaticOffsets.ExecutableRanges(image))
        for (var at = range.Start; at <= range.End - pattern.Length; at++)
        {
            var next = image.AsSpan(at, range.End - at).IndexOf(prefix);
            if (next < 0) break;
            at += next;
            if (at > range.End - pattern.Length) break;
            var agrees = true;
            for (var i = 0; i < pattern.Length && agrees; i++) agrees = pattern[i] is null || pattern[i] == image[at + i];
            if (!agrees) continue;
            if (match >= 0) throw new GateException($"offset discovery: ambiguous {name} anchor");
            match = at;
        }
        if (match < 0) throw new GateException($"offset discovery: cannot resolve {name}; this engine code needs an updated resolver");
        if (anchor["kind"]!.GetValue<string>() == "shape") return (0, match, pattern.Length);
        var operand = match + anchor["operand"]!.GetValue<int>();
        var width = anchor["width"]!.GetValue<int>();
        long value = width == 1 ? (anchor["signed"]?.GetValue<bool>() == true ? (sbyte)image[operand] : image[operand])
            : BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(operand, 4));
        var isRva = name.EndsWith("Rva", StringComparison.Ordinal);
        if (anchor["kind"]!.GetValue<string>() == "rip") value += origin + match + anchor["next"]!.GetValue<int>();
        if (anchor["baseOperand"] is { } baseOperand && origin + match + anchor["baseNext"]!.GetValue<int>() +
            BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(match + baseOperand.GetValue<int>(), 4)) != 0)
            throw new GateException("offset discovery: selected indices module-base proof changed");
        if (value <= 0 || value > (isRva ? moduleSize ?? image.Length : 0x50000)) throw new GateException($"offset discovery: invalid {name} operand");
        return ((ulong)value, match, pattern.Length);
    }
}
