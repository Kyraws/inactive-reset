using System.Collections.Frozen;

namespace InactiveReset.Core;

public static class VehicleCatalog
{
    private sealed record Vehicle(string Chassis, string Model);

    // Build 4200 catalog export (2026-10-08). Chassis groups follow the installed
    // component evidence and variant splits in .scratch/ui-refinements/spec.md.
    private static readonly FrozenDictionary<string, Vehicle[]> Aliases = Load();

    public const string UnknownModel = "Unknown car";

    public static IReadOnlyDictionary<string, string> Models { get; } = Aliases.Keys
        .Where(name => Model(name) is not null)
        .ToFrozenDictionary(name => name, name => Model(name)!, StringComparer.OrdinalIgnoreCase);

    public static string? Model(string? name) =>
        Resolve(name) is { } entries && entries.All(entry => entry.Model == entries[0].Model)
            ? entries[0].Model : null;

    public static string CompatibilityKey(string name) =>
        Resolve(name) is { } entries && entries.All(entry => entry.Chassis == entries[0].Chassis)
            ? "chassis:" + entries[0].Chassis : "vehicle:" + name.ToUpperInvariant();

    public static bool Compatible(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
        CompatibilityKey(first) == CompatibilityKey(second);

    private static Vehicle[]? Resolve(string? name) =>
        name is not null && Aliases.TryGetValue(name, out var entries) ? entries : null;

    private static FrozenDictionary<string, Vehicle[]> Load()
    {
        using var stream = typeof(VehicleCatalog).Assembly.GetManifestResourceStream("InactiveReset.Core.vehicle-catalog.tsv")
            ?? throw new InvalidOperationException("The vehicle catalog is missing.");
        using var reader = new StreamReader(stream);
        var aliases = new Dictionary<string, HashSet<Vehicle>>(StringComparer.OrdinalIgnoreCase);
        void Add(string alias, Vehicle vehicle)
        {
            if (!aliases.TryGetValue(alias, out var entries)) aliases.Add(alias, entries = []);
            entries.Add(vehicle);
        }

        Vehicle? group = null;
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (line.StartsWith('#'))
            {
                group = new(fields[0][1..], fields[1]);
                continue;
            }
            if (group is null || fields.Length != 3) throw new InvalidOperationException("Invalid vehicle catalog row.");
            var (id, description, version) = (fields[0], fields[1], fields[2]);
            Add(id, group);
            Add(id + ".veh", group);
            Add(description, group);
            Add(description + " " + version, group);
        }
        return aliases.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray(), aliases.Comparer);
    }
}
