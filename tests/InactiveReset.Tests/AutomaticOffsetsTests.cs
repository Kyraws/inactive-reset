using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class AutomaticOffsetsTests
{
    [Fact]
    public void Field_anchors_follow_changed_operands_and_refuse_missing_ambiguous_or_disagreeing_readers()
    {
        var seed = JsonNode.Parse("""
            {"anchors":[{"pattern":"F3 0F 10 81 ?? ?? ?? ?? 90", "operand":4},
                         {"pattern":"F3 0F 11 81 ?? ?? ?? ?? 91", "operand":4}]}
            """)!.AsObject();
        var image = new byte[80];
        void Put(int at, byte tail, uint value)
        {
            new byte[] { 0xF3, 0x0F, 0x10, 0x81 }.CopyTo(image, at);
            image[at + 2] = tail == 0x90 ? (byte)0x10 : (byte)0x11;
            BitConverter.GetBytes(value).CopyTo(image, at + 4);
            image[at + 8] = tail;
        }
        Put(4, 0x90, 0x15A0C);
        Assert.Throws<GateException>(() => AutomaticOffsets.ResolveField(image, seed, "pose"));
        Put(28, 0x91, 0x15A0C);
        Assert.Equal(0x15A0Cu, AutomaticOffsets.ResolveField(image, seed, "pose"));
        Put(28, 0x91, 0x15A14);
        Assert.Throws<GateException>(() => AutomaticOffsets.ResolveField(image, seed, "pose"));
        Put(28, 0x91, 0x15A0C);
        Put(52, 0x90, 0x15A0C);
        Assert.Throws<GateException>(() => AutomaticOffsets.ResolveField(image, seed, "pose"));
    }

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("D9B92CA9")]
    public void D9B92CA9_resolves_without_an_existing_profile() =>
        CheckSavedBuild("D9B92CA9", 0x15A04, 0x1CED8, 0x471D4);

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("29CE422A")]
    public void Build_29CE422A_resolves_without_an_existing_profile() =>
        CheckSavedBuild("29CE422A", 0x15A04, 0x1CED8, 0x471D4);

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("66942337")]
    public void Build_66942337_resolves_without_an_existing_profile() =>
        CheckSavedBuild("66942337", 0x15A0C, 0x1CEE0, 0x471E4);

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("0F6DCAC1")]
    public void Build_0F6DCAC1_resolves_without_an_existing_profile() =>
        CheckSavedBuild("0F6DCAC1", 0x159DC, 0x1CEA8, 0x47194);

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("66942337")]
    public void Discovery_follows_retuned_relocated_tuning_and_changed_pit_speed_field()
    {
        var image = File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var original = AutomaticOffsets.Discover(image, new string('A', 64), "test");
        var tuning = (int)OffsetProfile.ParseHex(original["engineModel"]!["tunables"]!["searchStartFactor"]!["rva"]!.GetValue<string>());
        var moved = tuning + 0x100;
        var readers = 0;
        foreach (var range in AutomaticOffsets.ExecutableRanges(image))
        for (var at = range.Start; at + 9 <= range.End; at++)
        {
            if (image[at] != 0xF3) continue;
            var op = at + 1;
            if (image[op] is >= 0x40 and <= 0x4F) op++;
            if (image[op] != 0x0F || image[op + 1] is not (0x10 or 0x59) ||
                (image[op + 2] & 0xC7) != 5) continue;
            var displacement = BitConverter.ToInt32(image, op + 3);
            var target = (long)op + 7 + displacement;
            if (target < tuning || target >= tuning + 24) continue;
            BitConverter.GetBytes(displacement + 0x100).CopyTo(image, op + 3);
            readers++;
        }
        Assert.True(readers >= 5);
        new float[] { 0.65f, 0.2f, 2f, 30f, 50f, 1f }.SelectMany(BitConverter.GetBytes)
            .ToArray().CopyTo(image, moved);
        var gate = (int)OffsetProfile.ParseHex(original["rules"]!["flagRules"]!["readBy"]!.GetValue<string>());
        var field = BitConverter.ToInt32(image, gate + 4) + 8;
        BitConverter.GetBytes(field).CopyTo(image, gate + 4);
        var clear = gate + 13 + (sbyte)image[gate + 12];
        BitConverter.GetBytes(field).CopyTo(image, clear + 2);
        var resolved = AutomaticOffsets.Discover(image, new string('B', 64), "simulated-update");
        foreach (var fieldName in new[] { "searchStartFactor", "searchStepFactor", "searchMaxFactor", "yawOffsetDegrees" })
        {
            var before = OffsetProfile.ParseHex(original["engineModel"]!["tunables"]![fieldName]!["rva"]!.GetValue<string>());
            var after = OffsetProfile.ParseHex(resolved["engineModel"]!["tunables"]![fieldName]!["rva"]!.GetValue<string>());
            Assert.Equal(before + 0x100, after);
        }
        Assert.Equal(original["rules"]!["flagRules"]!["rva"]!.GetValue<string>(),
            resolved["rules"]!["flagRules"]!["rva"]!.GetValue<string>());
    }

    private static void CheckSavedBuild(string build, int pose, int sector, int slot)
    {
        // Local research fixtures are intentionally not distributed with the app.
        var json = AutomaticOffsets.Discover(File.ReadAllBytes(LocalDumpFactAttribute.PathFor(build)),
            new string('A', 64), "test");
        var profile = OffsetProfile.Parse(json.ToJsonString(), "local");
        Assert.Equal((ulong)pose, profile.Containers.Field("vehCachedPose").Offset);
        Assert.Equal((ulong)sector, profile.Containers.Field("sector").Offset);
        Assert.Equal((ulong)slot, profile.Containers.Field("slotIndex").Offset);
        Assert.Equal(build != "0F6DCAC1", profile.SpotTable.IndexedDestination is not null);
        Assert.False(profile.PlacementValidated);
    }

    [Trait("Category", "LocalGame")]
    [LocalDumpFact("66942337")]
    public void Current_code_fingerprint_reuses_discovery_but_rejects_changes_outside_the_transform_probe()
    {
        var image = File.ReadAllBytes(LocalDumpFactAttribute.PathFor("66942337"));
        var directory = Path.Combine(Path.GetTempPath(), "inactive-reset-offsets-" + Guid.NewGuid().ToString("N"));
        var captures = 0;
        byte[] Capture() { captures++; return image; }
        byte[] Read(ulong at, int count) => image.AsSpan((int)at, count).ToArray();
        try
        {
            var first = AutomaticOffsets.LoadOrDiscover(directory, new string('A', 64), "test", Capture, Read, out var discovered);
            Assert.True(discovered);
            AutomaticOffsets.LoadOrDiscover(directory, new string('A', 64), "test", Capture, Read, out discovered);
            Assert.False(discovered);
            Assert.Equal(2, captures);
            var originalCache = File.ReadAllText(first.SourcePath!);
            var originalProbe = Read(first.Probe.Rva.Value, first.Probe.Bytes.Length);
            var tuning = SpotTableResolver.FindTuningBlock(image)!.Value;
            ReadOnlySpan<byte> maxMultiply = [0xF3, 0x0F, 0x59, 0x3D];
            var mutated = false;
            for (var at = 0; at < image.Length - 25; at++)
            {
                if (!image.AsSpan(at, 4).SequenceEqual(maxMultiply) ||
                    (long)at + 8 + BitConverter.ToInt32(image, at + 4) != tuning + 8) continue;
                mutated = true;
                image[at + 21] ^= 4; // Change a tuning consumer outside the cached transform probe.
                break;
            }
            Assert.True(mutated, "expected to find and mutate the tuning consumer");
            Assert.Equal(originalProbe, Read(first.Probe.Rva.Value, first.Probe.Bytes.Length));
            Assert.Throws<GateException>(() => AutomaticOffsets.LoadOrDiscover(directory, new string('A', 64), "test", Capture, Read, out _));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(originalCache), JsonNode.Parse(File.ReadAllText(first.SourcePath!))));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}

public sealed class LocalDumpFactAttribute : FactAttribute
{
    public LocalDumpFactAttribute(string build)
    {
        if (!File.Exists(PathFor(build)))
            Skip = $"Missing local mapped-game dump for build {build}: {PathFor(build)}. Captures are not distributed.";
    }

    public static string PathFor(string build) => build == "0F6DCAC1" &&
        Environment.GetEnvironmentVariable("INACTIVE_RESET_BASELINE_DUMP") is { } baseline
            ? baseline
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "artifacts",
                $"LMU_runtime_{build}.bin"));
}
