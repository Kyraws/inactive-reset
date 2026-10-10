using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>Derive a build's addresses locally from its decrypted mapped code.</summary>
public static class AutomaticOffsets
{
    public const int ResolverVersion = 4;
    private static readonly object CacheGate = new();
    private static (string Hash, string Version, string Code, string Json)? _verified;

    public static OffsetProfile LoadOrDiscover(string directory, string hash, string version,
                                               Func<byte[]> capture, Func<ulong, int, byte[]> readProbe,
                                               out bool discovered)
        => LoadOrDiscover(directory, hash, version, capture, readProbe, out discovered, null);

    internal static OffsetProfile LoadOrDiscover(string directory, string hash, string version,
        Func<byte[]> capture, Func<ulong, int, byte[]> readProbe, out bool discovered,
        Func<byte[], string, string, JsonObject>? derive)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("expected a full executable SHA-256", nameof(hash));
        var image = capture();
        string verifiedJson;
        using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            digest.AppendData(BitConverter.GetBytes(image.Length));
            digest.AppendData(image.AsSpan(0, Math.Min(image.Length, 0x1000)));
            foreach (var range in ExecutableRanges(image))
                digest.AppendData(image.AsSpan(range.Start, range.End - range.Start));
            var code = Convert.ToHexString(digest.GetHashAndReset());
            lock (CacheGate)
            {
                if (derive is not null) verifiedJson = derive(image, hash, version).ToJsonString();
                else if (_verified is not { } prior || prior.Hash != hash || prior.Version != version || prior.Code != code)
                {
                    verifiedJson = Discover(image, hash, version).ToJsonString();
                    _verified = (hash, version, code, verifiedJson);
                }
                else verifiedJson = prior.Json;
            }
        }
        var path = Path.Combine(directory, $"{hash}.auto.json");
        if (File.Exists(path))
        {
            try
            {
                var cachedJson = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                    { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (cachedJson is not JsonObject) throw new OffsetProfileException("cached profile must be an object");
                var cached = OffsetProfile.Parse(cachedJson.ToJsonString(), path);
                if (JsonNode.DeepEquals(cachedJson, JsonNode.Parse(verifiedJson)) &&
                    readProbe(cached.Probe.Rva.Require("cached probe"), cached.Probe.Bytes.Length).AsSpan()
                         .SequenceEqual(cached.Probe.Bytes))
                {
                    discovered = false;
                    return cached;
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or OffsetProfileException
                or StaleOffsetException or ArgumentException or OverflowException or MemoryAccessException
                or KeyNotFoundException or InvalidOperationException or FormatException) { }
        }

        var json = JsonNode.Parse(verifiedJson)!.AsObject();
        var profile = OffsetProfile.Parse(json.ToJsonString(), path);
        if (!readProbe(profile.Probe.Rva.Require("discovered probe"), profile.Probe.Bytes.Length).AsSpan()
            .SequenceEqual(profile.Probe.Bytes))
            throw new GateException("automatic offset discovery: live probe changed during capture");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json.ToJsonString(new() { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        discovered = true;
        return profile;
    }

    public static JsonObject Discover(byte[] image, string hash, string version)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("expected a full executable SHA-256", nameof(hash));
        var indices = Need(SpotTableResolver.FindSpecialSlotIndices(image), "slot/pit/garage indices");
        var stride = Need(SpotTableResolver.FindContainerStride(image), "container stride");
        var array = Need(SpotTableResolver.FindContainerArrayBase(image), "container array");
        var garage = Need(SpotTableResolver.FindGaragePos(image), "garage table");
        var pit = Need(SpotTableResolver.FindPitPos(image), "pit table consumer");
        var owner = Need(SpotTableResolver.FindControlOwner(image), "control owner");
        var dimensions = Need(SpotTableResolver.FindVehicleDimensions(image), "vehicle dimensions");
        var sign = Need(SpotTableResolver.FindLateralSign(image), "lateral sign");
        var state = Need(SpotTableResolver.FindPitState(image), "pit state");
        var rules = Need(SpotTableResolver.FindPitSpeedRule(image), "Flag Rules");
        var flags = Need(SpotTableResolver.FindTrackLimitsFlags(image), "track limits flags");
        var result = Resource("discovery-template.json");
        var tunables = result["engineModel"]!["tunables"]!.AsObject();
        var tuningStart = SpotTableResolver.FindTuningBlock(image)
            ?? throw new GateException("automatic offset discovery: cannot uniquely resolve engine tuning consumers");
        Address(tunables, "searchStartFactor", tuningStart);
        Address(tunables, "searchStepFactor", tuningStart + 4);
        Address(tunables, "searchMaxFactor", tuningStart + 8);
        Address(tunables, "yawOffsetDegrees", tuningStart + 16);
        var build = result["build"]!.AsObject();
        build["executableSha256"] = hash;
        build["fileVersion"] = version;
        build["sizeOfImage"] = Hex((ulong)image.Length);
        build["automaticResolverVersion"] = ResolverVersion;
        // Structural resolution is recorded separately from a maintainer's live experiment.
        build["placementValidated"] = false;
        var probe = result["probe"]!.AsObject();
        probe["rva"] = Hex(indices.TransformRva);
        probe["confidence"] = "E";
        probe["bytes"] = string.Join(" ", image.AsSpan((int)indices.TransformRva, 16).ToArray().Select(b => b.ToString("X2")));
        probe["stablePrefixLength"] = 12;
        var spot = result["spotTable"]!.AsObject();
        Address(spot, "pitPosTable", pit.PitPosRva);
        Address(spot, "garPosTable", garage.GaragePosRva);
        Address(spot, "mult", garage.MultRva);
        Address(spot, "count", garage.CountRva);
        var indexed = SpotTableResolver.FindIndexedDestination(image);
        if (pit.ReaderPath == SpotTableResolver.PitPosReaderPath.SpecialSlotBranch)
        {
            indexed = Need(indexed, "indexed destination table");
            var mode = Need(SpotTableResolver.FindDestinationMode(image), "destination mode");
            Address(spot, "indexedDestination", indexed.TableRva);
            spot["destinationModeOffset"] = Hex(mode.Offset);
        }
        var containers = result["containers"]!.AsObject();
        Address(containers, "arrayBase", array.ArrayBaseRva);
        containers["stride"] = Hex(stride.Stride);
        var fields = containers["offsets"]!.AsObject();
        Field(fields, "slotIndex", indices.SlotIndex);
        Field(fields, "pitIndex", indices.PitIndex);
        Field(fields, "garageIndex", indices.GarageIndex);
        Field(fields, "controlOwner", owner.Offset);
        Field(fields, "vehicleLength", dimensions.LengthOffset);
        Field(fields, "vehicleWidth", dimensions.WidthOffset);
        Field(fields, "lateralSign", sign.Offset);
        Field(fields, "pitState", state.StateOffset);
        Field(fields, "pitFlag", state.PitFlagOffset);
        foreach (var seed in Resource("discovery-seeds.json"))
            Field(fields, seed.Key, ResolveField(image, seed.Value!.AsObject(), seed.Key));
        // These members form the engine's lap promotion block: int, byte latch,
        // then double start time. The base is independently resolved above.
        var lap = OffsetProfile.ParseHex(fields["countLapFlag"]!["off"]!.GetValue<string>());
        Field(fields, "lapCountsNext", lap + 4);
        Field(fields, "lapStartEt", lap + 8);
        var ruleNodes = result["rules"]!.AsObject();
        Address(ruleNodes, "flagRules", rules.FlagRulesRva);
        ruleNodes["flagRules"]!["readBy"] = Hex(rules.SpeedGateRva);
        var limits = ruleNodes["trackLimits"]!["derivedFlags"]!.AsArray();
        for (var i = 0; i < limits.Count; i++)
        {
            limits[i]!["rva"] = Hex(flags.FirstRva + (uint)i);
            limits[i]!["confidence"] = "E";
        }
        return result;
    }

    public static uint ResolveField(byte[] image, JsonObject seed, string name)
    {
        uint? found = null;
        var sites = new HashSet<int>();
        var ranges = ExecutableRanges(image);
        foreach (var anchor in seed["anchors"]!.AsArray())
        {
            var tokens = anchor!["pattern"]!.GetValue<string>().Split(' ');
            var bytes = tokens.Select(t => t == "??" ? (byte?)null : Convert.ToByte(t, 16)).ToArray();
            var prefix = bytes.TakeWhile(b => b is not null).Select(b => b!.Value).ToArray();
            var operand = anchor["operand"]!.GetValue<int>();
            var match = -1;
            foreach (var range in ranges)
            for (var at = range.Start; at <= range.End - bytes.Length; at++)
            {
                var next = image.AsSpan(at, range.End - at).IndexOf(prefix);
                if (next < 0) break;
                at += next;
                if (at > range.End - bytes.Length) break;
                var agrees = true;
                for (var b = 1; b < bytes.Length && agrees; b++)
                    agrees = bytes[b] is null || image[at + b] == bytes[b];
                if (!agrees) continue;
                if (match >= 0) throw new GateException($"automatic offset discovery: ambiguous {name} anchor");
                match = at;
            }
            if (match < 0) continue;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(match + operand, 4));
            if (value is < 0x20 or > 0x50000 || (found is not null && found != value))
                throw new GateException($"automatic offset discovery: disagreeing {name} readers");
            found = value;
            sites.Add(match);
        }
        if (found is null || sites.Count < (name == "restOffsetSecondary" ? 1 : 2))
            throw new GateException($"automatic offset discovery: cannot resolve {name}");
        return found.Value;
    }

    private static T Need<T>(T? value, string field) where T : class =>
        value ?? throw new GateException($"automatic offset discovery: cannot uniquely resolve {field}");
    internal static IReadOnlyList<(int Start, int End)> ExecutableRanges(byte[] image)
    {
        // Raw byte buffers are useful for testing the masked matcher itself.
        if (image.Length < 0x1000 || image[0] != 'M' || image[1] != 'Z')
            return [(0, image.Length)];
        var pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C, 4));
        if (pe < 0x40 || pe > image.Length - 24)
            throw new GateException("automatic offset discovery: invalid PE header");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 6, 2));
        var optional = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 20, 2));
        var table = pe + 24 + optional;
        if (count is < 1 or > 96 || table > image.Length - count * 40)
            throw new GateException("automatic offset discovery: invalid PE sections");
        var result = new List<(int, int)>();
        for (var i = 0; i < count; i++)
        {
            var at = table + i * 40;
            if ((BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 36, 4)) & 0x20000000) == 0) continue;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 8, 4));
            var start = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 12, 4));
            if ((ulong)start + size > (ulong)image.Length)
                throw new GateException("automatic offset discovery: executable section outside image");
            result.Add(((int)start, (int)(start + size)));
        }
        return result;
    }
    private static string Hex(ulong value) => $"0x{value:X}";
    private static void Address(JsonObject parent, string name, ulong value)
    {
        parent[name] ??= new JsonObject();
        parent[name]!["rva"] = Hex(value);
        parent[name]!["confidence"] = "E";
    }
    private static void Field(JsonObject fields, string name, ulong value) =>
        fields[name]!["off"] = Hex(value);
    private static JsonObject Resource(string name)
    {
        using var stream = typeof(AutomaticOffsets).Assembly.GetManifestResourceStream($"InactiveReset.Core.{name}")
            ?? throw new InvalidOperationException($"missing offset discovery resource {name}");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
