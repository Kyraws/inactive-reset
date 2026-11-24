using System.Diagnostics;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
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
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var debugPort = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var form = new Form { ClientSize = new Size(1320, 900), Opacity = 0, ShowInTaskbar = false };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Load += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profile,
                    new CoreWebView2EnvironmentOptions($"--remote-debugging-port={debugPort} --remote-debugging-address=127.0.0.1")).WaitAsync(TimeSpan.FromSeconds(45));
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
                        return report;
                    });

                await using var host = new UiTestHost();
                host.SeedLibrary();
                await Run("production-page", async () =>
                {
                    await Navigate(host.Server.Url);
                    await WaitFor("getComputedStyle(document.body).visibility==='visible' && document.getElementById('connection').textContent.includes('main menu')", "Production page did not initialize");

                    await Check("document.querySelector('.workspace-nav [aria-current=page]').dataset.page==='setup' && !document.getElementById('setupPanel').hidden && document.querySelector('.skip-link').hash==='#editor'", "Session & Conditions did not open as the default workspace");
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

                await Run("supporting-pages", async () =>
                {
                    form.ClientSize = new Size(1040, 680);
                    try
                    {
                        await WaitFor("innerWidth===1040 && innerHeight===680", "Minimum desktop viewport did not resize");
                        foreach (var (page, control) in new[] { ("presets", "presetSearch"), ("library", "librarySearch"), ("settings", "practiceVoice"), ("activity", "exportActivityButton") })
                        {
                            await core.ExecuteScriptAsync($"setPage('{page}')");
                            await Check($"(()=>{{const editor=document.getElementById('editor');const control=document.getElementById('{control}');return !!editor.querySelector('.lab-panel:not([hidden]) > .redesign-header h1') && control.getBoundingClientRect().width>0 && editor.scrollWidth<=editor.clientWidth;}})()", $"{page} heading or controls did not fit the minimum viewport");
                        }
                    }
                    finally { form.ClientSize = new Size(1320, 900); }
                    return "Shared headings and visible controls fit the 1040×680 client without horizontal scrolling";
                });

                await Run("recording-selection", async () =>
                {
                    form.ClientSize = new Size(1040, 680);
                    await WaitFor("innerWidth===1040 && innerHeight===680", "Minimum desktop viewport did not resize");
                    var directory = Path.Combine(host.DataDirectory, "recordings");
                    Directory.CreateDirectory(directory);
                    const string car = "TF Sport 2026 #2:LM";
                    var identity = new InactiveReset.Core.RecordingIdentity("game.exe", new string('A', 64), "sdk", "track", car, 1, 123);
                    var sample = new InactiveReset.Core.TelemetrySnapshot("track", car,
                        new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), 0, 2, 10.125,
                        LocalVelocityMetresPerSecond: new(0, 0, -10))
                    { Details = new(1, 10, 5, true, 0, 0, new Dictionary<string, double?>(), [], null) { IdentityMatches = true } };
                    InactiveReset.Core.TimelineEntry Point(long sequence, double time, float distance, float x, float z) =>
                        new("sample", sequence, sequence, Identity: identity, SegmentId: "segment",
                            Sample: sample with { ElapsedTime = time, LapDistance = distance, Gear = sequence == 44 ? 9 : 2,
                                Pose = sample.Pose with { Position = new(x, 2, z) } });
                    var path = Path.Combine(directory, "part.jsonl");
                    File.WriteAllLines(path, new[] {
                        new InactiveReset.Core.TimelineEntry("header", 0, 0, Identity: identity, Utc: DateTimeOffset.UtcNow, SegmentId: "segment"),
                        Point(41, 10, 500, 0, 3), Point(42, 10.2, 0, 0, 0),
                        Point(43, 10.4, 100, 8, 7), Point(44, 10.6, 500, 5, 12),
                    }.Select(e => JsonSerializer.Serialize(e)));
                    await core.ExecuteScriptAsync("setPage('practice')");
                    await Check("!document.getElementById('practiceSaved').hidden && document.getElementById('timelineScrub').disabled && document.getElementById('lapElapsed').value===''", "Practice shows both destination sources and an inactive scrubber");
                    await WaitFor("document.getElementById('timelineSession').options[0].textContent==='No completed laps'", "An incomplete lap appeared");
                    var emptyWidth = await core.ExecuteScriptAsync("document.getElementById('timelineSession').getBoundingClientRect().width");
                    File.AppendAllLines(path, new[] { Point(45, 10.8, 0, 0, 0) }.Select(e => JsonSerializer.Serialize(e)));
                    await WaitFor("document.querySelectorAll('#lapRail button').length===1", "Completed lap did not appear automatically");
                    const string longCircuit = "An unusually long circuit layout name for the recorded session width check";
                    var otherIdentity = identity with { Track = longCircuit, ProcessId = 124 };
                    var otherStart = new InactiveReset.Core.TimelineEntry("header", 0, 0, Identity: otherIdentity, Utc: DateTimeOffset.UtcNow, SegmentId: "other");
                    var otherFrames = new[] { Point(41, 10, 500, 0, 3), Point(42, 10.2, 0, 0, 0), Point(43, 10.4, 100, 8, 7), Point(44, 10.6, 500, 5, 12), Point(45, 10.8, 0, 0, 0) }
                        .Select(p => p with { Identity = otherIdentity, SegmentId = "other", Sample = p.Sample! with { TrackName = longCircuit } });
                    File.WriteAllLines(Path.Combine(directory, "other.jsonl"), otherFrames.Prepend(otherStart).Select(e => JsonSerializer.Serialize(e)));
                    await WaitFor("document.getElementById('timelineSession').options.length===2", "Second recording did not appear");
                    await Check($"(()=>{{const picker=document.getElementById('timelineSession');return picker.getBoundingClientRect().width==={emptyWidth} && picker.options[picker.selectedIndex].text.includes('track · 1 completed lap') && Array.from(picker.options).some(o=>/\\d.*\\d:\\d.*unusually long circuit.*1 completed lap/.test(o.text)) && picker.getBoundingClientRect().right<=document.getElementById('editor').getBoundingClientRect().right;}})()", "Chooser width, selection, date/time or completed-lap label changed with long recordings");
                    await core.ExecuteScriptAsync("document.querySelector('#lapRail button').click()");
                    await WaitFor("!!document.querySelector('#practiceMap .recorded-line')", "Recorded lap map did not render");
                    await Check("document.getElementById('timelineMapMeta').textContent.includes('Recorded lap 1 · 0:00.600 · track · Corvette GT3') && document.getElementById('lapElapsed').value===''", "Lap metadata or empty elapsed state is incorrect");
                    await core.ExecuteScriptAsync("selectElapsed(parseLapElapsed('0:00.210'))");
                    await WaitFor("timelineSelection?.Sequence===43", "Elapsed time did not select the nearest retained frame");
                    await Check("document.getElementById('destinationName').textContent.includes('Recorded lap 1 · 0:00.200') && document.getElementById('placeBtn').disabled && document.getElementById('standingBtn').disabled", "Selected frame or disconnected reset gate failed");
                    using (var screenshot = File.Create(Path.Combine(output, "recording-selection.png")))
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                    await core.ExecuteScriptAsync("document.getElementById('practiceTyresView').click()");
                    await Check("document.getElementById('practiceTyres').open && timelineSelection?.Sequence===43", "Tyre editor lost the active target");
                    await core.ExecuteScriptAsync("document.getElementById('practiceTyres').close()");
                    form.ClientSize = new Size(1320, 900);
                    return "Stable chooser with long labels at 1040×680, model details, elapsed-frame selection and disconnected reset gates";
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
                    await core.ExecuteScriptAsync("poll()");
                    await WaitFor("!document.getElementById('accessNotice').hidden && !document.getElementById('launchChoices').hidden", "Launch choices did not appear while LMU was closed");
                    await Check("!document.getElementById('launchLocalChoice').disabled && !document.getElementById('launchEacChoice').disabled && document.getElementById('accessText').textContent.includes('review')", "First-use launch choices or next step missing");
                    await Check("document.getElementById('editorFields').hidden && document.getElementById('saveStatus').textContent.includes('closed')", "Closed game retained a stale session or in-sync message");
                    await Capture("first-launch");
                    host.Menu.Available = true; host.Menu.Mode = "SETTING_GRANDPRIX"; host.GameRunning = true;
                    await core.ExecuteScriptAsync("poll()");
                    await WaitFor("document.getElementById('launchModeBadge').textContent==='EAC OFF' && !document.getElementById('startButton').disabled", "Direct-launch menu did not recover");
                    await Capture("direct-menu");
                    await core.ExecuteScriptAsync("setPage('practice')");
                    return "First launch, both EAC modes, multiplayer lock, per-action explanations and recovery";
                });
                if (host.Menu.Writes != 0) throw new Exception("Browser smoke checks unexpectedly changed game settings");
                await Navigate("about:blank");
                await PlaywrightChecks.RunAsync($"http://127.0.0.1:{debugPort}", Run);
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
