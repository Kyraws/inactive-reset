using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class AutomaticOffsetCacheTests : IDisposable
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ir-cache-test-" + Guid.NewGuid().ToString("N"));
    private readonly JsonObject _profile;
    private readonly byte[] _probe;
    private string CachePath => Path.Combine(_directory, Hash + ".auto.json");

    public AutomaticOffsetCacheTests()
    {
        // Tracked regression metadata, not a private executable capture.
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "offsets", "0F6DCAC1.json"));
        _profile = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        _profile["build"]!["executableSha256"] = Hash;
        _profile["build"]!["automaticResolverVersion"] = AutomaticOffsets.ResolverVersion;
        _probe = OffsetProfile.Parse(_profile.ToJsonString(), "test").Probe.Bytes;
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void An_exact_cache_is_reused_without_capturing_the_game()
    {
        File.WriteAllText(CachePath, _profile.ToJsonString());
        var expected = OffsetProfile.Load(CachePath);
        var loaded = AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => throw new InvalidOperationException("cache reuse must not capture"),
            (rva, length) =>
            {
                Assert.Equal(expected.Probe.Rva.Value, rva);
                Assert.Equal(_probe.Length, length);
                return _probe;
            }, out var discovered);

        Assert.False(discovered);
        Assert.Equal(Hash, loaded.ExecutableSha256);
        Assert.Equal(expected.Containers.Stride, loaded.Containers.Stride);
    }

    [Theory]
    [InlineData("malformed-json")]
    [InlineData("old-resolver")]
    [InlineData("wrong-build")]
    [InlineData("unresolved-probe")]
    [InlineData("changed-probe")]
    [InlineData("unreadable-probe")]
    public void An_invalid_cache_requires_discovery_and_capture_failure_does_not_replace_it(string problem)
    {
        switch (problem)
        {
            case "old-resolver": _profile["build"]!["automaticResolverVersion"] = AutomaticOffsets.ResolverVersion - 1; break;
            case "wrong-build": _profile["build"]!["executableSha256"] = new string('B', 64); break;
            case "unresolved-probe": _profile["probe"]!["confidence"] = "U"; break;
        }
        var original = problem == "malformed-json" ? "{truncated" : _profile.ToJsonString();
        File.WriteAllText(CachePath, original);
        var captures = 0;
        var discoveryFailure = new GateException("discovery has no usable image");

        var thrown = Assert.Throws<GateException>(() => AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => { captures++; throw discoveryFailure; },
            (_, _) => problem switch
            {
                "changed-probe" => new byte[_probe.Length],
                "unreadable-probe" => throw new MemoryAccessException("probe is unreadable"),
                _ => _probe,
            }, out _));

        Assert.Same(discoveryFailure, thrown);
        Assert.Equal(1, captures);
        Assert.Equal(original, File.ReadAllText(CachePath));
        Assert.Equal(CachePath, Assert.Single(Directory.GetFiles(_directory)));
    }

    [Fact]
    public void An_unresolvable_image_does_not_replace_the_existing_cache()
    {
        _profile["build"]!["automaticResolverVersion"] = AutomaticOffsets.ResolverVersion - 1;
        var original = _profile.ToJsonString();
        File.WriteAllText(CachePath, original);

        Assert.Throws<GateException>(() => AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => new byte[0x1000], (_, _) => _probe, out _));

        Assert.Equal(original, File.ReadAllText(CachePath));
        Assert.Equal(CachePath, Assert.Single(Directory.GetFiles(_directory)));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
