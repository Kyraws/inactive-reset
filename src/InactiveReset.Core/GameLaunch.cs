using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace InactiveReset.Core;

/// <summary>
/// The game could not be found on this machine, or the folder we were pointed
/// at is not a Le Mans Ultimate install.
/// </summary>
public sealed class GameInstallException(string message) : Exception(message);

/// <summary>
/// Which of the game's two entry points to start.
///
/// These are genuinely different programs on disk, not a flag on one program,
/// and the difference decides whether this tool may touch the process at all.
/// </summary>
public enum LaunchMode
{
    /// <summary>
    /// <c>Le Mans Ultimate.exe</c>, started with no anticheat in the process
    /// tree. Steamstub still decrypts the image in memory, so the running
    /// process is a normal, fully readable one -- this is the only launch this
    /// tool can attach to.
    ///
    /// <para>The game allows local sessions only on this launch path, as
    /// confirmed by the project owner. Use single-player Practice.</para>
    /// </summary>
    Direct,

    /// <summary>
    /// <c>start_protected_game.exe</c>, which is what Steam itself launches. It
    /// starts the EasyAntiCheat bootstrapper, which starts the game.
    ///
    /// <para>This tool refuses to attach to a session launched this way, by
    /// design -- see the anticheat gate in <see cref="GameSession.Attach"/>. It
    /// is here so that switching back to normal play does not mean leaving the
    /// tool and finding Steam.</para>
    /// </summary>
    Protected,
}

/// <summary>
/// A located Le Mans Ultimate install.
/// </summary>
/// <param name="Root">The install folder, which is also the working directory
/// both executables must be started in.</param>
/// <param name="Source">How it was found, in words -- an override, a stored
/// path, or which Steam library. Shown to the user, because "we found an
/// install" is much less useful than "we found THIS one".</param>
public sealed record GameInstall(string Root, string Source)
{
    /// <summary>
    /// Steam's id for Le Mans Ultimate. Used to read the install folder out of
    /// Steam's own manifest rather than guessing a folder name.
    /// </summary>
    public const int SteamAppId = 2399420;

    public const string DirectExeName = "Le Mans Ultimate.exe";
    public const string ProtectedExeName = "start_protected_game.exe";

    public string DirectExe => Path.Combine(Root, DirectExeName);
    public string ProtectedExe => Path.Combine(Root, ProtectedExeName);

    public string ExecutableFor(LaunchMode mode) =>
        mode == LaunchMode.Direct ? DirectExe : ProtectedExe;
}

/// <summary>What happened when a launch was asked for.</summary>
public sealed record LaunchResult(
    LaunchMode Mode, string ExecutablePath, int ProcessId, string Message);

/// <summary>
/// Whether the game is up, and if so which way it was started.
///
    /// <para><see cref="Protected"/> uses a machine-wide anticheat process
    /// check, as does <see cref="GameSession.Attach"/>. It does not inspect
    /// parent-child relationships.</para>
/// </summary>
public sealed record GameRunState(bool Running, bool Protected, int? ProcessId)
{
    /// <summary>
    /// The process is running and no protected-launch process was detected.
    /// Build, placement, and session conditions still need checking.
    /// </summary>
    public bool Attachable => Running && !Protected;
}

/// <summary>
/// Find the installed game and start it, one way or the other.
///
/// <para>Deliberately gate-first, in the same style as
/// <see cref="GameSession"/>: every refusal happens before a process is
/// started, and a refusal says what to do about it. Starting the wrong entry
/// point is cheap to do and expensive to notice -- you find out minutes later,
/// when the tool refuses to attach.</para>
///
/// <para>This module never touches process memory and needs no
/// <see cref="GameSession"/>. It is usable before the game exists, which is the
/// whole point of it.</para>
/// </summary>
public static partial class GameLauncher
{
    /// <summary>
    /// Is the game running, and was it started protected? Cheap enough to call
    /// on every UI poll.
    /// </summary>
    public static GameRunState Running()
    {
        var game = Process.GetProcessesByName(GameSession.ProcessName).FirstOrDefault();
        try
        {
            return new GameRunState(game is not null, AnticheatPresent(), game?.Id);
        }
        finally
        {
            game?.Dispose();
        }
    }

