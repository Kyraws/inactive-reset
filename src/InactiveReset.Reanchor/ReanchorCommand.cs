using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>
/// Patch day, as one command.
///
/// Addresses used to be compiled-in constants, so an LMU update meant hours of
/// manual re-derivation followed by a rebuild — and a missed constant did not
/// fail loudly, it read plausible bytes from the wrong place. Here the addresses
/// are data, and this regenerates that data.
/// </summary>
internal static class ReanchorCommand
{
    public static int Dump(string[] args)
    {
        var process = Process.GetProcessesByName(GameSession.ProcessName).FirstOrDefault()
            ?? throw new GateException($"{GameSession.ProcessName} is not running.");

        var output = args.FirstOrDefault(a => !a.StartsWith("--"))
            ?? $"lmu-dump-{DateTime.Now:yyyyMMdd-HHmmss}.bin";

        Console.WriteLine($"dumping pid {process.Id}...");
        var result = ModuleDumper.Dump(process, output);

        Console.WriteLine($"  module base   0x{result.ModuleBase:X}");
        Console.WriteLine($"  size          0x{result.Size:X} ({result.Size:N0} bytes)");
        Console.WriteLine($"  unreadable    {result.UnreadableBytes:N0} bytes");
        Console.WriteLine($"  sha256        {result.Sha256[..16]}...");
        Console.WriteLine($"  written to    {result.Path}");
        Console.WriteLine("\nfile offset == RVA. Keep this: it is the 'before' image for the next patch.");
        return 0;
    }

