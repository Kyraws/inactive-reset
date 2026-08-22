namespace InactiveReset.Core;

/// <summary>
/// Constants describing what the engine does with a spot-table entry.
///
/// These are engine-derived, not tuned. <see cref="YawOffsetMode2"/> and the
/// search factors are READ LIVE from the running engine (see
/// <see cref="EngineTunables"/>) because the engine keeps them in mutable data
/// and retunes them between builds; <see cref="RestForwardDistance"/> and
/// <see cref="RestVerticalOffset"/> come from a calibration measured for one
/// track and one vehicle.
/// </summary>
public sealed record PlacementModel
{
    /// <summary>
    /// GetPitDestination mode 2 applies <c>oriOut[1] -= sign * this</c>, in
    /// radians. Read live from the running engine -- see <see cref="EngineTunables"/>.
    ///
    /// A separate defect used to apply on top of this: placements landed ~0.57 m
    /// off on 266D1AF6 and ~0.71 m off on 0F6DCAC1, with these tunables
    /// byte-identical and the same calibration. It was long recorded as an
    /// 11.633 deg heading error and treated as uncorrectable by calibration.
    ///
    /// It was neither. The engine's displacement from a written destination is a
    /// constant vehicle-frame vector, and the model simply had no term for its
    /// off-axis component -- see <see cref="RestLateralOffset"/>. Correcting D
    /// alone was indeed wrong; correcting D and L together is not. The miss is
    /// per build, so both must be re-measured when the game patches.
    /// See docs/HEADING_BUG.md.
    /// </summary>
    public required float YawOffsetMode2 { get; init; }

    /// <summary>TestPitSpotClearance search: d = start*width, step*width, max*width.</summary>
    public required float SearchStartFactor { get; init; }
    public required float SearchStepFactor { get; init; }
    public required float SearchMaxFactor { get; init; }

    /// <summary>
    /// rest = destination + D*heading + sign*L*lateral + (0, H, 0).
    /// Per track AND per vehicle AND per build. Never reuse across combinations.
    /// </summary>
    public required float RestForwardDistance { get; init; }
    public required float RestVerticalOffset { get; init; }

    /// <summary>
    /// L: the engine's rest displacement along the LATERAL axis of the
    /// destination yaw, in metres.
    ///
    /// The engine's displacement from a written destination is a constant vector
    /// in the vehicle frame, and until this term existed the model could only
    /// represent its forward and vertical components. The off-axis remainder --
    /// 0.503 m at Barcelona on 0F6DCAC1 -- had nowhere to go, which is why no
    /// amount of calibrating D and H ever removed it.
    ///
    /// This is NOT a rotation being smeared into a translation. Measured on
    /// 2026-08-22 by placing with D deliberately doubled: the error moved by
    /// exactly -delta-D along the heading (0.2 mm agreement) and the lateral
    /// component did not move at all, so the engine's displacement does not
    /// depend on where the destination was written. Whether the engine arrives
    /// there by rotating or by translating is UNOBSERVABLE from outside -- a
    /// heading error with the engine's own fixed range produces a constant
    /// vehicle-frame vector, identical in every measurable way to a constant
    /// vehicle-frame translation. The engine's range is not a thing this tool
    /// can vary, so no experiment separates them.
    ///
    /// SIGNED, like the search offset: the model applies
    /// <c>sign * RestLateralOffset</c>, so this value is a MAGNITUDE and the
    /// direction comes from the engine's own lateral sign.
    ///
    /// Confirmed 2026-08-22 across two cars. Every placement in a BMW at
    /// Barcelona (lateral sign +1) landed within 3 mm; every placement in a
    /// Genesis (lateral sign -1) missed by 1.07-1.08 m of pure lateral, and the
    /// implied L was -(the L used) to within 1.5 cm in all four. The miss
    /// appeared at BOTH tracks in the Genesis, so it is not a track property --
    /// it was the sign, applied to the search offset but not to this term.
    /// </summary>
    public required float RestLateralOffset { get; init; }

