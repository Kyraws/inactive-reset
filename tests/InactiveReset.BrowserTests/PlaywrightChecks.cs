using System.Text.Json;
using System.Text.Json.Nodes;
using InactiveReset.TestSupport;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

internal static class PlaywrightChecks
{
    public static async Task RunAsync(string endpoint, Func<string, Func<Task<string>>, Task> run)
    {
        using var playwright = await Playwright.CreateAsync();
        // Attach to the real isolated WebView2 control; no separate Chromium installation.
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(endpoint, new() { Timeout = 15000 });
        var page = browser.Contexts.Single().Pages.Single();
        page.SetDefaultTimeout(10000);

        async Task Check(string name, Func<UiTestHost, Task<string>> check, int expectedWrites = 0)
        {
            await using var host = new UiTestHost();
            host.SeedLibrary();
            var errors = new List<string>();
            void PageError(object? sender, string error) => errors.Add(error);
            page.PageError += PageError;
            try
            {
                await run("playwright-" + name, async () =>
                {
                    await page.GotoAsync(host.Server.Url);
                    await Expect(page.Locator("#connection")).ToContainTextAsync("main menu");
                    var detail = await check(host);
                    if (host.Menu.Writes != expectedWrites)
                        throw new Exception($"Expected {expectedWrites} menu writes, got {host.Menu.Writes}.");
                    if (errors.Count != 0) throw new Exception("Unhandled page errors: " + string.Join("; ", errors));
                    return detail + $"; {host.AttachmentReads} polling attachment attempts refused by the fixture";
                });
            }
            finally
            {
                page.PageError -= PageError;
                await page.GotoAsync("about:blank"); // Stop polling before disposing the fake backend.
            }
        }

        await Check("library-search", async host =>
        {
            await page.GetByRole(AriaRole.Navigation, new() { Name = "Workspace" })
                .GetByRole(AriaRole.Button, new() { Name = "Checkpoint Library" }).ClickAsync();
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(1);
            var sort = page.GetByRole(AriaRole.Combobox, new() { Name = "Sort checkpoints" });
            await sort.PressAsync("Space");
            await Expect(page.Locator("#librarySort:open")).ToHaveCountAsync(1);
            if (await sort.EvaluateAsync<string>("el => getComputedStyle(el, '::picker(select)').backgroundColor") != "rgb(33, 27, 25)")
                throw new Exception("The open dropdown does not use the workspace panel palette.");
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.PressAsync("Enter");
            await Expect(sort).ToHaveValueAsync("track");
            await Expect(sort).ToBeFocusedAsync();
            await page.Locator("#librarySearch").FillAsync("does-not-exist");
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(0);
            await Expect(page.Locator("#checkpointCards")).ToContainTextAsync("No matching named checkpoints");
            await page.Locator("#librarySearch").FillAsync("VALID");
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(1);
            await Expect(page.Locator("#libraryPreview")).ToContainTextAsync("Valid checkpoint");
            return "Keyboard dropdown and focus, themed picker, search recovery and checkpoint preview";
        });

        await Check("horizontal-rails", async _ =>
        {
            const string catalogUrl = "**/api/session/catalog";
            const string lapsUrl = "**/api/recordings/laps";
            const string stateUrl = "**/api/state";
            await page.RouteAsync(catalogUrl, async route =>
            {
                var response = await route.FetchAsync();
                var catalog = JsonNode.Parse(await response.TextAsync())!;
                var cars = catalog["cars"]!.AsArray();
                for (var i = 1; i <= 10; i++)
                {
                    cars.Add(JsonSerializer.SerializeToNode(new
                    {
                        id = $"model-{i}", desc = $"Model {i:00}", isOwned = true, fullPathTree = $"2026, GT3, Model {i:00}"
                    }));
                    cars.Add(JsonSerializer.SerializeToNode(new
                    {
                        id = $"livery-{i}", desc = $"Test car livery {i:00}", isOwned = true, fullPathTree = "2026, GT3, Test car"
                    }));
                }
                await route.FulfillAsync(new() { Response = response, Json = catalog });
            });
            var lapCount = 16;
            await page.RouteAsync(stateUrl, route => route.FulfillAsync(new()
            {
                Json = new { connected = true, rollingStartSupported = true, session = new { track = "Test track", vehicle = "Test car" } }
            }));
            await page.RouteAsync(lapsUrl, route => route.FulfillAsync(new()
            {
                Json = Enumerable.Range(1, lapCount).Select(i => new
                {
                    Id = $"lap-{i}", Number = i, Duration = 90 + i, Identity = new { Track = "Test track", Vehicle = "Test car" },
                    SegmentId = "session", HasSpeedEligiblePoints = true
                }).ToArray()
            }));
            await page.SetViewportSizeAsync(1040, 680);
            try
            {
                await page.ReloadAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Car & livery" }).ClickAsync();
                foreach (var name in new[] { "Car models", "Liveries" })
                {
                    var rail = page.GetByRole(AriaRole.Group, new() { Name = name });
                    await rail.HoverAsync();
                    await page.Mouse.WheelAsync(0, 230);
                    await page.WaitForFunctionAsync("name => document.querySelector('[aria-label=\"' + name + '\"]').scrollLeft > 0", name);
                }
                var models = page.GetByRole(AriaRole.Group, new() { Name = "Car models" });
                var liveries = page.GetByRole(AriaRole.Group, new() { Name = "Liveries" });
                var livery = liveries.Locator("[data-livery=livery-8]");
                await livery.ScrollIntoViewIfNeededAsync();
                var liveryOffset = await liveries.EvaluateAsync<int>("el => el.scrollLeft");
                await livery.ClickAsync();
                await Expect(livery).ToHaveAttributeAsync("aria-pressed", "true");
                await Expect(livery).ToBeFocusedAsync();
                if (await liveries.EvaluateAsync<int>("el => el.scrollLeft") != liveryOffset || liveryOffset == 0)
                    throw new Exception("Livery rail lost its horizontal position when a livery was selected.");

                var model = models.Locator("[data-car=model-4]");
                await model.FocusAsync();
                var offset = await models.EvaluateAsync<int>("el => el.scrollLeft");
                await page.Keyboard.PressAsync("Enter");
                await Expect(model).ToHaveAttributeAsync("aria-pressed", "true");
                await Expect(model).ToBeFocusedAsync();
                if (await models.EvaluateAsync<int>("el => el.scrollLeft") != offset || offset == 0)
                    throw new Exception("Car model rail lost its horizontal position when the selected model was rerendered.");

                await page.Keyboard.PressAsync("Escape");
                await page.Locator(".workspace-nav [data-page=practice]").ClickAsync();
                await Expect(page.GetByRole(AriaRole.Checkbox, new() { Name = "Apply tyres" })).ToBeDisabledAsync();
                var laps = page.GetByRole(AriaRole.Group, new() { Name = "Complete laps" });
                await Expect(laps.GetByRole(AriaRole.Button)).ToHaveCountAsync(lapCount);
                await laps.HoverAsync();
                await page.Mouse.WheelAsync(120, 2);
                await page.WaitForFunctionAsync("document.getElementById('lapRail').scrollLeft > 0");
                await laps.EvaluateAsync("el => el.scrollLeft = 0");
                await laps.FocusAsync();
                await page.Keyboard.PressAsync("ArrowRight");
                await page.WaitForFunctionAsync("document.getElementById('lapRail').scrollLeft > 0");
                var lap = laps.GetByRole(AriaRole.Button, new() { Name = "Lap 10", Exact = true });
                await lap.FocusAsync();
                offset = await laps.EvaluateAsync<int>("el => el.scrollLeft");
                lapCount++;
                await page.EvaluateAsync("() => loadRecordedLaps()");
                await Expect(laps.GetByRole(AriaRole.Button)).ToHaveCountAsync(lapCount);
                await Expect(lap).ToBeFocusedAsync();
                if (await laps.EvaluateAsync<int>("el => el.scrollLeft") != offset || offset == 0)
                    throw new Exception("Lap rail lost its horizontal position on refresh.");
                return "Car and livery wheel scrolling, native horizontal and keyboard scrolling on the lap rail, and focus and offset on rerender";
            }
            finally
            {
                await page.UnrouteAsync(catalogUrl);
                await page.UnrouteAsync(lapsUrl);
                await page.UnrouteAsync(stateUrl);
                await page.SetViewportSizeAsync(1320, 900);
            }
        });

        await Check("refresh-error", async host =>
        {
            await page.GetByRole(AriaRole.Navigation, new() { Name = "Workspace" })
                .GetByRole(AriaRole.Button, new() { Name = "Official Events" }).ClickAsync();
            var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh events" });
            await refresh.ClickAsync();
            await Expect(page.Locator("#workspaceOfficialStatus")).ToContainTextAsync("No RaceControl login");
            await Expect(refresh).ToBeEnabledAsync();
            await Expect(page.Locator("#officialWorkspaceEvents")).ToContainTextAsync("No cached events");
            return "Failed refresh displays the backend error, preserves an empty catalog and enables retry";
        });

        await Check("protected-recovery", async host =>
        {
            host.Protected = true;
            await page.ReloadAsync();
            await Expect(page.Locator("#launchModeBadge")).ToHaveTextAsync("EAC ON");
            await page.GetByRole(AriaRole.Navigation, new() { Name = "Workspace" })
                .GetByRole(AriaRole.Button, new() { Name = "Practice Teleport" }).ClickAsync();
            await Expect(page.Locator("#placeHint")).ToContainTextAsync("EAC on");
            foreach (var id in new[] { "placeBtn", "standingBtn", "tyreResetBtn" })
                await Expect(page.Locator("#" + id)).ToBeDisabledAsync();
            host.Protected = false;
            await page.ReloadAsync();
            await Expect(page.Locator("#launchModeBadge")).ToHaveTextAsync("EAC OFF");
            await Expect(page.Locator("#startButton")).ToBeEnabledAsync();
            return "Protected launch blocks memory actions and a direct-launch menu recovers without game access";
        });

        await Check("session-apply", async host =>
        {
            await page.GetByRole(AriaRole.Navigation, new() { Name = "Workspace" })
                .GetByRole(AriaRole.Button, new() { Name = "Session & Conditions" }).ClickAsync();
            var start = page.Locator("#builderStart");
            await Expect(start).ToBeEnabledAsync();
            var summaryToggle = page.GetByRole(AriaRole.Button, new() { Name = "Session summary" });
            await summaryToggle.ClickAsync();
            var summary = page.GetByRole(AriaRole.Complementary, new() { Name = "Session summary" });
            await Expect(summary).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
            await Expect(summary).ToBeHiddenAsync();
            await Expect(summaryToggle).ToBeFocusedAsync();

            var builder = page.Locator("#setupPanel");
            var clock = builder.GetByLabel("Practice start time", new() { Exact = true });
            await clock.FillAsync("16:45");
            await Expect(builder.GetByRole(AriaRole.Slider)).ToHaveValueAsync("1005");
            await Expect(start).ToBeDisabledAsync();
            await builder.GetByRole(AriaRole.Button, new() { Name = "Reload from LMU", Exact = true }).ClickAsync();
            await builder.GetByRole(AriaRole.Button, new() { Name = "Are you sure?", Exact = true }).ClickAsync();
            await Expect(clock).ToHaveValueAsync("");
            await Expect(start).ToBeEnabledAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "Session & time" }).ClickAsync();
            var timing = page.GetByRole(AriaRole.Dialog, new() { Name = "Session & time" });
            var duration = timing.GetByRole(AriaRole.Spinbutton, new() { Name = "Practice duration" });
            await duration.FillAsync("61");
            await duration.PressAsync("Tab");
            await timing.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
            await Expect(start).ToBeDisabledAsync();
            if (host.Menu.Writes != 0) throw new Exception("Editing the draft changed the LMU menu.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Apply session" }).ClickAsync();
            await Expect(page.Locator("#builderStatus")).ToContainTextAsync("Session applied");
            await Expect(start).ToBeEnabledAsync();
            if (host.Menu.PracticeLength != 61) throw new Exception("LMU did not receive the practice duration.");
            return "Draft timing stays local until Apply and Start remains a separate action";
        }, expectedWrites: 1);
    }
}
