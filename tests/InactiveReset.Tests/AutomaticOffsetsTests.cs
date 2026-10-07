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

    [LocalDumpFact("D9B92CA9")]
    public void D9B92CA9_resolves_without_an_existing_profile() =>
        CheckSavedBuild("D9B92CA9", 0x15A04, 0x1CED8, 0x471D4);

    [LocalDumpFact("29CE422A")]
    public void Build_29CE422A_resolves_without_an_existing_profile() =>
        CheckSavedBuild("29CE422A", 0x15A04, 0x1CED8, 0x471D4);

    [LocalDumpFact("66942337")]
    public void Build_66942337_resolves_without_an_existing_profile() =>
        CheckSavedBuild("66942337", 0x15A0C, 0x1CEE0, 0x471E4);

    [LocalDumpFact("0F6DCAC1")]
    public void Build_0F6DCAC1_resolves_without_an_existing_profile() =>
        CheckSavedBuild("0F6DCAC1", 0x159DC, 0x1CEA8, 0x47194);

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

    [LocalDumpFact("66942337")]
    public void Cache_reuses_the_exact_build_and_rediscovers_after_build_or_probe_changes()
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
            Assert.Equal(1, captures);
            AutomaticOffsets.LoadOrDiscover(directory, new string('B', 64), "test", Capture, Read, out discovered);
            Assert.True(discovered);
            AutomaticOffsets.LoadOrDiscover(directory, new string('A', 64), "test", Capture,
                (_, count) => new byte[count], out discovered);
            Assert.True(discovered);
            Assert.Equal(3, captures);
            Assert.Equal(first.Containers.Stride, OffsetProfile.Load(first.SourcePath!).Containers.Stride);
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
