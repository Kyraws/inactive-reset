using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InactiveReset.Core;

public sealed class MemoryAccessException(string message) : Exception(message);

/// <summary>
/// Read, and optionally write, another process's memory.
///
/// Deliberately limited. There is no page-protection change, no allocation, no
/// remote thread, no debugger attachment. If a target page is not already
/// writable, the write is refused rather than made possible -- a page that
/// needs <c>VirtualProtectEx</c> is a page we have no business writing.
/// </summary>
public sealed partial class ProcessMemory : IDisposable
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;

    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_GUARD = 0x100;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_WRITECOPY = 0x08;

    private nint _handle;

    public int ProcessId { get; }
    public bool CanWrite { get; }

    private ProcessMemory(nint handle, int processId, bool canWrite)
    {
        _handle = handle;
        ProcessId = processId;
        CanWrite = canWrite;
    }

    public static bool SamePeIdentity(ReadOnlySpan<byte> diskHeader, ReadOnlySpan<byte> mapped, int moduleSize)
    {
        if (diskHeader.Length < 0x1000 || mapped.Length < 0x1000 ||
            diskHeader[0] != 'M' || diskHeader[1] != 'Z' ||
            mapped[0] != 'M' || mapped[1] != 'Z') return false;
        var pe = BinaryPrimitives.ReadInt32LittleEndian(diskHeader.Slice(0x3C, 4));
        if (pe < 0x40 || pe > 0x400 ||
            BinaryPrimitives.ReadInt32LittleEndian(mapped.Slice(0x3C, 4)) != pe ||
            BinaryPrimitives.ReadUInt32LittleEndian(diskHeader.Slice(pe, 4)) != 0x4550 ||
            !diskHeader.Slice(pe, 24).SequenceEqual(mapped.Slice(pe, 24))) return false;
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(diskHeader.Slice(pe + 6, 2));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(diskHeader.Slice(pe + 20, 2));
        var sectionAt = pe + 24 + optionalSize;
        if (sections is < 1 or > 96 || optionalSize < 0x40 ||
            sectionAt + sections * 40 > diskHeader.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(diskHeader.Slice(pe + 24, 2)) != 0x20B ||
            !diskHeader.Slice(pe + 24, 2).SequenceEqual(mapped.Slice(pe + 24, 2)) ||
            BinaryPrimitives.ReadUInt32LittleEndian(diskHeader.Slice(pe + 24 + 0x38, 4)) != moduleSize)
            return false;
        return diskHeader.Slice(sectionAt, sections * 40)
                         .SequenceEqual(mapped.Slice(sectionAt, sections * 40));
    }


    public static ProcessMemory OpenRead(int processId) => Open(processId, write: false);

    public static ProcessMemory OpenWrite(int processId) => Open(processId, write: true);

    private static ProcessMemory Open(int processId, bool write)
    {
        var access = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ;
        if (write)
        {
            access |= PROCESS_VM_WRITE | PROCESS_VM_OPERATION;
        }

        var handle = OpenProcess(access, false, processId);
        if (handle == 0)
        {
            var code = Marshal.GetLastWin32Error();
            throw new MemoryAccessException(
                $"OpenProcess({(write ? "read+write" : "read")}) failed for pid {processId}: " +
                $"{new System.ComponentModel.Win32Exception(code).Message}");
        }
        return new ProcessMemory(handle, processId, write);
    }

    // ---- reads -------------------------------------------------------------

    public void ReadExact(ulong address, Span<byte> buffer)
    {
        if (!TryRead(address, buffer, out var read))
        {
            var code = Marshal.GetLastWin32Error();
            throw new MemoryAccessException(
                $"read of {buffer.Length} bytes at 0x{address:X} failed " +
                $"(got {read}): {new System.ComponentModel.Win32Exception(code).Message}");
        }
    }

    public bool TryRead(ulong address, Span<byte> buffer, out int bytesRead)
    {
        var ok = ReadProcessMemory(_handle, (nint)address, out MemoryMarshal.GetReference(buffer),
                                   (nuint)buffer.Length, out var read);
        bytesRead = (int)read;
        return ok && bytesRead == buffer.Length;
    }

    public byte[] ReadBytes(ulong address, int count)
    {
        var buffer = new byte[count];
        ReadExact(address, buffer);
        return buffer;
    }

    public byte ReadByte(ulong address)
    {
        Span<byte> buffer = stackalloc byte[1];
        ReadExact(address, buffer);
        return buffer[0];
    }

    public int ReadInt32(ulong address)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(address, buffer);
        return BitConverter.ToInt32(buffer);
    }

    public float ReadSingle(ulong address)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(address, buffer);
        return BitConverter.ToSingle(buffer);
    }

    public ulong ReadUInt64(ulong address)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(address, buffer);
        return BitConverter.ToUInt64(buffer);
    }

    // ---- writes ------------------------------------------------------------

    /// <summary>
    /// Write bytes, then read them back and confirm. Refuses any page that is
    /// not already committed and writable, and never changes page protection.
    /// </summary>
    public void WriteVerified(ulong address, ReadOnlySpan<byte> payload)
    {
        if (!CanWrite)
        {
            throw new MemoryAccessException("this handle was opened read-only");
        }
        RequireWritableDataPage(address, payload.Length);

        var ok = WriteProcessMemory(_handle, (nint)address,
                                    in MemoryMarshal.GetReference(payload),
                                    (nuint)payload.Length, out var written);
        if (!ok || (int)written != payload.Length)
        {
            var code = Marshal.GetLastWin32Error();
            throw new MemoryAccessException(
                $"write of {payload.Length} bytes at 0x{address:X} failed: " +
                $"{new System.ComponentModel.Win32Exception(code).Message}");
        }

        Span<byte> readback = payload.Length <= 64 ? stackalloc byte[payload.Length]
                                                   : new byte[payload.Length];
        ReadExact(address, readback);
        if (!readback.SequenceEqual(payload))
        {
            throw new MemoryAccessException(
                $"write at 0x{address:X} did not verify by readback");
        }
    }

    /// <summary>
    /// Refuse anything that is not wholly inside a committed, writable,
    /// non-guard data page. An executable page here would mean we are about to
    /// write code, which this tool never does.
    /// </summary>
    public void RequireWritableDataPage(ulong address, int length)
    {
        if (address == 0)
        {
            throw new MemoryAccessException("refusing a null write address");
        }

        if (VirtualQueryEx(_handle, (nint)address, out var info,
                           (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
        {
            var code = Marshal.GetLastWin32Error();
            throw new MemoryAccessException(
                $"cannot query the page at 0x{address:X}: " +
                $"{new System.ComponentModel.Win32Exception(code).Message}");
        }

        var regionBase = (ulong)info.BaseAddress;
        var regionEnd = regionBase + info.RegionSize;
        var protection = info.Protect & 0xFF;
        var writable = protection is PAGE_READWRITE or PAGE_WRITECOPY;

        if (info.State != MEM_COMMIT
            || !writable
            || (info.Protect & PAGE_GUARD) != 0
            || (info.Protect & PAGE_NOACCESS) != 0
            || address < regionBase
            || regionEnd < (ulong)length
            || address > regionEnd - (ulong)length)
        {
            throw new MemoryAccessException(
                $"the {length}-byte target at 0x{address:X} is not wholly inside a committed, " +
                $"writable, non-executable data page (state 0x{info.State:X}, " +
                $"protect 0x{info.Protect:X}); refusing. This tool never changes page protection.");
        }
    }

    // ---- module ------------------------------------------------------------

    /// <summary>
    /// Base address of the process's main module. ASLR'd, so it differs every
    /// launch -- read it, never assume it.
    /// </summary>
    public static ulong MainModuleBase(Process process) =>
        (ulong)process.MainModule!.BaseAddress.ToInt64();

    /// <summary>Capture RVA-aligned mapped bytes; refuse unreadable pages.</summary>
    public byte[] CaptureMappedModule(Process process)
    {
        var module = process.MainModule
            ?? throw new MemoryAccessException("cannot read the game's main module");
        var size = module.ModuleMemorySize;
        if (size < 0x1000 || size > 0x20000000)
            throw new MemoryAccessException($"implausible mapped module size 0x{size:X}");
        var image = new byte[size];
        var baseAddress = (ulong)module.BaseAddress.ToInt64();
        const int chunk = 0x10000;
        for (var offset = 0; offset < size; offset += chunk)
        {
            var span = image.AsSpan(offset, Math.Min(chunk, size - offset));
            if (!TryRead(baseAddress + (ulong)offset, span, out var read))
                throw new MemoryAccessException(
                    $"mapped module unreadable at RVA 0x{offset:X} ({read}/{span.Length} bytes)");
        }
        return image;
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            CloseHandle(_handle);
            _handle = 0;
        }
    }

    // ---- interop -----------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(nint process, nint address,
        out byte buffer, nuint size, out nuint bytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(nint process, nint address,
        in byte buffer, nuint size, out nuint bytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQueryEx(nint process, nint address,
        out MemoryBasicInformation info, nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
