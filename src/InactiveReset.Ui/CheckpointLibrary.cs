using System.Security.Cryptography;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>File operations preserve the complete original capture, including unknown metadata.</summary>
public sealed class CheckpointLibrary(string directory)
{
    private readonly object _gate = new();
    private (string Original, string Trash, string Hash, string Token)? _undo;

    public string RenameOrCopy(string id, string name, bool copy)
    {
        CaptureService.ValidateName(name);
        if (name.Equals(CaptureService.QuickCaptureName, StringComparison.OrdinalIgnoreCase))
            throw new CheckpointException("Quick capture is reserved; choose a permanent checkpoint name.");
        lock (_gate)
        {
            var source = Resolve(id);
            if (!copy && Path.GetFileNameWithoutExtension(source).Equals(CaptureService.QuickCaptureName, StringComparison.OrdinalIgnoreCase))
                throw new CheckpointException("Save a permanent copy of Quick capture instead of renaming its slot.");
            var target = Path.Combine(Path.GetDirectoryName(source)!, name + ".json");
            ResolveParent(target);
            if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return Id(source);
            if (copy) File.Copy(source, target, overwrite: false);
            else File.Move(source, target, overwrite: false);
            return Id(target);
        }
    }

    public string Delete(string id)
    {
        lock (_gate)
        {
            var source = Resolve(id);
            var trashDirectory = Path.Combine(directory, ".deleted");
            Directory.CreateDirectory(trashDirectory);
            var token = Guid.NewGuid().ToString("N");
            var target = Path.Combine(trashDirectory, token + ".capture");
            ResolveParent(target);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            File.Move(source, target);
            _undo = (source, target, hash, token);
            return token;
        }
    }

    public string Undo(string token)
    {
        lock (_gate)
        {
            if (_undo is not { } undo || undo.Token != token) throw new CheckpointException("This deletion can no longer be undone.");
            ResolveParent(undo.Original);
            ResolveParent(undo.Trash);
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(undo.Trash))) != undo.Hash)
                throw new CheckpointException("The deleted capture changed; it was not restored.");
            File.Move(undo.Trash, undo.Original, overwrite: false);
            _undo = null;
            return Id(undo.Original);
        }
    }

    private string Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Path.IsPathRooted(id)) throw new CheckpointException("Use a checkpoint selection ID.");
        var path = Path.GetFullPath(Path.Combine(directory, id));
        ResolveParent(path);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new CheckpointException("Checkpoint not found.");
        Checkpoint.Load(path);
        return path;
    }

    private void ResolveParent(string path)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new CheckpointException("Checkpoint must stay in its library.");
        for (var item = new FileInfo(path) as FileSystemInfo; item is not null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new CheckpointException("Linked checkpoint paths are not supported.");
            if (item.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private string Id(string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');
}
