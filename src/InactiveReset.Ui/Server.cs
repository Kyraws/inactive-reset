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

    private void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var readOnly = path is "/" or "/api/state";
            if (context.Request.HttpMethod != (readOnly ? "GET" : "POST") ||
                (!readOnly && context.Request.ContentType?.Split(';')[0].Trim() != "application/json"))
            {
                context.Response.StatusCode = 400;
                SendJson(context, new JsonObject { ["error"] = "invalid request method or content type" });
                return;
            }
            switch (path)
            {
                case "/":
                    SendHtml(context, Page.Html);
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

    private static void SendHtml(HttpListenerContext context, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }
}
