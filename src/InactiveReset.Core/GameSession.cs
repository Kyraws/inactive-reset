using System.Diagnostics;
using System.Security.Cryptography;

namespace InactiveReset.Core;

public class GateException(string message) : Exception(message);

/// <summary>Result of one gate check, so the UI can show what passed and why.</summary>
public sealed record GateCheck(string Name, bool Passed, string Detail);

/// <summary>
/// A verified connection to a running Le Mans Ultimate.
///
/// Production process-memory operations use this attachment path. Telemetry
/// capture uses shared memory separately; maintainer probes have their own
/// checks. Session type is not independently verified here.
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
    /// Read the engine's placement tunables from the running process.
    ///
    /// Deliberately a METHOD, not a property cached at attach: the block lives in
    /// mutable <c>.data</c>, so its values can legitimately change while a session
    /// is open. Every placement reads them again, which costs four float reads and
    /// removes a whole class of "the tool believed something that stopped being
    /// true" bug.
    /// </summary>
    public EngineTunables ReadEngineTunables() =>
        EngineTunables.Read(Memory, ModuleBase, Offsets.EngineModel);

    /// <summary>
    /// Attach to the running game and run every build gate.
    /// </summary>
    /// <param name="offsetDirectory">Folder for locally discovered build offsets.</param>
    /// <param name="forWriting">Open a write-capable handle.</param>
    public static GameSession Attach(
        string offsetDirectory, bool forWriting)
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

        var moduleBase = ProcessMemory.MainModuleBase(process);
        var memory = forWriting ? ProcessMemory.OpenWrite(process.Id)
                                : ProcessMemory.OpenRead(process.Id);
        OffsetProfile offsets;
        try
        {
            var diskHeader = new byte[0x1000];
            using (var executable = File.OpenRead(imagePath)) executable.ReadExactly(diskHeader);
            if (!ProcessMemory.SamePeIdentity(diskHeader, memory.ReadBytes(moduleBase, 0x1000),
                process.MainModule!.ModuleMemorySize))
                throw new GateException("the running image differs from the installed executable; restart Le Mans Ultimate");
            offsets = AutomaticOffsets.LoadOrDiscover(offsetDirectory, hash,
                FileVersionInfo.GetVersionInfo(imagePath).FileVersion ?? "unknown",
                () => memory.CaptureMappedModule(process),
                (rva, length) => memory.ReadBytes(moduleBase + rva, length), out var discovered);
            checks.Add(new GateCheck("offset discovery", true,
                discovered ? $"resolved offsets locally for build {hash[..8]}" : $"local offsets for build {hash[..8]}"));
            checks.Add(new GateCheck("build", true, $"{hash[..8]} -> {Path.GetFileName(offsets.SourcePath)}"));
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
        // Refuse before reading anything. A probe that was not re-derived for
        // this build points into the previous one, and the bytes it finds there
        // say nothing about whether this profile fits -- so comparing them is
        // not a weaker check, it is a meaningless one.
        var probeRva = probe.Rva.Require("the build probe");

        var live = memory.ReadBytes(moduleBase + probeRva, probe.Bytes.Length);
        if (live.AsSpan().SequenceEqual(probe.Bytes))
        {
            checks.Add(new GateCheck("probe", true, $"RVA 0x{probeRva:X} matches"));
            return;
        }

        var prefixMatches = live.AsSpan(0, probe.StablePrefixLength)
                                .SequenceEqual(probe.Bytes.AsSpan(0, probe.StablePrefixLength));
        var detail = prefixMatches
            ? $"the function at RVA 0x{probeRva:X} is unchanged, but its operands differ - " +
              "data has moved, so this profile is for a different build"
            : $"the bytes at RVA 0x{probeRva:X} are not this function at all - " +
              "the addresses do not describe this build";

        throw new GateException(
            $"probe mismatch: {detail}\n" +
            $"  expected {Convert.ToHexString(probe.Bytes)}\n" +
            $"  live     {Convert.ToHexString(live)}\n" +
            "Restart Inactive Reset to discover offsets for the running build.");
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