    /// <summary>
    /// Compare an old dump against the running game and emit a new offset
    /// profile.
    /// </summary>
    public static int Run(string offsetDirectory, string[] args)
    {
        var oldDumpPath = Value(args, "--old-dump")
            ?? throw new ArgumentException("--old-dump <file> is required (the pre-patch image)");
        var basePath = Value(args, "--base")
            ?? throw new ArgumentException("--base <profile.json> is required (the pre-patch offsets)");

        var process = Process.GetProcessesByName(GameSession.ProcessName).FirstOrDefault()
            ?? throw new GateException($"{GameSession.ProcessName} is not running.");

        var newDumpPath = Value(args, "--new-dump");
        if (newDumpPath is null)
        {
            newDumpPath = Path.Combine(Path.GetTempPath(), $"lmu-new-{DateTime.Now:HHmmss}.bin");
            Console.WriteLine("capturing the running game...");
            var dumped = ModuleDumper.Dump(process, newDumpPath);
            Console.WriteLine($"  {dumped.Size:N0} bytes, {dumped.UnreadableBytes:N0} unreadable\n");
        }

        var older = File.ReadAllBytes(oldDumpPath);
        var newer = File.ReadAllBytes(newDumpPath);
        Console.WriteLine($"old image 0x{older.Length:X}   new image 0x{newer.Length:X}");
        if (older.Length != newer.Length)
        {
            Console.WriteLine("  (sizes differ - the module changed size, so code certainly moved)");
        }
        Console.WriteLine();

        var profile = JsonNode.Parse(File.ReadAllText(basePath))!.AsObject();
        var addresses = Collect(profile).ToList();

        // One pass over the old image, reused for every data address.
        Console.WriteLine("indexing references in the old image...");
        var stopwatch = Stopwatch.StartNew();
        var index = Reanchor.BuildReferenceIndex(older);
        Console.WriteLine($"  {index.Count:N0} referenced addresses in {stopwatch.Elapsed.TotalSeconds:F1} s\n");

        Console.WriteLine($"{"name",-34} {"old",-12} {"new",-12} delta      evidence");
        Console.WriteLine(new string('-', 92));

        var unresolved = new List<string>();
        var resolvedData = new List<(ulong Old, ulong New)>();
        var deferred = new List<(string[] Path, string Name, ulong OldRva)>();
        foreach (var (path, name, oldRva, isCode) in addresses)
        {
            if (isCode)
            {
                var match = Reanchor.RemapCode(older, newer, oldRva);
                if (match is null)
                {
                    Report(name, oldRva, null, "NO UNIQUE MATCH");
                    unresolved.Add(name);
                    MarkUnresolved(profile, path);
                    continue;
                }
                Report(name, oldRva, match.NewRva, $"{match.BytesUsed}B signature", match.Delta);
                Apply(profile, path, match.NewRva, unanimous: true);
            }
            else
            {
                var match = Reanchor.RemapData(older, newer, oldRva, index);
                if (match is not null)
                {
                    var evidence = match.Unanimous
                        ? $"{match.Votes}/{match.Sites} refs agree"
                        : $"{match.Votes}/{match.Sites}, {match.CandidateCount} CANDIDATES";
                    Report(name, oldRva, match.NewRva, evidence, match.Delta);
                    Apply(profile, path, match.NewRva, match.Unanimous);
                    resolvedData.Add((oldRva, match.NewRva));
                    continue;
                }

                // Reference voting failed. That happens when the functions doing
                // the referencing were themselves recompiled, which is a
                // property of the patch and says nothing about whether the datum
                // is still findable. Constants still are -- try the content.
                var byValue = Reanchor.RemapDataByValue(older, newer, oldRva);
                if (byValue is not null)
                {
                    Report(name, oldRva, byValue.NewRva,
                           $"value anchor, {byValue.Windows} window(s) to {byValue.WidestBytes}B",
                           byValue.Delta);
                    Apply(profile, path, byValue.NewRva, byValue.Corroborated);
                    resolvedData.Add((oldRva, byValue.NewRva));
                    continue;
                }

                Report(name, oldRva, null, "NO RESOLVED REFERENCES");
                unresolved.Add(name);
                MarkUnresolved(profile, path);
                deferred.Add((path, name, oldRva));
            }
        }

        if (deferred.Count > 0 && args.Contains("--infer-adjacent"))
        {
            InferAdjacent(profile, deferred, resolvedData, unresolved);
        }

        // Refresh build identity from the running game.
        var imagePath = process.MainModule!.FileName;
        var hash = GameSession.Sha256File(imagePath);
        var build = profile["build"]!.AsObject();
        build["executableSha256"] = hash;
        build["fileVersion"] = FileVersionInfo.GetVersionInfo(imagePath).FileVersion ?? "unknown";
        build["sizeOfImage"] = $"0x{newer.Length:X}";
        build["derivedFrom"] = Path.GetFileName(newDumpPath);
        build["derivedUtc"] = DateTime.UtcNow.ToString("O");

        // The probe bytes are read from the NEW image at the NEW address, so the
        // build gate matches this build rather than the previous one.
        //
        // ONLY when the probe was actually re-derived. Refreshing them at an
        // address we failed to re-derive reads the new image at the OLD offset
        // and stores whatever is there as the expected value -- so the gate then
        // compares the new image against itself and can never fail. That turned
        // a failed reanchor into a profile whose every gate passed, on build
        // 0F6DCAC1. The probe is the one check that catches "this profile is for
        // a different build"; it must not be handed a self-fulfilling answer.
        var probe = profile["probe"]!.AsObject();
        var probeRva = ParseHex(probe["rva"]!.GetValue<string>());
        var probeResolved = probe["confidence"]?.GetValue<string>() != "U";
        if (probeResolved)
        {
            probe["bytes"] = string.Join(' ',
                newer.Skip((int)probeRva).Take(16).Select(b => b.ToString("X2")));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(
                "PROBE NOT RE-DERIVED. Its bytes were left as the previous build's, so the");
            Console.WriteLine(
                "build gate will refuse this profile. That is deliberate: a profile whose");
            Console.WriteLine(
                "probe is stale cannot vouch for anything else in it.");
        }

        var outputPath = Value(args, "--out")
            ?? Path.Combine(offsetDirectory, $"{hash[..8]}.json");
        File.WriteAllText(outputPath,
            profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"build     {hash[..8]}  ({build["fileVersion"]})");
        Console.WriteLine($"written   {outputPath}");

        if (unresolved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{unresolved.Count} address(es) could NOT be re-derived and are marked");
            Console.WriteLine("confidence \"U\" in the profile. Anything using them will refuse rather");
            Console.WriteLine("than read the wrong place:");
            foreach (var name in unresolved)
            {
                Console.WriteLine($"  - {name}");
            }
        }
        return unresolved.Count == 0 ? 0 : 1;
    }

    // ---- profile walking ----------------------------------------------------

    /// <summary>
    /// Every address in the profile, tagged as code or data.
    ///
    /// Code lives under "functions" and "probe"; everything else is data. That
    /// distinction decides which re-derivation technique applies, and getting it
    /// wrong produces confident nonsense.
    /// </summary>
    private static IEnumerable<(string[] Path, string Name, ulong Rva, bool IsCode)> Collect(JsonObject profile)
    {
        foreach (var (name, node) in Walk(profile, []))
        {
            var isCode = node.Path.Length > 0
                && node.Path[0] is "functions" or "probe";
            yield return (node.Path, name, node.Rva, isCode);
        }
    }

