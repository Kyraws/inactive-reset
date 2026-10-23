using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LmuSessionClientTests
{
    [Fact]
    public async Task DirectNumericValuesUseFreshReadbackAndRepeatingThemDoesNotWriteAgain()
    {
        using var game = new MenuGame();
        await game.Client.ChangeAsync(Parse("""{"action":"sessionValue","key":"SESSSET_Practice_Length","value":90}"""));
        await game.Client.ChangeAsync(Parse("""{"action":"sessionValue","key":"SESSSET_Practice_Length","value":95}"""));
        var result = await game.Client.ChangeAsync(Parse("""{"action":"sessionValue","key":"SESSSET_Practice_Length","value":95}"""));
        Assert.Equal(95, result["settings"]!["SESSSET_Practice_Length"]!["currentValue"]!.GetValue<int>());
        Assert.Equal(new[] { 30, 5 }, game.Requests.Where(r => r.Path == "/rest/sessions/settings").Select(r => Parse(r.Body)["value"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData("SESSSET_practice1_starting_time", "Practice1StartingTime")]
    [InlineData("SESSSET_qualify_starting_time", "QualifyingStartingTime")]
    [InlineData("SESSSET_race_starting_time", "RaceStartingTime")]
    [InlineData("SESSSET_warmup_starting_time", "WarmupStartingTime")]
    public async Task ClockEntryUsesOnePresetWriteAndPreservesUnrelatedValues(string key, string field)
    {
        using var game = new MenuGame { ConditionsPreset = Parse("""{"Player":{"Race Conditions":{"Practice1StartingTime":-1,"QualifyingStartingTime":-1,"RaceStartingTime":-1,"WarmupStartingTime":-1},"Controls":{"fixture":"unchanged"}},"Weather":{"fixture":"unchanged"},"Grid":["owned-car"]}""") };
        var result = await game.Client.ChangeAsync(new JsonObject { ["action"] = "sessionValue", ["key"] = key, ["value"] = 945 });
        Assert.Equal(945, result["settings"]![key]!["currentValue"]!.GetValue<int>());
        var applied = Parse(Assert.Single(game.Requests, r => r.Path.EndsWith("/applyPreset")).Body);
        Assert.Equal(945, applied["Player"]!["Race Conditions"]![field]!.GetValue<int>());
        Assert.Equal("unchanged", applied["Player"]!["Controls"]!["fixture"]!.GetValue<string>());
        Assert.Equal("unchanged", applied["Weather"]!["fixture"]!.GetValue<string>());
        Assert.Equal("owned-car", Assert.Single(applied["Grid"]!.AsArray())!.GetValue<string>());
        await game.Client.ChangeAsync(new JsonObject { ["action"] = "sessionValue", ["key"] = key, ["value"] = -1 });
        Assert.Equal(-1, game.ConditionsPreset!["Player"]!["Race Conditions"]![field]!.GetValue<int>());
    }

    [Theory]
    [InlineData("SESSSET_flag_rules", 1)]
    [InlineData("SESSSET_Practice_Length", -1)]
    [InlineData("SESSSET_practice1_starting_time", -4)]
    [InlineData("SESSSET_practice1_starting_time", 1440)]
    public async Task InvalidDirectValuesDoNotWrite(string key, int value)
    {
        using var game = new MenuGame();
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(new JsonObject { ["action"] = "sessionValue", ["key"] = key, ["value"] = value }));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task RefusedClockEntryDoesNotClaimConfirmation()
    {
        using var game = new MenuGame { RefuseConditions = true, ConditionsPreset = Parse("""{"Player":{"Race Conditions":{"Practice1StartingTime":600}},"Weather":{}}""") };
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"sessionValue","key":"SESSSET_practice1_starting_time","value":945}""")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialPracticeConditionsPreserveOtherSettingsAndRequireReadback(bool refused)
    {
        using var game = new MenuGame { RefuseConditions = refused, ConditionsPreset = Parse("""{"Grid":["owned-car"],"Player":{"Race Conditions":{"Practice1StartingTime":600,"QualifyingStartingTime":900},"DRIVING AIDS":{"unchanged":true},"Controls":{"fixture":"unchanged"}},"Weather":{"Practice":{"Road":{"RealRoad":"green","WaterDepth":0,"LoadTemperaturesFromRealRoadFile":false},"Weather":[{"Temperature":20}]},"Qualifying":{"unchanged":true}}}""") };
        var body = Parse("""{"action":"practiceConditions","time":985,"road":"natural","waterDepth":-0.01,"loadTemperatures":false}""");
        if (refused) await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(body));
        else await game.Client.ChangeAsync(body);
        var applied = Parse(Assert.Single(game.Requests, r => r.Path.EndsWith("/applyPreset")).Body);
        Assert.Equal(985, applied["Player"]!["Race Conditions"]!["Practice1StartingTime"]!.GetValue<int>());
        Assert.Equal(900, applied["Player"]!["Race Conditions"]!["QualifyingStartingTime"]!.GetValue<int>());
        Assert.True(applied["Player"]!["DRIVING AIDS"]!["unchanged"]!.GetValue<bool>());
        Assert.Equal("unchanged", applied["Player"]!["Controls"]!["fixture"]!.GetValue<string>());
        Assert.Equal(20, applied["Weather"]!["Practice"]!["Weather"]![0]!["Temperature"]!.GetValue<int>());
        Assert.True(applied["Weather"]!["Qualifying"]!["unchanged"]!.GetValue<bool>());
        Assert.Equal(2, game.Requests.Count(r => r.Path.EndsWith("/requestPreset")));
    }

    [Theory]
    [InlineData(-1, "natural", 0)]
    [InlineData(1440, "natural", 0)]
    [InlineData(900, "arbitrary-file", 0)]
    [InlineData(900, "natural", 2)]
    public async Task InvalidOfficialConditionsDoNotWrite(int time, string road, double water)
    {
        using var game = new MenuGame();
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(new JsonObject { ["action"] = "practiceConditions", ["time"] = time, ["road"] = road, ["waterDepth"] = water }));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task LaunchRequiresTheActualMainMenuBeforeAnyWrites()
    {
        using var game = new MenuGame { Navigation = "NAV_EVENT" };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData("GSTATE_DYN", "NAV_EVENT", "SETTING_GRANDPRIX")]
    [InlineData("GSTATE_SETUP", "NAV_MAIN_MENU", "SETTING_MULTIPLAYER")]
    public async Task ActiveOrOnlineSessionCannotBeEdited(string state, string navigation, string mode)
    {
        using var game = new MenuGame { State = state, Navigation = navigation, Mode = mode };
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"setting","key":"SESSSET_Practice_Length","steps":1}""")));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task ReturnToMenuUnloadsTheEventBeforeAllowingSetupEdits()
    {
        using var game = new MenuGame { State = "GSTATE_DYN", Navigation = "NAV_EVENT" };
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"setting","key":"SESSSET_Practice_Length","steps":1}""")));
        await game.Client.ReturnToMenuAsync();
        Assert.Equal(new[] { "/navigation/action/NAV_BACK_TO_EVENT", "/navigation/action/NAV_TO_MAIN_MENU" }, game.Requests.Where(r => r.Method == "POST").Select(r => r.Path));
        await game.Client.ChangeAsync(Parse("""{"action":"setting","key":"SESSSET_Practice_Length","steps":1}"""));
    }

    [Fact]
    public async Task LaunchUsesPresetFlowAndPreservesUnrelatedPlayerSettings()
    {
        using var game = new MenuGame();
        var progress = new List<string>();
        await game.Client.StartAsync(progress.Add);
        var posts = game.Requests.Where(r => r.Method == "POST").ToArray();
        Assert.Equal(new[] { "/rest/sessions/SessionPresets/requestPreset", "/rest/garage/refreshsetups", "/rest/sessions/SessionPresets/applyPreset", "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", "/rest/sessions/SaveLoad/loadGame" }, posts.Select(p => p.Path));
        Assert.Equal("unchanged", JsonNode.Parse(posts[2].Body)! ["Player"]!["Controls"]!["fixture"]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse(posts[2].Body)!["Grid"]!.AsArray());
        Assert.Equal(42, JsonNode.Parse(posts[4].Body)! ["save"]!["generatedMarker"]!.GetValue<int>());
        Assert.Contains(progress, p => p.StartsWith("Session loaded"));
    }

    [Fact]
    public void BrowserNumberNormalizationDoesNotChangeSetup()
    {
        Assert.True(LmuSessionClient.SameSetup(Parse("""{"value":20.0,"array":[1.00]}"""), Parse("""{"array":[1],"value":20}""")));
        Assert.False(LmuSessionClient.SameSetup(Parse("""{"value":20.0}"""), Parse("""{"value":21}""")));
        Assert.False(LmuSessionClient.SameSetup(Parse("""{"value":20}"""), Parse("""{"value":"20"}""")));
    }

    [Fact]
    public async Task StaleDisplayedSetupCannotLaunch()
    {
        using var game = new MenuGame();
        var setup = await game.Client.SetupAsync();
        setup["settings"]!["SESSSET_Practice_Length"]!["currentValue"] = 90;
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }, expectedSetup: setup));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void LaunchGridMatchesDisplayedCountAndPreservesSavedSelection(int count, int entries)
    {
        var preset = Parse("""{"Grid":["player","ai1","ai2"],"Player":{"Game Options":{"Opponents":44}}}""");
        var setup = Parse("""{"selection":{"selectedCar":{"id":"player"}},"settings":{"SESSSET_Num_Opponents":{"currentValue":0}}}""");
        setup["settings"]!["SESSSET_Num_Opponents"]!["currentValue"] = count;
        var launch = LmuSessionClient.LaunchPreset(preset, setup);
        Assert.Equal(entries, launch["Grid"]!.AsArray().Count);
        Assert.Equal(count, launch["Player"]!["Game Options"]!["Opponents"]!.GetValue<int>());
        Assert.Equal(3, preset["Grid"]!.AsArray().Count);
        Assert.Equal(44, preset["Player"]!["Game Options"]!["Opponents"]!.GetValue<int>());
        setup["settings"]!["SESSSET_Num_Opponents"]!["currentValue"] = 3;
        Assert.Throws<GateException>(() => LmuSessionClient.LaunchPreset(preset, setup));
    }

    [Fact]
    public async Task ChangedGeneratedPresetCannotLoad()
    {
        using var game = new MenuGame { AlterGeneratedPreset = true };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
    }

    [Fact]
    public async Task PresetThatChangesDisplayedSettingsCannotGenerateOrLoad()
    {
        using var game = new MenuGame { AlterPresetOnApply = true };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("generateSaveFileFromSessionPreset") || r.Path.EndsWith("loadGame"));
    }

    [Fact]
    public async Task FailedGenerationNeverLoadsOrRetriesTheSession()
    {
        using var game = new MenuGame { FailPath = "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset" };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
        Assert.Single(game.Requests, r => r.Path == game.FailPath);
    }

    [Fact]
    public async Task NoEnabledSessionsOrUnownedContentCannotLaunch()
    {
        using var game = new MenuGame { Amount = 0 };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        game.Amount = 1;
        game.Owned = false;
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(_ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
    }

    [Fact]
    public async Task SettingStepsAreRelativeAndUnknownSettingsAreRejected()
    {
        using var game = new MenuGame();
        var result = await game.Client.ChangeAsync(Parse("""{"action":"setting","key":"SESSSET_Practice_Length","steps":-15}"""));
        Assert.Equal(45, result["settings"]!["SESSSET_Practice_Length"]!["currentValue"]!.GetValue<int>());
        var posted = JsonNode.Parse(Assert.Single(game.Requests, r => r.Path == "/rest/sessions/settings").Body)!;
        Assert.Equal(-15, posted["value"]!.GetValue<int>());
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"setting","key":"../arbitrary","steps":1}""")));
        Assert.Single(game.Requests, r => r.Path == "/rest/sessions/settings");
    }

    [Fact]
    public async Task OnlyOwnedCarsAndTracksCanBeSelectedAndTrackIdIsSentAsText()
    {
        using var game = new MenuGame();
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"car","id":"unowned"}""")));
        await game.Client.ChangeAsync(Parse("""{"action":"track","id":"track-id"}"""));
        var posted = Assert.Single(game.Requests, r => r.Path == "/rest/race/track" && r.Method == "POST");
        Assert.Equal("track-id", posted.Body);
        Assert.Equal("text/plain", posted.ContentType);
    }

    [Fact]
    public async Task AssistsUseAbsoluteLiveRangesAndCannotChangeOtherOptions()
    {
        using var game = new MenuGame();
        await game.Client.ChangeAsync(Parse("""{"action":"assist","key":"DRIVEAIDS_steering_help","value":2}"""));
        var posted = JsonNode.Parse(Assert.Single(game.Requests, r => r.Path == "/rest/options/long").Body)!;
        Assert.Equal(2, posted["value"]!.GetValue<int>());
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"assist","key":"DRIVEAIDS_steering_help","value":4}""")));
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"assist","key":"GFXOPT_test","value":1}""")));
        var setup = await game.Client.SetupAsync();
        Assert.False(setup["assists"]!.AsObject().ContainsKey("GFXOPT_test"));
    }

    [Fact]
    public async Task WeatherValidatesNodeAndKeyBeforeWriting()
    {
        using var game = new MenuGame();
        await game.Client.ChangeAsync(Parse("""{"action":"weather","session":"PRACTICE","node":"START","key":"WNV_TEMPERATURE","steps":-1}"""));
        Assert.Contains(game.Requests, r => r.Method == "POST" && r.Path == "/rest/sessions/weather/PRACTICE/START/WNV_TEMPERATURE" && r.Body == "-1");
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"weather","session":"PRACTICE","node":"../invalid","key":"WNV_TEMPERATURE","steps":1}""")));
        Assert.Single(game.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task WarmupCountCannotExceedOne()
    {
        using var game = new MenuGame();
        await Assert.ThrowsAsync<GateException>(() => game.Client.ChangeAsync(Parse("""{"action":"amount","session":"WARMUP","amount":2}""")));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData("QUALIFY1")]
    [InlineData("RACE1")]
    public async Task PracticeToolsRejectOtherSessionTypes(string session)
    {
        using var game = new MenuGame { State = "GSTATE_DYN", Navigation = "NAV_EVENT", Session = session };
        await Assert.ThrowsAsync<GateException>(() => game.Client.RequirePracticeAsync());
        game.Session = "PRACTICE1";
        await game.Client.RequirePracticeAsync();
    }

    [Fact]
    public async Task GameStateReadUsesExistingReadOnlyRoute()
    {
        using var game = new MenuGame();
        var state = await game.Client.GameStateAsync();
        Assert.True(state["playerVehicleLoaded"]!.GetValue<bool>());
        var request = Assert.Single(game.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/rest/sessions/GetGameState", request.Path);
        Assert.Equal("", request.Body);
    }

    [Fact]
    public async Task HoldBrakesOverrideUsesOnlyItsVerifiedOptionInPractice()
    {
        using var game = new MenuGame { State = "GSTATE_DYN", Navigation = "NAV_EVENT" };
        Assert.True(await game.Client.HoldBrakesAsync());
        await game.Client.SetHoldBrakesAsync(false);
        var post = Assert.Single(game.Requests, r => r.Method == "POST");
        Assert.Equal("/rest/options/long", post.Path);
        Assert.Equal("DRIVEAIDS_hold_brakes", JsonNode.Parse(post.Body)!["optionEntry"]!.GetValue<string>());
        Assert.Equal(0, JsonNode.Parse(post.Body)!["value"]!.GetValue<int>());
        game.Session = "RACE1";
        await Assert.ThrowsAsync<GateException>(() => game.Client.SetHoldBrakesAsync(true));
        Assert.Single(game.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task HoldBrakesOverrideRejectsUnknownOptionShapeBeforeWriting()
    {
        using var game = new MenuGame { State = "GSTATE_DYN", HoldBrakesMaximum = 2 };
        await Assert.ThrowsAsync<GateException>(() => game.Client.SetHoldBrakesAsync(false));
        Assert.DoesNotContain(game.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollingBrakeHoldScopeChecksGarageAndPreservesLivePreference(bool original)
    {
        using var game = new MenuGame { State = "GSTATE_DYN", HoldBrakes = original };
        using (var hold = RollingBrakeHold.Prepare(game.Client, () => true, _ => { }, default))
        {
            Assert.False(game.HoldBrakes);
            hold.Restore();
            Assert.Equal(original, game.HoldBrakes);
        }
        Assert.Equal(original ? 2 : 0, game.Requests.Count(r => r.Method == "POST"));
        game.InMonitor = false;
        Assert.Throws<GateException>(() => RollingBrakeHold.Prepare(game.Client, () => true, _ => { }, default));
        Assert.Equal(original ? 2 : 0, game.Requests.Count(r => r.Method == "POST"));
    }

    private static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();

    private sealed class MenuGame : HttpMessageHandler
    {
        public string State = "GSTATE_SETUP", Navigation = "NAV_MAIN_MENU", Mode = "SETTING_GRANDPRIX", Session = "PRACTICE1";
        public string? FailPath;
        public string StintState = "DISABLED";
        public bool AlterGeneratedPreset, AlterPresetOnApply;
        public JsonObject? ConditionsPreset;
        public bool RefuseConditions;
        private int _practiceLength = 60;
        public int Amount = 1;
        public int HoldBrakesMaximum = 1;
        public bool Owned = true;
        public bool HoldBrakes = true, InMonitor = true;
        public List<(string Method, string Path, string Body, string? ContentType)> Requests { get; } = [];
        private readonly HttpClient _http;
        public LmuSessionClient Client { get; }
        public MenuGame()
        {
            _http = new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://127.0.0.1:6397/") };
            Client = new LmuSessionClient(_http);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, path, body, request.Content?.Headers.ContentType?.MediaType));
            if (path == FailPath) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("fixture failure") };
            string response;
            switch (path)
            {
                case "/navigation/state": response = new JsonObject { ["state"] = new JsonObject { ["gameState"] = State, ["navigationState"] = Navigation, ["settingMode"] = Mode, ["gameSession"] = Session }, ["loadingStatus"] = new JsonObject { ["loading"] = false } }.ToJsonString(); break;
                case "/rest/sessions":
                    var settings = Parse("""{"SESSSET_Practice_Length":{"currentValue":LENGTH,"stringValue":"","numStepsTotal":1440},"SESSSET_Num_Opponents":{"currentValue":0,"stringValue":"0"}}""".Replace("LENGTH", _practiceLength.ToString()));
                    foreach (var (key, field) in new[] { ("SESSSET_practice1_starting_time", "Practice1StartingTime"), ("SESSSET_qualify_starting_time", "QualifyingStartingTime"), ("SESSSET_race_starting_time", "RaceStartingTime"), ("SESSSET_warmup_starting_time", "WarmupStartingTime") })
                        if (ConditionsPreset?["Player"]?["Race Conditions"]?[field] is { } minute)
                            settings[key] = new JsonObject { ["currentValue"] = minute.DeepClone(), ["stringValue"] = "", ["numStepsTotal"] = 1442 };
                    response = settings.ToJsonString(); break;
                case "/rest/sessions/amount": response = new JsonObject { ["PRACTICE"] = Amount, ["QUALIFY"] = 0, ["WARMUP"] = 0, ["RACE"] = 0 }.ToJsonString(); break;
                case "/rest/sessions/weather": response = """{"PRACTICE":{"START":{"WNV_TEMPERATURE":{"currentValue":20,"stringValue":"20 °"}}}}"""; break;
                case "/rest/options/settings": response = """{"DRIVEAIDS_hold_brakes":{"minValue":0,"maxValue":MAXIMUM,"stepValue":1,"currentValue":HOLD,"valueType":"LONG"},"DRIVEAIDS_steering_help":{"minValue":0,"maxValue":3,"stepValue":1,"currentValue":0,"valueType":"LONG"},"GFXOPT_test":{"currentValue":0}}""".Replace("MAXIMUM", HoldBrakesMaximum.ToString()).Replace("HOLD", HoldBrakes ? "1" : "0"); break;
                case "/rest/garage/UIScreen/SessionSetup": response = """{"selectedCar":{"id":"owned-car","isOwned":OWNED},"trackInfo":{"id":"track-id","isOwned":true}}""".Replace("OWNED", Owned.ToString().ToLowerInvariant()); break;
                case "/rest/sessions/getAllVehicles": response = """[{"id":"owned-car","isOwned":true,"fullPathTree":"WEC 2026, Hypercar, Test"},{"id":"unowned","isOwned":false}]"""; break;
                case "/rest/race/track": response = """[{"id":"track-id","owned":true}]"""; break;
                case "/rest/sessions/SessionPresets/requestPreset": response = ConditionsPreset?.ToJsonString() ?? """{"Grid":["owned-car"],"Player":{"Game Options":{"Opponents":0},"Controls":{"fixture":"unchanged"}},"Weather":{"fixture":"unchanged"}}"""; break;
                case "/rest/sessions/SessionPresets/applyPreset": if (ConditionsPreset is not null && !RefuseConditions) ConditionsPreset = Parse(body); if (AlterPresetOnApply) _practiceLength = 45; response = ""; break;
                case "/rest/garage/refreshsetups": response = ""; break;
                case "/rest/sessions/settings": _practiceLength += Parse(body)["value"]!.GetValue<int>(); response = ""; break;
                case "/rest/sessions/weather/PRACTICE/START/WNV_TEMPERATURE": response = ""; break;
                case "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset":
                    var generated = Parse(body);
                    if (AlterGeneratedPreset) generated["Player"]!["Game Options"]!["Opponents"] = 1;
                    response = new JsonObject { ["SessionPreset"] = generated, ["generatedMarker"] = 42 }.ToJsonString(); break;
                case "/rest/sessions/SaveLoad/loadGame": State = "GSTATE_DYN"; Navigation = "NAV_EVENT"; response = ""; break;
                case "/navigation/action/NAV_BACK_TO_EVENT": State = "GSTATE_SETUP"; response = ""; break;
                case "/navigation/action/NAV_TO_MAIN_MENU": Navigation = "NAV_MAIN_MENU"; response = ""; break;
                case "/rest/sessions/GetGameState": response = new JsonObject { ["playerVehicleLoaded"] = true, ["inMonitor"] = InMonitor, ["isReplayActive"] = false, ["MultiStintState"] = StintState }.ToJsonString(); break;
                case "/rest/options/long":
                    if (JsonNode.Parse(body)?["optionEntry"]?.GetValue<string>() == "DRIVEAIDS_hold_brakes")
                        HoldBrakes = JsonNode.Parse(body)!["value"]!.GetValue<int>() == 1;
                    response = ""; break;
                default: throw new InvalidOperationException($"Unexpected fixture request: {request.Method} {path}");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
