using System.Diagnostics;
using System.Runtime.InteropServices;
using InactiveReset.Core;
using System.ComponentModel;
using System.Text.Json;
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
    private readonly string _bindingsPath;
    private InputBindings _bindings;
    private readonly Dictionary<int, string> _hotkeys = [];
    private readonly WheelInput _wheel = new();
    private readonly System.Windows.Forms.Timer _learningTimer = new() { Interval = 15000 };
    private string? _learningAction, _learningKind, _inputError;
    private static readonly JsonSerializerOptions InputJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HttpClient InputStateClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public MainWindow(string url, string bindingsPath)
    {
        _url = url;
        _bindingsPath = bindingsPath;
        try { _bindings = InputBindings.Load(bindingsPath); }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        { _bindings = InputBindings.Defaults(); _inputError = "Input settings could not be loaded; using defaults. " + ex.Message; }
        _wheel.Pressed += WheelPressed;
        _wheel.DevicesChanged += () => _ = PublishInputsAsync();
        _learningTimer.Tick += (_, _) => { EndLearning(); _ = PublishInputsAsync("Assignment timed out. Release the button, then try again."); };

        Text = "Inactive Reset";
        ClientSize = new Size(1280, 800);
        MinimumSize = SizeFromClientSize(new Size(1040, 680));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 25, 29);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.ForeColor = Color.Gainsboro;
        _status.Font = new Font("Segoe UI", 10F);
        _status.Text = "starting…";
        Controls.Add(_status);

        _view.Dock = DockStyle.Fill;
        _view.Visible = false;
        _view.DefaultBackgroundColor = Color.FromArgb(23, 25, 29);
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
            core.WebMessageReceived += InputMessage;
            core.NavigationCompleted += (_, _) => _ = PublishInputsAsync();
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private void UnregisterKeys()
    {
        foreach (var id in _hotkeys.Keys) UnregisterHotKey(Handle, id);
        _hotkeys.Clear();
    }

    private bool RegisterKeys(InputBindings bindings)
    {
        UnregisterKeys();
        var failures = new List<string>();
        for (var i = 0; i < InputBindings.Actions.Length; i++)
        {
            var action = InputBindings.Actions[i];
            if (bindings.Bindings[action].Keyboard is not { } key) continue;
            var id = 0x4952 + i;
            if (RegisterHotKey(Handle, id, 0x4000u | (uint)key.Modifiers, (uint)key.KeyCode)) _hotkeys[id] = action;
            else failures.Add(action);
        }
        if (failures.Count > 0) _inputError = "Keyboard shortcut unavailable for: " + string.Join(", ", failures) + ". Choose another shortcut or close the app using it.";
        return failures.Count == 0;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterKeys(_bindings);
        _wheel.Register(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterKeys();
        _wheel.Register(IntPtr.Zero);
        _learningTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && _hotkeys.TryGetValue(message.WParam.ToInt32(), out var action))
        {
            if (_learningAction is null) _ = RunInputAsync(action);
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x00FF) _wheel.Read(message.LParam);
        if (message.Msg == 0x00FE) _wheel.DeviceChange(message.LParam, message.WParam.ToInt32() == 2);
        base.WndProc(ref message); // WM_INPUT foreground packets require default cleanup.
    }

    private bool GameFocused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == 0) return false;
        using var process = Process.GetProcessById((int)pid);
        return string.Equals(process.ProcessName, GameSession.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunInputAsync(string action)
    {
        if (_view.CoreWebView2 is null) return;
        try
        {
            if (!GameFocused()) return;
            var update = "";
            if (action is "previous" or "next")
            {
                // Refresh in the host: occluded WebView pages may defer fetch continuations.
                using var state = JsonDocument.Parse(await InputStateClient.GetStringAsync(_url + "api/state"));
                if (!GameFocused() || _learningAction is not null) return;
                update = $"renderCheckpoints({state.RootElement.GetRawText()});";
            }
            await _view.CoreWebView2.ExecuteScriptAsync($"if (typeof runPracticeInput === 'function') {{ {update} runPracticeInput({JsonSerializer.Serialize(action)}); }}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        { /* Foreground process or page may close between the input and dispatch. */ }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { _inputError = "Could not refresh available checkpoints. Try again. " + ex.Message; await PublishInputsAsync(); }
    }

    private void WheelPressed(WheelBinding button)
    {
        if (_learningAction is not null)
        {
            if (_learningKind != "wheel" || GetForegroundWindow() != Handle) return;
            var action = _learningAction;
            EndLearning();
            try { ApplyBindings(_bindings.With(action, _bindings.Bindings[action] with { Wheel = button })); }
            catch (ArgumentException ex) { _inputError = ex.Message; _ = PublishInputsAsync(); }
            return;
        }
        var match = _bindings.Bindings.FirstOrDefault(b => b.Value.Wheel is { } w && w.Button == button.Button &&
            string.Equals(w.DevicePath, button.DevicePath, StringComparison.OrdinalIgnoreCase));
        if (match.Key is not null) _ = RunInputAsync(match.Key);
    }

    private void EndLearning()
    {
        var keyboard = _learningKind == "keyboard";
        _learningAction = _learningKind = null;
        _learningTimer.Stop();
        if (keyboard) RegisterKeys(_bindings);
    }

    private void ApplyBindings(InputBindings candidate)
    {
        try
        {
            candidate.Validate();
            _inputError = null;
            if (!RegisterKeys(candidate)) { var error = _inputError; RegisterKeys(_bindings); _inputError = error; }
            else { candidate.Save(_bindingsPath); _bindings = candidate; }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { RegisterKeys(_bindings); _inputError = ex.Message; }
        _ = PublishInputsAsync();
    }

    private void InputMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (new Uri(e.Source).GetLeftPart(UriPartial.Authority) != new Uri(_url).GetLeftPart(UriPartial.Authority)) return;
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "inputBindings") return;
            var command = root.GetProperty("command").GetString();
            if (command == "get") { _wheel.RefreshDevices(); _ = PublishInputsAsync(); return; }
            if (command == "cancel") { EndLearning(); _ = PublishInputsAsync(); return; }
            if (command == "defaults") { EndLearning(); ApplyBindings(InputBindings.Defaults()); return; }
            var action = root.GetProperty("action").GetString()!;
            if (!InputBindings.Actions.Contains(action)) throw new ArgumentException("Unknown input action.");
            if (command is "learnKeyboard" or "learnWheel")
            {
                EndLearning(); _inputError = null;
                _learningAction = action; _learningKind = command == "learnKeyboard" ? "keyboard" : "wheel";
                if (_learningKind == "keyboard") UnregisterKeys();
                _learningTimer.Start(); _ = PublishInputsAsync(); return;
            }
            var binding = _bindings.Bindings[action];
            if (command == "keyboard")
            {
                if (_learningKind != "keyboard" || _learningAction != action) throw new ArgumentException("Start keyboard assignment first.");
                EndLearning();
                binding = binding with { Keyboard = new(root.GetProperty("keyCode").GetInt32(), root.GetProperty("modifiers").GetInt32()) };
            }
            else if (command == "clearKeyboard") { EndLearning(); binding = binding with { Keyboard = null }; }
            else if (command == "clearWheel") { EndLearning(); binding = binding with { Wheel = null }; }
            else throw new ArgumentException("Unknown input command.");
            ApplyBindings(_bindings.With(action, binding));
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
        { _inputError = ex.Message; _ = PublishInputsAsync(); }
    }

    private async Task PublishInputsAsync(string? message = null)
    {
        if (_view.CoreWebView2 is null) return;
        var detail = JsonSerializer.Serialize(new { bindings = _bindings.Bindings, devices = _wheel.Devices,
            learningAction = _learningAction, learningKind = _learningKind, error = _inputError ?? _wheel.Error, message }, InputJson);
        try { await _view.CoreWebView2.ExecuteScriptAsync($"window.dispatchEvent(new CustomEvent('input-bindings', {{detail:{detail}}}));"); }
        catch (InvalidOperationException) { /* Page is navigating or closing. */ }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) _view.Dispose();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Url => _url;
}
