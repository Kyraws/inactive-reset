using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class OffsetProfileTests
{
    [Theory]
    [InlineData("0F6DCAC1")]
    [InlineData("1AC2F605")]
    public void Shipped_profiles_preserve_container_relative_lap_fields(string build)
    {
        var profile = OffsetProfile.Load(ProfileFixture.PathFor(build));
        Assert.StartsWith(build, profile.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
        foreach (var (name, type) in new[] {
            ("countLapFlag", "int32"), ("lapCountsNext", "byte"), ("pitFlag", "byte"),
            ("pitState", "int32"), ("lapStartEt", "double"), ("lapNumber", "int32"), ("sector", "int32") })
        {
            var field = profile.Containers.Field(name);
            Assert.Equal(type, field.Type);
            Assert.Equal(OffsetBase.Container, field.Base);
            Assert.InRange(field.Offset, 1UL, profile.Containers.Stride - 8);
        }
        Assert.Equal(profile.Containers.Field("countLapFlag").Offset + 4,
            profile.Containers.Field("lapCountsNext").Offset);
        Assert.Equal(profile.Containers.Field("countLapFlag").Offset + 8,
            profile.Containers.Field("lapStartEt").Offset);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 0, false)]
    [InlineData(null, 0, false)]
    [InlineData(false, AutomaticOffsets.ResolverVersion, true)]
    [InlineData(false, AutomaticOffsets.ResolverVersion - 1, false)]
    public void Placement_requires_live_validation_or_the_current_automatic_resolver(
        bool? validated, int resolver, bool accepted)
    {
        var json = ProfileFixture.Read();
        json["build"]!.AsObject().Remove("placementValidated");
        if (validated is not null) json["build"]!["placementValidated"] = validated.Value;
        json["build"]!["automaticResolverVersion"] = resolver;
        var profile = OffsetProfile.Parse(json.ToJsonString(), "test");
        if (accepted) profile.RequirePlacementValidated();
        else Assert.Throws<GateException>(profile.RequirePlacementValidated);
    }

    [Theory]
    [InlineData("U", Confidence.Unresolved)]
    [InlineData("E", Confidence.Established)]
    public void Probe_confidence_survives_parsing_and_controls_address_use(string confidence, Confidence expected)
    {
        var json = ProfileFixture.Read();
        json["probe"]!["confidence"] = confidence;
        json["probe"]!["note"] = "not re-derived";
        var probe = OffsetProfile.Parse(json.ToJsonString(), "test").Probe;
        Assert.Equal(expected, probe.Rva.Confidence);
        if (expected == Confidence.Unresolved)
            Assert.Contains("not re-derived", Assert.Throws<StaleOffsetException>(() => probe.Rva.Require("probe")).Message);
        else Assert.Equal(probe.Rva.Value, probe.Rva.Require("probe"));
    }

    [Theory]
    [InlineData(false, "?")]
    [InlineData(true, "2")]
    public void Rules_only_display_values_when_the_address_is_resolved(bool resolved, string display)
    {
        var rule = new RuleState("Track limits", 2, Consumption.ReadLive, true, null, null,
            Rva: 0x3008, Address: resolved ? 0x14003008UL : 0, Resolved: resolved);
        Assert.Equal(display, rule.Display);
        Assert.Equal(resolved, rule.Resolved);
    }
}
