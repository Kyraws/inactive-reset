using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InactiveReset.Core;

// Limited process identity query works without module inspection or memory attachment, including EAC.
internal static partial class RecordingProcessPath
{
    internal static unsafe string Read(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var buffer = new char[32768];
            uint size = (uint)buffer.Length;
            fixed (char* pointer = buffer)
                if (!QueryFullProcessImageNameW(handle, 0, pointer, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new string(buffer, 0, (int)size);
        }
        finally { CloseHandle(handle); }
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool QueryFullProcessImageNameW(nint process, uint flags, char* path, ref uint size);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
}
