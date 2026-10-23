using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>LMU's local menu API. Session changes use the same calls as the installed menu.</summary>
public sealed class LmuSessionClient
{
    private static readonly string[] NumericSettings = ["SESSSET_AI_Strength", "SESSSET_Damage_Multi", "SESSSET_Num_Opponents", "SESSSET_Practice_Length", "SESSSET_Qualify_Length", "SESSSET_WarmUp_Length", "SESSSET_Race_Laps", "SESSSET_race_time", "SESSSET_cuts_allowed", "SESSSET_tires_available_in_garage", "SESSSET_recon_pit_closed", "SESSSET_recon_pit_open"];
    private static readonly Dictionary<string, string> StartingTimes = new()
    {
        ["SESSSET_practice1_starting_time"] = "Practice1StartingTime", ["SESSSET_qualify_starting_time"] = "QualifyingStartingTime",
        ["SESSSET_race_starting_time"] = "RaceStartingTime", ["SESSSET_warmup_starting_time"] = "WarmupStartingTime",
    };
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

    public async Task<JsonObject> GameStateAsync(CancellationToken cancellation = default) =>
        Object(await RequestAsync(HttpMethod.Get, "/rest/sessions/GetGameState", cancellation: cancellation));

    public async Task<string> AuthenticationTicketAsync(CancellationToken cancellation = default)
    {
        // shortcut: this inline RaceOS route has no named menu helper; recheck it when LMU changes authentication.
        using var response = await _http.GetAsync(new Uri($"http://127.0.0.1:{_routes().Port}/rest/profile/getAuthSessionTicket"), cancellation);
        if (!response.IsSuccessStatusCode) throw new GateException("LMU could not authenticate with RaceControl. Wait for its main menu and try again.");
        var value = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return value?["authSessionTicket"]?.GetValue<string>() is { Length: > 0 and < 10000 } ticket
            ? ticket : throw new GateException("LMU did not provide a RaceControl authentication ticket.");
    }

    public async Task RequirePracticeAsync(CancellationToken cancellation = default)
    {
        var nav = await NavigationAsync(cancellation);
        var state = Object(nav["state"]);
        if (state["settingMode"]?.GetValue<string>() != "SETTING_GRANDPRIX" ||
            state["gameSession"]?.GetValue<string>()?.StartsWith("PRACTICE", StringComparison.Ordinal) != true ||
            state["gameState"]?.GetValue<string>() != "GSTATE_DYN" || nav["loadingStatus"]?["loading"]?.GetValue<bool>() == true)
            throw new GateException("Load an offline Practice session before using the practice tools.");
    }

    internal async Task<bool> HoldBrakesAsync(CancellationToken cancellation = default)
    {
        var settings = Object(await RequestAsync(HttpMethod.Get, "/rest/options/settings", cancellation: cancellation));
        var hold = settings["DRIVEAIDS_hold_brakes"] as JsonObject;
        if (hold?["valueType"]?.GetValue<string>() != "LONG" ||
            hold["minValue"]?.GetValue<double>() != 0 || hold["maxValue"]?.GetValue<double>() != 1 ||
            hold["stepValue"]?.GetValue<double>() != 1 || hold["currentValue"]?.GetValue<double>() is not (0 or 1))
            throw new GateException("LMU's Hold Brakes setting is unavailable or has an unsupported range.");
        return hold["currentValue"]!.GetValue<double>() == 1;
    }

