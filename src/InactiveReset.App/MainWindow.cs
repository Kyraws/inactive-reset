using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace InactiveReset.App;

/// <summary>
/// A real application window hosting the same UI the CLI serves.
///
/// WebView2 renders it using the Edge runtime that ships with Windows, so this
/// is a thin wrapper rather than a bundled browser — there is one HTML file, and
/// the window and the browser tab show the same thing by construction.
/// </summary>
internal sealed class MainWindow : Form
{
    private readonly string _url;
    private readonly WebView2 _view = new();
    private readonly Label _status = new();

    public MainWindow(string url)
    {
        _url = url;

        Text = "Inactive Reset";
        Width = 1180;
        Height = 860;
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 23, 26);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.ForeColor = Color.Gainsboro;
        _status.Font = new Font("Segoe UI", 10F);
        _status.Text = "starting…";
        Controls.Add(_status);

        _view.Dock = DockStyle.Fill;
        _view.Visible = false;
        _view.DefaultBackgroundColor = Color.FromArgb(20, 23, 26);
        Controls.Add(_view);

        Load += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            // Keep the WebView2 user-data folder out of Program Files and away
            // from the exe, which may sit on a read-only or synced path.
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InactiveReset", "WebView2");
            Directory.CreateDirectory(dataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: dataFolder);
            await _view.EnsureCoreWebView2Async(environment);

            var core = _view.CoreWebView2;

            // It is a control panel, not a browser. No context menu, no dev
            // tools, no drag-and-drop navigation, and external links open in the
            // real browser rather than replacing the UI.
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenExternally(e.Uri);
            };

            core.Navigate(_url);
            _view.Visible = true;
            _status.Visible = false;
        }
        catch (Exception ex)
        {
            // The most likely cause by far is a missing WebView2 runtime on an
            // older Windows. Say so, and give a route that still works.
            _view.Visible = false;
            _status.Visible = true;
            _status.Text =
                "Could not start the embedded browser.\n\n"
                + ex.Message
                + $"\n\nThe UI is still running at {_url}\n"
                + "Open that address in any browser, or install the WebView2 runtime.";
        }
    }

    private static void OpenExternally(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Nothing useful to do if the shell refuses.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _view.Dispose();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Url => _url;
}
