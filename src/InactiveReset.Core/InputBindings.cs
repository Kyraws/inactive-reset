using System.Text.Json;

namespace InactiveReset.Core;

public sealed record KeyboardBinding(int KeyCode, int Modifiers);
public sealed record WheelBinding(string DevicePath, int Button);
public sealed record ActionBinding(KeyboardBinding? Keyboard = null, WheelBinding? Wheel = null);

public sealed class InputBindings
{
    public static readonly string[] Actions = ["capture", "teleport", "standing", "previous", "next"];
    public Dictionary<string, ActionBinding> Bindings { get; init; } = [];
    public static InputBindings Defaults() => new()
    {
        Bindings = new()
        {
            ["capture"] = new(new(0x43, 3)), ["teleport"] = new(new(0x52, 3)),
            ["standing"] = new(new(0x52, 7)), ["previous"] = new(new(0x26, 3)),
            ["next"] = new(new(0x28, 3)),
        }
    };

    public void Validate()
    {
        if (Bindings is null || Bindings.Count != Actions.Length || Actions.Any(a => !Bindings.ContainsKey(a)))
            throw new ArgumentException("Invalid input action list.");
        var keys = new HashSet<KeyboardBinding>();
        var buttons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in Bindings.Values)
        {
            if (binding is null) throw new ArgumentException("Missing input binding.");
            if (binding.Keyboard is { } key)
            {
                if (key.KeyCode is < 8 or > 254 || key.KeyCode is 16 or 17 or 18 or 27 or 91 or 92 or >= 160 and <= 165 ||
                    key.Modifiers is < 0 or > 7 || (key.Modifiers == 0 && key.KeyCode is not (>= 112 and <= 135)))
                    throw new ArgumentException("Use Ctrl, Alt or Shift with a key, or an unmodified F1–F24 key. Escape cancels assignment.");
                if (!keys.Add(key)) throw new ArgumentException("That keyboard shortcut is already assigned to another action.");
            }
            if (binding.Wheel is { } wheel)
            {
                if (string.IsNullOrWhiteSpace(wheel.DevicePath) || wheel.DevicePath.Length > 2048 || wheel.Button is < 1 or > 65535)
                    throw new ArgumentException("Invalid wheel button.");
                if (!buttons.Add($"{wheel.DevicePath}|{wheel.Button}"))
                    throw new ArgumentException("That wheel button is already assigned to another action.");
            }
        }
    }

    public InputBindings With(string action, ActionBinding binding)
    {
        if (!Actions.Contains(action)) throw new ArgumentException("Unknown input action.");
        var result = new InputBindings { Bindings = new(Bindings) { [action] = binding } };
        result.Validate();
        return result;
    }

    public static InputBindings Load(string path)
    {
        if (!File.Exists(path)) return Defaults();
        if (new FileInfo(path).Length > 32768) throw new ArgumentException("Input settings file is too large.");
        var result = JsonSerializer.Deserialize<InputBindings>(File.ReadAllText(path))
            ?? throw new ArgumentException("Input settings are empty.");
        result.Validate();
        return result;
    }

    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

// Track each HID report independently: axis-only reports must not release another report's buttons.
public sealed class WheelButtonEdges
{
    private readonly Dictionary<(string Device, byte Report), HashSet<int>> _held = [];
    public int[] Update(string device, byte report, IEnumerable<int> buttons)
    {
        var key = (device.ToUpperInvariant(), report);
        var current = buttons.ToHashSet();
        var known = _held.TryGetValue(key, out var previous);
        _held[key] = current;
        return known ? current.Except(previous!).Order().ToArray() : [];
    }
    public void Remove(string device)
    {
        foreach (var key in _held.Keys.Where(k => string.Equals(k.Device, device, StringComparison.OrdinalIgnoreCase)).ToArray())
            _held.Remove(key);
    }
}
