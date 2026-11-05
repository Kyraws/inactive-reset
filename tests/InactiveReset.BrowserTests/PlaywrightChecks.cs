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

        async Task Check(string name, Func<UiTestHost, Task<string>> check)
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
                    if (host.Menu.Writes != 0)
                        throw new Exception("Read-only browser workflow attempted a menu write.");
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
            await page.Locator(".lab-rail [data-page=library]").ClickAsync();
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(1);
            await page.Locator("#librarySearch").FillAsync("does-not-exist");
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(0);
            await Expect(page.Locator("#checkpointCards")).ToContainTextAsync("No matching named checkpoints");
            await page.Locator("#librarySearch").FillAsync("VALID");
            await Expect(page.Locator("#checkpointCards .checkpoint-item")).ToHaveCountAsync(1);
            await Expect(page.Locator("#libraryPreview")).ToContainTextAsync("Valid checkpoint");
            return "Real clicks and input: empty search result, case-insensitive recovery and checkpoint preview";
        });

        await Check("refresh-error", async host =>
        {
            await page.Locator("#refreshOfficialButton").ClickAsync();
            await Expect(page.Locator("#officialStatus")).ToContainTextAsync("No RaceControl login");
            await Expect(page.Locator("#refreshOfficialButton")).ToBeEnabledAsync();
            await Expect(page.Locator("#officialEvents .official-event")).ToHaveCountAsync(0);
            return "Failed refresh displays the backend error, preserves an empty catalog and enables retry";
        });

        await Check("protected-recovery", async host =>
        {
            host.Protected = true;
            await page.ReloadAsync();
            await Expect(page.Locator("#launchModeBadge")).ToHaveTextAsync("EAC ON");
            await page.Locator(".lab-rail [data-page=practice]").ClickAsync();
            await Expect(page.Locator("#placeHint")).ToContainTextAsync("EAC on");
            foreach (var id in new[] { "placeBtn", "standingBtn", "tyreResetBtn" })
                await Expect(page.Locator("#" + id)).ToBeDisabledAsync();
            host.Protected = false;
            await page.ReloadAsync();
            await Expect(page.Locator("#launchModeBadge")).ToHaveTextAsync("EAC OFF");
            await Expect(page.Locator("#startButton")).ToBeEnabledAsync();
            return "Protected launch blocks memory actions and a direct-launch menu recovers without game access";
        });
    }
}
