using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using InactiveReset.TestSupport;
using Xunit;

namespace InactiveReset.Tests;

public sealed class UiServerTests
{
    [Fact]
    public async Task EacAllowsSinglePlayerRestEditingWithoutReadingGameMemory()
    {
        await using var host = new UiTestHost { Protected = true };
        using var response = await host.Client.PostAsJsonAsync("api/session/action", new { action = "setting", key = "SESSSET_Practice_Length", steps = 30 });
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(result!["error"]);
        Assert.Equal(90, result["settings"]!["SESSSET_Practice_Length"]!["currentValue"]!.GetValue<int>());
        var state = await State(host);
        Assert.True(state["game"]!["protected"]!.GetValue<bool>());
        Assert.False(state["connected"]!.GetValue<bool>());
        Assert.Null(state["error"]);
        Assert.Contains("EAC", state["memoryUnavailableReason"]!.GetValue<string>());
        Assert.Equal(1, host.Menu.Writes);
        Assert.Equal(0, host.AttachmentReads);
    }

    [Theory]
    [InlineData("api/place")]
    [InlineData("api/tyres")]
    [InlineData("api/rules")]
    public async Task EacRejectsMemoryActionsBeforeAnyAttachmentOrMenuWrite(string path)
    {
        await using var host = new UiTestHost { Protected = true };
        using var response = await host.Client.PostAsJsonAsync(path, new { checkpoint = "test", trackLimits = true });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("EAC", (await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>());
        Assert.Equal(0, host.Menu.Writes);
        Assert.Equal(0, host.AttachmentReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultiplayerStillRejectsRestChangesInEitherLaunchMode(bool protectedLaunch)
    {
        await using var host = new UiTestHost { Protected = protectedLaunch };
        host.Menu.Mode = "SETTING_MULTIPLAYER";
        using var response = await host.Client.PostAsJsonAsync("api/session/action", new { action = "setting", key = "SESSSET_Practice_Length", steps = 30 });
        Assert.NotNull((await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]);
        Assert.Equal(0, host.Menu.Writes);
        Assert.Equal(0, host.AttachmentReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MenuCannotCaptureLeftoverTelemetryInEitherLaunchMode(bool protectedLaunch)
    {
        await using var host = new UiTestHost { Protected = protectedLaunch };
        using var response = await host.Client.PostAsJsonAsync("api/capture", new { quick = true });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("Load a session", (await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(host.DataDirectory, "checkpoints")));
        Assert.Equal(0, host.AttachmentReads);
    }

    [Fact]
    public async Task OfficialCacheReadsOfflineAndFailedRefreshMakesNoMenuWrites()
    {
        await using var host = new UiTestHost();
        var cache = await host.Client.GetFromJsonAsync<JsonObject>("api/official-sessions");
        Assert.Empty(cache!["events"]!.AsArray());
        using var failed = await host.Client.PostAsJsonAsync("api/official-sessions/refresh", new JsonObject());
        Assert.False(failed.IsSuccessStatusCode);
        Assert.Contains("No RaceControl login", await failed.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Menu.Writes);
    }
    [Fact]
    public async Task MalformedCheckpointDoesNotBreakTheStateResponse()
    {
        await using var host = new UiTestHost();
        host.SeedLibrary();
        var state = await State(host);
        Assert.Equal("Valid checkpoint", Assert.Single(state["checkpoints"]!.AsArray())!["name"]!.GetValue<string>());
        Assert.Contains("broken.json", Assert.Single(state["checkpointErrors"]!.AsArray())!.GetValue<string>());
        Assert.Equal(0, host.Menu.Writes);
    }

    [Fact]
    public async Task RootServesTheCurrentPageAndAllReferencedAssets()
    {
        await using var host = new UiTestHost();
        var html = await host.Client.GetStringAsync("");
        Assert.Contains("id=\"practicePanel\"", html);
        Assert.Equal(html, await host.Client.GetStringAsync("lab"));
        foreach (var asset in Regex.Matches(html, "(?:src|href)=\"(/[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct())
            Assert.False(string.IsNullOrWhiteSpace(await host.Client.GetStringAsync(asset)));
    }

    [Theory]
    [InlineData("GET", "api/place", null, null)]
    [InlineData("POST", "api/state", "application/json", "{}")]
    [InlineData("POST", "api/lab/preferences", "text/plain", "{}")]
    [InlineData("POST", "api/lab/preferences", "application/json", "{")]
    public async Task InvalidRequestsReturnAReadableErrorWithoutMenuWrites(string method, string path, string? contentType, string? body)
    {
        await using var host = new UiTestHost();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType!);
        using var response = await host.Client.SendAsync(request);
        Assert.False(response.IsSuccessStatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0, host.Menu.Writes);
    }

    [Fact]
    public async Task PreferencesPersistAndConcurrentSavesLeaveOneCompleteDocument()
    {
        await using var host = new UiTestHost();
        var first = (await host.Client.GetFromJsonAsync<JsonObject>("api/lab/preferences"))!;
        var second = first.DeepClone().AsObject();
        first["density"] = "compact";
        second["theme"] = "daylight";
        var responses = await Task.WhenAll(host.Client.PostAsJsonAsync("api/lab/preferences", first),
            host.Client.PostAsJsonAsync("api/lab/preferences", second));
        foreach (var response in responses) { using (response) response.EnsureSuccessStatusCode(); }
        var saved = (await host.Client.GetFromJsonAsync<JsonObject>("api/lab/preferences"))!;
        Assert.True(JsonNode.DeepEquals(saved, first) || JsonNode.DeepEquals(saved, second));
        var disk = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.DataDirectory, "ui-lab.json")));
        Assert.True(JsonNode.DeepEquals(saved, disk));
    }

    [Fact]
    public async Task BusyOperationRejectsAnotherChangeAndCancellationFinishesTheFirst()
    {
        await using var host = new UiTestHost();
        host.Menu.HoldWrites = true;
        var first = host.Client.PostAsJsonAsync("api/session/action", Assist());
        await host.Menu.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await State(host))["busy"]!.GetValue<bool>());
        using var second = await host.Client.PostAsJsonAsync("api/session/action", Assist());
        Assert.False(second.IsSuccessStatusCode);
        Assert.Equal(1, host.Menu.Writes);
        using var cancel = await host.Client.PostAsJsonAsync("api/cancel", new { });
        cancel.EnsureSuccessStatusCode();
        using var completed = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(JsonNode.Parse(await completed.Content.ReadAsStringAsync())!["error"]);
        Assert.False((await State(host))["busy"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ShutdownCancelsPendingWorkAndRefusesNewOperations()
    {
        await using var host = new UiTestHost();
        host.Menu.HoldWrites = true;
        var first = host.Client.PostAsJsonAsync("api/session/action", Assist());
        await host.Menu.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var completed = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using var refused = await host.Client.PostAsJsonAsync("api/session/action", Assist());
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal(1, host.Menu.Writes);
    }

    [Fact]
    public async Task PracticeWritesAreRefusedWhileTheMenuIsActive()
    {
        await using var host = new UiTestHost();
        using var refused = await host.Client.PostAsJsonAsync("api/rules", new { trackLimits = true, enable = false });
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal(0, host.Menu.Writes);
    }

    private static object Assist() => new { action = "assist", key = "DRIVEAIDS_steering_help", value = 1 };
    private static async Task<JsonObject> State(UiTestHost host) =>
        (await host.Client.GetFromJsonAsync<JsonObject>("api/state"))!;
}
