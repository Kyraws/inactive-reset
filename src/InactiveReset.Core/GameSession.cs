using System.Diagnostics;
using System.Security.Cryptography;

namespace InactiveReset.Core;

public sealed class GateException(string message) : Exception(message);

/// <summary>Result of one gate check, so the UI can show what passed and why.</summary>
public sealed record GateCheck(string Name, bool Passed, string Detail);

/// <summary>
/// A verified connection to a running Le Mans Ultimate.
///
/// Nothing else in this project may touch the game without going through here.
/// The gates exist because a stale address does not fail loudly -- it lands on
/// plausible unrelated bytes and silently reports nonsense.
/// </summary>
public sealed class GameSession : IDisposable
{
    public const string ProcessName = "Le Mans Ultimate";

    public required Process Process { get; init; }
    public required ulong ModuleBase { get; init; }
    public required string ExecutableSha256 { get; init; }
    public required OffsetProfile Offsets { get; init; }
    public required ProcessMemory Memory { get; init; }
    public required IReadOnlyList<GateCheck> Gates { get; init; }

    /// <summary>Translate a profile RVA into a live address.</summary>
    public ulong Resolve(Rva rva, string what) => ModuleBase + rva.Require(what);

    /// <summary>
    /// Attach to the running game and run every build gate.
    /// </summary>
    /// <param name="offsetDirectory">Folder of per-build JSON profiles.</param>
    /// <param name="forWriting">Open a write-capable handle.</param>
    public static GameSession Attach(string offsetDirectory, bool forWriting)
    {
        var checks = new List<GateCheck>();

        var process = Process.GetProcessesByName(ProcessName).FirstOrDefault()
            ?? throw new GateException($"{ProcessName} is not running.");
        checks.Add(new GateCheck("process", true, $"pid {process.Id}"));

        // EasyAntiCheat: this tool is for offline practice. If EAC is loaded the
        // game was launched through the protected path and we stay out.
        var eac = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("EasyAnti", StringComparison.OrdinalIgnoreCase)
                     || p.ProcessName.Contains("start_protected", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ProcessName)
            .ToArray();
        if (eac.Length > 0)
        {
            throw new GateException(
                $"EasyAntiCheat is running ({string.Join(", ", eac)}). Refusing. " +
                $"Launch \"{ProcessName}.exe\" directly for offline practice.");
        }
        checks.Add(new GateCheck("anticheat", true, "absent"));

        var imagePath = process.MainModule?.FileName
            ?? throw new GateException("cannot read the game's image path");
        var hash = Sha256File(imagePath);

        var offsets = OffsetProfile.ForBuild(offsetDirectory, hash);
        checks.Add(new GateCheck("build", true, $"{hash[..8]} -> {Path.GetFileName(offsets.SourcePath)}"));

        var moduleBase = ProcessMemory.MainModuleBase(process);
        var memory = forWriting ? ProcessMemory.OpenWrite(process.Id)
                                : ProcessMemory.OpenRead(process.Id);

        try
        {
            VerifyProbe(memory, moduleBase, offsets.Probe, checks);
        }
        catch
        {
            memory.Dispose();
            throw;
        }

        return new GameSession
        {
            Process = process,
            ModuleBase = moduleBase,
            ExecutableSha256 = hash,
            Offsets = offsets,
            Memory = memory,
            Gates = checks,
        };
    }

    /// <summary>
    /// Compare live bytes at the probe against the profile.
    ///
    /// The distinction between the stable prefix and the rest matters: bytes
    /// past the prefix are RIP-relative displacements, which change whenever
    /// data moves even if the function is untouched. A mismatch there means
    /// "this profile describes a different build", not "the game changed this
    /// function" -- and saying so saves the next person an afternoon.
    /// </summary>
    private static void VerifyProbe(ProcessMemory memory, ulong moduleBase,
                                    ProbeSpec probe, List<GateCheck> checks)
    {
        var live = memory.ReadBytes(moduleBase + probe.Rva, probe.Bytes.Length);
        if (live.AsSpan().SequenceEqual(probe.Bytes))
        {
            checks.Add(new GateCheck("probe", true, $"RVA 0x{probe.Rva:X} matches"));
            return;
        }

        var prefixMatches = live.AsSpan(0, probe.StablePrefixLength)
                                .SequenceEqual(probe.Bytes.AsSpan(0, probe.StablePrefixLength));
        var detail = prefixMatches
            ? $"the function at RVA 0x{probe.Rva:X} is unchanged, but its operands differ - " +
              "data has moved, so this profile is for a different build"
            : $"the bytes at RVA 0x{probe.Rva:X} are not this function at all - " +
              "the addresses do not describe this build";

        throw new GateException(
            $"probe mismatch: {detail}\n" +
            $"  expected {Convert.ToHexString(probe.Bytes)}\n" +
            $"  live     {Convert.ToHexString(live)}\n" +
            "Run `reanchor` to derive a profile for the installed build.");
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Dispose()
    {
        Memory.Dispose();
        Process.Dispose();
    }
}
