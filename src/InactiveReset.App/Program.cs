using System.Net;
using System.Net.Sockets;
using InactiveReset.Ui;

namespace InactiveReset.App;

/// <summary>
/// Double-click entry point: starts the local UI server and shows it in a
/// window. No console, no browser, no arguments needed.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var offsets = FindUpwards("offsets", Value(args, "--offsets"));
            var data = FindUpwards("data", Value(args, "--data"));
            var port = FreePort();

            var server = new Server(offsets, data, port);
            using var cancellation = new CancellationTokenSource();

            // The server runs for the life of the window. Failures surface in
            // the window itself rather than vanishing into a background task:
            // a silently dead server would just look like an empty UI.
            var serving = Task.Run(() => server.RunAsync(cancellation.Token), cancellation.Token);

            using var window = new MainWindow(server.Url + (args.Contains("--classic") ? "session" : "lab"));
            var closing = false;
            var canClose = false;
            window.FormClosing += async (_, e) =>
            {
                if (canClose) return;
                e.Cancel = true;
                if (closing) return;
                closing = true;
                window.Text = "Inactive Reset - cleaning up; return to the garage if placement is active";
                var cleanup = server.StopAsync();
                await Task.Yield(); // Let the cancelled close event finish before closing again.
                await cleanup;
                canClose = true;
                window.Close();
            };
            window.FormClosed += (_, _) => cancellation.Cancel();

            serving.ContinueWith(
                task => ShowFatal(task.Exception?.GetBaseException()?.Message ?? "the UI server stopped"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            Application.Run(window);
        }
        catch (Exception ex)
        {
            ShowFatal(ex.Message);
        }
    }

    private static void ShowFatal(string message) =>
        MessageBox.Show(message, "Inactive Reset", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>
    /// Ask the OS for a free loopback port rather than hardcoding one, so two
    /// copies do not collide and a busy port is not a startup failure.
    /// </summary>
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? Value(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindUpwards(string folder, string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, folder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(AppContext.BaseDirectory, folder);
    }
}
