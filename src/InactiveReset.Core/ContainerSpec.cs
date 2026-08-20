using System.Text.Json;

namespace InactiveReset.Core;

/// <summary>Which base an offset is measured from.</summary>
public enum OffsetBase
{
    /// <summary>From the slot container base.</summary>
    Container,
    /// <summary>From the vehicle, which is <c>container + 8</c>.</summary>
    Vehicle,
}

/// <summary>
/// The static array of slot containers, and the fields within one.
///
/// <code>
///   container[i] = arrayBase + i * stride
///   vehicle[i]   = container[i] + 8
/// </code>
///
/// The <c>+ 8</c> has caused real errors in this project's history. Always name
/// which base an offset belongs to; never mix them. Searching by OFFSET rather
/// than by absolute address is the reliable way to re-derive these.
/// </summary>
public sealed class ContainerSpec
{
    public required Rva ArrayBase { get; init; }
    public required Rva Pointer { get; init; }
    public required Rva Table { get; init; }
    public required ulong Stride { get; init; }
    public required ulong VehicleDelta { get; init; }
    public required IReadOnlyDictionary<string, FieldOffset> Offsets { get; init; }

    /// <summary>Absolute address of slot container <paramref name="index"/>.</summary>
    public ulong ContainerAddress(ulong moduleBase, int index) =>
        moduleBase + ArrayBase.Require("container array base") + (ulong)index * Stride;

    /// <summary>Absolute address of a named field on a given slot.</summary>
    public ulong FieldAddress(ulong moduleBase, int index, string field)
    {
        if (!Offsets.TryGetValue(field, out var offset))
        {
            throw new OffsetProfileException($"no container field named '{field}'");
        }
        var container = ContainerAddress(moduleBase, index);
        var baseAddress = offset.Base == OffsetBase.Vehicle ? container + VehicleDelta : container;
        return baseAddress + offset.Offset;
    }

    public FieldOffset Field(string name) =>
        Offsets.TryGetValue(name, out var offset)
            ? offset
            : throw new OffsetProfileException($"no container field named '{name}'");

    internal static ContainerSpec FromJson(JsonElement node)
    {
        var offsets = new Dictionary<string, FieldOffset>(StringComparer.Ordinal);
        foreach (var property in OffsetProfile.Require(node, "offsets").EnumerateObject())
        {
            if (property.Name.StartsWith('$'))
            {
                continue;
            }
            offsets[property.Name] = FieldOffset.FromJson(property.Value);
        }

        return new ContainerSpec
        {
            ArrayBase = OffsetProfile.ReadRva(node, "arrayBase"),
            Pointer = OffsetProfile.ReadRva(node, "pointer"),
            Table = OffsetProfile.ReadRva(node, "table"),
            Stride = OffsetProfile.ParseHex(node.GetProperty("stride").GetString()!),
            VehicleDelta = (ulong)node.GetProperty("vehicleDelta").GetInt64(),
            Offsets = offsets,
        };
    }
}

public sealed class FieldOffset
{
    public required ulong Offset { get; init; }
    public required string Type { get; init; }
    public required OffsetBase Base { get; init; }
    public string? Note { get; init; }

    internal static FieldOffset FromJson(JsonElement node) => new()
    {
        Offset = OffsetProfile.ParseHex(node.GetProperty("off").GetString()!),
        Type = node.GetProperty("type").GetString()!,
        Base = node.TryGetProperty("base", out var b) && b.GetString() == "vehicle"
            ? OffsetBase.Vehicle
            : OffsetBase.Container,
        Note = node.TryGetProperty("note", out var n) ? n.GetString() : null,
    };
}
