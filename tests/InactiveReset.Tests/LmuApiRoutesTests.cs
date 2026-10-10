using System.IO.Compression;
using System.Net;
using System.Text;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LmuApiRoutesTests
{
    // Synthetic supported helper shapes; no proprietary menu code is included.
    internal const string Menu = """
        const restServiceUrl = "/rest/";
        const navigationRestUrl = `/navigation/`;
        const sessionsRestUrl = `${restServiceUrl}sessions/`;
        const garageRestUrl = `${restServiceUrl}garage/`;
        const optionsRestUrl = `${restServiceUrl}options/`;
        const raceRestUrl = `${restServiceUrl}race/`;
        function getState(config) { return getJson(`${navigationRestUrl}state`, config); }
        function navToMainMenu() { return postJson(`${navigationRestUrl}action/NAV_TO_MAIN_MENU`, null); }
        function getSessionSetup() { return getJson(`${garageRestUrl}UIScreen/SessionSetup`); }
        function getSessionSettings() { return getJson(`${sessionsRestUrl}`); }
        function getSessionAmounts() { return getJson(`${sessionsRestUrl}amount`); }
        function getWeatherSettings() { return getJson(`${sessionsRestUrl}weather`); }
        function getSettings() { return getJson(`${optionsRestUrl}settings`); }
        function getAllVehicles() { return getJson(`${sessionsRestUrl}getAllVehicles`); }
        function postTrack(id2) { return postText(`${raceRestUrl}track`, id2); }
        function postSessionSetting(sessionSetting, stepValue) { return postJsonAndGetJson(`${sessionsRestUrl}settings`, {sessionSetting, value: stepValue}); }
        function postSessionAmount(sessionType, amount) { return postJson(`${sessionsRestUrl}${sessionType.toLowerCase()}/sessions`, amount); }
        function postWeatherSetting(setting) { const url = `${sessionsRestUrl}weather/${setting.sessionType}/${setting.node}/${setting.key}`; return postJsonAndGetJson(url, setting.value); }
        function postWeatherPreset(session2, presetType) { return postJson(`${sessionsRestUrl}weather/${session2}/${presetType}`, null); }
        function updateSetting$2(key2, valueType, newValue) { return postJson(optionsRestUrl + valueType.toLowerCase(), {optionEntry: key2, value: newValue}); }
        function postCar(id2) { const body2 = {id: id2, paintBlobID: null, teamName: null, vehicleNumber: null}; return postText(`${garageRestUrl}SetCurrentVehicle`, JSON.stringify(body2)); }
        function getGrid(saveJson) { return postJsonAndGetJson(`${sessionsRestUrl}Championship/getGrid`, saveJson); }
        function getDefaultPresetForTrack(sceneDesc) { return postJson(`${sessionsRestUrl}SessionPresets/getDefaultPresetForTrack`, sceneDesc ? {trackName: sceneDesc} : null); }
        function refreshSetups() { return postJson(`${garageRestUrl}refreshsetups`, null); }
        function requestPreset() { return postJson(`${sessionsRestUrl}SessionPresets/requestPreset`, null); }
        function applyPreset(preset) { return postJson(`${sessionsRestUrl}SessionPresets/applyPreset`, preset); }
        function generateSaveFileFromSessionPreset(preset) { return postJson(`${sessionsRestUrl}SaveLoad/generateSaveFileFromSessionPreset`, preset); }
        function loadSaveFile(saveJson) { return postJson(`${sessionsRestUrl}SaveLoad/loadGame`, saveJson); }
        function getSessionGameState() { return getJson(`${sessionsRestUrl}GetGameState`); }
        """;

    [Fact]
    public void Recovers_changed_namespaces_leaves_dynamic_segments_and_port()
    {
        var routes = LmuApiRoutes.Parse(Menu.Replace("/rest/", "/v2/")
            .Replace("SaveLoad/loadGame", "Restore/open").Replace("/navigation/", "/nav2/"), 6401);
        Assert.Equal("http://127.0.0.1:6401/v2/sessions/Restore/open", routes.Resolve(HttpMethod.Post, "/rest/sessions/SaveLoad/loadGame").AbsoluteUri);
        Assert.Equal("/nav2/state", routes.Resolve(HttpMethod.Get, "/navigation/state").AbsolutePath);
        Assert.Equal("/v2/sessions/weather/PRACTICE1/Start/Temperature", routes.Resolve(HttpMethod.Post, "/rest/sessions/weather/PRACTICE1/Start/Temperature").AbsolutePath);
        Assert.Equal("/v2/options/long", routes.Resolve(HttpMethod.Post, "/rest/options/long").AbsolutePath);
        Assert.Equal("/v2/sessions/practice/sessions", routes.Resolve(HttpMethod.Post, "/rest/sessions/practice/sessions").AbsolutePath);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("payload")]
    [InlineData("method")]
    [InlineData("remote")]
    [InlineData("cycle")]
    [InlineData("conflict")]
    [InlineData("carPayload")]
    public void Refuses_unknown_or_ambiguous_contracts(string change)
    {
        var source = change switch
        {
            "missing" => Menu.Replace("function loadSaveFile", "function unknownHelper"),
            "duplicate" => Menu + "function loadSaveFile(saveJson) { return postJson('/other',saveJson); }",
            "payload" => Menu.Replace("value: stepValue", "steps: stepValue"),
            "method" => Menu.Replace("postJson(`${sessionsRestUrl}SaveLoad/loadGame`", "getJson(`${sessionsRestUrl}SaveLoad/loadGame`"),
            "remote" => Menu.Replace("\"/rest/\"", "\"https://example.com/\""),
            "cycle" => Menu.Replace("\"/rest/\"", "`${restServiceUrl}`"),
            "carPayload" => Menu.Replace("paintBlobID: null", "requiredNewField: null"),
            _ => Menu.Replace("SaveLoad/loadGame", "SessionPresets/applyPreset"),
        };
        Assert.Throws<GateException>(() => LmuApiRoutes.Parse(source));
    }

    [Fact]
    public void Ignores_comment_and_string_decoys_and_refuses_bad_dynamic_paths()
    {
        var routes = LmuApiRoutes.Parse("/* function loadSaveFile() {} */ const decoy = 'function loadSaveFile() {}'; const regex = /['\"]+/g; const nested = `outer${`inner${1}`}text`;\n" + Menu);
        Assert.Throws<GateException>(() => routes.Resolve(HttpMethod.Get, "/rest/sessions/SaveLoad/loadGame"));
        Assert.Throws<GateException>(() => routes.Resolve(HttpMethod.Post, "/rest/options/.."));
        Assert.Throws<GateException>(() => routes.Resolve(HttpMethod.Post, "//example.com/evil"));
    }

    [Fact]
    public void Package_replacement_and_settings_changes_invalidate_recovered_routes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ir-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Bin"));
        Directory.CreateDirectory(Path.Combine(root, "UserData", "player"));
        void Write(string source, int port)
        {
            var path = Path.Combine(root, "Bin", "UI.zip");
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("start\\assets\\app-updated.js").Open())) writer.Write(source);
            File.WriteAllText(Path.Combine(root, "UserData", "player", "Settings.JSON"), "{\"Miscellaneous\":{\"WebUI port\":" + port + "}}");
        }
        try
        {
            Write(Menu, 6397);
            var original = LmuApiRoutes.Load(root);
            Write(Menu.Replace("SaveLoad/loadGame", "SaveLoad/loadV2"), 6402);
            var updated = LmuApiRoutes.Load(root);
            Assert.NotEqual(original.SourceSha256, updated.SourceSha256);
            Assert.Equal("http://127.0.0.1:6402/rest/sessions/SaveLoad/loadV2", updated.Resolve(HttpMethod.Post, "/rest/sessions/SaveLoad/loadGame").AbsoluteUri);
            Write(Menu, 0);
            Assert.Throws<GateException>(() => LmuApiRoutes.Load(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Client_uses_recovered_routes_and_refuses_bad_contract_before_any_request()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"state\":{}}", Encoding.UTF8, "application/json") };
        }));
        var client = new LmuSessionClient(http, () => LmuApiRoutes.Parse(Menu.Replace("/navigation/", "/nav2/"), 6403));
        await client.NavigationAsync();
        Assert.Equal("http://127.0.0.1:6403/nav2/state", Assert.Single(paths));
        var unsupported = new LmuSessionClient(http, () => LmuApiRoutes.Parse(Menu.Replace("function applyPreset", "function unsupported")));
        await Assert.ThrowsAsync<GateException>(() => unsupported.NavigationAsync());
        Assert.Single(paths);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    [Fact]
    public async Task Renamed_write_route_is_used_once_and_failed_write_is_never_retried()
    {
        var posts = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                posts.Add(path);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("refused") };
            }
            var json = path == "/navigation/state"
                ? """{"state":{"settingMode":"SETTING_GRANDPRIX","gameState":"GSTATE_SETUP","navigationState":"NAV_MAIN_MENU"}}"""
                : """{"Example":{}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        var client = new LmuSessionClient(http, () => LmuApiRoutes.Parse(Menu.Replace("/rest/", "/v2/")
            .Replace("${sessionsRestUrl}settings", "${sessionsRestUrl}adjustSettings")));
        var body = System.Text.Json.Nodes.JsonNode.Parse("""{"action":"setting","key":"Example","steps":1}""")!.AsObject();
        await Assert.ThrowsAsync<GateException>(() => client.ChangeAsync(body));
        Assert.Equal("/v2/sessions/adjustSettings", Assert.Single(posts));
    }

    [Trait("Category", "LocalGame")]
    [LocalMenuFact]
    public void Installed_menu_resolves_all_supported_helpers_without_executing_the_game()
    {
        var routes = LmuApiRoutes.Load(LocalMenuFactAttribute.InstallRoot!);
        Assert.Equal("127.0.0.1", routes.Resolve(HttpMethod.Get, "/navigation/state").Host);
        Assert.Equal(64, routes.SourceSha256.Length);
    }
}

public sealed class LocalMenuFactAttribute : FactAttribute
{
    internal static string? InstallRoot { get; } = Find();
    private static string? Find()
    { try { return GameLauncher.Locate("data").Root; } catch (GameInstallException) { return null; } }
    public LocalMenuFactAttribute()
    { if (InstallRoot is null) Skip = "LMU is not installed; menu-package compatibility requires a local install."; }
}
