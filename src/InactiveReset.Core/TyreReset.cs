using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace InactiveReset.Core;

public sealed record TyreWheelOptions(double ConditionPercent = 100, double? TemperatureCelsius = null)
{
    public void Validate()
    {
        if (!double.IsFinite(ConditionPercent) || ConditionPercent is < 0 or > 100)
            throw new ArgumentException("tyre condition must be between 0 and 100 percent");
        if (TemperatureCelsius is { } temperature &&
            (!double.IsFinite(temperature) || temperature is < 0 or > 150))
            throw new ArgumentException("tyre temperature must be between 0 and 150 °C");
    }
}

// Wheel order is always FL, FR, RL, RR. Uniform callers remain supported.
public sealed record TyreResetOptions(double ConditionPercent = 100, double? TemperatureCelsius = null,
    IReadOnlyList<TyreWheelOptions>? Wheels = null)
{
    public void Validate()
    {
        if (Wheels is null) new TyreWheelOptions(ConditionPercent, TemperatureCelsius).Validate();
        else
        {
            if (Wheels.Count != 4) throw new ArgumentException("provide exactly four tyre settings in FL, FR, RL, RR order");
            foreach (var wheel in Wheels)
                (wheel ?? throw new ArgumentException("a tyre setting is missing")).Validate();
        }
    }

    public TyreWheelOptions[] ResolveWheels()
    {
        Validate();
        return Wheels?.ToArray() ?? Enumerable.Repeat(new TyreWheelOptions(ConditionPercent, TemperatureCelsius), 4).ToArray();
    }

    public static TyreResetOptions FromJson(JsonElement value)
    {
        TyreWheelOptions ReadWheel(JsonElement wheel) => new(
            wheel.GetProperty("conditionPercent").GetDouble(),
            wheel.TryGetProperty("temperatureCelsius", out var temperature) && temperature.ValueKind != JsonValueKind.Null
                ? temperature.GetDouble() : null);
        TyreResetOptions options;
        if (value.TryGetProperty("wheels", out var wheels))
        {
            if (wheels.ValueKind != JsonValueKind.Array) throw new ArgumentException("tyre wheels must be an array");
            options = new(Wheels: wheels.EnumerateArray().Select(ReadWheel).ToArray());
        }
        else options = new(value.TryGetProperty("conditionPercent", out var condition) ? condition.GetDouble() : 100,
            value.TryGetProperty("temperatureCelsius", out var temperature) && temperature.ValueKind != JsonValueKind.Null
                ? temperature.GetDouble() : null);
        options.Validate();
        return options;
    }
}

public sealed record TyreResetResult(bool Verified, string Message);

/// <summary>
/// Prepare the fitted tyres for the game's own next-Drive initializer. The
/// engine rebuilds distributed wear and puncture state using its current car
/// and compounds; an optional temperature override follows initialization.
/// </summary>
public sealed class TyreResetPreparation : IDisposable
{
    private readonly GameSession _session;
    private readonly TyreWheelOptions[] _wheelOptions;
    private readonly Dictionary<string, ulong> _offsets;
    private readonly ulong[] _temperatures;
    private readonly int _slot;
    private readonly SessionIdentity _identity;
    private readonly (ulong Vector, ulong Begin, ulong IndexAddress, int Index, ulong Record, int Compound)[] _tyres;
    private readonly TyreInventoryTransaction _transaction;
    private readonly SharedMemoryOffsets _telemetryOffsets;
    private bool _driveObserved;

