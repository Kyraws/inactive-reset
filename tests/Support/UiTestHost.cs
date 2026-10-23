using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Ui;

namespace InactiveReset.TestSupport;

// The real HTTP server and runner, with game reads and menu requests isolated from LMU.
internal sealed class UiTestHost : IAsyncDisposable
{
    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("inactive-reset-tests-").FullName;
    public TestMenu Menu { get; } = new();
    public Server Server { get; }
    public HttpClient Client { get; }
    public bool Protected { get; set; }
    public bool GameRunning { get; set; } = true;
    public int AttachmentReads;
    private readonly HttpClient _menuHttp;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _serving;

    public UiTestHost(Func<string, OfficialSessionsStore>? officialSessions = null)
    {
        _menuHttp = new HttpClient(Menu) { BaseAddress = new Uri("http://fixture.invalid/") };
        var runner = new PlacementRunner(Path.Combine(DataDirectory, "offsets"), DataDirectory,
            new LmuSessionClient(_menuHttp), () => { Interlocked.Increment(ref AttachmentReads); throw new GateException("No game attached in this fixture."); },
            () => new GameRunState(GameRunning, Protected, GameRunning ? 123 : null));
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        Server = new Server(runner, DataDirectory, number, officialSessions?.Invoke(DataDirectory) ?? new OfficialSessionsStore(DataDirectory,
            _ => Task.FromException<string>(new GateException("No RaceControl login in this fixture."))));
        _serving = Server.RunAsync(_shutdown.Token);
        Client = new HttpClient(new HttpClientHandler { UseProxy = false })
            { BaseAddress = new Uri(Server.Url), Timeout = TimeSpan.FromSeconds(10) };
    }

    public void SeedLibrary()
    {
        var checkpoints = Path.Combine(DataDirectory, "checkpoints");
        CaptureService.Save(new Checkpoint
        {
            Name = "Valid checkpoint", TrackName = "Test track", VehicleName = "Test car",
            Pose = new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
        }, checkpoints);
        File.WriteAllText(Path.Combine(checkpoints, "broken.json"), "{");
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try { await _serving.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            Client.Dispose(); _menuHttp.Dispose(); _shutdown.Dispose();
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}

internal sealed class TestMenu : HttpMessageHandler
{
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HoldWrites { get; set; }
    public int Writes;
    public int PracticeLength = 60;
    public string Mode = "SETTING_GRANDPRIX";
    public bool Available = true;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!Available) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("{\"error\":\"LMU is not running\"}", Encoding.UTF8, "application/json") };
        JsonNode? value;
        if (request.Method == HttpMethod.Post && path == "/rest/sessions/settings")
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!;
            if (body["sessionSetting"]?.GetValue<string>() != "SESSSET_Practice_Length") throw new InvalidOperationException("Unexpected fixture setting");
            PracticeLength += body["value"]!.GetValue<int>();
            Interlocked.Increment(ref Writes); value = new JsonObject();
        }
        else if (request.Method == HttpMethod.Post && path == "/rest/options/long")
        {
            Interlocked.Increment(ref Writes);
            WriteStarted.TrySetResult();
            if (HoldWrites) await Task.Delay(Timeout.Infinite, cancellation);
            value = new JsonObject();
        }
        else if (request.Method == HttpMethod.Get)
        {
            value = path switch
            {
                "/navigation/state" => JsonNode.Parse("""{"state":{"settingMode":"MODE","gameState":"GSTATE_SETUP","gameSession":"PRACTICE1","navigationState":"NAV_MAIN_MENU"},"loadingStatus":{"loading":false}}""".Replace("MODE", Mode)),
                "/rest/garage/UIScreen/SessionSetup" => JsonNode.Parse("""{"selectedCar":{"id":"car","desc":"Test car","isOwned":true,"fullPathTree":"2026, GT3, Test car"},"trackInfo":{"id":"track","trackName":"Test track","isOwned":true},"classesSelection":[],"fullGrid":false}"""),
                "/rest/sessions" => JsonNode.Parse("""{"SESSSET_Practice_Length":{"currentValue":LENGTH,"stringValue":"LENGTH","numStepsTotal":1440},"SESSSET_practice1_starting_time":{"currentValue":-1,"stringValue":"Default (10:05 AM)","numStepsTotal":1442}}""".Replace("LENGTH", PracticeLength.ToString())),
                "/rest/sessions/amount" => JsonNode.Parse("""{"PRACTICE":1,"QUALIFY":0,"WARMUP":0,"RACE":0}"""),
                "/rest/sessions/weather" => JsonNode.Parse("""{"PRACTICE":{},"QUALIFY":{},"RACE":{}}"""),
                "/rest/options/settings" => JsonNode.Parse("""{"DRIVEAIDS_steering_help":{"minValue":0,"maxValue":3,"stepValue":1,"currentValue":0,"valueType":"LONG"}}"""),
                "/rest/sessions/getAllVehicles" => JsonNode.Parse("""[{"id":"car","desc":"Test car","isOwned":true,"fullPathTree":"2026, GT3, Test car"}]"""),
                "/rest/race/track" => JsonNode.Parse("""[{"id":"track","name":"Test track","shortName":"Test track","owned":true}]"""),
                _ => throw new InvalidOperationException($"Unexpected fixture GET {path}"),
            };
        }
        else throw new InvalidOperationException($"Unexpected fixture request {request.Method} {path}");
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(value!.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
