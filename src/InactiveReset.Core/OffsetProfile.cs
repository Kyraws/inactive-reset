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
/// The predecessor compiled these into C++ constants, and every patch day cost
/// hours of re-derivation followed by a rebuild. Keep it that way: if you find
/// yourself about to write a hex literal in C#, it belongs here instead.
/// </summary>
public sealed class OffsetProfile
{
    public required string ExecutableSha256 { get; init; }
    public required string FileVersion { get; init; }
    public required ulong SizeOfImage { get; init; }

    public required ProbeSpec Probe { get; init; }
    public required SpotTableSpec SpotTable { get; init; }
    public required ContainerSpec Containers { get; init; }
    public required RulesSpec Rules { get; init; }
    public required EngineModelSpec EngineModel { get; init; }
    public required RestGateSpec RestGates { get; init; }

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
            ExecutableSha256 = build.GetProperty("executableSha256").GetString()!,
            FileVersion = build.GetProperty("fileVersion").GetString()!,
            SizeOfImage = ParseHex(build.GetProperty("sizeOfImage").GetString()!),

            Probe = new ProbeSpec
            {
                Rva = ParseHex(probe.GetProperty("rva").GetString()!),
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
            },

            Containers = ContainerSpec.FromJson(containers),
            Rules = RulesSpec.FromJson(rules),

            EngineModel = new EngineModelSpec
            {
                YawOffsetMode2 = engine.GetProperty("yawOffsetMode2").GetSingle(),
                SearchStartFactor = engine.GetProperty("searchStartFactor").GetSingle(),
                SearchStepFactor = engine.GetProperty("searchStepFactor").GetSingle(),
                SearchMaxFactor = engine.GetProperty("searchMaxFactor").GetSingle(),
            },

            RestGates = new RestGateSpec
            {
                BaseUrl = rest.GetProperty("baseUrl").GetString()!,
                Endpoints = rest.GetProperty("endpoints").EnumerateArray()
                                .Select(e => e.GetString()!).ToArray(),
                SessionsRead = rest.GetProperty("sessionsRead").GetString()!,
            },
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
    public required ulong Rva { get; init; }
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
    public required Rva PitPosTable { get; init; }
    public required Rva GarPosTable { get; init; }
    public required Rva Mult { get; init; }
    public required Rva Count { get; init; }
    public required int EntryBytes { get; init; }
    public required int WriteBytes { get; init; }
}

public sealed class EngineModelSpec
{
    /// <summary>
    /// GetPitDestination mode 2 applies <c>oriOut[1] -= sign * this</c>.
    /// SUSPECTED WRONG for the current build; see docs/HEADING_BUG.md.
    /// </summary>
    public required float YawOffsetMode2 { get; init; }
    public required float SearchStartFactor { get; init; }
    public required float SearchStepFactor { get; init; }
    public required float SearchMaxFactor { get; init; }
}

public sealed class RestGateSpec
{
    public required string BaseUrl { get; init; }

    /// <summary>Read-only endpoints that together prove offline single-player Practice.</summary>
    public required string[] Endpoints { get; init; }

    public required string SessionsRead { get; init; }
}
