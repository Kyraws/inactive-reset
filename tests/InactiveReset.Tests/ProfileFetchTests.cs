using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The one outbound connection in the project, and the checks that make it safe
/// to have. A profile decides where 24 bytes get written into another process,
/// so "we downloaded something" is never good enough on its own.
/// </summary>
public sealed class ProfileFetchTests
{
    private const string Hash =
        "1AC2F6059AA1FBC948373DF6883B83E91F82728330F1458D8C9D935C8CFB709D";

    [Fact]
    public void UrlIsDerivedFromTheBuildHashAndPinnedToOneHost()
    {
        var url = ProfileFetch.UrlFor(Hash);

        Assert.StartsWith("https://raw.githubusercontent.com/Kyraws/inactive-reset/", url);
        Assert.EndsWith("/offsets/1AC2F605.json", url);
        // No lookup, no API call, no token: the path is a pure function of the
        // hash, so a 404 means "not published" and nothing else.
        Assert.Equal(url, ProfileFetch.UrlFor(Hash.ToLowerInvariant()));
    }

    [Fact]
    public void ShortNameIsUpperCaseAndEightCharacters()
    {
        Assert.Equal("1AC2F605", ProfileFetch.Short(Hash));
        Assert.Equal("1AC2F605", ProfileFetch.Short(Hash.ToLowerInvariant()));
        Assert.Throws<ProfileFetchException>(() => ProfileFetch.Short("abc"));
    }

    [Fact]
    public void AProfileForThisBuildIsAccepted()
    {
        var body = "{\"build\":{\"executableSha256\":\"" + Hash + "\"}}";
        ProfileFetch.Verify(body, Hash, "test");   // does not throw
    }

    /// <summary>
    /// The check that matters. A profile aimed at a different build has addresses
    /// that are wrong in the plausible way -- they resolve, they read, they return
    /// numbers. Using one "anyway" is the exact failure mode this project keeps
    /// having, so a mismatch is discarded before it ever reaches disk.
    /// </summary>
    [Fact]
    public void AProfileForADifferentBuildIsRejected()
    {
        var other = new string('A', 64);
        var body = "{\"build\":{\"executableSha256\":\"" + other + "\"}}";

        var ex = Assert.Throws<ProfileFetchException>(() => ProfileFetch.Verify(body, Hash, "test"));
        Assert.Contains("different build", ex.Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"build\":{}}")]
    public void AResponseThatIsNotAProfileIsRejected(string body) =>
        Assert.Throws<ProfileFetchException>(() => ProfileFetch.Verify(body, Hash, "test"));

    /// <summary>Absent consent file means NOT granted. Failing closed is the point.</summary>
    [Fact]
    public void ConsentDefaultsToDeniedAndIsRememberedOnceGranted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ir-consent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var consent = new FetchConsent(dir);
            Assert.False(consent.Granted);

            consent.Grant();
            Assert.True(new FetchConsent(dir).Granted);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
