using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>Derive tyre inventory/physics fields from engine consumers, never from a previous build's addresses.</summary>
internal static class AutomaticTyreOffsets
{
    internal const int ResolverVersion = 2;
    private static readonly object Gate = new();

    internal static JsonObject Load(GameSession session, string directory)
    {
        lock (Gate)
        {
            return LoadOrDiscover(directory, session.ExecutableSha256,
                () => session.Memory.CaptureMappedModule(session.Process),
                (rva, size) => session.Memory.ReadBytes(session.ModuleBase + rva, size));
        }
    }

    internal static JsonObject LoadOrDiscover(string directory, string hash,
        Func<byte[]> capture, Func<ulong, int, byte[]> read)
    {
        var path = Path.Combine(directory, $"tyre-physics-{hash}.auto.json");
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                if (cached["resolverVersion"]!.GetValue<int>() == ResolverVersion &&
                    string.Equals(cached["executableSha256"]!.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase))
                {
                    RequireProbes(cached, read);
                    RequireCachedValues(cached);
                    return cached;
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or GateException or MemoryAccessException
                or InvalidOperationException or FormatException or NullReferenceException or OverflowException) { }
        }
        var resolved = Discover(capture(), hash);
        RequireProbes(resolved, read);
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, resolved.ToJsonString(new() { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return resolved;
    }

    internal static void RequireProbes(JsonObject profile, Func<ulong, int, byte[]> read)
    {
        if (profile["reset"]?["probes"] is not JsonArray probes || probes.Count != Seeds().Sum(field => field.Value!.AsArray().Count))
            throw new GateException("tyre discovery: incomplete cached code probes");
        foreach (var probe in probes)
        {
            var rva = OffsetProfile.ParseHex(probe!["rva"]!.GetValue<string>());
            var bytes = Convert.FromHexString(probe["bytes"]!.GetValue<string>());
            if (!read(rva, bytes.Length).AsSpan().SequenceEqual(bytes))
                throw new GateException("tyre discovery: engine code changed; cached addresses refused");
        }
    }

    private static JsonObject Seeds()
    {
        using var stream = typeof(AutomaticTyreOffsets).Assembly.GetManifestResourceStream("InactiveReset.Core.discovery-tyres.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static void RequireCachedValues(JsonObject cached)
    {
        var probes = cached["reset"]!["probes"]!.AsArray();
        var values = new Dictionary<string, ulong>();
        var index = 0;
        var size = cached["sizeOfImage"]!.GetValue<int>();
        var ranges = cached["codeRanges"]!.AsArray().Select(r =>
            (Start: r![0]!.GetValue<int>(), End: r[1]!.GetValue<int>())).ToArray();
        foreach (var field in Seeds())
        {
            ulong? found = null;
            foreach (var anchor in field.Value!.AsArray())
            {
                var probe = probes[index++]!;
                var rva = checked((int)OffsetProfile.ParseHex(probe["rva"]!.GetValue<string>()));
                var bytes = Convert.FromHexString(probe["bytes"]!.GetValue<string>());
                var value = ResolveAnchor(bytes, anchor!.AsObject(), field.Key, rva, size).Value;
                if (found is { } previous && previous != value)
                    throw new GateException($"tyre discovery: disagreeing cached {field.Key} consumers");
                found = value;
            }
            values[field.Key] = found!.Value;
        }
        var derived = BuildProfile(values, probes.DeepClone().AsArray(),
            cached["executableSha256"]!.GetValue<string>(), size, ranges);
        if (!JsonNode.DeepEquals(cached, derived))
            throw new GateException("tyre discovery: cached addresses do not match their code operands");
    }

    internal static JsonObject Discover(byte[] image, string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("expected full executable SHA-256");
        var seeds = Seeds();
        var values = new Dictionary<string, ulong>();
        var probes = new JsonArray();
        foreach (var field in seeds)
        {
            ulong? found = null;
            foreach (var anchor in field.Value!.AsArray())
            {
                var (value, at, length) = ResolveAnchor(image, anchor!.AsObject(), field.Key);
                if (found is { } previous && previous != value)
                    throw new GateException($"tyre discovery: disagreeing {field.Key} consumers");
                found = value;
                probes.Add(new JsonObject { ["rva"] = Hex((ulong)at), ["bytes"] = Convert.ToHexString(image.AsSpan(at, length)) });
            }
            values[field.Key] = found ?? throw new GateException($"tyre discovery: missing {field.Key}");
        }
        return BuildProfile(values, probes, hash, image.Length, AutomaticOffsets.ExecutableRanges(image));
    }

    private static JsonObject BuildProfile(Dictionary<string, ulong> values, JsonArray probes, string hash,
        int imageSize, IReadOnlyList<(int Start, int End)> codeRanges)
    {
        ulong V(string name) => values[name];
        if (V("uniformFlagOffset") != V("freshFlagsOffset") + 1 || V("savedWearOffset") != V("heatWearOffset") + 8 ||
            V("surfaceTailOffset") != V("surfaceTemperatureOffset") + 16 ||
            V("surfaceTemperatureOffset") != V("initialTemperatureOffset") + 48 ||
            V("recordStride") < V("savedWearOffset") + 8 || V("recordStride") % 8 != 0 ||
            V("wheelStride") is < 0x100 or > 0x10000)
            throw new GateException("tyre discovery: unsupported tyre record or thermal layout");
        foreach (var name in new[] { "inventoryRva", "selectedIndicesRva", "invulnerabilitySettingRva", "wearMultiplierSettingRva", "damageMultiplierSettingRva" })
            if (V(name) + (name == "inventoryRva" ? 96UL : 16UL) > (ulong)imageSize ||
                codeRanges.Any(r => V(name) >= (ulong)r.Start && V(name) < (ulong)r.End))
                throw new GateException($"tyre discovery: invalid data address for {name}");
        var profile = new JsonObject { ["executableSha256"] = hash, ["resolverVersion"] = ResolverVersion,
            ["sizeOfImage"] = imageSize,
            ["codeRanges"] = new JsonArray(codeRanges.Select(r => (JsonNode?)new JsonArray(r.Start, r.End)).ToArray()) };
        foreach (var name in new[] { "physicsPointerOffset", "invulnerabilityOffset", "invulnerabilitySettingRva", "wearMultiplierSettingRva", "damageMultiplierSettingRva" })
            profile[name] = Hex(V(name));
        var reset = new JsonObject();
        foreach (var name in new[] { "inventoryRva", "selectedIndicesRva", "recordStride", "compoundOffset", "freshFlagsOffset", "heatWearOffset", "wheelStride", "tyrePointerOffset", "thermalNodesCountOffset", "thermalNodesTableOffset", "tyreBulkTemperatureOffset" })
            reset[name] = Hex(V(name));
        // The contiguous cached channel layout is additionally checked against live SDK temperatures before any thermal write.
        var initial = V("initialTemperatureOffset");
        reset["temperatureOffsets"] = new JsonArray(new ulong[] { 8, 24, 32, 40, 48, 56, 64 }.Select(delta => (JsonNode?)JsonValue.Create(Hex(initial + delta))).ToArray());
        reset["probes"] = probes;
        profile["reset"] = reset;
        profile["probeRva"] = probes[0]!["rva"]!.DeepClone();
        profile["probeBytes"] = probes[0]!["bytes"]!.DeepClone();
        return profile;
    }

    internal static (ulong Value, int At, int Length) ResolveAnchor(byte[] image, JsonObject anchor, string name,
        int origin = 0, int? moduleSize = null)
    {
        var pattern = anchor["pattern"]!.GetValue<string>().Split(' ').Select(t => t == "??" ? (byte?)null : Convert.ToByte(t, 16)).ToArray();
        var prefix = pattern.TakeWhile(b => b is not null).Select(b => b!.Value).ToArray();
        if (prefix.Length == 0) throw new GateException("tyre discovery: invalid anchor");
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
            if (match >= 0) throw new GateException($"tyre discovery: ambiguous {name} anchor");
            match = at;
        }
        if (match < 0) throw new GateException($"tyre discovery: cannot resolve {name}; this engine code needs an updated resolver");
        if (anchor["kind"]!.GetValue<string>() == "shape") return (0, match, pattern.Length);
        var operand = match + anchor["operand"]!.GetValue<int>();
        var width = anchor["width"]!.GetValue<int>();
        long value = width == 1 ? image[operand] : BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(operand, 4));
        var isRva = name.EndsWith("Rva", StringComparison.Ordinal);
        if (anchor["kind"]!.GetValue<string>() == "rip") value += origin + match + anchor["next"]!.GetValue<int>();
        if (anchor["baseOperand"] is { } baseOperand && origin + match + anchor["baseNext"]!.GetValue<int>() +
            BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(match + baseOperand.GetValue<int>(), 4)) != 0)
            throw new GateException("tyre discovery: selected indices module-base proof changed");
        if (value <= 0 || value > (isRva ? moduleSize ?? image.Length : 0x50000)) throw new GateException($"tyre discovery: invalid {name} operand");
        return ((ulong)value, match, pattern.Length);
    }
    private static string Hex(ulong value) => $"0x{value:X}";
}
