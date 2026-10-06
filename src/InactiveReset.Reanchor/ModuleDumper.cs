using InactiveReset.Core;
using System.Diagnostics;

namespace InactiveReset.Reanchor;

/// <summary>
/// Captures a flat image of the game's mapped module, so that
/// <c>file offset == RVA</c>.
///
/// A runtime capture is necessary because the on-disk executable is wrapped by
/// Steamstub (a <c>.bind</c> section): its <c>.text</c> is ciphertext on disk
/// and only decrypted into memory by the loader. Analysing the file directly
/// yields garbage. Note this is Steam DRM, NOT EasyAntiCheat — which is why
/// launching the game directly gives a fully decrypted image with no anticheat
/// in the process tree at all.
/// </summary>
public static class ModuleDumper
{
    public sealed record CaptureResult(byte[] Image, ulong ModuleBase, int UnreadableBytes);
    public sealed record DumpResult(
        string Path, ulong ModuleBase, int Size, int UnreadableBytes, string Sha256);

    /// <summary>Refuse to key a live capture by a different on-disk executable.</summary>
    public static bool SamePeIdentity(ReadOnlySpan<byte> diskHeader, ReadOnlySpan<byte> mapped) =>
        ProcessMemory.SamePeIdentity(diskHeader, mapped, mapped.Length);

    /// <summary>
    /// Read the whole mapped module. Unreadable pages are zero-filled rather
    /// than skipped: compacting them would shift every byte after that point
    /// and quietly break <c>offset == RVA</c>, which is the only property that
    /// makes the dump useful.
    /// </summary>
    public static DumpResult Dump(Process process, string outputPath)
    {
        var capture = Capture(process);
        File.WriteAllBytes(outputPath, capture.Image);
        return new DumpResult(outputPath, capture.ModuleBase, capture.Image.Length,
                              capture.UnreadableBytes,
                              Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(capture.Image)));
    }

    public static CaptureResult Capture(Process process)
    {
        var module = process.MainModule
            ?? throw new MemoryAccessException("cannot read the game's main module");

        var moduleBase = (ulong)module.BaseAddress.ToInt64();

        // ModuleMemorySize is SizeOfImage — the VIRTUAL footprint, including the
        // zero gaps between sections. Using the file length instead would
        // truncate the capture at roughly half the module.
        var size = module.ModuleMemorySize;

        using var memory = ProcessMemory.OpenRead(process.Id);
        var image = new byte[size];
        var unreadable = 0;

        const int chunk = 0x10000;
        for (var offset = 0; offset < size; offset += chunk)
        {
            var length = Math.Min(chunk, size - offset);
            var span = image.AsSpan(offset, length);
            if (!memory.TryRead(moduleBase + (ulong)offset, span, out var read))
            {
                // Zero whatever did not arrive, preserving alignment.
                span[read..].Clear();
                unreadable += length - read;
            }
        }

        return new CaptureResult(image, moduleBase, unreadable);
    }
}
