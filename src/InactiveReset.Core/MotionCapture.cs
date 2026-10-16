namespace InactiveReset.Core;

/// <summary>Recorded SDK local velocity, in metres per second. No game writes.</summary>
public static class MotionCapture
{
    public static Vec3 Validate(Vec3 velocity) => velocity.IsFinite
        ? velocity : throw new CheckpointException("captured velocity contains a non-finite value");

    public static double SpeedKmh(Vec3 velocity)
    {
        Validate(velocity);
        return Math.Sqrt((double)velocity.X * velocity.X + (double)velocity.Y * velocity.Y
            + (double)velocity.Z * velocity.Z) * 3.6;
    }

    internal static Vec3 ReadLocalVelocity(int address, Func<int, double> readDouble) =>
        Validate(new((float)readDouble(address), (float)readDouble(address + 8),
            (float)readDouble(address + 16)));
}
