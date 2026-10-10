using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace InactiveReset.Core;

/// <summary>Read-only route recovery from the installed menu's supported helper shapes.</summary>
internal sealed class LmuApiRoutes
{
    // shortcut: named helper shapes are required; add verified shapes when the menu's bundling changes them.
    private sealed record Spec(string Logical, string Helper, string Call, string Payload,
        string[]? Dynamic = null);
    private static readonly Spec[] Specs =
    [
        new("/navigation/state", "getState", "getJson", "config"),
        new("/navigation/action/NAV_TO_MAIN_MENU", "navToMainMenu", "postJson", "null"),
        new("/rest/garage/UIScreen/SessionSetup", "getSessionSetup", "getJson", ""),
        new("/rest/sessions", "getSessionSettings", "getJson", ""),
        new("/rest/sessions/amount", "getSessionAmounts", "getJson", ""),
        new("/rest/sessions/weather", "getWeatherSettings", "getJson", ""),
        new("/rest/options/settings", "getSettings", "getJson", ""),
        new("/rest/sessions/getAllVehicles", "getAllVehicles", "getJson", ""),
        // The client reads the catalog at the same track resource used for selection.
        new("/rest/race/track", "postTrack", "postText", "id2"),
        new("/rest/sessions/settings", "postSessionSetting", "postJsonAndGetJson", "{sessionSetting,value:stepValue}"),
        new("/rest/sessions/{session}/sessions", "postSessionAmount", "postJson", "amount", ["sessionType.toLowerCase()"]),
        new("/rest/sessions/weather/{session}/{node}/{key}", "postWeatherSetting", "postJsonAndGetJson", "setting.value", ["setting.sessionType", "setting.node", "setting.key"]),
        new("/rest/sessions/weather/{session}/{preset}", "postWeatherPreset", "postJson", "null", ["session2", "presetType"]),
        new("/rest/options/{type}", "updateSetting$2", "postJson", "{optionEntry:key2,value:newValue}", ["valueType.toLowerCase()"]),
        new("/rest/garage/SetCurrentVehicle", "postCar", "postText", "JSON.stringify(body2)"),
        new("/rest/sessions/Championship/getGrid", "getGrid", "postJsonAndGetJson", "saveJson"),
        new("/rest/sessions/SessionPresets/getDefaultPresetForTrack", "getDefaultPresetForTrack", "postJson", "sceneDesc?{trackName:sceneDesc}:null"),
        new("/rest/garage/refreshsetups", "refreshSetups", "postJson", "null"),
        new("/rest/sessions/SessionPresets/requestPreset", "requestPreset", "postJson", "null"),
        new("/rest/sessions/SessionPresets/applyPreset", "applyPreset", "postJson", "preset"),
        new("/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", "generateSaveFileFromSessionPreset", "postJson", "preset"),
        new("/rest/sessions/SaveLoad/loadGame", "loadSaveFile", "postJson", "saveJson"),
        new("/rest/sessions/GetGameState", "getSessionGameState", "getJson", ""),
    ];
    private readonly Dictionary<string, string> _routes;
    internal int Port { get; }
    internal string SourceSha256 { get; }
    private static readonly object Gate = new();
    private static LmuApiRoutes? _last;
    private LmuApiRoutes(Dictionary<string, string> routes, int port, string digest)
    { _routes = routes; Port = port; SourceSha256 = digest; }
    internal static LmuApiRoutes Known() => new(Specs.ToDictionary(s => s.Logical, s => s.Logical), 6397, "test");

    internal static LmuApiRoutes LoadInstalled(string dataDirectory)
    {
        using var process = Process.GetProcessesByName(GameSession.ProcessName).FirstOrDefault();
        var root = process is null ? GameLauncher.Locate(dataDirectory).Root
            : Path.GetDirectoryName(process.MainModule?.FileName) ?? throw Refuse("cannot identify the running install");
        return Load(root);
    }

