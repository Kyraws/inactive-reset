using System.IO.Compression;

namespace InactiveReset.Ui;

/// <summary>Reads artwork from the user's installed LMU menu.</summary>
public sealed class LmuBrandAssets(Func<string> archivePath)
{
    private const int CachedImageLimit = 4 * 1024 * 1024;
    private const int TrackImageLimit = 12 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _logos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Kind, string Key), byte[]> _images = [];
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

    public byte[]? ReadImage(string kind, string key)
    {
        if (key.Length is < 1 or > 140 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))) return null;
        var (folder, suffix) = kind switch
        {
            "car" => ("cars/FrontThumbnail/", "_frontAngle.webp"),
            "track" => ("tracks/backgrounds/", ".webp"),
            "track-card" => ("tracks/cards/", ".webp"),
            _ => ("", ""),
        };
        if (folder.Length == 0) return null;
        lock (_gate)
        {
            var path = archivePath();
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (_imageArchive != (path, writeTime))
            {
                _images.Clear();
                _imageArchive = (path, writeTime);
            }
            key = key.ToLowerInvariant();
            if (_images.TryGetValue((kind, key), out var cached)) return cached;
            using var archive = ZipFile.OpenRead(path);
            var prefix = "start/images/" + folder;
            ZipArchiveEntry? Find(string name) => archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Replace('\\', '/').Equals(prefix + name + suffix, StringComparison.OrdinalIgnoreCase));
            var entry = Find(key);
            if (entry is null && kind is "track" or "track-card")
            {
                var stem = key.Split('_')[0];
                entry = Find(stem) ?? archive.Entries.FirstOrDefault(entry =>
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return false;
                    var venue = name[prefix.Length..^suffix.Length].ToLowerInvariant();
                    if (venue.EndsWith("wec") || venue.EndsWith("elms")) venue = venue[..(venue.EndsWith("wec") ? ^3 : ^4)];
                    return venue.Length >= 3 && (stem == venue || stem == venue + "wec" || stem == venue + "elms");
                });
            }
            var limit = kind == "track" ? TrackImageLimit : CachedImageLimit;
            if (entry is null || entry.Length <= 0 || entry.Length > limit) return null;
            using var input = entry.Open();
            var bytes = new byte[(int)entry.Length];
            input.ReadExactly(bytes);
            if (bytes.Length <= CachedImageLimit)
            {
                if (_images.Count >= 32) _images.Clear();
                _images[(kind, key)] = bytes;
            }
            return bytes;
        }
    }

    private (string Path, DateTime Updated) _imageArchive;
}