    /// <summary>
    /// Build the model from tunables READ FROM THE RUNNING ENGINE plus a measured
    /// calibration. There is no overload taking the profile's snapshot directly:
    /// the snapshot is a fallback that <see cref="EngineTunables.Read"/> applies
    /// after range-checking it, and letting callers bypass that would reintroduce
    /// exactly the silent staleness this design removes.
    /// </summary>
    public static PlacementModel Create(
        EngineTunables engine, float forwardDistance, float verticalOffset, float lateralOffset) => new()
    {
        YawOffsetMode2 = engine.YawOffsetRadians,
        SearchStartFactor = engine.SearchStartFactor,
        SearchStepFactor = engine.SearchStepFactor,
        SearchMaxFactor = engine.SearchMaxFactor,
        RestForwardDistance = forwardDistance,
        RestVerticalOffset = verticalOffset,
        RestLateralOffset = lateralOffset,
    };
}

/// <summary>Globals read from the module image.</summary>
public readonly record struct SpotTableGlobals(
    ulong PitPosTable, ulong GarPosTable, int Mult, int Count);

/// <summary>Fields read from a slot container.</summary>
///
/// <param name="RestOffsetPrimary">
/// With <paramref name="RestOffsetSecondary"/>, the engine's own rest placement
/// offset. ApplyVehicleTransform sets the vehicle position to
/// <c>destination - M*(0, b, a)</c> where local +Z is forward and
/// <c>a = RestOffsetPrimary + RestOffsetSecondary</c>, so the forward placement
/// distance is <c>-a</c>. Per vehicle, constant, and bit-stable while driving.
/// </param>
public readonly record struct ContainerState(
    int SlotIndex, int PitIndex, int GarageIndex, int ControlOwner,
    float VehicleLength, float VehicleWidth, float LateralSignSource,
    float RestOffsetPrimary = 0f, float RestOffsetSecondary = 0f)
{
    /// <summary>
    /// Where ApplyVehicleTransform puts the car, forward of the destination,
    /// before it settles. Zero when the profile does not carry the fields.
    /// </summary>
    public float EnginePlacementDistance => -(RestOffsetPrimary + RestOffsetSecondary);

    public bool HasEnginePlacementDistance =>
        RestOffsetPrimary != 0f || RestOffsetSecondary != 0f;
}

public sealed record GateResult(bool Passed, IReadOnlyList<string> Failures);

/// <summary>
/// The forward and inverse models of the engine's garage-to-Drive placement.
///
/// Pure. No process access, no Windows, no I/O — so it is unit-testable without
/// a running game, which is the only reason any of it was ever pinned down.
/// </summary>
public static class PlacementMath
{
    /// <summary>The engine's own slot &lt; 0x68 test.</summary>
    private const int SlotIndexLimit = 0x68;

    /// <summary>Distance of search candidate n (0-based). Negative if out of range.</summary>
    public static float PredictSearchCandidate(int n, ContainerState container, PlacementModel model)
    {
        if (n < 0)
        {
            return -1f;
        }
        var width = container.VehicleWidth;
        var distance = (model.SearchStartFactor + n * model.SearchStepFactor) * width;
        return distance > model.SearchMaxFactor * width ? -1f : distance;
    }

    /// <summary>
    /// GetPitDestination mode 2, assuming the FIRST search candidate is accepted.
    /// That assumption held in every observation (empty pit lane) but is not
    /// guaranteed at an arbitrary point.
    /// </summary>
    public static SpotEntry PredictDriveDestination(
        SpotEntry pitPos, ContainerState container, PlacementModel model)
    {
        var sign = Geometry.LateralSign(container.LateralSignSource);

        // ORDER MATTERS. The engine builds the lateral axis from the UNADJUSTED
        // stored yaw, and only then adjusts the yaw. Computing the axis from the
        // adjusted yaw gives a subtly wrong lateral offset.
        var lateral = Geometry.LateralAxis(pitPos.Orientation.Y);
        var distance = PredictSearchCandidate(0, container, model);

        var orientation = pitPos.Orientation with
        {
            Y = pitPos.Orientation.Y - sign * model.YawOffsetMode2,
        };

        var position = pitPos.Position;
        if (distance >= 0f)
        {
            position = new Vec3(
                position.X + lateral.X * sign * distance,
                position.Y + lateral.Y * sign * distance,
                position.Z + lateral.Z * sign * distance);
        }

        return new SpotEntry(position, orientation);
    }