    public TyreResetPreparation(GameSession session, string offsetDirectory, TyreResetOptions options)
    {
        _wheelOptions = options.ResolveWheels();
        _session = session;
        if (!session.Memory.CanWrite) throw new GateException("tyre reset requires a write-capable attachment");
        using var document = JsonDocument.Parse(AutomaticTyreOffsets.Load(session, offsetDirectory).ToJsonString());
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("executableSha256").GetString(),
            session.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new GateException("tyre reset profile belongs to another game build");
        var reset = root.GetProperty("reset");
        foreach (var probe in reset.GetProperty("probes").EnumerateArray())
        {
            var bytes = Convert.FromHexString(probe.GetProperty("bytes").GetString()!);
            if (!session.Memory.ReadBytes(session.ModuleBase + Hex(probe.GetProperty("rva")), bytes.Length)
                .AsSpan().SequenceEqual(bytes)) throw new GateException("tyre initializer code probe changed");
        }
        _offsets = reset.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => Hex(p.Value));
        _offsets["physicsPointerOffset"] = Hex(root.GetProperty("physicsPointerOffset"));
        _temperatures = reset.GetProperty("temperatureOffsets").EnumerateArray().Select(Hex).ToArray();
        var reader = new LiveStateReader(session);
        _slot = reader.ResolveSlotIndex();
        if (reader.ReadControlOwner(_slot) != 1) throw new GateException("return to the garage before preparing tyres");
        _telemetryOffsets = SharedMemoryOffsets.Load(Path.Combine(offsetDirectory, "shared-memory.json"));
        using var telemetry = new SharedMemoryReader(_telemetryOffsets);
        var sample = telemetry.Read() ?? throw new GateException("no loaded player car for tyre reset");
        _identity = new(sample.TrackName, sample.VehicleName);
        var fitted = sample.Tyres ?? throw new GateException("fitted compound telemetry is unavailable");
        var wheels = new[] { fitted.FrontLeft, fitted.FrontRight, fitted.RearLeft, fitted.RearRight };
        _tyres = new (ulong, ulong, ulong, int, ulong, int)[4];
        var patches = new List<TyreInventoryPatch>();
        for (var i = 0; i < 4; i++)
        {
            var vector = session.ModuleBase + O("inventoryRva") + (ulong)i * 24;
            var begin = session.Memory.ReadUInt64(vector);
            var end = session.Memory.ReadUInt64(vector + 8);
            var indexAddress = session.ModuleBase + O("selectedIndicesRva") + (ulong)i * 4;
            var index = session.Memory.ReadInt32(indexAddress);
            if (begin == 0 || end < begin || (end - begin) % O("recordStride") != 0 ||
                index < 0 || (ulong)index >= (end - begin) / O("recordStride"))
                throw new GateException("invalid fitted tyre inventory selection");
            var record = begin + (ulong)index * O("recordStride");
            var compound = session.Memory.ReadInt32(record + O("compoundOffset"));
            if (compound != wheels[i].CompoundIndex)
                throw new GateException("fitted compound and tyre inventory disagree; reload the garage");
            _tyres[i] = (vector, begin, indexAddress, index, record, compound);
            foreach (var (address, bytes) in InventoryPayloads(record, O("heatWearOffset"),
                O("freshFlagsOffset"), _wheelOptions[i].ConditionPercent))
            {
                session.Memory.RequireWritableDataPage(address, bytes.Length);
                patches.Add(new(address, session.Memory.ReadBytes(address, bytes.Length), bytes));
            }
        }
        _transaction = new TyreInventoryTransaction(patches, session.Memory.WriteVerified,
            () => SameContext(), () => reader.ReadControlOwner(_slot) == 0);
    }

    public void Stage()
    {
        if (new LiveStateReader(_session).ReadControlOwner(_slot) != 1)
            throw new GateException("Drive was pressed before the tyres were prepared");
        _transaction.Stage();
    }

    public void ObserveDrive()
    {
        _driveObserved = true;
        _transaction.Commit(); // Never restore worn inventory after the game has loaded it.
    }

    public void CheckContext() => RequireSameContext();

    public TyreResetResult AfterDrive()
    {
        ObserveDrive();
        try
        {
            var timer = Stopwatch.StartNew();
            while (_tyres.Any(t => _session.Memory.ReadByte(t.Record + O("freshFlagsOffset") + 1) != 0))
            {
                RequireSameContext();
                if (timer.Elapsed.TotalSeconds > 2)
                    throw new GateException("the game did not consume the prepared tyre records on Drive");
                Thread.Sleep(20);
            }
            RequireSameContext();
            if (_wheelOptions.Any(w => w.TemperatureCelsius is not null)) SetTemperature();
            using var telemetry = new SharedMemoryReader(_telemetryOffsets);
            Thread.Sleep(100);
            var state = telemetry.Read()?.Tyres ?? throw new GateException("tyre telemetry disappeared after Drive");
            var wheels = new[] { state.FrontLeft, state.FrontRight, state.RearLeft, state.RearRight };
            var labels = new[] { "FL", "FR", "RL", "RR" };
            for (var i = 0; i < 4; i++)
                if (!RequestedTyreMatches(wheels[i], _wheelOptions[i]))
                    throw new GateException($"{labels[i]} condition, damage flags or initial temperature did not match the requested reset");
            return new(true, "fitted tyres verified: " + string.Join("; ", _wheelOptions.Select((w, i) =>
                $"{labels[i]} {w.ConditionPercent:G}% / " + (w.TemperatureCelsius is { } t ? $"{t:G} °C initial" : "game temperature"))));
        }
        catch (Exception ex) when (ex is GateException or MemoryAccessException)
        {
            return new(false, $"tyre reset could not be verified: {ex.Message}");
        }
    }

    private void SetTemperature()
    {
        var vehicle = _session.Offsets.Containers.ContainerAddress(_session.ModuleBase, _slot)
            + _session.Offsets.Containers.VehicleDelta;
        var physics = _session.Memory.ReadUInt64(vehicle + O("physicsPointerOffset"));
        if (physics == 0) throw new GateException("player tyre physics is not initialized");
        using (var telemetry = new SharedMemoryReader(_telemetryOffsets))
        {
            var state = telemetry.Read()?.Tyres ?? throw new GateException("no telemetry to validate the discovered thermal layout");
            var fitted = new[] { state.FrontLeft, state.FrontRight, state.RearLeft, state.RearRight };
            for (var i = 0; i < 4; i++)
            {
                var wheel = fitted[i];
                var observed = new[] { wheel.CarcassCelsius, wheel.InnerLayerCelsius.Left, wheel.InnerLayerCelsius.Centre,
                    wheel.InnerLayerCelsius.Right, wheel.SurfaceCelsius.Left, wheel.SurfaceCelsius.Centre, wheel.SurfaceCelsius.Right };
                for (var channel = 0; channel < observed.Length; channel++)
                {
                    var live = BitConverter.ToDouble(_session.Memory.ReadBytes(physics + (ulong)i * O("wheelStride") + _temperatures[channel], 8)) - 273.15;
                    if (!double.IsFinite(live) || !double.IsFinite(observed[channel]) || Math.Abs(live - observed[channel]) > 2)
                        throw new GateException("discovered tyre temperature layout disagrees with live telemetry; temperature writes refused");
                }
            }
        }
        var targets = new List<(ulong Address, byte[] Payload)>();
        var models = new List<(ulong Address, ulong Tyre)>();
        for (var i = 0; i < 4; i++)
        {
            if (_wheelOptions[i].TemperatureCelsius is not { } celsius) continue;
            var kelvin = BitConverter.GetBytes(celsius + 273.15);
            var layers = new byte[40];
            for (var layer = 0; layer < 5; layer++) kelvin.CopyTo(layers, layer * 8);
            var wheel = physics + (ulong)i * O("wheelStride");
            var tyreAddress = wheel + O("tyrePointerOffset");
            var tyre = _session.Memory.ReadUInt64(tyreAddress);
            if (tyre == 0) throw new GateException("a detailed tyre model is missing");
            models.Add((tyreAddress, tyre));
            var count = _session.Memory.ReadInt32(tyre + O("thermalNodesCountOffset"));
            if (count is <= 0 or > 4096) throw new GateException("invalid tyre thermal node count");
            var table = _session.Memory.ReadUInt64(tyre + O("thermalNodesTableOffset"));
            if (table == 0) throw new GateException("tyre thermal node table is missing");
            var pointers = _session.Memory.ReadBytes(table, count * 8);
            for (var node = 0; node < count; node++)
                targets.Add((BitConverter.ToUInt64(pointers, node * 8), layers));
            targets.Add((tyre + O("tyreBulkTemperatureOffset"), kelvin));
            foreach (var offset in _temperatures) targets.Add((wheel + offset, kelvin));
        }
        foreach (var target in targets) _session.Memory.RequireWritableDataPage(target.Address, target.Payload.Length);
        RequireSameContext();
        if (_session.Memory.ReadUInt64(vehicle + O("physicsPointerOffset")) != physics ||
            models.Any(m => _session.Memory.ReadUInt64(m.Address) != m.Tyre))
            throw new GateException("tyre physics changed before applying temperature");
        var written = 0;
        foreach (var target in targets)
        {
            if (written++ % 64 == 0)
            {
                RequireSameContext(checkTelemetry: false);
                if (new LiveStateReader(_session).ReadControlOwner(_slot) != 0 ||
                    _session.Memory.ReadUInt64(vehicle + O("physicsPointerOffset")) != physics ||
                    models.Any(m => _session.Memory.ReadUInt64(m.Address) != m.Tyre))
                    throw new GateException("tyre physics changed while applying temperature");
            }
            _session.Memory.WriteThermalValues(target.Address, target.Payload);
        }
    }

    private bool SameContext(bool checkTelemetry = true)
    {
        if (_session.Process.HasExited || new LiveStateReader(_session).ResolveSlotIndex() != _slot ||
            _tyres.Any(t => _session.Memory.ReadUInt64(t.Vector) != t.Begin ||
                _session.Memory.ReadInt32(t.IndexAddress) != t.Index ||
                _session.Memory.ReadInt32(t.Record + O("compoundOffset")) != t.Compound)) return false;
        if (!checkTelemetry) return true;
        using var reader = new SharedMemoryReader(_telemetryOffsets);
        var sample = reader.Read();
        return sample is not null && string.Equals(sample.TrackName, _identity.TrackName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(sample.VehicleName, _identity.VehicleName, StringComparison.OrdinalIgnoreCase);
    }

    private void RequireSameContext(bool checkTelemetry = true)
    {
        if (!SameContext(checkTelemetry)) throw new GateException("tyre selection or game session changed");
    }
    private ulong O(string name) => _offsets[name];
    internal static bool RequestedTyreMatches(WheelTyreState wheel, TyreWheelOptions requested) =>
        !wheel.Flat && !wheel.Detached && double.IsFinite(wheel.WearRaw) &&
        Math.Abs(wheel.WearRaw * 100 - requested.ConditionPercent) <= 1 &&
        (requested.TemperatureCelsius is not { } temperature || InternalTemperatureMatches(wheel, temperature));
    internal static bool InternalTemperatureMatches(WheelTyreState wheel, double requested) =>
        new[] { wheel.CarcassCelsius, wheel.InnerLayerCelsius.Left, wheel.InnerLayerCelsius.Centre, wheel.InnerLayerCelsius.Right }
            .All(t => double.IsFinite(t) && Math.Abs(t - requested) <= 2);
    private static ulong Hex(JsonElement value) => ulong.Parse(value.GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    internal static IEnumerable<(ulong Address, byte[] Payload)> InventoryPayloads(
        ulong record, ulong heatWearOffset, ulong flagsOffset, double condition)
    {
        var values = new byte[16];
        BitConverter.GetBytes(-1d).CopyTo(values, 0); // Negative skips the separate heat-index setter.
        BitConverter.GetBytes(condition).CopyTo(values, 8); // Inventory uses percent; physics uses 0..1.
        yield return (record + heatWearOffset, values);
        yield return (record + flagsOffset, new byte[] { 1, 1 });
    }
    public void Dispose()
    {
        if (!_driveObserved) _transaction.Dispose();
    }
}

internal sealed record TyreInventoryPatch(ulong Address, byte[] Before, byte[] After);
internal sealed class TyreInventoryTransaction(IReadOnlyList<TyreInventoryPatch> patches,
    SpotWriteTransaction.VerifiedWrite write, Func<bool> sameContext, Func<bool> playerHasControl) : IDisposable
{
    private int _touched;
    private bool _committed;
    internal void Stage()
    {
        try
        {
            foreach (var patch in patches)
            {
                if (!sameContext() || playerHasControl()) throw new GateException("tyre session changed before Drive");
                _touched++;
                write(patch.Address, patch.After);
            }
        }
        catch (Exception error)
        {
            try { Dispose(); }
            catch (Exception restoreError)
            {
                throw new AggregateException("tyre staging failed and original inventory could not be restored", error, restoreError);
            }
            throw;
        }
    }
    internal void Commit() => _committed = true;
    public void Dispose()
    {
        if (_committed || _touched == 0) return;
        if (!sameContext() || playerHasControl()) { _committed = true; return; }
        while (_touched > 0)
        {
            var patch = patches[_touched - 1];
            write(patch.Address, patch.Before);
            _touched--;
        }
    }
}
