using System.Text.Json;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.TestSupport;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class UiVisualReview
{
    private static readonly string[] Routes =
    [
        "/api/session/state", "/api/session/setup", "/api/session/catalog", "/api/state",
        "/api/track-maps", "/api/official-sessions", "/api/lab/preferences",
        "/api/recordings/laps", "/api/recordings/status"
    ];

    public static async Task CaptureAsync(string[] args)
    {
        if (args.Length is not (2 or 4) || !Uri.TryCreate(args[0], UriKind.Absolute, out var origin)
            || origin.Scheme != "http" || !origin.IsLoopback)
            throw new ArgumentException("Use --capture-visual <loopback app URL> <snapshot.json> [<archived setup.json> <archived catalog.json>].");

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(90) };
        async Task<JsonNode?> Read(string route) => JsonNode.Parse(await http.GetStringAsync(route));

        var responses = new JsonObject();
        foreach (var path in Routes)
        {
            responses[path] = args.Length == 4 ? path switch
            {
                "/api/session/state" => JsonNode.Parse("""
                    {"state":{"settingMode":"SETTING_GRANDPRIX","gameState":"GSTATE_SETUP","gameSession":"PRACTICE1","navigationState":"NAV_MAIN_MENU"},"loadingStatus":{"loading":false}}
                    """),
                "/api/session/setup" => JsonNode.Parse(await File.ReadAllTextAsync(args[2])),
                "/api/session/catalog" => JsonNode.Parse(await File.ReadAllTextAsync(args[3])),
                _ => await Read(path),
            } : await Read(path);
        }
        if (responses["/api/session/setup"] is not JsonObject { Count: > 0 } setup || setup["selection"] is not JsonObject
            || responses["/api/session/catalog"]?["cars"] is not JsonArray { Count: > 0 } cars
            || responses["/api/session/catalog"]?["tracks"] is not JsonArray { Count: > 0 } tracks)
            throw new InvalidDataException("The visual snapshot needs valid LMU setup and catalog data.");

        foreach (var track in responses["/api/track-maps"]?["tracks"]?.AsArray() ?? [])
        {
            var path = "/api/track-map?id=" + Uri.EscapeDataString(track!["id"]!.GetValue<string>());
            responses[path] = await Read(path);
        }
        foreach (var lap in responses["/api/recordings/laps"]?.AsArray() ?? [])
        {
            if (lap?["IsOpeningRun"]?.GetValue<bool>() == true) continue;
            var path = "/api/recordings/lap?id=" + Uri.EscapeDataString(lap!["Id"]!.GetValue<string>());
            responses[path] = await Read(path);
        }
        var snapshot = new JsonObject
        {
            ["capturedUtc"] = DateTimeOffset.UtcNow,
            ["menuSource"] = args.Length == 4 ? "archived LMU setup and catalog, replayed in single-player menu" : "live LMU",
            ["responses"] = responses,
        };
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, snapshot.ToJsonString());
            File.Move(temp, output, overwrite: true);
        }
        finally { File.Delete(temp); }
        Console.WriteLine($"Cached {cars.Count} cars, {tracks.Count} layouts and {responses.Count} app responses in {output}.");
    }

    public static async Task ReplayAsync(string debugEndpoint, string[] args)
    {
        if (args.Length is < 2 or > 4)
            throw new ArgumentException("Use --visual-review <snapshot.json> <screenshot directory> [<input-bindings.json> [<UI source directory>]].");
        var snapshot = args[0];
        var destination = args[1];
        var bindings = args.Length >= 3 ? InputBindings.Load(args[2]) : InputBindings.Defaults();
        var bindingDetail = JsonSerializer.Serialize(new { bindings = bindings.Bindings, devices = Array.Empty<object>() },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(snapshot));
        var responses = data.RootElement.GetProperty("responses");
        await using var host = new UiTestHost(uiSourceDirectory: args.Length == 4 ? args[3] : null);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(debugEndpoint);
        var page = browser.Contexts.Single().Pages.Single();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.GetLeftPart(UriPartial.Authority) != host.Server.Url.TrimEnd('/') || route.Request.Method != "GET")
                await route.AbortAsync();
            else if (responses.TryGetProperty(uri.PathAndQuery, out var response))
                await route.FulfillAsync(new() { ContentType = "application/json", Body = response.GetRawText() });
            else if (uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) &&
                uri.AbsolutePath is not ("/api/vehicle-models" or "/api/artwork") &&
                !uri.AbsolutePath.StartsWith("/api/brands/", StringComparison.Ordinal))
                await route.FulfillAsync(new() { Status = 404, ContentType = "application/json", Body = """{"error":"Not in the visual snapshot"}""" });
            else
                await route.ContinueAsync();
        });

        Directory.CreateDirectory(destination);
        foreach (var width in new[] { 1040, 720 })
        {
            await page.SetViewportSizeAsync(width, 680);
            await page.GotoAsync(host.Server.Url);
            await Expect(page.Locator("[data-builder-open=car]")).ToBeVisibleAsync();
            foreach (var name in new[] { "setup", "car", "practice", "official", "presets", "library", "settings", "activity" })
            {
                if (name == "car")
                    await page.Locator("[data-builder-open=car]").ClickAsync();
                else
                    await page.Locator($".workspace-nav [data-page={name}]").ClickAsync();
                if (name == "practice") await Expect(page.Locator("#timelineSession option").First).Not.ToHaveTextAsync("Loading recorded laps…");
                if (name == "settings")
                {
                    await page.EvaluateAsync("detail => window.dispatchEvent(new CustomEvent('input-bindings', { detail: JSON.parse(detail) }))", bindingDetail);
                    await Expect(page.Locator(".input-binding-row")).ToHaveCountAsync(InputBindings.Actions.Length);
                }
                await page.EvaluateAsync("() => document.fonts.ready");
                await page.ScreenshotAsync(new() { Path = Path.Combine(destination, $"{width}-{name}.png"), Animations = ScreenshotAnimations.Disabled });
                if (name == "car") await page.Keyboard.PressAsync("Escape");
                if (name == "settings" && await page.Locator("#bindingDialog").CountAsync() != 0)
                {
                    await page.Locator("[data-action=capture][data-input=learnKeyboard]").ClickAsync();
                    await Expect(page.Locator("#bindingDialog")).ToBeVisibleAsync();
                    await page.ScreenshotAsync(new() { Path = Path.Combine(destination, $"{width}-binding.png"), Animations = ScreenshotAnimations.Disabled });
                    await page.Keyboard.PressAsync("Escape");
                }
            }
        }
        if (errors.Count != 0) throw new Exception("Workspace replay errors: " + string.Join("; ", errors.Distinct()));
        if (host.Menu.Writes != 0) throw new Exception("The visual review attempted an LMU menu write.");
        Console.WriteLine($"Rendered the workspace from {Path.GetFileName(snapshot)} in {destination}.");
    }
}
