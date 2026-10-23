using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>Official RaceControl events and their Qualifying/Race practice presets. No registration or game writes.</summary>
internal sealed class OfficialSessionsStore(string dataDirectory, Func<CancellationToken, Task<string>> ticket,
    HttpClient? http = null)
{
    private static readonly HttpClient Remote = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _path = Path.Combine(dataDirectory, "official-sessions.json");
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly object _fileGate = new();
    private const int MaxBytes = 8_000_000;
    private static readonly string[] Categories = ["beginner", "intermediate", "advanced", "weekly", "special"];

    public JsonObject Read()
    {
        lock (_fileGate)
        {
            if (!File.Exists(_path)) return Empty();
            try
            {
                if (new FileInfo(_path).Length > MaxBytes) throw new InvalidDataException();
                var saved = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
                if (saved?["schema"]?.GetValue<int>() != 1 || saved["events"] is not JsonArray events || events.Count > 200)
                    throw new InvalidDataException();
                foreach (var e in events)
                    if (e is JsonObject item) ValidateEvent(item); else throw new InvalidDataException();
                return saved;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException or GateException)
            { throw new GateException("Saved official sessions could not be read. Refresh official sessions to replace this cache."); }
        }
    }

    public async Task<JsonObject> RefreshAsync(CancellationToken cancellation = default)
    {
        await _refresh.WaitAsync(cancellation);
        try
        {
            JsonObject saved;
            try { saved = Read(); } catch (GateException) { saved = Empty(); }
            var auth = await Request(HttpMethod.Post, "authenticate", null,
                new JsonObject { ["token"] = await ticket(cancellation), ["game"] = "lmu", ["platform"] = "steam" }, cancellation);
            var token = auth?["accessToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(token)) throw new GateException("RaceControl login failed. Sign in through LMU and try again.");
            var warnings = new JsonArray();
            JsonNode? schedule = null;
            try { schedule = await Request(HttpMethod.Get, "api/v1/daily/schedule", token, null, cancellation); }
            catch (Exception ex) when (IsConnectionError(ex)) { warnings.Add("Daily start times could not be refreshed."); }

            var results = await Task.WhenAll(Categories.Select(async category =>
            {
                try { return (Category: category, Events: await FetchCategory(category, token, schedule, cancellation), Error: (string?)null); }
                catch (Exception ex) when (IsConnectionError(ex))
                { return (Category: category, Events: (JsonArray?)null, Error: $"{CategoryName(category)} unavailable. Saved presets have been kept. {ex.Message}"); }
            }));
            var events = new JsonArray();
            var refreshedAt = DateTimeOffset.UtcNow.ToString("O");
            foreach (var result in results)
            {
                if (result.Error is not null) warnings.Add(result.Error);
                var items = result.Events ?? new JsonArray(saved["events"]!.AsArray()
                    .Where(e => e?["category"]?.GetValue<string>() == result.Category).Select(e => e!.DeepClone()).ToArray());
                foreach (var e in items)
                {
                    var item = e!.DeepClone().AsObject();
                    if (result.Events is not null) item["fetchedAt"] = refreshedAt;
                    item["stale"] = result.Events is null;
                    events.Add(item);
                }
            }
            if (results.All(r => r.Events is null)) throw new GateException("RaceControl could not refresh official sessions. Saved presets are still available.");
            var value = new JsonObject { ["schema"] = 1, ["fetchedAt"] = refreshedAt, ["events"] = events, ["warnings"] = warnings };
            var json = value.ToJsonString();
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new GateException("The official session list is too large to save.");
            lock (_fileGate)
            {
                Directory.CreateDirectory(dataDirectory);
                var temp = _path + ".tmp";
                try { File.WriteAllText(temp, json); File.Move(temp, _path, overwrite: true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new GateException("Official sessions could not be refreshed. Launch LMU, wait for its main menu and try again. Saved presets are still available."); }
        finally { _refresh.Release(); }
    }

    private async Task<JsonArray> FetchCategory(string category, string token, JsonNode? schedule, CancellationToken cancellation)
    {
        var daily = category is "beginner" or "intermediate" or "advanced";
        var response = await Request(HttpMethod.Get, daily ? $"api/v1/daily/list/{category}?take=12" : $"api/v1/special-events/{category}", token, null, cancellation);
        if (response is not JsonArray list || list.Count > 100) throw new GateException("RaceControl returned an unsupported event list.");
        var events = new Dictionary<string, JsonObject>();
        var seen = new HashSet<string>();
        foreach (var entry in list)
        {
            if (entry is not JsonObject e) throw new GateException("RaceControl returned an incomplete event.");
            var expected = daily ? "daily" : category == "weekly" ? "weekly" : "specialevent";
            if (Text(e, "type") != expected) throw new GateException("RaceControl returned an unexpected event type.");
            var config = e["configuration"] as JsonObject;
            var seriesId = daily ? config?["seriesId"]?.GetValue<string>() : e["seriesId"]?.GetValue<string>();
            if (seriesId is not { Length: > 0 and <= 100 }) throw new GateException("RaceControl returned an event without its series.");
            // A daily series can rotate between different cars, circuits and forecasts.
            var signature = ConfigurationKey(e, config);
            if (!seen.Add(signature)) continue;
            if (daily)
            {
                var overview = await Request(HttpMethod.Post, "api/v1/event/overview", token,
                    new JsonObject { ["game"] = "lmu", ["eventType"] = "daily", ["eventId"] = Text(e, "id") }, cancellation);
                config = overview?["configuration"] as JsonObject;
            }
            if (config is null) throw new GateException("RaceControl returned an event without its configuration.");
            var sessions = new JsonArray();
            string? startingRoad = null;
            foreach (var session in config["sessions"]?.AsArray() ?? throw new GateException("RaceControl omitted the sessions."))
            {
                var road = session?["realRoad"]?.GetValue<string>();
                if (road is not null && road != "natural") startingRoad = road;
                if (session?["type"]?.GetValue<string>() is "Qualifying" or "Race")
                {
                    var savedSession = session.DeepClone().AsObject();
                    // Standalone Practice has no preceding session from which "natural" can inherit rubber.
                    savedSession["practiceRealRoad"] = startingRoad;
                    sessions.Add(savedSession);
                }
            }
            var title = daily ? config["title"]?.DeepClone() : e["title"]?.DeepClone();
            var times = daily ? schedule?["tiers"]?[category]?.AsArray().FirstOrDefault(s => s?["seriesid"]?.GetValue<string>() == seriesId && s?["eventname"]?.GetValue<string>() == title?.GetValue<string>())?["times"]?.DeepClone()
                : new JsonArray((e["events"] as JsonArray ?? []).Select(s => s?["starts"]?.DeepClone()).ToArray());
            var id = category + ":" + seriesId + ":" + ConfigurationKey(e, config);
            var item = new JsonObject
            {
                ["id"] = id, ["category"] = category,
                ["title"] = title,
                ["content"] = config["content"]?.DeepClone(), ["settings"] = config["settings"]?.DeepClone(),
                ["sessions"] = sessions, ["times"] = times ?? new JsonArray(),
            };
            ValidateEvent(item);
            events.TryAdd(id, item);
        }
        return new JsonArray(events.Values.Select(e => (JsonNode)e).ToArray());
    }

    private async Task<JsonNode?> Request(HttpMethod method, string path, string? token, JsonObject? body, CancellationToken cancellation)
    {
        // Fixed service origin; event data never controls request hosts or paths.
        using var request = new HttpRequestMessage(method, new Uri("https://raceos.gg/" + path));
        if (token is not null) request.Headers.Add("Game-Authorization", "Bearer " + token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await (http ?? Remote).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode) throw new GateException($"RaceControl refused the request ({(int)response.StatusCode}).");
        await response.Content.LoadIntoBufferAsync(MaxBytes);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    private static void ValidateEvent(JsonObject e)
    {
        if (!Categories.Contains(Text(e, "category")) || Text(e, "id").Length > 210 || Text(e, "title").Length > 200 ||
            e["content"]?["track"]?["subItems"] is not JsonArray layouts || layouts.Count != 1 ||
            e["content"]?["cars"] is not JsonArray cars || cars.Count is < 1 or > 100 ||
            e["sessions"] is not JsonArray sessions || sessions.Count is < 1 or > 2 || e["settings"] is not JsonObject)
            throw new GateException("RaceControl returned an unsupported event configuration.");
        if (layouts[0] is not JsonObject layout || Text(layout, "scn").Length > 200)
            throw new GateException("RaceControl returned an unsupported track layout.");
        foreach (var car in cars)
            if (car is not JsonObject vehicle ||
                !(vehicle["name"]?.ToString() is { Length: > 0 and <= 200 } || vehicle["friendly"]?.ToString() is { Length: > 0 and <= 200 }))
                throw new GateException("RaceControl returned an unsupported vehicle.");
        var types = new HashSet<string>();
        foreach (var session in sessions)
        {
            if (session is not JsonObject s) throw new GateException("RaceControl returned an incomplete session.");
            if (Text(s, "type") is not ("Qualifying" or "Race") || !types.Add(Text(s, "type")) ||
                s["weather"] is not JsonArray weather || weather.Count != 5)
                throw new GateException("RaceControl returned unsupported session weather.");
            Text(s, "duration"); Text(s, "timeOfDay");
            if (s["practiceRealRoad"] is not null && s["practiceRealRoad"]!.GetValue<string>() is not
                ("green" or "preset:LIGHT.RRBIN" or "preset:MEDIUM.RRBIN" or "preset:HEAVY.RRBIN" or "preset:SATURATED.RRBIN"))
                throw new GateException("RaceControl returned an unsupported starting track surface.");
            foreach (var w in weather)
                foreach (var field in new[] { "sky", "rainChange", "temperature", "humidity", "windSpeed", "windDirection" })
                    if (!double.TryParse(w?[field]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 1000)
                        throw new GateException("RaceControl returned invalid weather values.");
        }
    }

    private static string Text(JsonObject value, string key) => value[key]?.GetValue<string>() is { Length: > 0 } text
        ? text : throw new GateException("RaceControl returned incomplete event data.");
    private static bool IsConnectionError(Exception ex) => ex is HttpRequestException or TaskCanceledException or GateException or System.Text.Json.JsonException or InvalidOperationException or FormatException;
    private static string CategoryName(string category) => char.ToUpperInvariant(category[0]) + category[1..] + " events";
    private static JsonObject Empty() => new() { ["schema"] = 1, ["events"] = new JsonArray(), ["warnings"] = new JsonArray() };
    private static string ConfigurationKey(JsonObject entry, JsonObject? config)
    {
        var fields = new JsonObject();
        foreach (var key in new[] { "title", "content", "sessions", "settings" }) fields[key] = config?[key]?.DeepClone();
        fields["title"] ??= entry["title"]?.DeepClone();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fields.ToJsonString())))[..16];
    }
}
