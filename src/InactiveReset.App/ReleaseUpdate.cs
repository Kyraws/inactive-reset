using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace InactiveReset.App;

internal static class ReleaseUpdate
{
    private const string Manifest = "release-files.json";
    private const string Journal = "rollback.json";
    private const string Desktop = "inactive-reset-ui.exe";
    private const string Cli = "inactive-reset.exe";
    private const string ProductionRepo = "inactive-reset";
    private const string TestRepo = "inactive-reset-update-test";
    private const string StagingPrefix = ".inactive-reset-update-";
    private static readonly HttpClient GitHub = new() { Timeout = TimeSpan.FromMinutes(5) };

    private sealed record Change(string Relative, bool Existed, string? NewHash);

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static async Task OfferAsync(Form window, string[] args)
    {
        var root = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(root, Manifest))) return;

        var repo = args.Contains("--update-test-feed") ? TestRepo : ProductionRepo;
        var current = typeof(ReleaseUpdate).Assembly.GetName().Version!;
        Version version;
        string downloadUrl;
        string digest;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/Kyraws/{repo}/releases/latest");
            request.Headers.UserAgent.ParseAdd("InactiveReset-Updater");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await GitHub.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var tag = release.RootElement.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var parsed)
                || parsed.Build < 0 || parsed.Revision != -1
                || new Version(parsed.Major, parsed.Minor, parsed.Build, 0) <= current)
                return;
            version = parsed;
            var assetName = $"inactive-reset-{version}-win-x64.zip";
            var asset = release.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(item => item.GetProperty("name").GetString() == assetName);
            if (asset.ValueKind == JsonValueKind.Undefined) return;
            downloadUrl = $"https://github.com/Kyraws/{repo}/releases/download/{tag}/{assetName}";
            if (asset.GetProperty("browser_download_url").GetString() != downloadUrl) return;
            digest = asset.GetProperty("digest").GetString() ?? "";
            if (digest.Length != 71 || !digest.StartsWith("sha256:") || !ValidHash(digest[7..])) return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
            or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return;
        }

        if (window.IsDisposed || !window.Enabled) return;
        if (IsAdministrator())
        {
            MessageBox.Show(window, $"Inactive Reset {version} is available. Reopen Inactive Reset without administrator privileges to install it.",
                "Update available", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (
            MessageBox.Show(window, $"Inactive Reset {version} is available. Install and restart now?",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        window.Enabled = false;
        var staging = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        var handedOff = false;
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(staging);
                var archivePath = Path.Combine(staging, "release.zip");
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                request.Headers.UserAgent.ParseAdd("InactiveReset-Updater");
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var response = await GitHub.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                using (var file = File.Create(archivePath))
                    await response.Content.CopyToAsync(file, timeout.Token);
                if (!HashEquals(archivePath, digest[7..]))
                    throw new IOException("The download did not match the GitHub release checksum.");
                var contents = Path.Combine(staging, "new");
                ZipFile.ExtractToDirectory(archivePath, contents);
                var files = ReadManifest(contents);
                if (files.Any(entry => !HashEquals(CheckedPath(contents, entry.Key, mustExist: true), entry.Value)))
                    throw new IOException("A package file did not match its release manifest.");
                if (new[] { Desktop, Cli }.Any(name =>
                    FileVersionInfo.GetVersionInfo(Path.Combine(contents, name)).FileVersion != $"{version}.0"))
                    throw new IOException("The package executables do not match the release tag.");
                File.Delete(archivePath);
                File.Copy(Path.Combine(root, Desktop), Path.Combine(staging, "update-helper.exe"));
            });
            if (window.IsDisposed) return;
            using var helper = Start(Path.Combine(staging, "update-helper.exe"), root,
                ["--apply-update", root, Environment.ProcessId.ToString(), Environment.CurrentDirectory, .. args]);
            handedOff = true;
            window.Close();
        }
        catch (Exception ex)
        {
            if (!window.IsDisposed)
                MessageBox.Show(window, ex.Message, "Could not prepare update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!handedOff) RemoveStaging(staging);
            if (!window.IsDisposed) window.Enabled = true;
        }
    }

    internal static bool RecoverOnStartup(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var staging = Directory.EnumerateDirectories(root, StagingPrefix + "*")
            .FirstOrDefault(path => IsStagingName(Path.GetFileName(path)) && File.Exists(Path.Combine(path, Journal)));
        if (staging is null) return false;
        if (IsAdministrator())
            throw new IOException("An update is pending. Reopen Inactive Reset without administrator privileges to recover it.");
        var helper = CheckedPath(staging, "update-helper.exe", mustExist: true);
        using var process = Start(helper, root,
            ["--recover-update", root, Environment.ProcessId.ToString(), Environment.CurrentDirectory, .. args]);
        return true;
    }

    internal static void Apply(string[] args, bool recover = false)
    {
        if (IsAdministrator()) throw new IOException("Run updates without administrator privileges.");
        if (args is not [_, var root, var pid, var workingDirectory, ..] || !int.TryParse(pid, out var id))
            throw new ArgumentException("Invalid update handoff.");
        var staging = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (!string.Equals(Path.GetDirectoryName(staging), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
            StringComparison.OrdinalIgnoreCase) ||
            !IsStagingName(Path.GetFileName(staging)))
            throw new IOException("The update staging directory is outside the installation.");

        Process? previous = null;
        try { previous = Process.GetProcessById(id); }
        catch (ArgumentException) { /* The app has already exited. */ }
        using (previous)
        {
            if (previous is not null && !previous.WaitForExit(300_000))
                throw new IOException("Inactive Reset is still closing. Close it normally before retrying.");
        }

        var contents = Path.Combine(staging, "new");
        var backup = Path.Combine(staging, "backup");
        CheckedPath(staging, Journal, mustExist: false);
        // Windows releases this lock if the helper is terminated.
        using var updateLock = File.Open(Path.Combine(root, ".inactive-reset-update.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var journal = Path.Combine(staging, Journal);
        if (recover)
        {
            Restore(root, staging, journal);
            Restart();
            return;
        }
        if (Directory.EnumerateDirectories(root, StagingPrefix + "*")
            .Any(path => File.Exists(Path.Combine(path, Journal))))
            throw new IOException("A previous update needs recovery. Reopen Inactive Reset to recover it.");
        var oldFiles = ReadManifest(root);
        var newFiles = ReadManifest(contents);
        var changes = new List<Change>();
        foreach (var path in newFiles.Keys.Append(Manifest))
        {
            var target = CheckedPath(root, path, mustExist: false);
            var source = CheckedPath(contents, path, mustExist: true);
            if (path != Manifest && !HashEquals(source, newFiles[path]))
                throw new IOException($"The staged package file changed: {path}");
            var exists = File.Exists(target);
            if (exists && path != Manifest)
            {
                if (!oldFiles.TryGetValue(path, out var oldHash) || !HashEquals(target, oldHash))
                    throw new IOException($"The update would overwrite a local file: {path}");
                if (oldHash.Equals(newFiles[path], StringComparison.OrdinalIgnoreCase)) continue;
            }
            var saved = Path.Combine(backup, path);
            Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            changes.Add(new(path, exists, path == Manifest ? null : newFiles[path]));
        }

        var pending = journal + ".tmp";
        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, changes);
            file.Flush(flushToDisk: true);
        }
        File.Move(pending, journal);
        try
        {
            foreach (var change in changes)
            {
                var target = CheckedPath(root, change.Relative, mustExist: false);
                var source = CheckedPath(contents, change.Relative, mustExist: true);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (change.Existed)
                    File.Replace(source, target, Path.Combine(backup, change.Relative));
                else
                    File.Move(source, target);
            }
            File.Delete(journal);
        }
        catch
        {
            Restore(root, staging, journal);
            throw;
        }
        Restart();

        void Restart()
        {
            using var restarted = Start(Path.Combine(root, Desktop), workingDirectory,
                ["--finished-update", Path.GetFileName(staging), Environment.ProcessId.ToString(), .. args[4..]]);
        }
    }

    private static void Restore(string root, string staging, string journal)
    {
        var changes = JsonSerializer.Deserialize<Change[]>(File.ReadAllText(journal))
            ?? throw new IOException("The update recovery record is empty.");
        foreach (var change in changes.Reverse())
        {
            if (change.Relative != Manifest && (!IsReleasePath(change.Relative) || !ValidHash(change.NewHash)))
                throw new IOException("The update recovery record contains an invalid path.");
            var target = CheckedPath(root, change.Relative, mustExist: false);
            var saved = CheckedPath(staging, "backup/" + change.Relative, mustExist: false);
            var source = CheckedPath(staging, "new/" + change.Relative, mustExist: false);
            if (change.Existed && File.Exists(saved))
                File.Move(saved, target, overwrite: true);
            else if (!change.Existed && !File.Exists(source) && File.Exists(target) &&
                change.NewHash is not null && HashEquals(target, change.NewHash))
                File.Delete(target);
        }
        File.Delete(journal);
    }

    internal static void Cleanup(string stagingName, string helperPid)
    {
        if (!IsStagingName(stagingName) || !int.TryParse(helperPid, out var id)) return;
        try
        {
            using var helper = Process.GetProcessById(id);
            if (!helper.WaitForExit(10_000)) return;
        }
        catch (ArgumentException) { /* The helper has already exited. */ }
        RemoveStaging(Path.Combine(AppContext.BaseDirectory, stagingName));
    }

    private static bool IsStagingName(string name) => name.StartsWith(StagingPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name[StagingPrefix.Length..], "N", out _);

    private static void RemoveStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, string> ReadManifest(string root)
    {
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(CheckedPath(root, Manifest, mustExist: true)));
        if (entries is null || !entries.ContainsKey(Desktop) || !entries.ContainsKey(Cli)
            || !entries.ContainsKey("offsets/shared-memory.json"))
            throw new IOException("The release manifest is missing required files.");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, hash) in entries)
        {
            if (!IsReleasePath(path) || !ValidHash(hash) || !files.TryAdd(path, hash))
                throw new IOException("The release manifest contains an invalid file entry.");
            CheckedPath(root, path, mustExist: false);
        }
        return files;
    }

    private static bool IsReleasePath(string path) =>
        path is Desktop or Cli or "README.md" or "GLOSSARY.md" or "LICENSE" or "NOTICE"
            or "offsets/shared-memory.json" or "tests/LIVE_CHECKLIST.md"
        || path.StartsWith("data/profiles-default/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)
        || path.StartsWith("docs/", StringComparison.Ordinal)
        || path.StartsWith("tools/", StringComparison.Ordinal) && path.EndsWith("/README.md", StringComparison.Ordinal);

    private static string CheckedPath(string root, string relative, bool mustExist)
    {
        if (relative.Contains('\\') || relative.Split('/').Any(part => part.Length == 0
            || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new IOException("The release contains an invalid path.");
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The installation path contains a link.");
        }
        var path = root;
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"The update would follow a link: {relative}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        if (Directory.Exists(path) || mustExist && !File.Exists(path))
            throw new IOException($"The package file is missing or is a directory: {relative}");
        return path;
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HashEquals(string path, string expected)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static Process Start(string executable, string directory, string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("Could not start " + executable);
    }
}
