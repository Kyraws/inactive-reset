using System.Globalization;
using System.Text.Json;

namespace InactiveReset.Core;

/// <summary>
/// How confident we are in an address. Anything <see cref="Unresolved"/> is a
/// value carried from a previous build that was never re-derived, i.e. wrong.
/// </summary>
public enum Confidence
{
    /// <summary>Multiple concurring references, or direct proof.</summary>
    Established,
    /// <summary>A single reference, or inferred by adjacency.</summary>
    Inferred,
    /// <summary>Not re-derived for this build. STALE. Do not use.</summary>
    Unresolved,
}

/// <summary>An address plus how much we trust it.</summary>
public sealed record Rva(ulong Value, Confidence Confidence, string? Note = null)
{
    public override string ToString() => $"0x{Value:X8}";

    /// <summary>
    /// Throws unless this address was actually re-derived for the loaded build.
    /// Call before any read or write that matters -- a stale RVA does not fail
    /// loudly, it lands on plausible unrelated bytes and reports nonsense.
    /// </summary>
    public ulong Require(string what)
    {
        if (Confidence == Confidence.Unresolved)
        {
            throw new StaleOffsetException(
                $"{what} is not re-derived for this build (RVA {this}). {Note}");
        }
        return Value;
    }
}

public sealed class StaleOffsetException(string message) : Exception(message);

public sealed class OffsetProfileException(string message) : Exception(message);

/// <summary>
/// Everything address-shaped for one exact LMU build, loaded from JSON.
///
/// This type exists so that an LMU patch is a DATA change, not a code change.
/// These were once compiled-in constants, and every patch day cost hours of
/// re-derivation followed by a rebuild. Keep it that way: if you find yourself
/// about to write a hex literal in C#, it belongs here instead.
/// </summary>
public sealed class OffsetProfile
{
    public required string ExecutableSha256 { get; init; }
    public required string FileVersion { get; init; }
    public required ulong SizeOfImage { get; init; }
    public required bool PlacementValidated { get; init; }
    public int AutomaticResolverVersion { get; init; }

    public required ProbeSpec Probe { get; init; }
    public required SpotTableSpec SpotTable { get; init; }
    public required ContainerSpec Containers { get; init; }
    public required RulesSpec Rules { get; init; }
    public required EngineModelSpec EngineModel { get; init; }
    public required RestGateSpec RestGates { get; init; }

    public void RequirePlacementValidated()
    {
        if (!PlacementValidated && AutomaticResolverVersion != AutomaticOffsets.ResolverVersion)
            throw new GateException(
                "this build has candidate offsets but has not passed matching-car placement, control, rules, and exact-byte restore validation");
    }

    /// <summary>Where this profile came from, for diagnostics.</summary>
    public string? SourcePath { get; init; }

