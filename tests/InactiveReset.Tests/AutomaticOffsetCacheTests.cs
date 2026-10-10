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
        _profile = ProfileFixture.Read();
        _profile["build"]!["executableSha256"] = Hash;
        _profile["build"]!["automaticResolverVersion"] = AutomaticOffsets.ResolverVersion;
        _probe = OffsetProfile.Parse(_profile.ToJsonString(), "test").Probe.Bytes;
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void An_exact_cache_is_reused_only_after_current_code_derivation()
    {
        File.WriteAllText(CachePath, _profile.ToJsonString());
        var expected = OffsetProfile.Load(CachePath);
        var captures = 0;
        var derivations = 0;
        var loaded = AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => { captures++; return new byte[0x1000]; },
            (rva, length) =>
            {
                Assert.Equal(expected.Probe.Rva.Value, rva);
                Assert.Equal(_probe.Length, length);
                return _probe;
            }, out var discovered, (_, _, _) => { derivations++; return _profile.DeepClone().AsObject(); });

        Assert.False(discovered);
        Assert.Equal(Hash, loaded.ExecutableSha256);
        Assert.Equal(expected.Containers.Stride, loaded.Containers.Stride);
        Assert.Equal(1, captures);
        Assert.Equal(1, derivations);
    }

    [Fact]
    public void A_different_executable_hash_gets_its_own_cache()
    {
        var original = _profile.ToJsonString();
        File.WriteAllText(CachePath, original);
        var otherHash = new string('B', 64);
        var other = _profile.DeepClone().AsObject();
        other["build"]!["executableSha256"] = otherHash;
        var loaded = AutomaticOffsets.LoadOrDiscover(_directory, otherHash, "test",
            () => new byte[0x1000], (_, _) => _probe, out var discovered, (_, hash, _) =>
            {
                Assert.Equal(otherHash, hash);
                return other;
            });
        Assert.True(discovered);
        Assert.Equal(otherHash, loaded.ExecutableSha256);
        Assert.Equal(Path.Combine(_directory, otherHash + ".auto.json"), loaded.SourcePath);
        Assert.Equal(original, File.ReadAllText(CachePath));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData("sector")]
    [InlineData("stride")]
    [InlineData("rule")]
    [InlineData("model")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("old-resolver")]
    [InlineData("wrong-build")]
    [InlineData("unresolved-probe")]
    public void Altered_cached_values_are_repaired_despite_an_unchanged_build_and_probe(string field)
    {
        var altered = _profile.DeepClone();
        switch (field)
        {
            case "sector": altered["containers"]!["offsets"]!["sector"]!["off"] = "0x1CEE8"; break;
            case "stride": altered["containers"]!["stride"] = "0x47300"; break;
            case "rule": altered["rules"]!["flagRules"]!["rva"] = "0x3100"; break;
            case "model": altered["engineModel"]!["fallback"]!["searchStartFactor"] = 0.75; break;
            case "old-resolver": altered["build"]!["automaticResolverVersion"] = AutomaticOffsets.ResolverVersion - 1; break;
            case "wrong-build": altered["build"]!["executableSha256"] = new string('B', 64); break;
            case "unresolved-probe": altered["probe"]!["confidence"] = "U"; break;
        }
        File.WriteAllText(CachePath, field == "null" ? "null" : field == "malformed" ? "{truncated" : altered.ToJsonString());
        AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test", () => new byte[0x1000],
            (_, _) => _probe, out var discovered, (_, _, _) => _profile.DeepClone().AsObject());
        Assert.True(discovered);
        Assert.True(JsonNode.DeepEquals(_profile, JsonNode.Parse(File.ReadAllText(CachePath))));
    }

    [Fact]
    public void A_changed_live_probe_during_capture_never_creates_a_cache()
    {
        Assert.Throws<GateException>(() => AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => new byte[0x1000], (_, _) => new byte[_probe.Length], out _,
            (_, _, _) => _profile.DeepClone().AsObject()));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Capture_failure_preserves_the_existing_cache_without_reading_its_probe()
    {
        var original = _profile.ToJsonString();
        File.WriteAllText(CachePath, original);
        var captures = 0;
        var discoveryFailure = new GateException("discovery has no usable image");

        var thrown = Assert.Throws<GateException>(() => AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => { captures++; throw discoveryFailure; },
            (_, _) => throw new InvalidOperationException("probe must not be read after failed capture"), out _));

        Assert.Same(discoveryFailure, thrown);
        Assert.Equal(1, captures);
        Assert.Equal(original, File.ReadAllText(CachePath));
        Assert.Equal(CachePath, Assert.Single(Directory.GetFiles(_directory)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_live_probe_preserves_an_existing_cache(bool unreadable)
    {
        var original = _profile.ToJsonString();
        File.WriteAllText(CachePath, original);
        byte[] Read(ulong _, int length) => unreadable
            ? throw new MemoryAccessException("probe is unreadable") : new byte[length];
        void Load() => AutomaticOffsets.LoadOrDiscover(_directory, Hash, "test",
            () => new byte[0x1000], Read, out _, (_, _, _) => _profile.DeepClone().AsObject());
        if (unreadable) Assert.Throws<MemoryAccessException>(Load);
        else Assert.Throws<GateException>(Load);
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
