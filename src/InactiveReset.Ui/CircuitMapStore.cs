using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>Validated layout-specific maps remain available when LMU is closed.</summary>
public sealed class CircuitMapStore(string dataDirectory, Func<string, Task<JsonObject>> fetch)
{
    public JsonArray List()
    {
        var result = new JsonArray();
        var folder = Path.Combine(dataDirectory, "circuit-maps");
        if (!Directory.Exists(folder)) return result;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Take(500))
        {
            try
            {
                var map = JsonNode.Parse(File.ReadAllText(path));
                if (map?["track"] is JsonObject track) result.Add(track.DeepClone());
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        }
        return result;
    }

    public async Task<JsonObject> ReadAsync(string id, bool refresh = false)
    {
        if (id.Length is < 1 or > 128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new GateException("Invalid track identity.");
        var path = Path.Combine(dataDirectory, "circuit-maps", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
        JsonObject? saved = null;
        if (File.Exists(path))
        {
            try
            {
                saved = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject;
                if (saved?["id"]?.GetValue<string>() != id || saved["points"] is not JsonArray points) saved = null;
                else LmuSessionClient.ValidateTrackMap(points);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or GateException or IOException or InvalidOperationException) { saved = null; }
        }
        if (saved is not null && !refresh) { saved["cached"] = true; return saved; }
        try
        {
            var map = await fetch(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temp, map.ToJsonString()); File.Move(temp, path, overwrite: true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return map;
        }
        catch (Exception ex) when (saved is not null && ex is (GateException or HttpRequestException or IOException))
        { saved["cached"] = true; saved["warning"] = "Refresh unavailable; showing saved geometry."; return saved; }
    }
}
