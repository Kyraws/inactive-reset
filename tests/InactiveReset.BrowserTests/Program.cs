using System.Diagnostics;
using System.Text.Json;
using InactiveReset.TestSupport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    private sealed record Result(string Name, bool Passed, string Detail);

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var root = FindRoot();
        var output = Path.Combine(root, "artifacts", "TestResults", "browser");
        Directory.CreateDirectory(output);
        var profile = Directory.CreateTempSubdirectory("inactive-reset-browser-").FullName;
        var results = new List<Result>();
        using var form = new Form { ClientSize = new Size(1320, 900), Opacity = 0, ShowInTaskbar = false };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Load += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profile).WaitAsync(TimeSpan.FromSeconds(45));
                await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(45));
                var core = view.CoreWebView2;
                Console.WriteLine("WebView2 " + environment.BrowserVersionString);
                core.SetVirtualHostNameToFolderMapping("ui.test", root, CoreWebView2HostResourceAccessKind.Allow);
                await core.AddScriptToExecuteOnDocumentCreatedAsync("window.testErrors=[];window.addEventListener('error',e=>{if(e.message)window.testErrors.push(e.message)});window.addEventListener('unhandledrejection',e=>window.testErrors.push(String(e.reason)));");

                async Task Navigate(string url)
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                    { if (e.IsSuccess) ready.TrySetResult(); else ready.TrySetException(new Exception(e.WebErrorStatus.ToString())); }
                    core.NavigationCompleted += Completed;
                    try { core.Navigate(url); await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
                    finally { core.NavigationCompleted -= Completed; }
                }

                async Task WaitFor(string expression, string description)
                {
                    var watch = Stopwatch.StartNew();
                    while (await core.ExecuteScriptAsync(expression) != "true")
                    {
                        if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(description);
                        await Task.Delay(50);
                    }
                }

                async Task Check(string expression, string description)
                {
                    if (await core.ExecuteScriptAsync(expression) != "true") throw new Exception(description);
                }

                async Task Run(string name, Func<Task<string>> check)
                {
                    try
                    {
                        var detail = await check();
                        await Check("window.testErrors.length===0", "Unhandled page error: " + await core.ExecuteScriptAsync("window.testErrors"));
                        results.Add(new(name, true, detail));
                        Console.WriteLine($"PASS {name}: {detail}");
                    }
                    catch (Exception ex)
                    {
                        var detail = ex.Message;
                        try { detail += "; page errors: " + await core.ExecuteScriptAsync("window.testErrors||[]"); }
                        catch (Exception) { /* Navigation may have failed before script execution became available. */ }
                        results.Add(new(name, false, detail));
                        Console.WriteLine($"FAIL {name}: {detail}");
                        try
                        {
                            using var screenshot = File.Create(Path.Combine(output, name + "-failure.png"));
                            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                        }
                        catch (Exception captureError) { Console.WriteLine("Screenshot unavailable: " + captureError.Message); }
                    }
                }

                foreach (var page in new[] { "lab-ui", "redline-ui", "checkpoint-ui", "session-ui", "input-bindings-ui" })
                    await Run(page, async () =>
                    {
                        await Navigate("https://ui.test/tests/" + page + ".html");
                        await WaitFor("(()=>{const t=document.getElementById('report')?.textContent||'';return /^(PASS|FAIL)/.test(t)||/checks passed$/.test(t)})()", "Suite did not finish");
                        var report = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.getElementById('report').textContent"))!;
                        if (report.StartsWith("FAIL")) throw new Exception(report);
                        if (page == "redline-ui")
                        {
                            await core.ExecuteScriptAsync("document.getElementById('preview').contentWindow.redlineTestUi.enableWeekend()");
                            foreach (var (width, height) in new[] { (1040, 680), (1280, 800) })
                            {
                                await core.ExecuteScriptAsync($"Object.assign(document.getElementById('preview').style,{{width:'{width}px',height:'{height}px'}})");
                                foreach (var section in new[] { "selection", "sessions", "weather", "grid", "rules" })
                                {
                                    await core.ExecuteScriptAsync($"document.getElementById('preview').contentWindow.redlineTestUi.setPage('setup','{section}')");
                                    await Check("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('editor'),p=d.querySelector('[data-builder]:not([hidden])');return e.scrollHeight<=e.clientHeight+2 && p.scrollHeight<=p.clientHeight+2;})()", $"Builder {section} needs scrolling at {width}x{height}: " + await core.ExecuteScriptAsync("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('editor'),p=d.querySelector('[data-builder]:not([hidden])');return JSON.stringify({editor:[e.clientHeight,e.scrollHeight],panel:[p.clientHeight,p.scrollHeight]});})()"));
                                }
                                foreach (var screen in new[] { "practice", "library", "presets", "settings", "activity", "home", "official" })
                                {
                                    await core.ExecuteScriptAsync($"document.getElementById('preview').contentWindow.redlineTestUi.setPage('{screen}')");
                                    await Check("(()=>{const e=document.getElementById('preview').contentDocument.getElementById('editor');return e.scrollHeight<=e.clientHeight+2;})()", $"Page {screen} needs scrolling at {width}x{height}");
                                    if (screen == "practice")
                                        await Check("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('editor'),p=d.querySelector('.tyre-toolbar');return p.scrollHeight<=p.clientHeight+2 && ['capBtn','tyreResetBtn'].every(id=>d.getElementById(id).getBoundingClientRect().bottom<=e.getBoundingClientRect().bottom);})()", $"Practice actions need scrolling at {width}x{height}");
                                    if (screen == "settings")
                                    {
                                        await Check("(()=>{const e=document.getElementById('preview').contentDocument.getElementById('editor');return e.scrollHeight<=e.clientHeight+2;})()", $"Input bindings need scrolling at {width}x{height}");
                                        await Check("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('editor'),b=d.querySelector('[data-input=defaults]');return b.getBoundingClientRect().bottom<=e.getBoundingClientRect().bottom && b.getBoundingClientRect().bottom<=d.getElementById('inputBindingsPanel').getBoundingClientRect().bottom;})()", $"Input binding footer is clipped at {width}x{height}");
                                    }
                                    if (screen == "library")
                                        await Check("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('editor');return [...d.querySelectorAll('#libraryPreview button')].every(b=>b.getBoundingClientRect().bottom<=e.getBoundingClientRect().bottom);})()", $"Checkpoint actions are clipped at {width}x{height}");
                                    if (screen is "home" or "official")
                                        await Check("(()=>{const d=document.getElementById('preview').contentDocument,root=d.getElementById(d.body.dataset.page==='home'?'officialEvents':'officialWorkspaceEvents'),cards=[...root.querySelectorAll('.official-event')];return cards.length===10 && cards.every(card=>[...card.querySelectorAll('[data-official-preset]')].length===2 && [...card.querySelectorAll('[data-official-preset]')].every(b=>{const r=b.getBoundingClientRect(),c=card.getBoundingClientRect();return r.height>=25 && r.bottom<=c.bottom+1 && r.left>=c.left && r.right<=c.right+1;}));})()", $"Official practice buttons are clipped at {width}x{height}");
                                    if (screen == "home")
                                    {
                                        await Check("(()=>{const e=document.getElementById('preview').contentDocument.getElementById('officialEvents');return e.scrollHeight<=e.clientHeight+2;})()", $"The ten-event Overview needs scrolling at {width}x{height}");
                                        await core.ExecuteScriptAsync("document.getElementById('preview').contentWindow.redlineTestUi.setProtectedForLayout(true)");
                                        await Check("(()=>{const d=document.getElementById('preview').contentDocument,e=d.getElementById('officialEvents');return !d.getElementById('accessNotice').hidden && e.scrollHeight<=e.clientHeight+2;})()", $"The EAC notice crowds out Overview actions at {width}x{height}");
                                        await core.ExecuteScriptAsync("document.getElementById('preview').contentWindow.redlineTestUi.setProtectedForLayout(false)");
                                    }
                                    if (width == 1040 && screen is "practice" or "library" or "settings" or "home" or "official")
                                    {
                                        await core.ExecuteScriptAsync("document.getElementById('preview').contentDocument.getElementById('labToast').hidden=true");
                                        using var fitScreenshot = File.Create(Path.Combine(output, $"redline-fit-{screen}-{width}.png"));
                                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fitScreenshot);
                                    }
                                }
                            }
                            await core.ExecuteScriptAsync("Object.assign(document.getElementById('preview').style,{width:'1280px',height:'800px'})");
                            foreach (var screen in new[] { "setup", "library", "practice", "presets" })
                            {
                                await core.ExecuteScriptAsync($"document.getElementById('preview').contentWindow.redlineTestUi.setPage('{screen}')");
                                using var screenshot = File.Create(Path.Combine(output, "redline-" + screen + ".png"));
                                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                            }
                        }
                        return report;
                    });

                await using var host = new UiTestHost();
                host.SeedLibrary();
                await Run("production-page", async () =>
                {
                    await Navigate(host.Server.Url);
                    await WaitFor("getComputedStyle(document.body).visibility==='visible' && document.getElementById('connection').textContent.includes('main menu')", "Production page did not initialize");
                    await core.ExecuteScriptAsync("document.querySelector('[data-page=library]').click()");
                    await WaitFor("document.getElementById('checkpointCards').textContent.includes('Valid checkpoint')", "Library did not render its valid checkpoint");
                    await core.ExecuteScriptAsync("document.querySelector('[data-page=activity]').click()");
                    await Check("document.getElementById('log').textContent.includes('broken.json')", "Malformed checkpoint warning missing from Activity");
                    await core.ExecuteScriptAsync("document.querySelector('[data-log-filter=issues]').click()");
                    await Check("document.getElementById('log').textContent.includes('broken.json')", "Malformed checkpoint warning missing from Issues only");
                    await core.ExecuteScriptAsync("document.querySelector('[data-page=practice]').click()");
                    await Check("document.getElementById('placeBtn').disabled && document.getElementById('capBtn').disabled", "Game-dependent actions enabled without telemetry");
                    return "Real HTTP page, assets, startup, library, diagnostic filters and disconnected action gates";
                });

                await Run("first-use-access", async () =>
                {
                    async Task Capture(string name)
                    {
                        using var screenshot = File.Create(Path.Combine(output, name + ".png"));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                    }
                    host.Protected = true;
                    await core.ExecuteScriptAsync("setPage('setup','sessions');poll()");
                    await WaitFor("document.getElementById('launchModeBadge').textContent==='EAC ON' && !document.getElementById('startButton').disabled", "EAC single-player menu did not unlock REST setup");
                    await Check("!document.getElementById('editorFields').disabled && document.getElementById('accessText').textContent.includes('single-player main menu')", "EAC REST editing or its explanation is missing");
                    await Capture("eac-single-player");
                    await core.ExecuteScriptAsync("setPage('practice')");
                    await WaitFor("document.getElementById('placeHint').textContent.includes('EAC on')", "Practice restriction did not appear");
                    await Check("document.getElementById('placeBtn').disabled && document.getElementById('standingBtn').disabled && document.getElementById('tyreResetBtn').disabled", "EAC memory controls were not blocked");
                    await Capture("eac-practice");
                    host.Menu.Mode = "SETTING_MULTIPLAYER";
                    await core.ExecuteScriptAsync("setPage('setup','sessions');poll()");
                    await WaitFor("document.getElementById('startButton').disabled && document.getElementById('stateBanner').textContent.includes('multiplayer')", "Multiplayer gate did not appear");
                    await Capture("eac-online");
                    host.Menu.Available = false; host.GameRunning = false; host.Protected = false;
                    await core.ExecuteScriptAsync("setPage('home');poll()");
                    await WaitFor("!document.getElementById('launchChoices').hidden", "Launch choices did not appear while LMU was closed");
                    await Check("!document.getElementById('launchLocalChoice').disabled && !document.getElementById('launchEacChoice').disabled && document.getElementById('accessText').textContent.includes('review')", "First-use launch choices or next step missing");
                    await Check("!document.getElementById('currentSetupSummary').textContent.includes('Test track') && document.getElementById('saveStatus').textContent.includes('closed')", "Closed game retained a stale session or in-sync message");
                    await Capture("first-launch");
                    host.Menu.Available = true; host.Menu.Mode = "SETTING_GRANDPRIX"; host.GameRunning = true;
                    await core.ExecuteScriptAsync("poll()");
                    await WaitFor("document.getElementById('launchModeBadge').textContent==='EAC OFF' && !document.getElementById('startButton').disabled", "Direct-launch menu did not recover");
                    await Capture("direct-menu");
                    await core.ExecuteScriptAsync("setPage('practice')");
                    return "First launch, both EAC modes, multiplayer lock, per-action explanations and recovery";
                });
                foreach (var (width, height) in new[] { (1100, 720), (900, 600) })
                    foreach (var style in new[] { "original", "redline" })
                        await Run($"layout-{style}-{width}", async () =>
                        {
                            var previousWidth = form.ClientSize.Width;
                            var previousViewport = await core.ExecuteScriptAsync("document.documentElement.clientWidth");
                            form.ClientSize = new Size(width, height);
                            await core.ExecuteScriptAsync($"document.body.dataset.style='{style}';document.body.dataset.theme='midnight';document.body.dataset.density='compact'");
                            if (previousWidth != width)
                                await WaitFor($"document.documentElement.clientWidth>0 && document.documentElement.clientWidth!=={previousViewport}", "Viewport did not resize");
                            await Check("document.documentElement.scrollWidth<=document.documentElement.clientWidth+2", "Horizontal overflow");
                            await Check("getComputedStyle(document.getElementById('practicePanel')).display!=='none'", "Practice panel hidden");
                            await core.ExecuteScriptAsync("setPage('setup','sessions')");
                            await Check("document.querySelector('input[type=time][data-session-value]')!==null && document.documentElement.scrollWidth<=document.documentElement.clientWidth+2", "Direct time editor is missing or causes overflow");
                            await Check("document.getElementById('editor').scrollWidth<=document.getElementById('editor').clientWidth+2 && document.getElementById('editor').clientWidth>400", "Builder content overflows or has insufficient space");
                            using (var screenshot = File.Create(Path.Combine(output, $"session-edits-{style}-{width}.png")))
                                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                            await core.ExecuteScriptAsync("setPage('practice')");
                            return "No horizontal overflow; Practice remains reachable";
                        });
                if (host.Menu.Writes != 0) throw new Exception("Browser smoke checks unexpectedly changed game settings");
                await Navigate("about:blank"); // Stop page polling before disposing the isolated server and data.
            }
            catch (Exception ex) { results.Add(new("runner", false, ex.ToString())); Console.WriteLine(ex); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                Environment.ExitCode = results.Any(r => !r.Passed) ? 1 : 0;
                form.Close();
            }
        };
        Application.Run(form);
        view.Dispose();
        try { Directory.Delete(profile, recursive: true); }
        catch (IOException) { /* The browser process may still be releasing its temporary profile. */ }
        catch (UnauthorizedAccessException) { }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(directory.FullName, "tests"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run the browser checks from a source checkout.");
    }
}
