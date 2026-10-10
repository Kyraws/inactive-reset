using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Ui;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LabPreferencesTests
{
    [Fact]
    public void PreferencesRoundTripWithoutTouchingGameFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "inactive-reset-lab-" + Guid.NewGuid());
        try
        {
            var store = new LabPreferencesStore(directory);
            var prefs = store.Read();
            prefs["theme"] = "daylight";
            prefs["style"] = "redline";
            prefs["plans"]!.AsArray().Add(Plan());
            store.Write(prefs);
            Assert.Equal("daylight", store.Read()["theme"]!.GetValue<string>());
            Assert.Equal("redline", store.Read()["style"]!.GetValue<string>());
            Assert.Single(store.Read()["plans"]!.AsArray());
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal("ui-lab.json", Path.GetFileName(Directory.GetFiles(directory)[0]));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("theme", "\"unknown\"")]
    [InlineData("density", "null")]
    [InlineData("schema", "2")]
    [InlineData("pins", "[null]")]
    [InlineData("style", "\"unknown\"")]
    public void RejectsInvalidPreferences(string key, string json)
    {
        var prefs = new LabPreferencesStore("unused").Read();
        prefs[key] = JsonNode.Parse(json);
        Assert.Throws<GateException>(() => LabPreferencesStore.Validate(prefs));
    }

    [Fact]
    public void RejectsInvalidPlansAndTooManyPlans()
    {
        var prefs = new LabPreferencesStore("unused").Read();
        var plan = Plan();
        plan["target"]!["amounts"]!["WARMUP"] = 2;
        prefs["plans"]!.AsArray().Add(plan);
        Assert.Throws<GateException>(() => LabPreferencesStore.Validate(prefs));
        prefs["plans"] = new JsonArray();
        for (var i = 0; i < 41; i++) prefs["plans"]!.AsArray().Add(Plan());
        Assert.Throws<GateException>(() => LabPreferencesStore.Validate(prefs));
    }

    [Fact]
    public void RejectedWriteKeepsPreviousPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "inactive-reset-lab-" + Guid.NewGuid());
        try
        {
            var store = new LabPreferencesStore(directory);
            var prefs = store.Read();
            store.Write(prefs);
            prefs["plans"]!.AsArray().Add(Plan());
            prefs["plans"]![0]!["target"]!["settings"]!["unrelated"] = 1;
            Assert.Throws<GateException>(() => store.Write(prefs));
            Assert.Empty(store.Read()["plans"]!.AsArray());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static JsonObject Plan() => JsonNode.Parse("""{"id":"test-plan","name":"Test","target":{"car":"car","track":"track","amounts":{"PRACTICE":1,"QUALIFY":0,"WARMUP":0,"RACE":0},"settings":{"SESSSET_Practice_Length":30},"assists":{"DRIVEAIDS_auto_wipers":1},"classes":["GT3"],"fullGrid":false,"weather":{"PRACTICE":{"START":{"WNV_TEMPERATURE":20}}}}}""")!.AsObject();
}