    /// <summary>
    /// Locate the install: an explicit path if given, else a stored one, else
    /// Steam's own manifests.
    /// </summary>
    /// <param name="dataDirectory">
    /// Where a stored override may live. The stored path exists for installs
    /// Steam cannot describe -- a copied folder, a second install kept for
    /// testing -- so that being unusual costs one setting rather than the whole
    /// feature.
    /// </param>
    /// <param name="overrideRoot">
    /// An install folder supplied for this call only, from <c>--game-dir</c>.
    /// Wins over everything and is never written to disk.
    /// </param>
    /// <exception cref="GameInstallException">
    /// Nothing usable was found, or a path we were given is not an install. The
    /// message names every place that was looked at, because the fix is always
    /// to point the tool at the right folder.
    /// </exception>
    public static GameInstall Locate(string dataDirectory, string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Validated(overrideRoot, "--game-dir");
        }

        var stored = StoredRoot(dataDirectory);
        if (stored is not null)
        {
            return Validated(stored, $"stored in {OverridePath(dataDirectory)}");
        }

        var looked = new List<string>();
        foreach (var (root, source) in SteamCandidates())
        {
            if (File.Exists(Path.Combine(root, GameInstall.DirectExeName)))
            {
                return new GameInstall(root, source);
            }
            looked.Add(root);
        }

