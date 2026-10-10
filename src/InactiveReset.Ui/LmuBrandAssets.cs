using System.IO.Compression;

namespace InactiveReset.Ui;

/// <summary>Reads manufacturer artwork from the user's installed LMU menu.</summary>
public sealed class LmuBrandAssets(Func<string> archivePath)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _logos = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedPath;
    private DateTime _loadedWriteTime;

    public string? Read(string brand, bool daylight)
    {
        if (brand.Length is < 1 or > 80 || brand.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '&'))) return null;
        lock (_gate)
        {
            var path = archivePath();
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (_loadedPath != path || _loadedWriteTime != writeTime)
            {
                const string prefix = "start/images/manufacturer/Brand=";
                var logos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var archive = ZipFile.OpenRead(path);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".svg", StringComparison.Ordinal)) continue;
                    if (entry.Length is <= 0 or > 1_048_576) continue;
                    using var input = entry.Open();
                    using var reader = new StreamReader(input);
                    logos[name[prefix.Length..^4]] = reader.ReadToEnd();
                }
                _logos.Clear();
                foreach (var pair in logos) _logos.Add(pair.Key, pair.Value);
                _loadedPath = path;
                _loadedWriteTime = writeTime;
            }
            if (daylight && _logos.TryGetValue(brand + " Dark", out var dark)) return dark;
            return _logos.GetValueOrDefault(brand);
        }
    }
}
