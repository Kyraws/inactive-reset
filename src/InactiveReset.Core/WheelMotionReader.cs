using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace InactiveReset.Core;

public sealed record WheelMotionTelemetry(double ElapsedTime, double Throttle, double Brake,
    double FilteredThrottle, double FilteredBrake, double[] RotationRadiansPerSecond,
    double[] StaticRadiusMetres, double[] DeflectionMetres);

/// <summary>Extra read-only SDK channels for the experimental rolling start.</summary>
public sealed class WheelMotionReader : IDisposable
{
    private readonly SharedMemoryOffsets _offsets;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly int _throttle, _brake, _filteredThrottle, _filteredBrake;
    private readonly int _wheel, _stride, _rotation, _radius, _deflection;

    public WheelMotionReader(string offsetDirectory)
    {
        var path = Path.Combine(offsetDirectory, "shared-memory.json");
        _offsets = SharedMemoryOffsets.Load(path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var telem = json.RootElement.GetProperty("telemInfo");
        var wheel = json.RootElement.GetProperty("wheel");
        _throttle = telem.GetProperty("mUnfilteredThrottle").GetInt32();
        _brake = telem.GetProperty("mUnfilteredBrake").GetInt32();
        _filteredThrottle = telem.GetProperty("mFilteredThrottle").GetInt32();
        _filteredBrake = telem.GetProperty("mFilteredBrake").GetInt32();
        _wheel = telem.GetProperty("mWheel").GetInt32();
        _stride = telem.GetProperty("wheelStride").GetInt32();
        _rotation = wheel.GetProperty("mRotation").GetInt32();
        _radius = wheel.GetProperty("mStaticUndeflectedRadius").GetInt32();
        _deflection = wheel.GetProperty("mVerticalTireDeflection").GetInt32();
        _mapping = MemoryMappedFile.OpenExisting(_offsets.MapName, MemoryMappedFileRights.Read);
        try { _view = _mapping.CreateViewAccessor(0, _offsets.LayoutSize, MemoryMappedFileAccess.Read); }
        catch { _mapping.Dispose(); throw; }
    }

    public WheelMotionTelemetry Read()
    {
        if (_view.ReadByte(_offsets.PlayerHasVehicle) == 0) throw new GateException("No SDK player vehicle.");
        var index = _view.ReadByte(_offsets.PlayerVehicleIdx);
        if (index >= 104) throw new GateException("Invalid SDK player index.");
        var telem = _offsets.TelemInfo + index * _offsets.TelemInfoStride;
        double Value(int address)
        {
            var value = _view.ReadDouble(address);
            if (!double.IsFinite(value)) throw new GateException("Non-finite wheel/pedal telemetry.");
            return value;
        }
        double Pedal(int field)
        {
            var value = Value(telem + field);
            if (value is < 0 or > 1) throw new GateException("Invalid SDK pedal value.");
            return value;
        }
        var rotation = Enumerable.Range(0, 4).Select(i => Value(telem + _wheel + i * _stride + _rotation)).ToArray();
        var radius = Enumerable.Range(0, 4).Select(i => _view.ReadByte(telem + _wheel + i * _stride + _radius) * 0.01).ToArray();
        var deflection = Enumerable.Range(0, 4).Select(i => Value(telem + _wheel + i * _stride + _deflection)).ToArray();
        return new(Value(telem + _offsets.TelemElapsedTime), Pedal(_throttle), Pedal(_brake),
            Pedal(_filteredThrottle), Pedal(_filteredBrake), rotation, radius, deflection);
    }

    public void Dispose() { _view.Dispose(); _mapping.Dispose(); }
}