        throw new GameInstallException(
            "cannot find Le Mans Ultimate on this machine.\n" +
            (looked.Count == 0
                ? "  Steam's library configuration could not be read.\n"
                : "  Looked in:\n" + string.Join("\n", looked.Select(l => "    " + l)) + "\n") +
            "  Pass --game-dir \"<install folder>\", or save it once with\n" +
            "  inactive-reset launch --set-game-dir \"<install folder>\"");
    }

    /// <summary>
    /// Remember an install folder for future runs.
    ///
    /// Validated before it is written: a stored path that does not work is
    /// worse than no stored path, because it silently shadows the Steam lookup
    /// that would have succeeded.
    /// </summary>
    public static GameInstall SetInstallDirectory(string dataDirectory, string root)
    {
        var install = Validated(root, "--set-game-dir");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(OverridePath(dataDirectory),
            "{\n" +
            "  \"_comment\": \"Where Le Mans Ultimate is installed. Delete this file to go back to finding it through Steam.\",\n" +
            $"  \"gameDirectory\": {JsonSerializer.Serialize(install.Root)},\n" +
            $"  \"set_utc\": \"{DateTime.UtcNow:O}\"\n" +
            "}\n");
        return install;
    }

    /// <summary>
    /// Start the game.
    ///
    /// <para>Gates, in order: an install we can name, the entry point present
    /// on disk, Steam up, the game not already running, and -- for a direct
    /// launch -- no anticheat left over. All of them are checked before
    /// anything is started, so a refusal leaves the machine as it was.</para>
    /// </summary>
    /// <exception cref="GateException">A gate refused. The message says which and why.</exception>
    /// <exception cref="GameInstallException">The install could not be located.</exception>
    public static LaunchResult Launch(
        LaunchMode mode, string dataDirectory, string? overrideRoot = null)
    {
        var install = Locate(dataDirectory, overrideRoot);
        var executable = install.ExecutableFor(mode);

        if (!File.Exists(executable))
        {
            throw new GateException(
                $"{Path.GetFileName(executable)} is not in {install.Root}.\n" +
                (mode == LaunchMode.Protected
                    ? "  This install has no EasyAntiCheat launcher. Verify the game files in Steam."
                    : "  That folder does not look like a Le Mans Ultimate install."));
        }

        // Steamstub decrypts the image at startup and needs the Steam client to
        // do it. Without Steam the game shows its own error and exits, seconds
        // later and well away from the button that was pressed -- so say it here.
        if (Process.GetProcessesByName("steam").Length == 0)
        {
            throw new GateException(
                "Steam is not running. Le Mans Ultimate is Steamstub-wrapped and will " +
                "not start without it.");
        }

        var already = Running();
        if (already.Running)
        {
            throw new GateException(
                $"Le Mans Ultimate is already running (pid {already.ProcessId}), started " +
                $"{(already.Protected ? "with EasyAntiCheat" : "directly")}. " +
                "Close it before starting it the other way.");
        }

        // A leftover bootstrapper with no game behind it still fails the
        // anticheat gate, so a direct launch would produce a session this tool
        // then refuses to attach to. Catch it now, while the fix is easy.
        if (mode == LaunchMode.Direct && AnticheatPresent())
        {
            throw new GateException(
                "EasyAntiCheat is still running from a previous protected launch. Wait " +
                "for it to exit before launching directly - otherwise the anticheat gate " +
                "will refuse the session you are about to start.");
        }

        var process = Process.Start(new ProcessStartInfo(executable)
        {
            // The game resolves its own content relative to the install folder.
            WorkingDirectory = install.Root,
            UseShellExecute = false,
        }) ?? throw new GateException($"the operating system did not start {executable}.");

        return new LaunchResult(
            mode, executable, process.Id,
            mode == LaunchMode.Direct
                ? "launched directly - no anticheat, this tool can attach"
                : "launched with EasyAntiCheat - this tool will NOT attach to this session");
    }

    // ---- install discovery -------------------------------------------------

    private static string OverridePath(string dataDirectory) =>
        Path.Combine(dataDirectory, "game-install.json");

    private static string? StoredRoot(string dataDirectory)
    {
        var path = OverridePath(dataDirectory);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("gameDirectory", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // An unreadable override is not worth failing over: fall through to
            // the Steam lookup, which is what it was overriding.
            return null;
        }
    }

    private static GameInstall Validated(string root, string source)
    {
        var full = Path.GetFullPath(root.Trim().Trim('"'));
        if (!Directory.Exists(full))
        {
            throw new GameInstallException($"{source}: '{full}' does not exist.");
        }
        if (!File.Exists(Path.Combine(full, GameInstall.DirectExeName)))
        {
            throw new GameInstallException(
                $"{source}: '{full}' has no {GameInstall.DirectExeName} in it, so it is " +
                "not a Le Mans Ultimate install folder.");
        }
        return new GameInstall(full, source);
    }

    /// <summary>
    /// Every install folder Steam knows about, in the order worth trying.
    ///
    /// Read out of Steam's own files rather than assumed: the default library
    /// is only one of several, and the folder name comes from the app manifest
    /// because a library can hold an install whose folder was renamed.
    /// </summary>
    private static IEnumerable<(string Root, string Source)> SteamCandidates()
    {
        var steam = SteamRoot();
        if (steam is null)
        {
            yield break;
        }

        foreach (var library in Libraries(steam))
        {
            var manifest = Path.Combine(
                library, "steamapps", $"appmanifest_{GameInstall.SteamAppId}.acf");
            if (!File.Exists(manifest))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(manifest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var folder = Value(text, "installdir");
            if (folder is null)
            {
                continue;
            }

            yield return (
                Path.GetFullPath(Path.Combine(library, "steamapps", "common", folder)),
                $"Steam library {library}");
        }
    }

    private static string? SteamRoot()
    {
        foreach (var (hive, key, name) in new[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var subKey = root.OpenSubKey(key);
                // Steam writes this with forward slashes and in whatever case it
                // feels like. Normalise once, here, so nothing downstream has to
                // know that.
                if (subKey?.GetValue(name) is string path && Directory.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next one.
            }
        }
        return null;
    }

    /// <summary>
    /// Steam's library folders, the default one first.
    ///
    /// <c>libraryfolders.vdf</c> is Valve's own format, and there is no parser
    /// for it here on purpose -- one field is wanted, and a dependency to read
    /// it would be the only one in this project.
    /// </summary>
    private static IEnumerable<string> Libraries(string steamRoot)
    {
        yield return steamRoot;

        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (Match match in PathEntry().Matches(text))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.Equals(path, steamRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>Pull one quoted value out of a VDF/ACF file by key.</summary>
    private static string? Value(string text, string key)
    {
        var match = Regex.Match(
            text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace(@"\\", @"\") : null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();

    /// <summary>
    /// The same evidence <see cref="GameSession.Attach"/> gates on. Kept
    /// identical on purpose: if these two ever disagree, the tool offers a
    /// launch it will then refuse to use.
    /// </summary>
    private static bool AnticheatPresent() =>
        Process.GetProcesses().Any(p =>
            p.ProcessName.Contains("EasyAnti", StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Contains("start_protected", StringComparison.OrdinalIgnoreCase));
}
