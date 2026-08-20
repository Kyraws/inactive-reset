using System.Text.Json;

namespace InactiveReset.Core;

public sealed class ProfileFetchException(string message) : Exception(message);

/// <summary>
/// Fetches the offset profile for a build this machine does not have one for.
///
/// THIS IS THE ONLY OUTBOUND CONNECTION IN THE PROJECT. Everything else is
/// 127.0.0.1. That was a deliberate trade, not an accident, so the rules are
/// tight and stated here rather than spread across callers:
///
///   * HTTPS to one hard-coded host. No configurable base URL, no redirect to a
///     different host, no way for a config file to repoint it. A tool that
///     writes another process's memory should not have a steerable download URL.
///   * NEVER fetched without consent. See <see cref="FetchConsent"/>.
///   * The downloaded profile must PARSE and must declare the exact build hash
///     that was asked for. A profile aimed at a different build is discarded,
///     not "used anyway" -- its addresses would be wrong in the plausible way
///     that this project keeps getting burned by.
///
/// Publishing is a `git push`: profiles are served straight from the repository
/// at their committed path, so there is no release ritual that can be forgotten
/// half way through.
/// </summary>
public static class ProfileFetch
{
    private const string Host = "https://raw.githubusercontent.com";
    private const string Repo = "Kyraws/inactive-reset";
    private const string Branch = "main";

    /// <summary>
    /// Where the profile for a build hash would live. Deterministic: the app
    /// needs no index, no API call and no token to find it, and a 404 is an
    /// unambiguous "not published yet" rather than an ambiguous failure.
    /// </summary>
    public static string UrlFor(string executableSha256) =>
        $"{Host}/{Repo}/{Branch}/offsets/{Short(executableSha256)}.json";

    public static string Short(string executableSha256) =>
        executableSha256.Length >= 8 ? executableSha256[..8].ToUpperInvariant()
                                     : throw new ProfileFetchException("build hash is too short");

    /// <summary>
    /// Download the profile for <paramref name="executableSha256"/> into
    /// <paramref name="offsetDirectory"/> and return its path.
    ///
    /// Callers must have consent before calling. This method does not ask.
    /// </summary>
    public static async Task<string> FetchAsync(
        string executableSha256, string offsetDirectory,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        var url = UrlFor(executableSha256);
        var owned = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ProfileFetchException(
                    $"no profile has been published for build {Short(executableSha256)} yet.\n" +
                    "  This LMU build is newer than the last one that was verified.\n" +
                    $"  Checked: {url}");
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Verify(body, executableSha256, url);

            Directory.CreateDirectory(offsetDirectory);
            var path = Path.Combine(offsetDirectory, $"{Short(executableSha256)}.json");
            await File.WriteAllTextAsync(path, body, cancellationToken);
            return path;
        }
        catch (HttpRequestException ex)
        {
            throw new ProfileFetchException(
                $"could not reach {Host}: {ex.Message}\n" +
                "  You can also download the profile by hand and drop it in the\n" +
                $"  offsets folder: {url}");
        }
        finally
        {
            if (owned)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Refuse anything that is not a well-formed profile for exactly this build.
    ///
    /// Deliberately done BEFORE the file is written, so a bad response cannot
    /// leave a broken profile on disk for the next run to pick up.
    /// </summary>
    public static void Verify(string body, string expectedSha256, string url)
    {
        string? declared;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            declared = document.RootElement
                .GetProperty("build").GetProperty("executableSha256").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ProfileFetchException(
                $"the response from {url} is not an offset profile ({ex.Message})");
        }

        if (!string.Equals(declared, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProfileFetchException(
                "the downloaded profile is for a different build and was discarded.\n" +
                $"  asked for: {expectedSha256}\n" +
                $"  received:  {declared}");
        }
    }
}

/// <summary>
/// Whether the user has agreed to let the app reach the internet.
///
/// Stored as a file rather than assumed, because the first outbound connection
/// from a tool that writes into a game's memory should be something the user
/// saw and allowed. Someone will packet-capture this eventually; the honest
/// answer needs to be "you agreed to it, here is where that is recorded".
///
/// Absent file means NOT granted. Failing closed is the whole point.
/// </summary>
public sealed class FetchConsent
{
    private readonly string _path;

    public FetchConsent(string dataDirectory) =>
        _path = Path.Combine(dataDirectory, "fetch-consent.json");

    public bool Granted
    {
        get
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return false;
                }
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                return document.RootElement.TryGetProperty("allowProfileFetch", out var value)
                       && value.ValueKind == JsonValueKind.True;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return false;
            }
        }
    }

    public void Grant()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path,
            "{\n" +
            "  \"_comment\": \"Set by answering 'always' when asked to fetch an offset profile. Delete this file to be asked again.\",\n" +
            "  \"allowProfileFetch\": true,\n" +
            $"  \"granted_utc\": \"{DateTime.UtcNow:O}\"\n" +
            "}\n");
    }

    public string Path_ => _path;
}