    internal async Task SetHoldBrakesAsync(bool enabled, CancellationToken cancellation = default)
    {
        await RequirePracticeAsync(cancellation);
        await HoldBrakesAsync(cancellation);
        await PostAsync("/rest/options/long", new JsonObject
            { ["optionEntry"] = "DRIVEAIDS_hold_brakes", ["value"] = enabled ? 1 : 0 }, cancellation);
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
        string? absoluteKey = null;
        var absoluteValue = 0;
        switch (Text(body, "action"))
        {
            case "sessionValue":
            {
                var key = Text(body, "key");
                var clock = StartingTimes.TryGetValue(key, out var field);
                if (!clock && !NumericSettings.Contains(key)) throw new GateException("This setting does not support direct entry.");
                var value = Integer(body, "value", clock ? -3 : 0, clock ? 1439 : 10000);
                var settings = Object(await RequestAsync(HttpMethod.Get, "/rest/sessions", cancellation: cancellation));
                if (settings[key]?["currentValue"]?.GetValue<double>() is not { } before || !double.IsFinite(before) || before != Math.Truncate(before) || before < -3 || before > 10000)
                    throw new GateException("This session setting is not available in LMU.");
                absoluteKey = key; absoluteValue = value;
                if (value == before) break;
                if (clock)
                {
                    // Clock arrows move one minute per call; the preset sets the requested minute in one write.
                    var preset = await PresetAsync(cancellation);
                    if (preset["Player"]?["Race Conditions"] is not JsonObject conditions || !conditions.ContainsKey(field!))
                        throw new GateException("LMU does not expose this session's start time.");
                    conditions[field!] = value;
                    await RequireMenuAsync(cancellation);
                    await PostAsync("/rest/sessions/SessionPresets/applyPreset", preset, cancellation);
                }
                else
                {
                    await RequireMenuAsync(cancellation);
                    await PostAsync("/rest/sessions/settings", new JsonObject { ["sessionSetting"] = key, ["value"] = value - (int)before }, cancellation);
                }
                break;
            }
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
            case "practiceConditions":
            {
                var time = Integer(body, "time", 0, 1439);
                var road = Text(body, "road");
                if (road is not ("green" or "natural" or "preset:LIGHT.RRBIN" or "preset:MEDIUM.RRBIN" or "preset:HEAVY.RRBIN" or "preset:SATURATED.RRBIN"))
                    throw new GateException("This official session uses an unsupported track condition.");
                var water = body["waterDepth"]?.GetValue<double>() ?? throw new GateException("The track water depth is missing.");
                if (!double.IsFinite(water) || water < -0.01 || water > 1) throw new GateException("The track water depth is invalid.");
                var temperatures = body["loadTemperatures"]?.GetValue<bool>() ?? false;
                var preset = await PresetAsync(cancellation);
                if (preset["Player"]?["Race Conditions"] is not JsonObject conditions ||
                    !conditions.ContainsKey("Practice1StartingTime") || preset["Weather"]?["Practice"]?["Road"] is not JsonObject surface)
                    throw new GateException("LMU does not expose the required Practice conditions.");
                conditions["Practice1StartingTime"] = time;
                surface["RealRoad"] = road;
                surface["WaterDepth"] = water;
                surface["LoadTemperaturesFromRealRoadFile"] = temperatures;
                await PostAsync("/rest/sessions/SessionPresets/applyPreset", preset, cancellation);
                var readback = await PresetAsync(cancellation);
                var actual = readback["Weather"]?["Practice"]?["Road"];
                if (readback["Player"]?["Race Conditions"]?["Practice1StartingTime"]?.GetValue<int>() != time ||
                    actual?["RealRoad"]?.GetValue<string>() != road ||
                    actual?["WaterDepth"]?.GetValue<double>() is not { } depth || Math.Abs(depth - water) > 0.000001 ||
                    actual?["LoadTemperaturesFromRealRoadFile"]?.GetValue<bool>() != temperatures)
                    throw new GateException("LMU did not confirm the requested Practice time and track conditions. Refresh and review before retrying.");
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
        var updatedSetup = await SetupAsync(cancellation);
        if (absoluteKey is not null && updatedSetup["settings"]?[absoluteKey]?["currentValue"]?.GetValue<double>() != absoluteValue)
            throw new GateException("LMU did not confirm the requested value. Check its allowed range and try again.");
        return updatedSetup;
    }

    public async Task ReturnToMenuAsync(CancellationToken cancellation = default)
    {
        var nav = Object((await NavigationAsync(cancellation))["state"]);
        if (nav["settingMode"]?.GetValue<string>() != "SETTING_GRANDPRIX")
            throw new GateException("Session setup is available for local single-player sessions only.");
        if (nav["gameState"]?.GetValue<string>() == "GSTATE_DYN")
        {
            await PostAsync("/navigation/action/NAV_BACK_TO_EVENT", null, cancellation);
            await Task.Delay(500, cancellation);
        }
        await PostAsync("/navigation/action/NAV_TO_MAIN_MENU", null, cancellation);
        for (var i = 0; i < 120; i++)
        {
            nav = Object((await NavigationAsync(cancellation))["state"]);
            if (nav["gameState"]?.GetValue<string>() == "GSTATE_SETUP" && nav["navigationState"]?.GetValue<string>() == "NAV_MAIN_MENU") return;
            await Task.Delay(250, cancellation);
        }
        throw new GateException("LMU is still returning to the menu. Wait for it to finish, then refresh setup.");
    }

    public async Task StartAsync(Action<string> progress, CancellationToken cancellation = default,
        JsonObject? expectedSetup = null)
    {
        await RequireMenuAsync(cancellation);
        var setup = await SetupAsync(cancellation);
        if (expectedSetup is not null && !SameSetup(expectedSetup, setup))
            throw new GateException("LMU's setup changed since it was displayed. Refresh from LMU before starting.");
        var amounts = Object(setup["amounts"]);
        if (!amounts.Any(a => a.Value?.GetValue<int>() > 0)) throw new GateException("Enable at least one session before starting.");
        var selection = Object(setup["selection"]);
        if (selection["selectedCar"]?["isOwned"]?.GetValue<bool>() != true || selection["trackInfo"]?["isOwned"]?.GetValue<bool>() != true)
            throw new GateException("Choose a car and track that you own before starting.");
        var preset = await PresetAsync(cancellation);
        var launchPreset = LaunchPreset(preset, setup);
        progress("Preparing your session…");
        await PostAsync("/rest/garage/refreshsetups", null, cancellation);
        await PostAsync("/rest/sessions/SessionPresets/applyPreset", launchPreset, cancellation);
        var accepted = await SetupAsync(cancellation);
        if (!SameSessionValues(setup, accepted))
            throw new GateException("Applying LMU's preset changed the session setup. Refresh from LMU and review it before starting.");
        var save = Object(await PostAsync("/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", launchPreset, cancellation));
        if (save["SessionPreset"] is not JsonObject generated || !SameSetup(generated, launchPreset))
            throw new GateException("LMU's generated session does not match the accepted setup. Nothing was loaded.");
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

    internal static bool SameSessionValues(JsonObject expected, JsonObject actual) =>
        new[] { "settings", "amounts", "weather", "assists" }.All(key => SameSetup(expected[key], actual[key])) &&
        SameSetup(expected["selection"]?["selectedCar"], actual["selection"]?["selectedCar"]) &&
        SameSetup(expected["selection"]?["trackInfo"], actual["selection"]?["trackInfo"]);

    internal static bool SameSetup(JsonNode? left, JsonNode? right)
    {
        if (left is JsonObject a && right is JsonObject b)
            return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && SameSetup(p.Value, b[p.Key]));
        if (left is JsonArray x && right is JsonArray y)
            return x.Count == y.Count && x.Zip(y).All(p => SameSetup(p.First, p.Second));
        // Browser JSON round trips turn numeric tokens such as 20.0 into 20.
        if (left is JsonValue && right is JsonValue &&
            double.TryParse(left.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var l) &&
            double.TryParse(right.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r))
            return l.Equals(r);
        return JsonNode.DeepEquals(left, right);
    }

    internal static JsonObject LaunchPreset(JsonObject preset, JsonObject setup)
    {
        var count = setup["settings"]?["SESSSET_Num_Opponents"]?["currentValue"]?.GetValue<int>()
            ?? throw new GateException("LMU did not return the opponent count.");
        if (count < 0) throw new GateException("LMU returned an invalid opponent count.");
        var player = setup["selection"]?["selectedCar"]?["id"]?.GetValue<string>()
            ?? throw new GateException("Choose a car before starting.");
        var grid = preset["Grid"] as JsonArray ?? throw new GateException("LMU did not return an opponent grid.");
        var opponents = grid.Where(c => c?.GetValue<string>() != player).ToArray();
        if (count > opponents.Length)
            throw new GateException($"The grid has {opponents.Length} available opponents, but {count} were requested. Adjust the count or opponent classes.");
        var launch = (JsonObject)preset.DeepClone();
        launch["Player"]!["Game Options"]!["Opponents"] = count;
        // Save loading can use Grid independently of Opponents; never pass surplus entries.
        launch["Grid"] = new JsonArray((count == 0 ? [] : grid.Where(c => c?.GetValue<string>() == player)
            .Concat(opponents.Take(count))).Select(c => c?.DeepClone()).ToArray());
        return launch;
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
