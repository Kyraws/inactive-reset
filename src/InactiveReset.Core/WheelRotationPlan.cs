
namespace InactiveReset.Core;

/// <summary>One-shot wheel state restoration using the verified reset layout.</summary>
public sealed class WheelRotationPlan
{
    private sealed record Wheel(ulong Address, ulong Spin, ulong Upright, double Radius,
        double[] Inertia, double Rate, double[] Omega, double[] Momentum);
    private readonly GameSession _session;
    private readonly MotionLayout _layout;
    private readonly BodyVelocityPlan _bodies;
    private readonly Wheel[] _wheels;
    private bool _applied;
    public int WrittenFields { get; private set; }
    public int BytesToWrite => 4 * (24 + 24 + 8);
    public double[] Rates => _wheels.Select(w => w.Rate).ToArray();

    private WheelRotationPlan(GameSession session, BodyVelocityPlan bodies, Wheel[] wheels)
    { _session = session; _layout = session.RequireMotion(); _bodies = bodies; _wheels = wheels; }

    public static (double Rate, double[] Omega, double[] Momentum) Calculate(double longitudinalVelocity,
        double radius, double[] matrix, double[] inertia, double[] uprightOmega)
    {
        if (!double.IsFinite(longitudinalVelocity) ||
            !double.IsFinite(radius) || radius is < 0.15 or > 0.75 ||
            matrix.Length != 9 || inertia.Length != 3 || uprightOmega.Length != 3 ||
            matrix.Any(v => !double.IsFinite(v)) || inertia.Any(v => !double.IsFinite(v) || v is < 0.001 or > 100) ||
            uprightOmega.Any(v => !double.IsFinite(v) || Math.Abs(v) > 0.1))
            throw new GateException("Invalid wheel dimensions, radius, inertia, or stationary angular velocity.");
        for (var a = 0; a < 3; a++)
            for (var b = 0; b < 3; b++)
            {
                var dot = Enumerable.Range(0, 3).Sum(r => matrix[r * 3 + a] * matrix[r * 3 + b]);
                if (Math.Abs(dot - (a == b ? 1 : 0)) > 0.0001)
                    throw new GateException("Wheel orientation is not orthonormal.");
            }
        var determinant = matrix[0] * (matrix[4] * matrix[8] - matrix[5] * matrix[7])
            - matrix[1] * (matrix[3] * matrix[8] - matrix[5] * matrix[6])
            + matrix[2] * (matrix[3] * matrix[7] - matrix[4] * matrix[6]);
        if (Math.Abs(determinant - 1) > 0.0001) throw new GateException("Wheel orientation is reflected.");
        // SDK forward Z and the engine's wheel scalar are both negative when driving forwards.
        var rate = longitudinalVelocity / radius;
        var omega = Enumerable.Range(0, 3).Select(r => uprightOmega[r] + rate * matrix[r * 3]).ToArray();
        var localMomentum = Enumerable.Range(0, 3).Select(c => inertia[c] *
            Enumerable.Range(0, 3).Sum(r => matrix[r * 3 + c] * omega[r])).ToArray();
        var momentum = Enumerable.Range(0, 3).Select(r =>
            Enumerable.Range(0, 3).Sum(c => matrix[r * 3 + c] * localMomentum[c])).ToArray();
        if (!double.IsFinite(rate) || omega.Any(v => !double.IsFinite(v)) || momentum.Any(v => !double.IsFinite(v)))
            throw new GateException("Wheel velocity calculation is not finite.");
        return (rate, omega, momentum);
    }

    public static void RequireReleased(WheelMotionTelemetry telemetry)
    {
        RequirePedalsReleased(telemetry);
        if (telemetry.RotationRadiansPerSecond.Length != 4 ||
            telemetry.RotationRadiansPerSecond.Any(v => !double.IsFinite(v) || Math.Abs(v) > 0.2))
            throw new GateException("Stop all wheels before restoring speed.");
    }

    internal static void RequirePedalsReleased(WheelMotionTelemetry telemetry)
    {
        if (new[] { telemetry.Throttle, telemetry.Brake, telemetry.FilteredThrottle, telemetry.FilteredBrake }
                .Any(v => !double.IsFinite(v) || v is < 0 or > 0.01))
            throw new GateException(telemetry.Brake is >= 0 and <= 0.01 && telemetry.FilteredBrake is > 0.01 and <= 1
                ? "LMU is still applying brakes despite released brake input. Automatic brake-hold release or another braking assist has not released."
                : "Release throttle and brake before restoring speed.");
    }

