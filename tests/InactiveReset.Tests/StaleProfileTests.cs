using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// A profile reanchor could not fully re-derive must not present as a verified
/// one.
///
/// These pin the two defects found on build 0F6DCAC1 (2026-08-20), where a
/// reanchor that failed on 20 of 40 addresses produced a profile whose every
/// build gate passed.
/// </summary>
public sealed class StaleProfileTests
{
    [Fact]
    public void Unvalidated_or_unmarked_profile_refuses_placement()
    {
        var source = FindUpwards(Path.Combine("offsets", "0F6DCAC1.json"));
        var json = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var path = Path.Combine(Path.GetTempPath(), $"ir-validation-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json.ToJsonString());
            OffsetProfile.Load(path).RequirePlacementValidated();
            json["build"]!["placementValidated"] = false;
            File.WriteAllText(path, json.ToJsonString());
            Assert.Throws<GateException>(() => OffsetProfile.Load(path).RequirePlacementValidated());
            json["build"]!.AsObject().Remove("placementValidated");
            File.WriteAllText(path, json.ToJsonString());
            Assert.Throws<GateException>(() => OffsetProfile.Load(path).RequirePlacementValidated());
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The real tracked profile, with only the probe's confidence changed.
    ///
    /// Built from <c>offsets/1AC2F605.json</c> rather than hand-written, so
    /// these assert against the schema the tool actually ships instead of a
    /// stripped-down idea of it.
    /// </summary>
    private static OffsetProfile Load(string confidence)
    {
        var source = FindUpwards(Path.Combine("offsets", "1AC2F605.json"));
        var json = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var probe = json["probe"]!.AsObject();
        probe["confidence"] = confidence;
        probe["note"] = "NOT re-derived for this build by reanchor; the value is stale.";

        var path = Path.Combine(Path.GetTempPath(), $"ir-probe-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json.ToJsonString());
        try
        {
            return OffsetProfile.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FindUpwards(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"could not find {relative} above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// The probe's confidence used to be dropped on the floor: ProbeSpec.Rva was
    /// a bare ulong, so "U" in the JSON was parsed by nothing and enforced by
    /// nothing.
    /// </summary>
    [Fact]
    public void An_unresolved_probe_keeps_its_confidence_through_parsing()
    {
        Assert.Equal(Confidence.Unresolved, Load("U").Probe.Rva.Confidence);
    }

    [Fact]
    public void A_re_derived_probe_is_established()
    {
        Assert.Equal(Confidence.Established, Load("E").Probe.Rva.Confidence);
    }

    /// <summary>
    /// The gate must refuse rather than compare. Bytes found at an address that
    /// belongs to the previous build say nothing about whether this profile fits
    /// the running one.
    /// </summary>
    [Fact]
    public void An_unresolved_probe_refuses_to_be_used()
    {
        var probe = Load("U").Probe;

        var ex = Assert.Throws<StaleOffsetException>(() => probe.Rva.Require("the build probe"));
        Assert.Contains("not re-derived", ex.Message);
    }

    [Fact]
    public void A_re_derived_probe_resolves_to_its_address()
    {
        Assert.Equal(0x00AB80C0UL, Load("E").Probe.Rva.Require("the build probe"));
    }

    /// <summary>
    /// A rule whose address was not re-derived must report as unread, not as a
    /// confident value. Reading it lands on the previous build's bytes, which is
    /// how four track-limits flags displayed a clean "0" on build 0F6DCAC1.
    /// </summary>
    [Fact]
    public void An_unresolved_rule_reports_as_unread()
    {
        var unread = new RuleState(
            "Track limits flag 0", -1, Consumption.ReadLive, true, null, null,
            Rva: 0x3008, Address: 0, Resolved: false);

        Assert.Equal("?", unread.Display);
        Assert.False(unread.Resolved);
    }

    [Fact]
    public void A_resolved_rule_reports_its_value()
    {
        var read = new RuleState(
            "Track limits flag 0", 2, Consumption.ReadLive, true, null, null,
            Rva: 0x3008, Address: 0x14003008);

        Assert.Equal("2", read.Display);
        Assert.True(read.Resolved);
    }
}