    /// <summary>
    /// ApplyVehicleTransform + settling:
    /// rest = dest + D*heading + sign*L*lateral + (0, H, 0).
    /// </summary>
    public static Vec3 PredictRestPosition(
        SpotEntry destination, ContainerState container, PlacementModel model)
    {
        var heading = Geometry.HeadingAxis(destination.Orientation.Y);
        var lateral = Geometry.LateralAxis(destination.Orientation.Y);
        var offset = Geometry.LateralSign(container.LateralSignSource) * model.RestLateralOffset;
        return new Vec3(
            destination.Position.X
                + heading.X * model.RestForwardDistance
                + lateral.X * offset,
            destination.Position.Y + model.RestVerticalOffset,
            destination.Position.Z
                + heading.Z * model.RestForwardDistance
                + lateral.Z * offset);
    }

    /// <summary>
    /// The PitPos entry required for the car to come to rest at
    /// <paramref name="desiredRest"/> facing <paramref name="desiredYaw"/>.
    ///
    /// <paramref name="currentEntry"/> is the LIVE entry as read from the table,
    /// and it is required: its PITCH and ROLL are carried through unchanged. The
    /// engine applies all three euler angles, so pitch and roll are not free
    /// values.
    ///
    /// They are carried through rather than zeroed or recovered because zeroing
    /// places the car perfectly level, which is wrong on a slope, and recovering
    /// them would require inverting the engine's euler convention, which has
    /// never been verified — only the yaw column was pinned down.
    ///
    /// RESIDUAL RISK: the carried-through pitch/roll belong to the PIT BOX, not
    /// to the terrain under the target. They are small (~0.5–0.9 deg at
    /// Barcelona) but they are not the target's slope. Verify the achieved pose,
    /// and prefer near-flat ground.
    /// </summary>
    public static SpotEntry InvertToPitPosEntry(
        Vec3 desiredRest, float desiredYaw, ContainerState container,
        PlacementModel model, SpotEntry currentEntry)
    {
        var sign = Geometry.LateralSign(container.LateralSignSource);

        // dest.yaw = pitPos.yaw - sign*C   =>   pitPos.yaw = dest.yaw + sign*C
        var entryYaw = desiredYaw + sign * model.YawOffsetMode2;

        // rest = dest + D*heading(dest.yaw) + sign*L*lateral(dest.yaw) + (0, H, 0)
        var heading = Geometry.HeadingAxis(desiredYaw);
        var restLateral = Geometry.LateralAxis(desiredYaw);
        var restOffset = sign * model.RestLateralOffset;
        var destination = new Vec3(
            desiredRest.X
                - heading.X * model.RestForwardDistance
                - restLateral.X * restOffset,
            desiredRest.Y - model.RestVerticalOffset,
            desiredRest.Z
                - heading.Z * model.RestForwardDistance
                - restLateral.Z * restOffset);

        // dest.pos = pitPos.pos + sign*d*lateral(pitPos.yaw)
        var lateral = Geometry.LateralAxis(entryYaw);
        var distance = PredictSearchCandidate(0, container, model);
        if (distance >= 0f)
        {
            destination = new Vec3(
                destination.X - lateral.X * sign * distance,
                destination.Y - lateral.Y * sign * distance,
                destination.Z - lateral.Z * sign * distance);
        }

        return new SpotEntry(
            destination,
            new Vec3(currentEntry.Orientation.X, entryYaw, currentEntry.Orientation.Z));
    }

    /// <summary>GarPos index = pitIndex + mult*garageIndex. Negative when out of bounds.</summary>
    public static int ComputeGarPosIndex(ContainerState container, SpotTableGlobals globals)
    {
        if (globals.Mult <= 0)
        {
            return -1;
        }
        long index = container.PitIndex + (long)globals.Mult * container.GarageIndex;
        long effectiveCount = globals.Count > 0 ? globals.Count : 1;
        long capacity = effectiveCount * globals.Mult;
        return index < 0 || index >= capacity ? -1 : (int)index;
    }

