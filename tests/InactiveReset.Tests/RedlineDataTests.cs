using System.Text.Json.Nodes;
using InactiveReset.Core;
using InactiveReset.Ui;
using InactiveReset.TestSupport;
using System.Net.Http.Json;
using Xunit;

namespace InactiveReset.Tests;

public sealed class RedlineDataTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("inactive-reset-redline-").FullName;
    private string Seed(string name = "Quick capture") => CaptureService.Save(new Checkpoint
    {
        Name = name, TrackName = "Layout", VehicleName = "Car", CapturedUtc = DateTimeOffset.UtcNow,
        Pose = new(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
        Gear = 3, LocalVelocityMetresPerSecond = new(0, 0, 20),
    }, _root, name == "Quick capture");

    [Fact]
    public void Permanent_copy_rename_delete_and_undo_preserve_complete_capture()
    {
        var source = Seed(); var original = File.ReadAllText(source);
        var library = new CheckpointLibrary(_root);
        var copy = library.RenameOrCopy(Path.GetRelativePath(_root, source), "Corner", true);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_root, copy)));
        var renamed = library.RenameOrCopy(copy, "Entry", false);
        Assert.False(File.Exists(Path.Combine(_root, copy)));
        var token = library.Delete(renamed);
        Assert.Single(Checkpoint.LoadAll(_root));
        Assert.Equal(renamed, library.Undo(token));
        Assert.Equal(original, File.ReadAllText(Path.Combine(_root, renamed)));
        Assert.Throws<CheckpointException>(() => library.Undo(token));
    }

    [Fact]
    public void Library_refuses_traversal_reserved_names_and_overwrite()
    {
        var source = Seed(); var id = Path.GetRelativePath(_root, source); var library = new CheckpointLibrary(_root);
        Assert.Throws<CheckpointException>(() => library.Delete("../outside.json"));
        Assert.Throws<CheckpointException>(() => library.RenameOrCopy(id, "CON", true));
        Assert.Throws<CheckpointException>(() => library.RenameOrCopy(id, "Corner", false));
        var copy = library.RenameOrCopy(id, "Corner", true);
        Assert.Throws<IOException>(() => library.RenameOrCopy(id, "Corner", true));
        var token = library.Delete(copy); Seed("Corner");
        Assert.Throws<IOException>(() => library.Undo(token));
    }

    [Fact]
    public void Map_route_is_recovered_and_optional_for_older_menus()
    {
        const string helper = "function getTrackMap(trackId) { return getJson(`${raceRestUrl}track/${trackId}/trackmap`); }";
        var routes = LmuApiRoutes.Parse(LmuApiRoutesTests.Menu + helper);
        Assert.Equal("/rest/race/track/abc/trackmap", routes.Resolve(HttpMethod.Get, "/rest/race/track/abc/trackmap").AbsolutePath);
        Assert.Throws<GateException>(() => LmuApiRoutes.Parse(LmuApiRoutesTests.Menu).Resolve(HttpMethod.Get, "/rest/race/track/abc/trackmap"));
    }

    [Fact]
    public async Task Map_cache_keeps_layouts_separate_and_survives_unavailable_game()
    {
        var reads = 0;
        var store = new CircuitMapStore(_root, id =>
        {
            reads++;
            return Task.FromResult(new JsonObject { ["id"] = id, ["points"] = JsonNode.Parse("""[{"x":0,"z":0,"type":0},{"x":100,"z":0,"type":0},{"x":100,"z":100,"type":0}]""") });
        });
        await store.ReadAsync("layout-a"); await store.ReadAsync("layout-b"); await store.ReadAsync("layout-a");
        Assert.Equal(2, reads);
        var offline = new CircuitMapStore(_root, _ => throw new GateException("closed"));
        Assert.True((await offline.ReadAsync("layout-a", true))["cached"]!.GetValue<bool>());
        await Assert.ThrowsAsync<GateException>(() => offline.ReadAsync("../outside"));
        Assert.Throws<GateException>(() => LmuSessionClient.ValidateTrackMap(JsonNode.Parse("""[{"x":0,"z":0,"type":0}]""")!.AsArray()));
    }

    [Fact]
    public void Map_accepts_additional_waypoint_types_returned_by_lmu_and_checks_their_coordinates()
    {
        var points = JsonNode.Parse("""[{"type":0,"x":0,"z":0},{"type":0,"x":100,"z":0},{"type":0,"x":100,"z":100},{"type":1,"x":5,"z":5},{"type":149,"x":20,"z":30}]""")!.AsArray();
        LmuSessionClient.ValidateTrackMap(points);
        points[4]!["x"] = "invalid";
        Assert.Throws<GateException>(() => LmuSessionClient.ValidateTrackMap(points));
        points[4]!["x"] = 20; points[4]!["type"] = -1;
        Assert.Throws<GateException>(() => LmuSessionClient.ValidateTrackMap(points));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Real_http_map_and_library_actions_do_not_write_game_settings()
    {
        await using var host = new UiTestHost(); host.SeedLibrary();
        var map = await host.Client.GetFromJsonAsync<JsonObject>("api/track-map?id=track");
        Assert.Equal(3, map!["points"]!.AsArray().Count);
        var maps = await host.Client.GetFromJsonAsync<JsonObject>("api/track-maps");
        Assert.Single(maps!["tracks"]!.AsArray());
        var state = await host.Client.GetFromJsonAsync<JsonObject>("api/state");
        var cp = state!["checkpoints"]!.AsArray().Single()!;
        Assert.Equal(1, cp["x"]!.GetValue<float>()); Assert.Equal(3, cp["z"]!.GetValue<float>());
        using var rename = await host.Client.PostAsJsonAsync("api/checkpoints/manage", new { action = "rename", id = cp["id"]!.GetValue<string>(), name = "Renamed" });
        Assert.True(rename.IsSuccessStatusCode);
        var result = await rename.Content.ReadFromJsonAsync<JsonObject>();
        Assert.EndsWith("Renamed.json", result!["id"]!.GetValue<string>());
        using var unsafeName = await host.Client.PostAsJsonAsync("api/checkpoints/manage", new { action = "rename", id = result["id"]!.GetValue<string>(), name = "../outside" });
        Assert.False(unsafeName.IsSuccessStatusCode);
        Assert.Equal(0, host.Menu.Writes);
    }
}
