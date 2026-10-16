using System.Runtime.InteropServices;
using System.Text;
using InactiveReset.Core;

namespace InactiveReset.App;

// Read-only background HID input; never acquires a wheel exclusively or writes force feedback.
internal sealed class WheelInput
{
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceEntry { public IntPtr Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct UsageAndPage { public ushort Usage, Page; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr input, uint command, byte[]? data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, byte[]? data, ref uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputDeviceList([Out] DeviceEntry[]? devices, ref uint count, uint size);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(byte[] preparsed, byte[] caps);
    [DllImport("hid.dll")] private static extern uint HidP_MaxUsageListLength(int type, ushort page, byte[] preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetUsagesEx(int type, ushort collection, [Out] UsageAndPage[] usages, ref uint count, byte[] preparsed, byte[] report, uint length);
    private sealed record Device(string Path, string Name, byte[] Preparsed, int Capacity);
    private readonly Dictionary<IntPtr, Device> _devices = [];
    private readonly WheelButtonEdges _edges = new();
    public event Action<WheelBinding>? Pressed;
    public event Action? DevicesChanged;
    public string? Error { get; private set; }
    public object[] Devices => _devices.Values.Select(d => (object)new { path = d.Path, name = d.Name }).ToArray();

    public void Register(IntPtr window)
    {
        var devices = new ushort[] { 4, 5, 8 }.Select(u => new RawDevice { Page = 1, Usage = u, Flags = window == IntPtr.Zero ? 1u : 0x2100u, Window = window }).ToArray();
        Error = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawDevice>())
            ? null : $"Wheel input registration failed ({Marshal.GetLastWin32Error()}).";
        if (window != IntPtr.Zero) RefreshDevices();
    }

    public void RefreshDevices()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<DeviceEntry>();
        if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count > 1024) return;
        var entries = new DeviceEntry[count];
        var read = GetRawInputDeviceList(entries, ref count, size);
        if (read == uint.MaxValue) return;
        var connected = entries.Take((int)read).Where(d => d.Type == 2).Select(d => d.Handle).ToHashSet();
        foreach (var handle in _devices.Keys.Where(h => !connected.Contains(h)).ToArray()) Remove(handle);
        foreach (var handle in connected) GetDevice(handle);
        DevicesChanged?.Invoke();
    }

    private Device? GetDevice(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var found)) return found;
        uint size = 0;
        if (GetRawInputDeviceInfo(handle, 0x20000005, null, ref size) == uint.MaxValue || size is 0 or > 1048576) return null;
        var preparsed = new byte[size];
        if (GetRawInputDeviceInfo(handle, 0x20000005, preparsed, ref size) == uint.MaxValue) return null;
        var caps = new byte[64];
        if (HidP_GetCaps(preparsed, caps) != 0x110000 || BitConverter.ToUInt16(caps, 2) != 1 || BitConverter.ToUInt16(caps, 0) is not (4 or 5 or 8)) return null;
        var capacity = HidP_MaxUsageListLength(0, 0, preparsed);
        if (capacity is 0 or > 4096) return null;
        size = 0;
        if (GetRawInputDeviceInfo(handle, 0x20000007, null, ref size) == uint.MaxValue || size is 0 or > 2048) return null;
        var name = new byte[(size + 1) * 2];
        if (GetRawInputDeviceInfo(handle, 0x20000007, name, ref size) == uint.MaxValue) return null;
        var path = Encoding.Unicode.GetString(name).TrimEnd('\0');
        var label = path.Split('#').Skip(1).FirstOrDefault() ?? "HID controller";
        return _devices[handle] = new(path, label, preparsed, (int)capacity);
    }

    private void Remove(IntPtr handle)
    {
        if (_devices.Remove(handle, out var device)) _edges.Remove(device.Path);
    }

    public void DeviceChange(IntPtr handle, bool removed)
    {
        if (removed) Remove(handle); else GetDevice(handle);
        DevicesChanged?.Invoke();
    }

    public void Read(IntPtr input)
    {
        var header = (uint)(8 + IntPtr.Size * 2);
        uint size = 0;
        if (GetRawInputData(input, 0x10000003, null, ref size, header) == uint.MaxValue || size < header + 8 || size > 1048576) return;
        var data = new byte[size];
        if (GetRawInputData(input, 0x10000003, data, ref size, header) == uint.MaxValue || BitConverter.ToUInt32(data) != 2) return;
        var handle = IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(data, 8)) : new IntPtr(BitConverter.ToInt32(data, 8));
        var device = GetDevice(handle);
        if (device is null) return;
        var reportSize = BitConverter.ToUInt32(data, (int)header);
        var reports = BitConverter.ToUInt32(data, (int)header + 4);
        if (reportSize == 0 || reports > 4096 || (ulong)header + 8 + (ulong)reportSize * reports > (ulong)data.Length) return;
        var usages = new UsageAndPage[device.Capacity];
        for (uint i = 0; i < reports; i++)
        {
            var report = data.AsSpan(checked((int)(header + 8 + i * reportSize)), checked((int)reportSize)).ToArray();
            var count = (uint)usages.Length;
            if (HidP_GetUsagesEx(0, 0, usages, ref count, device.Preparsed, report, reportSize) != 0x110000 || count > usages.Length) continue;
            foreach (var button in _edges.Update(device.Path, report[0], usages.Take((int)count).Where(u => u.Page == 9).Select(u => (int)u.Usage)))
                Pressed?.Invoke(new(device.Path, button));
        }
    }
}