    /// <summary>Byte address of PitPos[pitIndex]. Caller must have passed the gates.</summary>
    public static ulong ComputePitPosEntryAddress(ulong tableBase, int pitIndex) =>
        tableBase + (ulong)pitIndex * 0x20u;

    /// <summary>
    /// Every structural precondition that can be checked from these values
    /// alone. Build, anticheat and session gates are enforced separately, before
    /// this is ever called.
    /// </summary>
    public static GateResult EvaluatePlacementPreconditions(
        ContainerState container, SpotTableGlobals globals, PlacementModel model)
    {
        var failures = new List<string>();

        if (globals.PitPosTable == 0)
        {
            failures.Add("PitPos table pointer is null");
        }
        if (globals.Mult <= 0)
        {
            failures.Add("MULT is not positive");
        }

        // The stacked-spot encoding (garageIndex >= 1,000,000) makes the engine
        // add a computed offset we cannot predict. Refuse.
        if (container.GarageIndex < 0 || container.GarageIndex >= globals.Count)
        {
            failures.Add("garageIndex outside [0, COUNT) - not the simple path "
                       + "(stacked-spot encoding or unassigned); refusing");
        }

        if (container.PitIndex < 0)
        {
            failures.Add("pitIndex is negative (spot not yet assigned)");
        }
        else if (globals.Mult > 0 && container.PitIndex >= globals.Mult)
        {
            failures.Add("pitIndex >= MULT - GetPitDestination would clamp, and the "
                       + "engine's own clamp reads past the allocation; refusing");
        }

        if (container.SlotIndex < 0 || container.SlotIndex >= SlotIndexLimit)
        {
            failures.Add("slotIndex outside [0, 0x68) - this is the pace-car / teleport "
                       + "path, not the pit path");
        }

        if (ComputeGarPosIndex(container, globals) < 0)
        {
            failures.Add("pitIndex + MULT*garageIndex outside the allocated table");
        }

        if (!(container.VehicleWidth > 0.1f) || !(container.VehicleWidth < 10f))
        {
            failures.Add("vehicleWidth implausible (expected 0.1..10 m)");
        }
        if (!(container.VehicleLength > 0.1f) || !(container.VehicleLength < 30f))
        {
            failures.Add("vehicleLength implausible (expected 0.1..30 m)");
        }
        if (Geometry.LateralSign(container.LateralSignSource) == 0f)
        {
            failures.Add("lateralSignSource is zero - lateral search direction undefined");
        }

        // The placement is triggered by pressing Drive from the garage, so the
        // car must still be under Ai when the override goes in.
        if (container.ControlOwner != 1)
        {
            failures.Add("controlOwner is not 1 (Ai) - the car must be in the garage "
                       + "before Drive");
        }

        if (model.SearchStartFactor <= 0f)
        {
            failures.Add("engine model searchStartFactor invalid");
        }

        return new GateResult(failures.Count == 0, failures);
    }

    /// <summary>
    /// The exact 24 bytes to write. The 8 bytes of padding at +0x18 are excluded
    /// by construction, not by remembering to skip them.
    /// </summary>
    public static byte[] EncodeSpotEntry(SpotEntry entry)
    {
        var bytes = new byte[24];
        var values = new[]
        {
            entry.Position.X, entry.Position.Y, entry.Position.Z,
            entry.Orientation.X, entry.Orientation.Y, entry.Orientation.Z,
        };
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), values[i]);
        }
        return bytes;
    }

    public static SpotEntry DecodeSpotEntry(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24)
        {
            throw new ArgumentException("a spot entry is 24 bytes", nameof(bytes));
        }
        Span<float> v = stackalloc float[6];
        for (var i = 0; i < 6; i++)
        {
            v[i] = BitConverter.ToSingle(bytes.Slice(i * 4, 4));
        }
        return new SpotEntry(new Vec3(v[0], v[1], v[2]), new Vec3(v[3], v[4], v[5]));
    }
}
