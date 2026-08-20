using System.Diagnostics;
using System.Security.Cryptography;

namespace InactiveReset.Core;

public class GateException(string message) : Exception(message);

/// <summary>Result of one gate check, so the UI can show what passed and why.</summary>
public sealed record GateCheck(string Name, bool Passed, string Detail);

/// <summary>
/// The running build has no offset profile on this machine. Everything a caller
/// needs to ask the user whether to download one -- including the exact URL, so
/// the question can be specific about where the tool is about to connect.
/// </summary>
public sealed record MissingProfile(string ExecutableSha256, string Url, string OffsetDirectory)
{
    public string Short => ExecutableSha256[..8].ToUpperInvariant();
}

/// <summary>
/// No offset profile for the running build, and none was fetched -- either the
/// caller cannot ask, or the user said no.
///
/// This is the ordinary patch-day state, not a fault, and it carries everything
/// needed to resolve it.
/// </summary>
public sealed class MissingProfileException(MissingProfile request, string detail)
    : GateException(
        $"no offset profile for Le Mans Ultimate build {request.Short}.\n" +
        $"  This build is newer than any profile on this machine.\n" +
        $"  {request.Url}\n\n" + detail)
{
    public MissingProfile Request { get; } = request;
}

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
    /// <param name="offsetDirectory">Folder of per-build JSON profiles.</param>
    /// <param name="forWriting">Open a write-capable handle.</param>
    /// <param name="onMissingProfile">
    /// Called when no profile exists for the running build -- the patch-day case.
    /// Return true to fetch one. Passing null (the default) means never fetch,
    /// which is what every non-interactive caller wants: a background process
    /// must not decide on its own to reach the internet.
    ///
    /// The decision lives HERE rather than in each front end so that the CLI and
    /// the window cannot disagree about when the tool talks to the network. Two
    /// front ends implementing one policy separately is how this project has
    /// drifted before.
    /// </param>
    public static GameSession Attach(
        string offsetDirectory, bool forWriting,
        Func<MissingProfile, bool>? onMissingProfile = null)
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

        var offsets = LoadOrFetch(offsetDirectory, hash, onMissingProfile, checks);
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
    /// The profile for this build, fetching it once if the caller allows.
    ///
    /// A fetch is attempted at most once: if the download succeeds but still does
    /// not yield a usable profile, that is a real problem and retrying would only
    /// hide it behind a loop.
    /// </summary>
    private static OffsetProfile LoadOrFetch(
        string offsetDirectory, string hash,
        Func<MissingProfile, bool>? onMissingProfile, List<GateCheck> checks)
    {
        try
        {
            return OffsetProfile.ForBuild(offsetDirectory, hash);
        }
        catch (OffsetProfileException inner)
        {
            var request = new MissingProfile(hash, ProfileFetch.UrlFor(hash), offsetDirectory);

            // Typed, so a front end can tell "this build is new" apart from
            // "something is broken" and offer the one action that helps. A bare
            // OffsetProfileException here renders as a generic failure, which is
            // how patch day looks like a bug to the person hitting it.
            if (onMissingProfile is null || !onMissingProfile(request))
            {
                throw new MissingProfileException(request, inner.Message);
            }

            ProfileFetch.FetchAsync(hash, offsetDirectory).GetAwaiter().GetResult();
            checks.Add(new GateCheck("profile fetch", true,
                $"downloaded {ProfileFetch.Short(hash)}.json"));
            return OffsetProfile.ForBuild(offsetDirectory, hash);
        }
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
