namespace InactiveReset.Core;

/// <summary>Researched gearbox/engine handoff, prepared and verified while physics is paused.</summary>
public sealed class GearMotionPlan
{
    public sealed record Target(int Gear, double EngineAngularVelocity, double Clutch)
    { public double EngineRpm => EngineAngularVelocity * 60 / (2 * Math.PI); }
    private sealed record Field(string Name, ulong Address, byte[] Payload);
    private readonly GameSession _game;
    private readonly MotionLayout _layout;
    private readonly MotionLayout.EngineSetter _setter;
    private readonly Action _requireCurrent;
    private readonly ulong _physics, _engine, _ratioAddress, _overrideAddress;
    private readonly double _ratio, _revBase;
    private readonly float _revOverride;
    private readonly int _maxGear;
    private readonly Field[] _fields;
    private bool _applied;
    public Target Requested { get; }
    public int WrittenFields { get; private set; }
    public int BytesToWrite => _fields.Sum(f => f.Payload.Length);

    private GearMotionPlan(GameSession game, Action requireCurrent, ulong physics, ulong engine, MotionLayout.EngineSetter setter,
        ulong ratioAddress, double ratio, ulong overrideAddress, float revOverride, double revBase, int maxGear,
        Target target, Field[] fields)
    {
        _game = game; _layout = game.RequireMotion(); _setter = setter; _requireCurrent = requireCurrent; _physics = physics; _engine = engine;
        _ratioAddress = ratioAddress; _ratio = ratio; _overrideAddress = overrideAddress;
        _revOverride = revOverride; _revBase = revBase; _maxGear = maxGear; Requested = target; _fields = fields;
    }

    public static Target Calculate(int gear, int maxGear, double ratio, double[] wheelRates,
        int driveType, double idle, double revLimit)
    {
        if (maxGear is < 1 or > 8 || gear < -1 || gear > maxGear ||
            !double.IsFinite(ratio) || wheelRates.Length != 4 || wheelRates.Any(v => !double.IsFinite(v)) ||
            !double.IsFinite(idle) || !double.IsFinite(revLimit) || idle <= 0 || revLimit < idle ||
            (gear > 0 && ratio <= 0) || (gear < 0 && ratio >= 0) || (gear == 0 && ratio != 0))
            throw new GateException("Invalid captured gear or loaded gearbox/engine configuration.");
        // EEC290 receives physics+618: 0=rear, 1=all, 2=front driven wheel rates.
        var driven = driveType switch
        {
            0 => (wheelRates[2] + wheelRates[3]) / 2,
            1 => wheelRates.Average(),
            2 => (wheelRates[0] + wheelRates[1]) / 2,
            _ => throw new GateException("Unsupported driven-axle configuration."),
        };
        var coupled = -driven * ratio;
        if (!double.IsFinite(coupled) || coupled < 0 || coupled > revLimit)
            throw new GateException($"Captured gear {gear} would exceed the loaded engine rev limit or oppose the recorded direction.");
        return new(gear, Math.Max(idle, coupled), gear == 0 || coupled < idle ? 1 : 0);
    }

    private static double ReadDouble(GameSession game, ulong address) =>
        BitConverter.ToDouble(game.Memory.ReadBytes(address, 8));

    public static GearMotionPlan Prepare(GameSession game, int slot, BodyVelocityPlan bodies,
        WheelRotationPlan wheels, int gear)
    {
        bodies.RequireCurrent(); wheels.RequireCurrent();
        return Prepare(game, slot, bodies.RequireCurrent, wheels.Rates, gear, requireNeutral: true);
    }

    public static GearMotionPlan PrepareForReset(GameSession game, int slot, DirectPosePlan pose, int gear) =>
        Prepare(game, slot, pose.RequireCurrent, pose.WheelRates, gear, requireNeutral: false);