    public static WheelRotationPlan Prepare(GameSession session, int slot, BodyVelocityPlan bodies, Vec3 velocity)
    {
        bodies.RequireCurrent();
        if (!RollingMotion.EligibleCapture(velocity))
            throw new GateException("Wheel experiment requires a straight, level low-speed capture.");
        var layout = session.RequireMotion();
        var vehicle = session.Offsets.Containers.ContainerAddress(session.ModuleBase, slot) + session.Offsets.Containers.VehicleDelta;
        var physics = session.Memory.ReadUInt64(vehicle + layout.VehiclePhysics);
        var wheels = Enumerable.Range(0, 4).Select(i =>
        {
            var address = physics + (ulong)i * layout.WheelStride;
            var spin = session.Memory.ReadUInt64(address + layout.WheelSpin);
            var upright = session.Memory.ReadUInt64(address + layout.WheelUpright);
            if (spin == upright || !bodies.Bodies.Contains(spin) || !bodies.Bodies.Contains(upright))
                throw new GateException("Wheel bodies are not distinct linked members.");
            double[] Read(ulong p, int count = 3) => ReadDoubles(session, p, count);
            var radius = Read(address + layout.WheelRadius, 1)[0];
            var inertia = Read(spin + layout.BodyInertia);
            if (Read(address + layout.WheelRate, 1).Any(v => !double.IsFinite(v) || Math.Abs(v) > 0.2) ||
                Read(spin + layout.BodyOmega).Any(v => !double.IsFinite(v) || Math.Abs(v) > 0.2))
                throw new GateException("Wheel body is still spinning.");
            var state = Calculate(velocity.Z, radius, Read(spin + layout.BodyMatrix, 9), inertia, Read(upright + layout.BodyOmega));
            return new Wheel(address, spin, upright, radius, inertia, state.Rate, state.Omega, state.Momentum);
        }).ToArray();
        if (wheels.SelectMany(w => new[] { w.Spin, w.Upright }).Distinct().Count() != 8)
            throw new GateException("Wheel body membership overlaps.");
        return new(session, bodies, wheels);
    }

    private static double[] ReadDoubles(GameSession session, ulong address, int count) =>
        Enumerable.Range(0, count).Select(i => BitConverter.ToDouble(session.Memory.ReadBytes(address + (ulong)i * 8, 8))).ToArray();

    public void RequireCurrent()
    {
        _bodies.RequireCurrent();
        foreach (var w in _wheels)
            if (_session.Memory.ReadUInt64(w.Address + _layout.WheelSpin) != w.Spin ||
                _session.Memory.ReadUInt64(w.Address + _layout.WheelUpright) != w.Upright ||
                ReadDoubles(_session, w.Address + _layout.WheelRadius, 1)[0] != w.Radius ||
                !ReadDoubles(_session, w.Spin + _layout.BodyInertia, 3).SequenceEqual(w.Inertia))
                throw new GateException("Wheel body, radius or inertia changed.");
    }

    public void Preflight()
    {
        RequireCurrent();
        foreach (var w in _wheels)
        {
            _session.Memory.RequireWritableDataPage(w.Spin + _layout.BodyOmega, 24);
            _session.Memory.RequireWritableDataPage(w.Spin + _layout.BodyMomentum, 24);
            _session.Memory.RequireWritableDataPage(w.Address + _layout.WheelRate, 8);
        }
    }

    public object[] ReadState()
    {
        RequireCurrent();
        return _wheels.Select(w => (object)new { rate = ReadDoubles(_session, w.Address + _layout.WheelRate, 1)[0],
            omega = ReadDoubles(_session, w.Spin + _layout.BodyOmega, 3), momentum = ReadDoubles(_session, w.Spin + _layout.BodyMomentum, 3) }).ToArray();
    }

    public void VerifyApplied()
    {
        RequireCurrent();
        foreach (var w in _wheels)
            if (!ReadDoubles(_session, w.Spin + _layout.BodyOmega, 3).SequenceEqual(w.Omega) ||
                !ReadDoubles(_session, w.Spin + _layout.BodyMomentum, 3).SequenceEqual(w.Momentum) ||
                ReadDoubles(_session, w.Address + _layout.WheelRate, 1)[0] != w.Rate)
                throw new GateException("The applied wheel state changed before final verification.");
    }

    public void Apply(Action? beforeWrite = null)
    {
        if (_applied) throw new GateException("A wheel experiment can only be applied once.");
        _applied = true;
        Preflight();
        foreach (var w in _wheels)
        {
            RequireCurrent();
            foreach (var (address, values) in new[] { (w.Spin + _layout.BodyOmega, w.Omega),
                (w.Spin + _layout.BodyMomentum, w.Momentum), (w.Address + _layout.WheelRate, new[] { w.Rate }) })
            {
                beforeWrite?.Invoke();
                _session.Memory.WriteVerified(address, values.SelectMany(BitConverter.GetBytes).ToArray());
                WrittenFields++;
            }
        }
    }
}
