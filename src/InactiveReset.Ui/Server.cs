using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>
/// A local HTTP front end.
///
/// Uses the shared Core services through PlacementRunner. Front-end
/// orchestration still differs from the CLI. Built on <see cref="HttpListener"/>
/// with loopback binding and no web-framework dependency.
///
/// Bound to loopback only. This exposes memory writes to the game; it has no
/// business listening on anything routable.
/// </summary>
public sealed class Server(string offsetDirectory, string dataDirectory, int port)
{
    private readonly PlacementRunner _runner = new(offsetDirectory, dataDirectory);
    private readonly LabPreferencesStore _labPreferences = new(dataDirectory);
    private readonly LmuBrandAssets _brandAssets = new(() => Path.Combine(GameLauncher.Locate(dataDirectory).Root, "Bin", "UI.zip"));

    public string Url => $"http://127.0.0.1:{port}/";
    public Task StopAsync() => _runner.StopAsync();

    public async Task RunAsync(CancellationToken cancellation)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(Url);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"cannot listen on {Url}: {ex.Message}. Another instance may already be running.", ex);
        }

        Console.WriteLine($"Inactive Reset UI on {Url}");
        Console.WriteLine("Ctrl+C to stop.");

        using var registration = cancellation.Register(() => listener.Abort());
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (cancellation.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => Handle(context), CancellationToken.None);
            }
        }
        finally
        {
            // The process must stay alive until a placement has restored its
            // temporary writes, including when the console server is cancelled.
            await _runner.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path.StartsWith("/api/brands/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
            {
                string? logo;
                try { logo = _brandAssets.Read(Uri.UnescapeDataString(path[12..]), context.Request.QueryString["theme"] == "daylight"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or GateException or GameInstallException) { logo = null; }
                if (logo is null) context.Response.StatusCode = 404;
                SendText(context, logo ?? "", "image/svg+xml");
                return;
            }
            var readOnly = path is "/" or "/session" or "/app.css" or "/session.js" or "/lab" or "/lab.css" or "/lab.js" or "/lab.race.svg" or "/lab.practice.js" or "/api/state" or "/api/session/setup" or "/api/session/catalog" or "/api/session/state" || path == "/api/lab/preferences" && context.Request.HttpMethod == "GET";
            if (context.Request.HttpMethod != (readOnly ? "GET" : "POST") ||
                (!readOnly && context.Request.ContentType?.Split(';')[0].Trim() != "application/json"))
            {
                context.Response.StatusCode = 400;
                SendJson(context, new JsonObject { ["error"] = "invalid request method or content type" });
                return;
            }
            switch (path)
            {
                case "/lab":
                    SendHtml(context, SessionPage.Read("Lab.html"));
                    break;
                case "/lab.css":
                    SendText(context, SessionPage.Read("Lab.css"), "text/css");
                    break;
                case "/lab.js":
                    SendText(context, SessionPage.Read("Lab.js"), "text/javascript");
                    break;
                case "/lab.race.svg":
                    SendText(context, SessionPage.Read("Lab.race.svg"), "image/svg+xml");
                    break;
                case "/lab.practice.js":
                    SendText(context, LabPage.PracticeScript(), "text/javascript");
                    break;
                case "/api/lab/preferences":
                    if (context.Request.HttpMethod == "GET") SendJson(context, _labPreferences.Read());
                    else
                    {
                        if (context.Request.ContentLength64 > 512_000) throw new GateException("The Lab preferences file is too large.");
                        SendJson(context, _labPreferences.Write(ReadBody(context)));
                    }
                    break;
                case "/":
                    SendHtml(context, Page.Html);
                    break;
                case "/session":
                    SendHtml(context, SessionPage.Read("Session.html"));
                    break;
                case "/app.css":
                    SendText(context, SessionPage.Read("App.css"), "text/css");
                    break;
                case "/session.js":
                    SendText(context, SessionPage.Read("Session.js"), "text/javascript");
                    break;
                case "/api/session/setup":
                    SendJson(context, await _runner.SessionSetupAsync());
                    break;
                case "/api/session/catalog":
                    SendJson(context, await _runner.SessionCatalogAsync());
                    break;
                case "/api/session/state":
                    SendJson(context, await _runner.SessionNavigationAsync());
                    break;
                case "/api/session/action":
                    SendJson(context, await _runner.SessionActionAsync(ReadBody(context)));
                    break;
                case "/api/state":
                    SendJson(context, _runner.State());
                    break;
                case "/api/rules":
                    SendJson(context, _runner.SetRules(ReadBody(context)));
                    break;
                case "/api/place":
                    SendJson(context, _runner.StartPlace(ReadBody(context)));
                    break;
                case "/api/tyres":
                    SendJson(context, _runner.StartTyres(ReadBody(context)));
                    break;
                case "/api/cancel":
                    SendJson(context, _runner.CancelPlace());
                    break;
                case "/api/launch":
                    SendJson(context, _runner.Launch(ReadBody(context)));
                    break;
                case "/api/capture":
                    SendJson(context, _runner.Capture(ReadBody(context)));
                    break;
                default:
                    context.Response.StatusCode = 404;
                    SendJson(context, new JsonObject { ["error"] = "not found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = 500;
                SendJson(context, new JsonObject
                {
                    ["error"] = ex.Message,
                    ["kind"] = ex.GetType().Name,
                });
            }
            catch (Exception)
            {
                // The client is gone. Nothing useful left to say.
            }
        }
    }

    private static JsonObject ReadBody(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "POST")
        {
            return [];
        }
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(body)
            ? []
            : JsonNode.Parse(body)?.AsObject() ?? [];
    }

    private static void SendJson(HttpListenerContext context, JsonNode node)
    {
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString(
            new JsonSerializerOptions { WriteIndented = false }));
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    private static void SendHtml(HttpListenerContext context, string html) => SendText(context, html, "text/html");

    private static void SendText(HttpListenerContext context, string text, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = type + "; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }
}
