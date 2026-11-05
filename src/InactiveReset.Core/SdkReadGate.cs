using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace InactiveReset.Core;

// Implements the SDK's TryLockSpin/Unlock protocol. Only the SDK coordination
// mapping is writable; the telemetry mapping and game process remain read-only.
internal sealed unsafe partial class SdkReadGate : IDisposable
{
    private nint _mapping, _view, _wake, _hold, _data;

    internal SdkReadGate(JsonElement layout)
    {
        try
        {
            if (!layout.TryGetProperty("synchronization", out var sync))
                throw new SharedMemoryException("SDK synchronization layout missing; regenerate shared-memory.json.");
            _mapping = OpenFileMappingA(6, false, sync.GetProperty("lockMap").GetString()!);
            if (_mapping != 0) _view = MapViewOfFile(_mapping, 6, 0, 0, 8);
            _wake = OpenEventA(2, false, sync.GetProperty("lockEvent").GetString()!);
            _hold = OpenEventA(0x100000, false, sync.GetProperty("holdEvent").GetString()!);
            _data = OpenEventA(0x100000, false, sync.GetProperty("dataEvent").GetString()!);
            if (_view == 0 || _wake == 0 || _hold == 0 || _data == 0)
                throw new SharedMemoryException("LMU SDK synchronization gates are unavailable.");
        }
        catch { Dispose(); throw; }
    }

    internal bool WaitForFrame(uint timeout) =>
        WaitForSingleObject(_hold, timeout) == 0 && WaitForSingleObject(_data, timeout) == 0;

    internal void Copy(Action copy)
    {
        var start = Stopwatch.GetTimestamp();
        while (Interlocked.CompareExchange(ref ((int*)_view)[1], 1, 0) != 0)
        {
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 5)
                throw new SharedMemoryException("SDK copy interrupted: shared-memory lock timed out.");
            Thread.SpinWait(20);
        }
        try { copy(); }
        finally
        {
            Interlocked.Exchange(ref ((int*)_view)[1], 0);
            if (Volatile.Read(ref ((int*)_view)[0]) > 0) SetEvent(_wake);
        }
    }

    public void Dispose()
    {
        if (_view != 0) { UnmapViewOfFile(_view); _view = 0; }
        foreach (var handle in new[] { _mapping, _wake, _hold, _data })
            if (handle != 0) CloseHandle(handle);
        _mapping = _wake = _hold = _data = 0;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint OpenFileMappingA(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [LibraryImport("kernel32.dll")]
    private static partial nint MapViewOfFile(nint mapping, uint access, uint high, uint low, nuint size);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint OpenEventA(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetEvent(nint handle);
    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint timeout);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnmapViewOfFile(nint view);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
}
