
namespace InactiveReset.Core;

/// <summary>Build-specific experiment on linear velocity of the linked rigid bodies.</summary>
public sealed class BodyVelocityPlan
{
    private readonly GameSession _session;
    private readonly MotionLayout _layout;
    private readonly int _slot;
    private readonly ulong _vehicle;
    private readonly ulong _physics;
    private readonly ulong _begin;
    private readonly ulong _end;
    private readonly ulong _chassisBegin;
    private readonly ulong _chassisEnd;
    private readonly byte[][] _payloads;
    public ulong[] Bodies { get; }
    public int BytesToWrite => Bodies.Length * 24;

    // The shared profile validates the native body layout before use.
    private BodyVelocityPlan(GameSession session, int slot, ulong vehicle, ulong physics,
        ulong begin, ulong end, ulong chassisBegin, ulong chassisEnd, ulong[] bodies, byte[][] payloads)
    {
        _session = session; _layout = session.RequireMotion(); _slot = slot; _vehicle = vehicle; _physics = physics;
        _begin = begin; _end = end; _chassisBegin = chassisBegin; _chassisEnd = chassisEnd;
        Bodies = bodies; _payloads = payloads;
    }

    public static byte[] ShiftVelocity(double[] current, double[] delta)
    {
        if (current.Length != 3 || delta.Length != 3 || current.Any(v => !double.IsFinite(v)) ||
            delta.Any(v => !double.IsFinite(v)) || current.Any(v => Math.Abs(v) > 0.1))
            throw new GateException("Body velocity must be finite and the car at rest.");
        var payload = new byte[24];
        for (var i = 0; i < 3; i++) BitConverter.GetBytes(current[i] + delta[i]).CopyTo(payload, i * 8);
        return payload;
    }

    public static BodyVelocityPlan Prepare(GameSession session, int slot, Vec3 velocity, RecordedPose pose)
    {
        var layout = session.RequireMotion();
        var vehicle = session.Offsets.Containers.ContainerAddress(session.ModuleBase, slot) + session.Offsets.Containers.VehicleDelta;
        var physics = session.Memory.ReadUInt64(vehicle + layout.VehiclePhysics);
        if (physics == 0) throw new GateException("No physics object.");
        var begin = session.Memory.ReadUInt64(physics + layout.PhysicsBodiesBegin);
        var end = session.Memory.ReadUInt64(physics + layout.PhysicsBodiesEnd);
        var chassisBegin = session.Memory.ReadUInt64(physics + layout.PhysicsChassisBegin);
        var chassisEnd = session.Memory.ReadUInt64(physics + layout.PhysicsChassisEnd);
        var count = VectorCount(begin, end);
        var chassisCount = VectorCount(chassisBegin, chassisEnd);
        if (count != 12 || chassisCount != 2)
            throw new GateException("This experiment requires the researched 12-body, two-chassis layout.");
        var chassis = session.Memory.ReadUInt64(chassisBegin);
        var bodies = Enumerable.Range(0, count).Select(i => session.Memory.ReadUInt64(begin + (ulong)i * 8)).ToArray();
        if (bodies.Any(p => p == 0 || p % 8 != 0) || bodies.Distinct().Count() != count || !bodies.Contains(chassis))
            throw new GateException("Body vector has null, unaligned, duplicate or missing chassis entries.");
        var targetFloats = RollingMotion.BuildPayload(velocity, pose);
        var target = Enumerable.Range(0, 3).Select(i => (double)BitConverter.ToSingle(targetFloats, i * 4)).ToArray();
        var currentChassis = ReadVector(session, chassis + layout.BodyVelocity);
        var delta = target.Zip(currentChassis, (wanted, current) => wanted - current).ToArray();
        var payloads = bodies.Select(p => ShiftVelocity(ReadVector(session, p + layout.BodyVelocity), delta)).ToArray();
        return new(session, slot, vehicle, physics, begin, end, chassisBegin, chassisEnd, bodies, payloads);
    }

    private static int VectorCount(ulong begin, ulong end)
    {
        if (begin == 0 || begin % 8 != 0 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 64)
            throw new GateException("Invalid linked-body vector.");
        return (int)((end - begin) / 8);
    }

    private static double[] ReadVector(GameSession session, ulong address)
    {
        var bytes = session.Memory.ReadBytes(address, 24);
        var values = Enumerable.Range(0, 3).Select(i => BitConverter.ToDouble(bytes, i * 8)).ToArray();
        if (values.Any(v => !double.IsFinite(v))) throw new GateException("Non-finite body velocity.");
        return values;
    }

    public void RequireCurrent()
    {
        var memory = _session.Memory;
        if (_session.Process.HasExited || new LiveStateReader(_session).ResolveSlotIndex() != _slot ||
            memory.ReadUInt64(_vehicle + _layout.VehiclePhysics) != _physics ||
            memory.ReadUInt64(_physics + _layout.PhysicsBodiesBegin) != _begin || memory.ReadUInt64(_physics + _layout.PhysicsBodiesEnd) != _end ||
            memory.ReadUInt64(_physics + _layout.PhysicsChassisBegin) != _chassisBegin || memory.ReadUInt64(_physics + _layout.PhysicsChassisEnd) != _chassisEnd)
            throw new GateException("Player physics/body vector changed.");
        for (var i = 0; i < Bodies.Length; i++)
            if (memory.ReadUInt64(_begin + (ulong)i * 8) != Bodies[i]) throw new GateException("Linked-body membership changed.");
    }

    public double[][] ReadVelocities()
    {
        RequireCurrent();
        return Bodies.Select(p => ReadVector(_session, p + _layout.BodyVelocity)).ToArray();
    }

    private bool _applied;
    public int WrittenBodies { get; private set; }
    public void VerifyApplied()
    {
        RequireCurrent();
        for (var i = 0; i < Bodies.Length; i++)
            if (!_session.Memory.ReadBytes(Bodies[i] + _layout.BodyVelocity, 24).AsSpan().SequenceEqual(_payloads[i]))
                throw new GateException("The applied body velocities changed before final verification.");
    }

    public void Apply(Action? beforeWrite = null)
    {
        if (_applied) throw new GateException("A body experiment can only be applied once.");
        _applied = true;
        RequireCurrent();
        // Check all pages before the first mutation; physics may still advance during the batch.
        foreach (var p in Bodies) _session.Memory.RequireWritableDataPage(p + _layout.BodyVelocity, 24);
        for (var i = 0; i < Bodies.Length; i++)
        {
            RequireCurrent();
            beforeWrite?.Invoke();
            _session.Memory.WriteVerified(Bodies[i] + _layout.BodyVelocity, _payloads[i]);
            WrittenBodies++;
        }
    }
}
