using System.Globalization;
using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>Lab-only UI choices and session plans, separate from game configuration.</summary>
public sealed class LabPreferencesStore(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "ui-lab.json");
    private readonly object _gate = new();

    public JsonObject Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return Defaults();
            var value = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                ?? throw new GateException("The saved Lab preferences could not be read.");
            Validate(value);
            return value;
        }
    }

    public JsonObject Write(JsonObject value)
    {
        Validate(value);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, value.ToJsonString());
            File.Move(temp, _path, overwrite: true);
        }
        return new JsonObject { ["ok"] = true };
    }

    private static JsonObject Defaults() => JsonNode.Parse("""{"schema":1,"theme":"midnight","density":"comfortable","pins":["SESSSET_Num_Opponents","SESSSET_AI_Strength","SESSSET_weather"],"cars":[],"tracks":[],"checkpoints":[],"plans":[]}""")!.AsObject();

    public static void Validate(JsonObject value)
    {
        if (value.ToJsonString().Length > 512_000 || value["schema"]?.ToJsonString() != "1") Invalid();
        if (Text(value["theme"]) is not ("midnight" or "daylight") || Text(value["density"]) is not ("comfortable" or "compact")) Invalid();
        if (value.ContainsKey("style") && Text(value["style"]) is not ("original" or "redline")) Invalid();
        foreach (var key in new[] { "pins", "cars", "tracks", "checkpoints" })
        {
            if (value[key] is not JsonArray list || list.Count > 200) Invalid();
            foreach (var item in (JsonArray)value[key]!)
                if (Text(item) is not { Length: > 0 and <= 256 }) Invalid();
        }
        if (value["plans"] is not JsonArray plans || plans.Count > 40) Invalid();
        foreach (var node in (JsonArray)value["plans"]!)
        {
            if (node is not JsonObject plan || Text(plan["name"]) is not { Length: > 0 and <= 60 } ||
                Text(plan["id"]) is not { Length: > 0 and <= 80 } || plan["target"] is not JsonObject) Invalid();
            var target = (JsonObject)node!["target"]!;
            foreach (var key in new[] { "car", "track" })
                if (target[key] is not null && Text(target[key]) is not { Length: > 0 and <= 256 }) Invalid();
            foreach (var group in new[] { "settings", "assists", "amounts" })
            {
                if (target[group] is not JsonObject entries || entries.Count > 100) Invalid();
                foreach (var (key, number) in (JsonObject)target[group]!)
                {
                    var isNumber = double.TryParse(number?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n);
                    if (key.Length > 100 || !isNumber || !double.IsFinite(n) || Math.Abs(n) > int.MaxValue) Invalid();
                    if (group == "settings" && !key.StartsWith("SESSSET_", StringComparison.Ordinal) ||
                        group == "assists" && !key.StartsWith("DRIVEAIDS_", StringComparison.Ordinal) ||
                        group == "amounts" && (key is not ("PRACTICE" or "QUALIFY" or "WARMUP" or "RACE") || n < 0 || n > (key == "WARMUP" ? 1 : 4) || n != Math.Truncate(n))) Invalid();
                }
            }
            if (target["classes"] is not JsonArray classes || classes.Count > 16) Invalid();
            foreach (var item in (JsonArray)target["classes"]!)
                if (Text(item) is not { Length: > 0 and <= 60 }) Invalid();
            if (target["fullGrid"] is not JsonValue flag || !flag.TryGetValue<bool>(out _)) Invalid();
            if (target["weather"] is not JsonObject weather || weather.Count > 3) Invalid();
            foreach (var (session, nodes) in (JsonObject)target["weather"]!)
            {
                if (session is not ("PRACTICE" or "QUALIFY" or "RACE") || nodes is not JsonObject points || points.Count > 5) Invalid();
                foreach (var (point, fields) in (JsonObject)nodes!)
                {
                    if (point is not ("START" or "NODE_25" or "NODE_50" or "NODE_75" or "FINISH") || fields is not JsonObject controls || controls.Count > 6) Invalid();
                    foreach (var (key, number) in (JsonObject)fields!)
                        if (!key.StartsWith("WNV_", StringComparison.Ordinal) || key.Length > 50 || !double.TryParse(number?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 1000) Invalid();
                }
            }
        }
    }

    private static string? Text(JsonNode? value) => value is JsonValue text && text.TryGetValue<string>(out var result) ? result : null;
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new GateException("This Lab preference or saved-plan file is not valid. Use a Lab 02 export with at most 40 plans.");
}
