using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LmuSessionClientTests
{
    [Fact]
    public async Task LaunchRequiresVisibleMenuConfirmationBeforeAnyRequest()
    {
        using var game = new MenuGame();
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(false, _ => { }));
        Assert.Empty(game.Requests);
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
    public async Task LaunchUsesPresetFlowAndPreservesUnrelatedPlayerSettings()
    {
        using var game = new MenuGame();
        var progress = new List<string>();
        await game.Client.StartAsync(true, progress.Add);
        var posts = game.Requests.Where(r => r.Method == "POST").ToArray();
        Assert.Equal(new[] { "/rest/sessions/SessionPresets/requestPreset", "/rest/garage/refreshsetups", "/rest/sessions/SessionPresets/applyPreset", "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", "/rest/sessions/SaveLoad/loadGame" }, posts.Select(p => p.Path));
        Assert.Equal("unchanged", JsonNode.Parse(posts[2].Body)! ["Player"]!["Controls"]!["fixture"]!.GetValue<string>());
        Assert.Equal(42, JsonNode.Parse(posts[4].Body)! ["save"]!["generatedMarker"]!.GetValue<int>());
        Assert.Contains(progress, p => p.StartsWith("Session loaded"));
    }

    [Fact]
    public async Task FailedGenerationNeverLoadsOrRetriesTheSession()
    {
        using var game = new MenuGame { FailPath = "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset" };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(true, _ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
        Assert.Single(game.Requests, r => r.Path == game.FailPath);
    }

    [Fact]
    public async Task NoEnabledSessionsOrUnownedContentCannotLaunch()
    {
        using var game = new MenuGame { Amount = 0 };
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(true, _ => { }));
        game.Amount = 1;
        game.Owned = false;
        await Assert.ThrowsAsync<GateException>(() => game.Client.StartAsync(true, _ => { }));
        Assert.DoesNotContain(game.Requests, r => r.Path.EndsWith("loadGame"));
    }

    [Fact]
    public async Task SettingStepsAreRelativeAndUnknownSettingsAreRejected()
    {
        using var game = new MenuGame();
        await game.Client.ChangeAsync(Parse("""{"action":"setting","key":"SESSSET_Practice_Length","steps":-15}"""));
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

    private static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();

    private sealed class MenuGame : HttpMessageHandler
    {
        public string State = "GSTATE_SETUP", Navigation = "NAV_MAIN_MENU", Mode = "SETTING_GRANDPRIX", Session = "PRACTICE1";
        public string? FailPath;
        public int Amount = 1;
        public bool Owned = true;
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
                case "/rest/sessions": response = """{"SESSSET_Practice_Length":{"currentValue":60,"stringValue":"","numStepsTotal":1440}}"""; break;
                case "/rest/sessions/amount": response = new JsonObject { ["PRACTICE"] = Amount, ["QUALIFY"] = 0, ["WARMUP"] = 0, ["RACE"] = 0 }.ToJsonString(); break;
                case "/rest/sessions/weather": response = """{"PRACTICE":{"START":{"WNV_TEMPERATURE":{"currentValue":20,"stringValue":"20 °"}}}}"""; break;
                case "/rest/options/settings": response = """{"DRIVEAIDS_steering_help":{"minValue":0,"maxValue":3,"stepValue":1,"currentValue":0,"valueType":"LONG"},"GFXOPT_test":{"currentValue":0}}"""; break;
                case "/rest/garage/UIScreen/SessionSetup": response = """{"selectedCar":{"id":"owned-car","isOwned":OWNED},"trackInfo":{"id":"track-id","isOwned":true}}""".Replace("OWNED", Owned.ToString().ToLowerInvariant()); break;
                case "/rest/sessions/getAllVehicles": response = """[{"id":"owned-car","isOwned":true,"fullPathTree":"WEC 2026, Hypercar, Test"},{"id":"unowned","isOwned":false}]"""; break;
                case "/rest/race/track": response = """[{"id":"track-id","owned":true}]"""; break;
                case "/rest/sessions/SessionPresets/requestPreset": response = """{"Grid":["owned-car"],"Player":{"Game Options":{"Opponents":0},"Controls":{"fixture":"unchanged"}},"Weather":{"fixture":"unchanged"}}"""; break;
                case "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset": response = """{"SessionPreset":{},"generatedMarker":42}"""; break;
                case "/rest/sessions/SaveLoad/loadGame": State = "GSTATE_DYN"; Navigation = "NAV_EVENT"; response = ""; break;
                case "/rest/sessions/GetGameState": response = """{"playerVehicleLoaded":true,"inMonitor":true}"""; break;
                default: response = ""; break;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
