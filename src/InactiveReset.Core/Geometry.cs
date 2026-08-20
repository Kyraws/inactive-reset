namespace InactiveReset.Core;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public bool IsFinite =>
        float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public override string ToString() => $"{X:F6}, {Y:F6}, {Z:F6}";
}

/// <summary>
/// A 0x20-stride spot-table entry: position float[3] at +0x00, euler
/// orientation float[3] (radians) at +0x0C. Bytes +0x18..+0x1F are padding the
/// engine does not read and this project must never touch.
/// </summary>
public readonly record struct SpotEntry(Vec3 Position, Vec3 Orientation)
{
    public override string ToString() => $"pos [{Position}]  ori [{Orientation}]";
}

/// <summary>
/// A pose as recorded from LMU's shared memory: world position plus the three
/// ROWS of the orientation matrix.
/// </summary>
public readonly record struct RecordedPose(Vec3 Position, Vec3 Row0, Vec3 Row1, Vec3 Row2);

/// <summary>The target derived from a recorded pose.</summary>
public sealed record TargetPose
{
    public bool Valid { get; init; }
    public Vec3 RestPosition { get; init; }
    public float Yaw { get; init; }

    /// <summary>
    /// Horizontal length of the world forward vector. Near zero means the car's
    /// forward axis was near-vertical when captured and the yaw is meaningless.
    /// </summary>
    public float HeadingHorizontalMagnitude { get; init; }

    public IReadOnlyList<string> Failures { get; init; } = [];
}

public static class Geometry
{
    /// <summary>sign(source) -> -1, 0 or +1. Mirrors GetPitDestination exactly.</summary>
    public static float LateralSign(float lateralSignSource) =>
        lateralSignSource == 0f ? 0f : lateralSignSource / MathF.Abs(lateralSignSource);

    /// <summary>
    /// Column 0 of ConvertEulerToMatrix3x3 — the LATERAL (local X) axis.
    /// Equivalent to yaw + pi/2: (cos yaw, 0, -sin yaw).
    /// </summary>
    public static Vec3 LateralAxis(float yaw) =>
        new(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));

    /// <summary>The forward/heading axis used by the placement-to-rest transform.</summary>
    public static Vec3 HeadingAxis(float yaw) =>
        new(MathF.Sin(yaw), 0f, MathF.Cos(yaw));

    /// <summary>
    /// Extract the placement yaw from a shared-memory orientation.
    ///
    /// <c>TelemInfoV01::mOri[3]</c> holds the ROWS of the orientation matrix,
    /// and the SDK states world X/Y/Z = dot(row 0/1/2, localVector). The
    /// vehicle's local forward is +Z, so its world forward is the THIRD COLUMN:
    /// <c>(row0.z, row1.z, row2.z)</c>.
    ///
    /// Verified against a memory capture: the live matrix yielded
    /// <c>atan2(row0.z, row2.z) = -0.032740</c> against a known applied yaw of
    /// <c>-0.032865</c> — agreement to 0.007 degrees. **The third ROW gives the
    /// wrong sign**, which is an easy and silent mistake to make.
    ///
    /// This is all a checkpoint tool needs; the engine's full euler convention
    /// does NOT have to be inverted, because pitch and roll are carried through
    /// from the existing table entry and the engine settles the car onto the
    /// surface anyway.
    /// </summary>
    public static float ExtractYawFromOrientationRows(Vec3 row0, Vec3 row2) =>
        MathF.Atan2(row0.Z, row2.Z);

    /// <summary>
    /// Convert a recorded pose into the desired resting pose and yaw.
    ///
    /// This is the ONLY place a recorded orientation becomes a placement yaw. It
    /// fails closed on non-finite values, on rows that are not unit length (so
    /// the record is not a rotation matrix), and on a near-vertical forward axis
    /// where atan2 is ill-conditioned — a checkpoint captured mid-crash must not
    /// silently produce an arbitrary heading.
    /// </summary>
    public static TargetPose BuildTargetFromRecordedPose(RecordedPose pose)
    {
        var failures = new List<string>();

        if (!pose.Position.IsFinite || !pose.Row0.IsFinite
            || !pose.Row1.IsFinite || !pose.Row2.IsFinite)
        {
            return new TargetPose
            {
                Failures = ["recorded pose contains a non-finite value"],
            };
        }

        // A rotation matrix has unit rows. Anything else is not an orientation
        // and the extracted yaw would be meaningless.
        var lengths = new[] { pose.Row0.Length, pose.Row1.Length, pose.Row2.Length };
        for (var i = 0; i < 3; i++)
        {
            if (!(lengths[i] > 0.99f) || !(lengths[i] < 1.01f))
            {
                failures.Add($"recorded orientation row {i} is not unit length - not a rotation matrix");
            }
        }

        // World forward = third COLUMN = (row0.z, row1.z, row2.z). Only its
        // horizontal part carries a heading.
        var headingHorizontal =
            MathF.Sqrt(pose.Row0.Z * pose.Row0.Z + pose.Row2.Z * pose.Row2.Z);
        if (!(headingHorizontal > 0.05f))
        {
            failures.Add("recorded forward axis is near-vertical - the placement yaw is ill-conditioned; refusing");
        }

        // A world coordinate far outside any circuit indicates a corrupt record.
        if (MathF.Abs(pose.Position.X) > 100000f
            || MathF.Abs(pose.Position.Y) > 100000f
            || MathF.Abs(pose.Position.Z) > 100000f)
        {
            failures.Add("recorded position is implausibly far from the origin");
        }

        return new TargetPose
        {
            RestPosition = pose.Position,
            Yaw = ExtractYawFromOrientationRows(pose.Row0, pose.Row2),
            HeadingHorizontalMagnitude = headingHorizontal,
            Valid = failures.Count == 0,
            Failures = failures,
        };
    }
}