    internal static LmuApiRoutes Load(string root)
    {
        try
        {
            using var zip = ZipFile.OpenRead(Path.Combine(root, "Bin", "UI.zip"));
            var entries = zip.Entries.Where(e => Regex.IsMatch(e.FullName.Replace('\\', '/'),
                @"^start/assets/app-[^/]+\.js$", RegexOptions.CultureInvariant)).ToArray();
            if (entries.Length != 1 || entries[0].Length is <= 0 or > 16 * 1024 * 1024)
                throw Refuse("the main menu bundle is missing, ambiguous or too large");
            using var input = entries[0].Open();
            using var output = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer)) != 0)
            {
                if (output.Length + count > 16 * 1024 * 1024) throw Refuse("menu bundle exceeds its size limit");
                output.Write(buffer, 0, count);
            }
            var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "UserData", "player", "Settings.JSON"))) as JsonObject
                ?? throw Refuse("WebUI settings must be an object");
            var ports = settings.SelectMany(p => p.Value is JsonObject section ? section : new JsonObject())
                .Where(p => p.Key == "WebUI port").Select(p => p.Value!.GetValue<int>()).ToArray();
            if (ports.Length != 1 || ports[0] is < 1025 or > 65535) throw Refuse("the WebUI port is missing or invalid");
            return Parse(new UTF8Encoding(false, true).GetString(output.ToArray()), ports[0]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or InvalidOperationException or DecoderFallbackException)
        { throw Refuse(ex.Message); }
    }

    internal static LmuApiRoutes Parse(string source, int port = 6397)
    {
        if (port is < 1025 or > 65535) throw Refuse("invalid WebUI port");
        if (source.Length > 16 * 1024 * 1024) throw Refuse("menu source exceeds its size limit");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        lock (Gate)
        {
            if (_last is { } previous && previous.SourceSha256 == digest && previous.Port == port) return previous;
            var tokens = Tokens(source);
            var constants = Declarations(tokens);
            var routes = new Dictionary<string, string>();
            foreach (var spec in Specs)
            {
                var functions = new List<List<string>>();
                for (var i = 0; i + 2 < tokens.Count; i++)
                {
                    if (tokens[i] != "function" || tokens[i + 1] != spec.Helper || tokens[i + 2] != "(") continue;
                    var start = i + 3;
                    while (start < tokens.Count && tokens[start] != "{") start++;
                    var end = End(tokens, start, "{", "}");
                    functions.Add(tokens.GetRange(start + 1, end - start - 1));
                }
                if (functions.Count != 1) throw Refuse($"missing or ambiguous {spec.Helper} helper");
                var body = functions[0];
                var locals = Declarations(body);
                var calls = new List<(string Route, string Payload)>();
                var placeholders = Regex.Matches(spec.Logical, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToArray();
                var dynamic = (spec.Dynamic ?? []).Zip(placeholders).ToDictionary(p => p.First, p => "{" + p.Second + "}");
                for (var i = 0; i + 1 < body.Count; i++)
                {
                    if (body[i] != spec.Call || body[i + 1] != "(") continue;
                    var end = End(body, i + 1, "(", ")");
                    var args = Arguments(body.GetRange(i + 2, end - i - 2));
                    if (args.Count == 0) throw Refuse($"unsupported {spec.Helper} call");
                    var route = Evaluate(args[0], constants, locals, dynamic, new HashSet<string>());
                    calls.Add((route.TrimEnd('/'), args.Count > 1 ? string.Concat(args[1]) : ""));
                }
                if (calls.Count != 1 || calls[0].Payload != spec.Payload) throw Refuse($"changed {spec.Helper} request contract");
                var path = calls[0].Route;
                if (!Regex.IsMatch(path, @"^/(?:[A-Za-z0-9_-]+|\{\w+\})(?:/(?:[A-Za-z0-9_-]+|\{\w+\}))*$"))
                    throw Refuse($"unsupported {spec.Helper} route");
                if (!Regex.Matches(path, @"\{\w+\}").Select(m => m.Value).Order().SequenceEqual(placeholders.Select(p => "{" + p + "}").Order()))
                    throw Refuse($"changed {spec.Helper} route parameters");
                if (spec.Helper == "postCar")
                {
                    var at = body.IndexOf("body2");
                    if (at < 1 || body[at - 1] != "const" || at + 2 >= body.Count || body[at + 1] != "=" || body[at + 2] != "{")
                        throw Refuse("changed vehicle selection payload");
                    var end = End(body, at + 2, "{", "}");
                    var properties = Arguments(body.GetRange(at + 3, end - at - 3));
                    if (!properties.Select(p => p.Count > 1 && p[1] == ":" ? p[0] : "").Order()
                        .SequenceEqual(new[] { "id", "paintBlobID", "teamName", "vehicleNumber" }.Order()) ||
                        !properties.Any(p => string.Concat(p) == "id:id2"))
                        throw Refuse("changed vehicle selection payload");
                }
                routes.Add(spec.Logical, path);
            }
            // Distinct operations must not silently collapse onto a single recovered endpoint.
            if (routes.Values.Distinct().Count() != routes.Count) throw Refuse("conflicting recovered routes");
            return _last = new LmuApiRoutes(routes, port, digest);
        }
    }

    internal Uri Resolve(HttpMethod method, string logical)
    {
        foreach (var spec in Specs)
        {
            var pattern = "^" + Regex.Replace(spec.Logical, @"\{(\w+)\}", "(?<$1>[^/]+)") + "$";
            var match = Regex.Match(logical, pattern, RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var expected = spec.Call == "getJson" ? HttpMethod.Get : HttpMethod.Post;
            if (method != expected && !(spec.Helper == "postTrack" && method == HttpMethod.Get)) throw Refuse("unexpected request method");
            var path = Regex.Replace(_routes[spec.Logical], @"\{(\w+)\}", m =>
            {
                var value = match.Groups[m.Groups[1].Value].Value;
                if (value is "." or "..") throw Refuse("invalid route parameter");
                return Uri.EscapeDataString(value);
            });
            return new Uri($"http://127.0.0.1:{Port}{path}");
        }
        throw Refuse("unknown client operation");
    }

    private static Dictionary<string, List<List<string>>> Declarations(List<string> tokens)
    {
        var result = new Dictionary<string, List<List<string>>>();
        for (var i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i] is not ("const" or "let" or "var") || tokens[i + 2] != "=") continue;
            var end = i + 3;
            // Only literal, identifier and simple concatenation declarations are supported.
            while (end < tokens.Count && tokens[end] != ";" && end - i < 30) end++;
            if (end == tokens.Count || tokens[end] != ";") continue;
            if (!result.TryGetValue(tokens[i + 1], out var values)) result[tokens[i + 1]] = values = [];
            values.Add(tokens.GetRange(i + 3, end - i - 3));
        }
        return result;
    }

    private static string Evaluate(List<string> tokens, Dictionary<string, List<List<string>>> globals,
        Dictionary<string, List<List<string>>> locals, Dictionary<string, string> dynamic, HashSet<string> visiting)
    {
        var expression = string.Concat(tokens);
        if (dynamic.TryGetValue(expression, out var placeholder)) return placeholder;
        if (tokens.Contains("+"))
        {
            var at = tokens.IndexOf("+");
            return Evaluate(tokens.GetRange(0, at), globals, locals, dynamic, visiting)
                + Evaluate(tokens.GetRange(at + 1, tokens.Count - at - 1), globals, locals, dynamic, visiting);
        }
        if (tokens.Count != 1) throw Refuse("unsupported URL expression");
        var token = tokens[0];
        if (token.Length >= 2 && token[0] is '\'' or '"' or '`')
        {
            var text = token[1..^1];
            if (text.Contains('\\')) throw Refuse("escaped URL expressions are unsupported");
            return Regex.Replace(text, @"\$\{([^{}]+)\}", m => Evaluate(Tokens(m.Groups[1].Value), globals, locals, dynamic, visiting));
        }
        var declarations = locals.TryGetValue(token, out var local) ? local : globals.GetValueOrDefault(token);
        if (declarations is not { Count: 1 } || !visiting.Add(token)) throw Refuse("missing, ambiguous or cyclic URL declaration");
        var value = Evaluate(declarations[0], globals, locals, dynamic, visiting);
        visiting.Remove(token);
        return value;
    }

    private static List<List<string>> Arguments(List<string> tokens)
    {
        var result = new List<List<string>>(); var start = 0; var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is "(" or "{" or "[") depth++;
            if (tokens[i] is ")" or "}" or "]") depth--;
            if (tokens[i] != "," || depth != 0) continue;
            result.Add(tokens.GetRange(start, i - start)); start = i + 1;
        }
        if (start < tokens.Count) result.Add(tokens.GetRange(start, tokens.Count - start));
        return result;
    }

    private static int End(List<string> tokens, int start, string open, string close)
    {
        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        { if (tokens[i] == open) depth++; if (tokens[i] == close && --depth == 0) return i; }
        throw Refuse("incomplete helper expression");
    }

    // This is a bounded lexer for supported helpers, not a JavaScript interpreter.
    private static List<string> Tokens(string text)
    {
        var result = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            { var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) throw Refuse("unterminated comment"); i = end + 2; continue; }
            var start = i++;
            if (text[start] == '/' && (result.Count == 0 || result[^1] is "(" or "=" or ":" or "," or "[" or "!" or "?" or "return" or ">" or "|" or "&"))
            {
                var characterClass = false;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == '[') characterClass = true;
                    if (text[i] == ']') characterClass = false;
                    if (text[i] == '/' && !characterClass) break;
                    if (text[i] is '\r' or '\n') throw Refuse("unsupported regular expression");
                }
                if (i >= text.Length) throw Refuse("unterminated regular expression");
                i++;
                while (i < text.Length && char.IsLetter(text[i])) i++;
            }
            else if (text[start] is '\'' or '"' or '`')
            {
                i = QuotedEnd(text, start, 0);
            }
            else if (char.IsLetterOrDigit(text[start]) || text[start] is '_' or '$')
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
            result.Add(text[start..i]);
        }
        return result;
    }

    private static int QuotedEnd(string text, int start, int nesting)
    {
        if (nesting > 64) throw Refuse("template nesting exceeds its limit");
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == quote) return i + 1;
            if (quote != '`' || text[i] != '$' || i + 1 >= text.Length || text[i + 1] != '{') continue;
            var depth = 1; i += 2;
            for (; i < text.Length && depth > 0; i++)
            {
                if (text[i] is '\'' or '"' or '`') { i = QuotedEnd(text, i, nesting + 1) - 1; continue; }
                if (text[i] == '{') depth++;
                if (text[i] == '}') depth--;
            }
            if (depth != 0) throw Refuse("unterminated template expression");
            i--;
        }
        throw Refuse("unterminated string");
    }
    private static GateException Refuse(string detail) => new($"LMU API discovery: {detail}. This menu contract needs an updated resolver.");
}
