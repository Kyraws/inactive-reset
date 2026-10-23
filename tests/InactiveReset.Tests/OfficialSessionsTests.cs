using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Ui;
using InactiveReset.TestSupport;
using Xunit;

namespace InactiveReset.Tests;

public sealed class OfficialSessionsTests
{
    [Fact]
    public async Task SuccessfulSyncSerializesThroughTheProductionHttpEndpoint()
    {
        using var http = new HttpClient(new RaceControlHandler());
        await using var host = new UiTestHost(directory => new OfficialSessionsStore(directory, _ => Task.FromResult("ticket-secret"), http));
        using var response = await host.Client.PostAsJsonAsync("api/official-sessions/refresh", new JsonObject());
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(5, result!["events"]!.AsArray().Count);
        Assert.Equal(0, host.Menu.Writes);
    }
    [Fact]
    public async Task SyncSavesOnlyQualifyingAndRaceWithoutCredentialsOrPersonalPresetWrites()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Directory, "ui-lab.json"), "personal presets unchanged");
        var result = await fixture.Store.RefreshAsync();
        Assert.Equal(5, result["events"]!.AsArray().Count);
        foreach (var e in result["events"]!.AsArray())
        {
            Assert.Equal(new[] { "Qualifying", "Race" }, e!["sessions"]!.AsArray().Select(s => s!["type"]!.GetValue<string>()));
            Assert.All(e["sessions"]!.AsArray(), s =>
            {
                Assert.Equal("natural", s!["realRoad"]!.GetValue<string>());
                Assert.Equal("preset:HEAVY.RRBIN", s["practiceRealRoad"]!.GetValue<string>());
            });
        }
        var disk = File.ReadAllText(Path.Combine(fixture.Directory, "official-sessions.json"));
        Assert.DoesNotContain("secret", disk);
        Assert.Equal("personal presets unchanged", File.ReadAllText(Path.Combine(fixture.Directory, "ui-lab.json")));
        Assert.True(JsonNode.DeepEquals(result, fixture.Store.Read()));
        Assert.All(fixture.Handler.Paths, p => Assert.False(p.Contains("register") || p.Contains("join")));
        await fixture.Store.RefreshAsync();
        Assert.Equal(5, fixture.Store.Read()["events"]!.AsArray().Count);
        Assert.Equal(3, fixture.Handler.OverviewCalls / 2); // One overview per daily series, despite duplicate list entries.
    }

    [Fact]
    public async Task FailedCategoryKeepsItsSavedPresetsAndMarksThemStale()
    {
        using var fixture = new Fixture();
        await fixture.Store.RefreshAsync();
        fixture.Handler.FailWeekly = true;
        var result = await fixture.Store.RefreshAsync();
        var weekly = Assert.Single(result["events"]!.AsArray(), e => e!["category"]!.GetValue<string>() == "weekly");
        Assert.True(weekly!["stale"]!.GetValue<bool>());
        Assert.Contains("Weekly", Assert.Single(result["warnings"]!.AsArray())!.GetValue<string>());
        Assert.DoesNotContain("secret", result.ToJsonString());
    }

    [Fact]
    public async Task AlternatingConfigurationsInTheSameSeriesRemainSeparate()
    {
        using var fixture = new Fixture();
        fixture.Handler.RotatingDaily = true;
        var result = await fixture.Store.RefreshAsync();
        Assert.Equal(8, result["events"]!.AsArray().Count);
        var beginner = result["events"]!.AsArray().Where(e => e!["category"]!.GetValue<string>() == "beginner").ToArray();
        Assert.Equal(2, beginner.Length);
        Assert.NotEqual(beginner[0]!["id"]!.GetValue<string>(), beginner[1]!["id"]!.GetValue<string>());
        Assert.NotEqual(beginner[0]!["sessions"]![0]!["weather"]![0]!["temperature"]!.ToString(), beginner[1]!["sessions"]![0]!["weather"]![0]!["temperature"]!.ToString());
    }

    [Fact]
    public async Task RaceUsesTheLatestExplicitSurfaceBeforeItsNaturalProgression()
    {
        using var fixture = new Fixture();
        fixture.Handler.QualifyingRoad = "preset:MEDIUM.RRBIN";
        var result = await fixture.Store.RefreshAsync();
        Assert.All(result["events"]!.AsArray(), e =>
            Assert.All(e!["sessions"]!.AsArray(), s => Assert.Equal("preset:MEDIUM.RRBIN", s!["practiceRealRoad"]!.GetValue<string>())));
    }

    [Fact]
    public async Task NaturalWithoutAnExplicitBaseIsNotGuessedToBeGreen()
    {
        using var fixture = new Fixture();
        fixture.Handler.PracticeRoad = "natural";
        var result = await fixture.Store.RefreshAsync();
        Assert.All(result["events"]!.AsArray(), e =>
            Assert.All(e!["sessions"]!.AsArray(), s => Assert.True(s!.AsObject().ContainsKey("practiceRealRoad") && s["practiceRealRoad"] is null)));
    }

    [Fact]
    public async Task TotalFailureDoesNotReplaceSavedData()
    {
        using var fixture = new Fixture();
        await fixture.Store.RefreshAsync();
        var saved = File.ReadAllText(Path.Combine(fixture.Directory, "official-sessions.json"));
        fixture.Handler.FailAll = true;
        await Assert.ThrowsAsync<GateException>(() => fixture.Store.RefreshAsync());
        Assert.Equal(saved, File.ReadAllText(Path.Combine(fixture.Directory, "official-sessions.json")));
    }

    [Fact]
    public async Task UnexpectedSourcesAndMalformedWeatherCannotReplaceCategory()
    {
        using var fixture = new Fixture();
        await fixture.Store.RefreshAsync();
        fixture.Handler.UnexpectedDailyType = true;
        var result = await fixture.Store.RefreshAsync();
        Assert.Equal(3, result["warnings"]!.AsArray().Count);
        fixture.Handler.UnexpectedDailyType = false;
        fixture.Handler.InvalidWeather = true;
        await Assert.ThrowsAsync<GateException>(() => fixture.Store.RefreshAsync());
        Assert.Equal(5, fixture.Store.Read()["events"]!.AsArray().Count);
    }

    [Fact]
    public async Task CacheIsAvailableWithoutLoginAndInvalidCacheCanBeReplacedBySync()
    {
        using var fixture = new Fixture();
        Assert.Empty(fixture.Store.Read()["events"]!.AsArray());
        Assert.Empty(fixture.Handler.Paths);
        File.WriteAllText(Path.Combine(fixture.Directory, "official-sessions.json"), "{");
        Assert.Throws<GateException>(() => fixture.Store.Read());
        await fixture.Store.RefreshAsync();
        Assert.Equal(5, fixture.Store.Read()["events"]!.AsArray().Count);
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("official-sessions-tests-").FullName;
        public RaceControlHandler Handler { get; } = new();
        public OfficialSessionsStore Store { get; }
        private readonly HttpClient _http;
        public Fixture()
        {
            _http = new HttpClient(Handler);
            Store = new OfficialSessionsStore(Directory, _ => Task.FromResult("ticket-secret"), _http);
        }
        public void Dispose() { _http.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }

    private sealed class RaceControlHandler : HttpMessageHandler
    {
        public bool FailWeekly, FailAll, UnexpectedDailyType, InvalidWeather, RotatingDaily;
        public string PracticeRoad = "preset:HEAVY.RRBIN", QualifyingRoad = "natural";
        public int OverviewCalls;
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);
            Assert.Equal("raceos.gg", request.RequestUri.Host);
            if (path == "/authenticate")
            {
                Assert.False(request.Headers.Contains("Game-Authorization"));
                Assert.Equal("ticket-secret", JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!["token"]!.GetValue<string>());
                return Json("""{"accessToken":"access-secret","player":{"secret":"never persist"}}""");
            }
            Assert.Equal("Bearer access-secret", Assert.Single(request.Headers.GetValues("Game-Authorization")));
            if (FailAll || FailWeekly && path.EndsWith("/weekly")) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("secret server details") };
            if (path.EndsWith("/schedule")) return Json("""{"tiers":{},"frequency":{}}""");
            if (path.Contains("/daily/list/"))
            {
                var category = path.Split('/').Last();
                var e = Event(category, UnexpectedDailyType ? "hosted" : "daily");
                var second = e.DeepClone().AsObject();
                if (RotatingDaily)
                {
                    second["id"] = category + "-alternate";
                    second["configuration"]!["sessions"]![1]!["weather"]![0]!["temperature"] = "25";
                }
                return Json(new JsonArray(e, second).ToJsonString());
            }
            if (path.EndsWith("/overview"))
            {
                Interlocked.Increment(ref OverviewCalls);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!;
                Assert.Equal("daily", body["eventType"]!.GetValue<string>());
                var eventId = body["eventId"]!.GetValue<string>();
                var e = Event(eventId.Replace("-alternate", ""), "daily");
                if (eventId.EndsWith("-alternate")) e["configuration"]!["sessions"]![1]!["weather"]![0]!["temperature"] = "25";
                return Json(e.ToJsonString());
            }
            var kind = path.Split('/').Last();
            var series = Event(kind, kind == "weekly" ? "weekly" : "specialevent");
            series["seriesId"] = kind;
            series["title"] = kind;
            series["events"] = new JsonArray(new JsonObject { ["starts"] = "2030-10-23T15:00:00Z" });
            return Json(new JsonArray(series).ToJsonString());
        }
        private JsonObject Event(string id, string type)
        {
            var weather = new JsonArray();
            for (var i = 0; i < 5; i++) weather.Add(JsonNode.Parse("""{"sky":"2","rainChange":"0","temperature":"24","humidity":"55","windSpeed":"7","windDirection":"2"}"""));
            if (InvalidWeather) weather.RemoveAt(0);
            var sessions = new JsonArray();
            foreach (var session in new[] { "Practice", "Qualifying", "Race" })
                sessions.Add(new JsonObject { ["type"] = session, ["duration"] = "20m", ["timeOfDay"] = "16h25m", ["realRoad"] = session == "Practice" ? PracticeRoad : session == "Qualifying" ? QualifyingRoad : "natural", ["waterDepth"] = -0.01, ["weather"] = weather.DeepClone() });
            return new JsonObject
            {
                ["id"] = id, ["type"] = type,
                ["configuration"] = new JsonObject
                {
                    ["seriesId"] = id, ["title"] = id,
                    ["content"] = JsonNode.Parse("""{"track":{"name":"Bahrain","subItems":[{"name":"Bahrain Outer","scn":"BahrainWEC_Outer"}]},"cars":[{"name":"BMW_M4_LMGT3","friendly":"BMW_M4_LMGT3_2023"}]}"""),
                    ["sessions"] = sessions, ["settings"] = new JsonObject { ["tyreWarmers"] = true },
                },
            };
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
