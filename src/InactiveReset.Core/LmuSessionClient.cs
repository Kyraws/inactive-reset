using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>LMU's local menu API. Session changes use the same calls as the installed menu.</summary>
public sealed class LmuSessionClient
{
    private static readonly HttpClient LocalHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        BaseAddress = new Uri("http://127.0.0.1:6397/"),
        Timeout = TimeSpan.FromSeconds(15),
    };
    private readonly HttpClient _http;
    private readonly Func<LmuApiRoutes> _routes;

    public LmuSessionClient(HttpClient? http = null, string? dataDirectory = null)
    {
        _http = http ?? LocalHttp;
        _routes = http is null ? () => LmuApiRoutes.LoadInstalled(dataDirectory ?? "data") : LmuApiRoutes.Known;
    }

    internal LmuSessionClient(HttpClient http, Func<LmuApiRoutes> routes)
    { _http = http; _routes = routes; }

    public async Task<JsonObject> NavigationAsync(CancellationToken cancellation = default) =>
        Object(await RequestAsync(HttpMethod.Get, "/navigation/state", cancellation: cancellation));

    public async Task RequirePracticeAsync(CancellationToken cancellation = default)
    {
        var nav = await NavigationAsync(cancellation);
        var state = Object(nav["state"]);
        if (state["settingMode"]?.GetValue<string>() != "SETTING_GRANDPRIX" ||
            state["gameSession"]?.GetValue<string>()?.StartsWith("PRACTICE", StringComparison.Ordinal) != true ||
            state["gameState"]?.GetValue<string>() != "GSTATE_DYN" || nav["loadingStatus"]?["loading"]?.GetValue<bool>() == true)
            throw new GateException("Load an offline Practice session before using the practice tools.");
    }

    public async Task<JsonObject> SetupAsync(CancellationToken cancellation = default)
    {
        var requests = new[] { "/rest/garage/UIScreen/SessionSetup", "/rest/sessions", "/rest/sessions/amount", "/rest/sessions/weather", "/rest/options/settings" };
        var results = await Task.WhenAll(requests.Select(path => RequestAsync(HttpMethod.Get, path, cancellation: cancellation)));
        var assists = new JsonObject();
        foreach (var (key, value) in Object(results[4]))
            if (key.StartsWith("DRIVEAIDS_", StringComparison.Ordinal)) assists[key] = value?.DeepClone();
        return new JsonObject
        {
            ["selection"] = results[0], ["settings"] = results[1], ["amounts"] = results[2],
            ["weather"] = results[3], ["assists"] = assists,
        };
    }

    public async Task<JsonObject> CatalogAsync(CancellationToken cancellation = default)
    {
        var carsTask = RequestAsync(HttpMethod.Get, "/rest/sessions/getAllVehicles", cancellation: cancellation);
        var tracksTask = RequestAsync(HttpMethod.Get, "/rest/race/track", cancellation: cancellation);
        await Task.WhenAll(carsTask, tracksTask);
        return new JsonObject { ["cars"] = await carsTask, ["tracks"] = await tracksTask };
    }

    public async Task<JsonObject> ChangeAsync(JsonObject body, CancellationToken cancellation = default)
    {
        await RequireMenuAsync(cancellation);
        switch (Text(body, "action"))
        {
            case "setting":
            {
                var key = Text(body, "key");
                var settings = Object(await RequestAsync(HttpMethod.Get, "/rest/sessions", cancellation: cancellation));
                if (!settings.ContainsKey(key)) throw new GateException("This session setting is not available in LMU.");
                var steps = Integer(body, "steps", -10000, 10000);
                await PostAsync("/rest/sessions/settings", new JsonObject { ["sessionSetting"] = key, ["value"] = steps }, cancellation);
                break;
            }
            case "amount":
            {
                var session = Session(body);
                var amount = Integer(body, "amount", 0, session == "WARMUP" ? 1 : 4);
                await PostAsync($"/rest/sessions/{session.ToLowerInvariant()}/sessions", JsonValue.Create(amount), cancellation);
                break;
            }
            case "weather":
            {
                var session = Session(body, weather: true);
                var node = Text(body, "node");
                var key = Text(body, "key");
                var weather = await RequestAsync(HttpMethod.Get, "/rest/sessions/weather", cancellation: cancellation);
                if (weather?[session]?[node] is not JsonObject values || !values.ContainsKey(key))
                    throw new GateException("This weather control is not available in LMU.");
                await PostAsync($"/rest/sessions/weather/{session}/{node}/{key}", JsonValue.Create(Integer(body, "steps", -100, 100)), cancellation);
                break;
            }
            case "weatherPreset":
            {
                var session = Session(body, weather: true);
                var preset = Text(body, "preset");
                if (preset is not ("SUNNY" or "CLOUDY" or "RAINY" or "DEFAULT")) throw new GateException("Unknown weather preset.");
                await PostAsync($"/rest/sessions/weather/{session}/{preset}", null, cancellation);
                break;
            }
            case "assist":
            {
                var key = Text(body, "key");
                var settings = Object(await RequestAsync(HttpMethod.Get, "/rest/options/settings", cancellation: cancellation));
                if (!key.StartsWith("DRIVEAIDS_", StringComparison.Ordinal) || settings[key] is not JsonObject setting)
                    throw new GateException("Only available driving assists can be changed here.");
                var value = body["value"]?.GetValue<double>() ?? throw new GateException("An assist value is required.");
                var min = setting["minValue"]!.GetValue<double>();
                var max = setting["maxValue"]!.GetValue<double>();
                var step = setting["stepValue"]!.GetValue<double>();
                var type = setting["valueType"]!.GetValue<string>();
                if (!double.IsFinite(value) || value < min || value > max ||
                    (step > 0 && Math.Abs((value - min) / step - Math.Round((value - min) / step)) > 0.0001) ||
                    type is not ("LONG" or "FLOAT")) throw new GateException("The assist value is outside LMU's available range.");
                await PostAsync("/rest/options/" + type.ToLowerInvariant(), new JsonObject { ["optionEntry"] = key, ["value"] = value }, cancellation);
                break;
            }
            case "car":
            {
                var id = Text(body, "id");
                var cars = Array(await RequestAsync(HttpMethod.Get, "/rest/sessions/getAllVehicles", cancellation: cancellation));
                if (!cars.Any(c => c?["id"]?.GetValue<string>() == id && c["isOwned"]?.GetValue<bool>() == true))
                    throw new GateException("Choose an installed car that you own.");
                await PostAsync("/rest/garage/SetCurrentVehicle", new JsonObject { ["id"] = id }, cancellation);
                break;
            }
            case "track":
            {
                var id = Text(body, "id");
                var tracks = Array(await RequestAsync(HttpMethod.Get, "/rest/race/track", cancellation: cancellation));
                if (!tracks.Any(t => t?["id"]?.GetValue<string>() == id && t["owned"]?.GetValue<bool>() == true))
                    throw new GateException("Choose an installed track that you own.");
                await RequestAsync(HttpMethod.Post, "/rest/race/track", rawBody: id, cancellation: cancellation);
                break;
            }
            case "grid":
            {
                var classes = body["classes"] as JsonArray ?? throw new GateException("Choose the opponent classes.");
                var cars = Array(await RequestAsync(HttpMethod.Get, "/rest/sessions/getAllVehicles", cancellation: cancellation));
                var available = cars.Select(c => c?["fullPathTree"]?.GetValue<string>()?.Split(',').ElementAtOrDefault(1)?.Trim()).Select(c => c == "Hypercar" ? "Hyper" : c).Where(c => c is not null).ToHashSet();
                if (classes.Count > available.Count || classes.Any(c => c is null || !available.Contains(c.GetValue<string>())))
                    throw new GateException("An opponent class is not available in LMU.");
                var fullGrid = body["fullGrid"]?.GetValue<bool>() ?? false;
                var result = Object(await PostAsync("/rest/sessions/Championship/getGrid", new JsonObject { ["fullGrid"] = fullGrid, ["Filter"] = classes.DeepClone() }, cancellation));
                var preset = await PresetAsync(cancellation);
                preset["Grid"] = (result["grid"] as JsonArray ?? throw new GateException("LMU did not return an opponent grid.")).DeepClone();
                await PostAsync("/rest/sessions/SessionPresets/applyPreset", preset, cancellation);
                break;
            }
            case "trackDefaults":
            {
                var selection = Object(await RequestAsync(HttpMethod.Get, "/rest/garage/UIScreen/SessionSetup", cancellation: cancellation));
                var name = selection["trackInfo"]?["sceneDesc"]?.GetValue<string>() ?? throw new GateException("Choose a track first.");
                var preset = Object(await PostAsync("/rest/sessions/SessionPresets/getDefaultPresetForTrack", new JsonObject { ["trackName"] = name }, cancellation));
                await PostAsync("/rest/sessions/SessionPresets/applyPreset", preset, cancellation);
                break;
            }
            default: throw new GateException("Unknown session action.");
        }
        return await SetupAsync(cancellation);
    }

    public async Task ReturnToMenuAsync(CancellationToken cancellation = default)
    {
        var nav = Object((await NavigationAsync(cancellation))["state"]);
        if (nav["settingMode"]?.GetValue<string>() != "SETTING_GRANDPRIX")
            throw new GateException("Session setup is available for local single-player sessions only.");
        await PostAsync("/navigation/action/NAV_TO_MAIN_MENU", null, cancellation);
        for (var i = 0; i < 120; i++)
        {
            nav = Object((await NavigationAsync(cancellation))["state"]);
            if (nav["gameState"]?.GetValue<string>() == "GSTATE_SETUP" && nav["navigationState"]?.GetValue<string>() == "NAV_MAIN_MENU") return;
            await Task.Delay(250, cancellation);
        }
        throw new GateException("LMU is still returning to the menu. Wait for it to finish, then refresh setup.");
    }

    public async Task StartAsync(bool menuConfirmed, Action<string> progress, CancellationToken cancellation = default)
    {
        if (!menuConfirmed) throw new GateException("Dismiss LMU's startup prompt and confirm that its main menu is visible.");
        await RequireMenuAsync(cancellation);
        var amounts = Object(await RequestAsync(HttpMethod.Get, "/rest/sessions/amount", cancellation: cancellation));
        if (!amounts.Any(a => a.Value?.GetValue<int>() > 0)) throw new GateException("Enable at least one session before starting.");
        var selection = Object(await RequestAsync(HttpMethod.Get, "/rest/garage/UIScreen/SessionSetup", cancellation: cancellation));
        if (selection["selectedCar"]?["isOwned"]?.GetValue<bool>() != true || selection["trackInfo"]?["isOwned"]?.GetValue<bool>() != true)
            throw new GateException("Choose a car and track that you own before starting.");
        var preset = await PresetAsync(cancellation);
        if (preset["Player"]?["Game Options"]?["Opponents"]?.GetValue<int>() > 0 && preset["Grid"] is JsonArray { Count: 0 })
            throw new GateException("Choose opponent classes in Grid, or set the opponent count to zero.");
        progress("Preparing your session…");
        await PostAsync("/rest/garage/refreshsetups", null, cancellation);
        await PostAsync("/rest/sessions/SessionPresets/applyPreset", preset, cancellation);
        var save = Object(await PostAsync("/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", preset, cancellation));
        if (save["SessionPreset"] is not JsonObject) throw new GateException("LMU did not generate a valid session. Nothing was loaded.");
        await RequireMenuAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        // After LMU accepts loadGame, cancellation stops monitoring; it cannot undo a game load.
        await PostAsync("/rest/sessions/SaveLoad/loadGame", new JsonObject { ["save"] = save }, cancellation);
        for (var i = 0; i < 720; i++)
        {
            var nav = await NavigationAsync(cancellation);
            var state = Object(nav["state"]);
            var loading = nav["loadingStatus"]?["loading"]?.GetValue<bool>() == true;
            var percentage = nav["loadingStatus"]?["percentage"]?.GetValue<double>() ?? -1;
            progress(loading && percentage >= 0 ? $"Loading session · {Math.Clamp(percentage * 100, 0, 100):F0}%" : "Waiting for LMU to finish loading…");
            if (!loading && state["gameState"]?.GetValue<string>() == "GSTATE_DYN")
            {
                var active = await RequestAsync(HttpMethod.Get, "/rest/sessions/GetGameState", cancellation: cancellation, allowUnavailable: true);
                if (active?["playerVehicleLoaded"]?.GetValue<bool>() == true && active["inMonitor"]?.GetValue<bool>() == true)
                {
                    progress("Session loaded. Switch to LMU and press Drive.");
                    return;
                }
            }
            await Task.Delay(250, cancellation);
        }
        throw new GateException("LMU has not finished loading. Check its screen before trying again; the session may still load.");
    }

    private async Task RequireMenuAsync(CancellationToken cancellation)
    {
        var nav = await NavigationAsync(cancellation);
        var state = Object(nav["state"]);
        if (state["settingMode"]?.GetValue<string>() != "SETTING_GRANDPRIX" ||
            state["gameState"]?.GetValue<string>() != "GSTATE_SETUP" || state["navigationState"]?.GetValue<string>() != "NAV_MAIN_MENU" ||
            nav["loadingStatus"]?["loading"]?.GetValue<bool>() == true)
            throw new GateException("Return to LMU's main menu before changing session setup.");
    }

    private async Task<JsonObject> PresetAsync(CancellationToken cancellation) =>
        Object(await PostAsync("/rest/sessions/SessionPresets/requestPreset", null, cancellation));

    private Task<JsonNode?> PostAsync(string path, JsonNode? body, CancellationToken cancellation) =>
        RequestAsync(HttpMethod.Post, path, body, cancellation: cancellation);

    private async Task<JsonNode?> RequestAsync(HttpMethod method, string path, JsonNode? body = null, string? rawBody = null, CancellationToken cancellation = default, bool allowUnavailable = false)
    {
        using var request = new HttpRequestMessage(method, _routes().Resolve(method, path));
        if (method == HttpMethod.Post) request.Content = new StringContent(rawBody ?? body?.ToJsonString() ?? "null", Encoding.UTF8, rawBody is null ? "application/json" : "text/plain");
        try
        {
            using var response = await _http.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            if (allowUnavailable && response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) return null;
            if (!response.IsSuccessStatusCode) throw new GateException($"LMU refused {path} ({(int)response.StatusCode}). {text[..Math.Min(text.Length, 400)]}");
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (HttpRequestException ex) { throw new GateException($"LMU's menu connection is unavailable. Launch local play and wait for its main menu. {ex.Message}"); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new GateException("LMU did not respond in time. Check the game before repeating the action."); }
    }

    private static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new GateException("LMU returned an unexpected menu response.");
    private static JsonArray Array(JsonNode? node) => node as JsonArray ?? throw new GateException("LMU returned an unexpected catalog response.");
    private static string Text(JsonObject body, string key) => body[key]?.GetValue<string>() ?? throw new GateException($"{key} is required.");
    private static int Integer(JsonObject body, string key, int min, int max)
    {
        var value = body[key]?.GetValue<int>() ?? throw new GateException($"{key} is required.");
        if (value < min || value > max) throw new GateException($"{key} must be between {min} and {max}.");
        return value;
    }
    private static string Session(JsonObject body, bool weather = false)
    {
        var value = Text(body, "session");
        if (value is not ("PRACTICE" or "QUALIFY" or "RACE" or "WARMUP") || weather && value == "WARMUP") throw new GateException("Unknown session type.");
        return value;
    }
}