    public static OffsetProfile Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        return FromJson(document.RootElement, path);
    }

    /// <summary>
    /// Find the profile matching a running build. Profiles are named by the
    /// first 8 hex digits of the executable hash, but the full hash inside is
    /// what is actually checked -- a filename is a hint, not evidence.
    /// </summary>
    public static OffsetProfile ForBuild(string directory, string executableSha256)
    {
        if (!Directory.Exists(directory))
        {
            throw new OffsetProfileException($"offset directory not found: {directory}");
        }

        var candidates = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var profile = Load(file);
                if (string.Equals(profile.ExecutableSha256, executableSha256,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
                candidates.Add($"  {Path.GetFileName(file)} -> {profile.ExecutableSha256[..8]}");
            }
            catch (Exception ex)
            {
                candidates.Add($"  {Path.GetFileName(file)} -> unreadable: {ex.Message}");
            }
        }

        throw new OffsetProfileException(
            $"no offset profile for build {executableSha256[..8]}. LMU has probably been " +
            $"patched; run `reanchor` to derive one.\nProfiles present:\n" +
            (candidates.Count == 0 ? "  (none)" : string.Join('\n', candidates)));
    }

    // ---- parsing -----------------------------------------------------------

    public static OffsetProfile Parse(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        return FromJson(document.RootElement, path);
    }

    private static OffsetProfile FromJson(JsonElement root, string path)
    {
        var build = Require(root, "build");
        var probe = Require(root, "probe");
        var spot = Require(root, "spotTable");
        var containers = Require(root, "containers");
        var rules = Require(root, "rules");
        var engine = Require(root, "engineModel");
        var rest = Require(root, "restGates");

        return new OffsetProfile
        {
            SourcePath = path,
            AutomaticResolverVersion = build.TryGetProperty("automaticResolverVersion", out var resolver)
                ? resolver.GetInt32() : 0,
            ExecutableSha256 = build.GetProperty("executableSha256").GetString()!,
            FileVersion = build.GetProperty("fileVersion").GetString()!,
            SizeOfImage = ParseHex(build.GetProperty("sizeOfImage").GetString()!),
            PlacementValidated = build.TryGetProperty("placementValidated", out var validated) &&
                                 validated.ValueKind == JsonValueKind.True,

            Probe = new ProbeSpec
            {
                // The confidence sits on the probe object itself, beside "rva",
                // which is the shape reanchor writes. Read it from there rather
                // than defaulting to Established -- a probe reanchor could not
                // re-derive must not present as a verified one.
                Rva = new Rva(
                    ParseHex(probe.GetProperty("rva").GetString()!),
                    ParseConfidence(probe),
                    probe.TryGetProperty("note", out var probeNote) ? probeNote.GetString() : null),
                Bytes = ParseByteString(probe.GetProperty("bytes").GetString()!),
                StablePrefixLength = probe.GetProperty("stablePrefixLength").GetInt32(),
            },

            SpotTable = new SpotTableSpec
            {
                PitPosTable = ReadRva(spot, "pitPosTable"),
                GarPosTable = ReadRva(spot, "garPosTable"),
                Mult = ReadRva(spot, "mult"),
                Count = ReadRva(spot, "count"),
                EntryBytes = spot.GetProperty("entryBytes").GetInt32(),
                WriteBytes = spot.GetProperty("writeBytes").GetInt32(),
                IndexedDestination = spot.TryGetProperty("indexedDestination", out _) ? ReadRva(spot, "indexedDestination") : null,
                DestinationModeOffset = spot.TryGetProperty("destinationModeOffset", out var mode) ? ParseHex(mode.GetString()!) : null,
            },

            Containers = ContainerSpec.FromJson(containers),
            Rules = RulesSpec.FromJson(rules),

            EngineModel = ReadEngineModel(engine),

            RestGates = new RestGateSpec
            {
                BaseUrl = rest.GetProperty("baseUrl").GetString()!,
                Endpoints = rest.GetProperty("endpoints").EnumerateArray()
                                .Select(e => e.GetString()!).ToArray(),
                SessionsRead = rest.GetProperty("sessionsRead").GetString()!,
            },
        };
    }

    private static EngineModelSpec ReadEngineModel(JsonElement engine)
    {
        var tunables = Require(engine, "tunables");
        var fallback = Require(engine, "fallback");
        var ranges = Require(engine, "ranges");

        return new EngineModelSpec
        {
            YawOffsetDegrees = ReadRva(tunables, "yawOffsetDegrees"),
            SearchStartFactorRva = ReadRva(tunables, "searchStartFactor"),
            SearchStepFactorRva = ReadRva(tunables, "searchStepFactor"),
            SearchMaxFactorRva = ReadRva(tunables, "searchMaxFactor"),

            FallbackYawOffsetRadians = fallback.GetProperty("yawOffsetMode2").GetSingle(),
            FallbackSearchStartFactor = fallback.GetProperty("searchStartFactor").GetSingle(),
            FallbackSearchStepFactor = fallback.GetProperty("searchStepFactor").GetSingle(),
            FallbackSearchMaxFactor = fallback.GetProperty("searchMaxFactor").GetSingle(),

            YawOffsetDegreesMin = ranges.GetProperty("yawOffsetDegreesMin").GetSingle(),
            YawOffsetDegreesMax = ranges.GetProperty("yawOffsetDegreesMax").GetSingle(),
            SearchFactorMin = ranges.GetProperty("searchFactorMin").GetSingle(),
            SearchFactorMax = ranges.GetProperty("searchFactorMax").GetSingle(),
        };
    }

    internal static JsonElement Require(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
            ? value
            : throw new OffsetProfileException($"offset profile is missing '{name}'");

    internal static Rva ReadRva(JsonElement parent, string name)
    {
        var node = Require(parent, name);
        // Either a bare string ("0x1234") or an object with confidence/note.
        if (node.ValueKind == JsonValueKind.String)
        {
            return new Rva(ParseHex(node.GetString()!), Confidence.Established);
        }
        return new Rva(
            ParseHex(node.GetProperty("rva").GetString()!),
            ParseConfidence(node),
            node.TryGetProperty("note", out var n) ? n.GetString() : null);
    }

    internal static Confidence ParseConfidence(JsonElement node) =>
        node.TryGetProperty("confidence", out var c)
            ? c.GetString() switch
            {
                "E" => Confidence.Established,
                "I" => Confidence.Inferred,
                "U" => Confidence.Unresolved,
                var other => throw new OffsetProfileException($"unknown confidence '{other}'"),
            }
            : Confidence.Established;

    internal static ulong ParseHex(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }
        return ulong.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    internal static byte[] ParseByteString(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(b => byte.Parse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
}

public sealed class ProbeSpec
{
    /// <summary>
    /// Deliberately an <see cref="Rva"/> and not a bare <c>ulong</c>.
    ///
    /// <para>It was a <c>ulong</c>, which meant the confidence written into the
    /// profile for the probe was parsed by nothing and enforced by nothing.
    /// A reanchor that failed to re-derive the probe still produced a profile
    /// whose build gate passed, because the probe bytes were re-read from the
    /// new image AT THE STALE ADDRESS -- so the gate compared the new image
    /// against itself and could never fail. The one check that exists to catch
    /// "this profile describes a different build" was disarmed exactly when
    /// re-derivation had failed. Observed on build 0F6DCAC1, 2026-08-20.</para>
    /// </summary>
    public required Rva Rva { get; init; }
    public required byte[] Bytes { get; init; }

    /// <summary>
    /// How many leading bytes are stable across rebuilds. Bytes beyond this are
    /// RIP-relative displacements that change whenever data moves, even when the
    /// function itself is untouched -- so a mismatch past this point means
    /// "addresses moved", not "the function changed".
    /// </summary>
    public required int StablePrefixLength { get; init; }
}

public sealed class SpotTableSpec
{
    public Rva? IndexedDestination { get; init; }
    public ulong? DestinationModeOffset { get; init; }
    public required Rva PitPosTable { get; init; }
    public required Rva GarPosTable { get; init; }
    public required Rva Mult { get; init; }
    public required Rva Count { get; init; }
    public required int EntryBytes { get; init; }
    public required int WriteBytes { get; init; }
}

/// <summary>
/// Where the engine's placement tunables live, and what to fall back on.
///
/// These are NOT constants of this project. In the 1AC2F605 build
/// GetPitDestination reads them from a mutable <c>.data</c> block, so Studio 397
/// can retune placement without moving a single address -- which they did, and
/// nothing noticed for nine days. Reading them live turns a retune into a
/// non-event and leaves only a genuine MOVE for `reanchor` to fix.
///
/// See <see cref="EngineTunables"/> for the read, the range checks and the
/// fallback rule.
/// </summary>
public sealed record EngineModelSpec
{
    /// <summary>Address of the yaw offset, stored by the engine in DEGREES.</summary>
    public required Rva YawOffsetDegrees { get; init; }
    public required Rva SearchStartFactorRva { get; init; }
    public required Rva SearchStepFactorRva { get; init; }
    public required Rva SearchMaxFactorRva { get; init; }

    /// <summary>Radians. Used only when the live read fails its range check.</summary>
    public required float FallbackYawOffsetRadians { get; init; }
    public required float FallbackSearchStartFactor { get; init; }
    public required float FallbackSearchStepFactor { get; init; }
    public required float FallbackSearchMaxFactor { get; init; }

    public required float YawOffsetDegreesMin { get; init; }
    public required float YawOffsetDegreesMax { get; init; }
    public required float SearchFactorMin { get; init; }
    public required float SearchFactorMax { get; init; }
}

public sealed class RestGateSpec
{
    public required string BaseUrl { get; init; }

    /// <summary>Legacy session-check configuration; not called by the current application.</summary>
    public required string[] Endpoints { get; init; }

    public required string SessionsRead { get; init; }
}