    private static GearMotionPlan Prepare(GameSession game, int slot, Action requireCurrent,
        double[] wheelRates, int gear, bool requireNeutral)
    {
        var layout = game.RequireMotion();
        requireCurrent();
        var vehicle = game.Offsets.Containers.ContainerAddress(game.ModuleBase, slot) + game.Offsets.Containers.VehicleDelta;
        var physics = game.Memory.ReadUInt64(vehicle + layout.VehiclePhysics);
        var engine = game.Memory.ReadUInt64(physics + layout.PhysicsEngine);
        if (engine == 0) throw new GateException("Engine state unavailable.");
        var setter = layout.ReadEngineSetter(game,engine);
        var maxGear = game.Memory.ReadInt32(physics + layout.GearMax);
        if (gear < -1 || gear > maxGear) throw new GateException("Captured gear does not exist in the loaded car.");
        var ratioAddress = physics + layout.GearRatios + (ulong)(gear + 1) * 8;
        var overrideAddress = physics + layout.GearRevOverrides + (ulong)(gear + 1) * 4;
        var ratio = ReadDouble(game, ratioAddress);
        var revOverride = game.Memory.ReadSingle(overrideAddress);
        var revBase = ReadDouble(game, physics + layout.GearRevBase);
        var target = Calculate(gear, maxGear, ratio, wheelRates,
            game.Memory.ReadInt32(physics + layout.GearDriveType), ReadDouble(game, physics + layout.GearIdle), revBase + revOverride);
        if (requireNeutral && (game.Memory.ReadInt32(physics + layout.GearEngaged) != 0 || game.Memory.ReadInt32(physics + layout.GearRequested) != 0 ||
            game.Memory.ReadInt32(physics + layout.GearShiftDirection) != 0))
            throw new GateException("Wait in neutral for the gearbox shift to finish.");
        Field Int(string name, ulong address, int value) => new(name, address, BitConverter.GetBytes(value));
        Field Double(string name, ulong address, double value) => new(name, address, BitConverter.GetBytes(value));
        Field[] fields =
        [
            Double("engine angular velocity", engine + setter.OmegaOffset, target.EngineAngularVelocity),
            Double("cached engine angular velocity", physics + layout.GearCachedOmega, target.EngineAngularVelocity),
            Double("effective clutch", physics + layout.GearClutch, target.Clutch),
            Double("active gear rev adjustment", physics + layout.GearActiveRevOverride, revOverride),
            Int("shift direction", physics + layout.GearShiftDirection, 0),
            Int("shift state", physics + layout.GearShiftState, 0),
            Int("input gear", physics + layout.GearInput, gear),
            Int("requested gear", physics + layout.GearRequested, gear),
            Int("previous gear", physics + layout.GearPrevious, gear),
            Int("engaged gear", physics + layout.GearEngaged, gear),
            Int("cached engaged gear", vehicle + layout.VehicleEngagedGear, gear),
            Int("cached requested gear", vehicle + layout.VehicleRequestedGear, gear),
        ];
        var plan = new GearMotionPlan(game, requireCurrent, physics, engine, setter, ratioAddress, ratio,
            overrideAddress, revOverride, revBase, maxGear, target, fields);
        plan.Preflight();
        return plan;
    }

    public void RequireCurrent()
    {
        _requireCurrent();
        if (_game.Memory.ReadUInt64(_physics + _layout.PhysicsEngine) != _engine || _game.Memory.ReadUInt64(_engine) != _setter.Vtable ||
            _game.Memory.ReadUInt64(_setter.Vtable + _layout.EngineSetterSlot) != _setter.Function ||
            !_game.Memory.ReadBytes(_setter.Function,_setter.Code.Length).AsSpan().SequenceEqual(_setter.Code) ||
            _game.Memory.ReadInt32(_physics + _layout.GearMax) != _maxGear || ReadDouble(_game, _ratioAddress) != _ratio ||
            _game.Memory.ReadSingle(_overrideAddress) != _revOverride || ReadDouble(_game, _physics + _layout.GearRevBase) != _revBase)
            throw new GateException("Gearbox/engine context or configuration changed.");
    }

    public void Preflight()
    {
        RequireCurrent();
        foreach (var field in _fields) _game.Memory.RequireWritableDataPage(field.Address, field.Payload.Length);
    }

    public double ReadEngineRpm()
    {
        RequireCurrent();
        var rpm = ReadDouble(_game, _fields[0].Address) * 60 / (2 * Math.PI);
        if (!double.IsFinite(rpm) || rpm < 0) throw new GateException("Invalid observed engine RPM.");
        return rpm;
    }

    public object ReadState()
    {
        RequireCurrent();
        return _fields.Select(f => new { f.Name, bytes = Convert.ToHexString(_game.Memory.ReadBytes(f.Address, f.Payload.Length)) }).ToArray();
    }

    public void Apply(Action beforeWrite)
    {
        if (_applied) throw new GateException("Gear state can only be applied once.");
        _applied = true; Preflight();
        foreach (var field in _fields)
        {
            beforeWrite(); RequireCurrent();
            _game.Memory.WriteVerified(field.Address, field.Payload); WrittenFields++;
        }
    }

    public void VerifyApplied()
    {
        RequireCurrent();
        foreach (var field in _fields)
            if (!_game.Memory.ReadBytes(field.Address, field.Payload.Length).AsSpan().SequenceEqual(field.Payload))
                throw new GateException($"Applied {field.Name} changed before gearbox verification.");
    }
}