    private static IEnumerable<(string Name, (string[] Path, ulong Rva) Node)> Walk(
        JsonNode? node, string[] path)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("rva", out var rva) && rva is JsonValue)
                {
                    var name = path.Length == 0 ? "root" : string.Join('.', path);
                    yield return (name, (path, ParseHex(rva.GetValue<string>())));
                    yield break;
                }
                foreach (var (key, child) in obj)
                {
                    if (key.StartsWith('$'))
                    {
                        continue;
                    }
                    foreach (var found in Walk(child, [.. path, key]))
                    {
                        yield return found;
                    }
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var found in Walk(array[i], [.. path, i.ToString()]))
                    {
                        yield return found;
                    }
                }
                break;
        }
    }

    private static JsonObject? Navigate(JsonObject profile, string[] path)
    {
        JsonNode? node = profile;
        foreach (var step in path)
        {
            node = node switch
            {
                JsonObject obj => obj.TryGetPropertyValue(step, out var next) ? next : null,
                JsonArray array when int.TryParse(step, out var index) && index < array.Count => array[index],
                _ => null,
            };
            if (node is null)
            {
                return null;
            }
        }
        return node as JsonObject;
    }

    private static void Apply(JsonObject profile, string[] path, ulong rva, bool unanimous)
    {
        var node = Navigate(profile, path);
        if (node is null)
        {
            return;
        }
        node["rva"] = $"0x{rva:X8}";
        node["confidence"] = unanimous ? "E" : "I";
    }

    /// <summary>
    /// Last resort: assume an address moved with its neighbours.
    ///
    /// <para>OPT-IN, behind <c>--infer-adjacent</c>, because this is a guess and
    /// the rest of this tool is not. It proposes <c>old + delta</c> where delta
    /// comes from nearby addresses that WERE re-derived, and it is only offered
    /// when several of them agree.</para>
    ///
    /// <para>The reason it is not the default: the deltas are not uniform. On
    /// build 0F6DCAC1 one region moved by -0x2B550 and another by -0x2C010, so
    /// a single global shift would have been wrong for half the profile.
    /// Everything written here is marked Inferred, never Established, and the
    /// note records exactly what it was inferred from.</para>
    /// </summary>
    private static void InferAdjacent(
        JsonObject profile,
        List<(string[] Path, string Name, ulong OldRva)> deferred,
        List<(ulong Old, ulong New)> resolved,
        List<string> unresolved)
    {
        // Close enough that a shared delta is plausible. Wide enough to find
        // neighbours at all: the profile's data addresses cluster in a handful
        // of regions tens of kilobytes apart.
        const ulong Neighbourhood = 0x20000;
        const int MinimumWitnesses = 2;

        Console.WriteLine();
        Console.WriteLine("--infer-adjacent: proposing addresses from neighbouring deltas");
        Console.WriteLine("  (Inferred, never Established - verify before trusting)");

        foreach (var (path, name, oldRva) in deferred)
        {
            var witnesses = resolved
                .Where(r => r.Old > oldRva - Neighbourhood && r.Old < oldRva + Neighbourhood)
                .Select(r => (long)r.New - (long)r.Old)
                .ToList();

            if (witnesses.Count < MinimumWitnesses)
            {
                Console.WriteLine($"  {name,-34} no neighbours - left unresolved");
                continue;
            }

            var deltas = witnesses.Distinct().ToList();
            if (deltas.Count != 1)
            {
                // Neighbours disagreeing is the signal that the region was
                // relaid out, which is precisely when this technique is wrong.
                Console.WriteLine(
                    $"  {name,-34} neighbours disagree ({deltas.Count} deltas) - left unresolved");
                continue;
            }

            var inferred = (ulong)((long)oldRva + deltas[0]);
            Console.WriteLine(
                $"  {name,-34} 0x{oldRva:X8} -> 0x{inferred:X8}  " +
                $"delta {Hex(deltas[0])} from {witnesses.Count} neighbour(s)");

            var node = Navigate(profile, path);
            if (node is null)
            {
                continue;
            }
            node["rva"] = $"0x{inferred:X8}";
            node["confidence"] = "I";
            node["note"] =
                $"INFERRED, not re-derived: {witnesses.Count} addresses within " +
                $"0x{Neighbourhood:X} all moved by {Hex(deltas[0])}. Verify before trusting.";
            unresolved.Remove(name);
        }
    }

    /// <summary>A signed hex delta. C# has no composite format that does this.</summary>
    private static string Hex(long delta) =>
        delta < 0 ? $"-0x{-delta:X}" : $"+0x{delta:X}";

    private static void MarkUnresolved(JsonObject profile, string[] path)
    {
        var node = Navigate(profile, path);
        if (node is null)
        {
            return;
        }
        node["confidence"] = "U";
        node["note"] = "NOT re-derived for this build by reanchor; the value is stale.";
    }

    // ---- output -------------------------------------------------------------

    private static void Report(string name, ulong oldRva, ulong? newRva, string evidence, long delta = 0)
    {
        if (newRva is null)
        {
            Console.WriteLine($"{Trim(name),-34} 0x{oldRva:X8}   {"---",-12} {"",-10} {evidence}");
            return;
        }
        var sign = delta >= 0 ? "+" : "-";
        var magnitude = $"{sign}0x{Math.Abs(delta):X}";
        Console.WriteLine(
            $"{Trim(name),-34} 0x{oldRva:X8}   0x{newRva:X8}   {magnitude,-9}  {evidence}");
    }

    private static string Trim(string name) =>
        name.Length <= 34 ? name : "…" + name[^33..];

    private static string? Value(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static ulong ParseHex(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }
        return ulong.Parse(trimmed, System.Globalization.NumberStyles.HexNumber);
    }
}
