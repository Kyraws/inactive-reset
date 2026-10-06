namespace InactiveReset.Core;

/// <summary>Everything read from the running game for one placement decision.</summary>
public sealed record LiveState
{
    public required SpotTableGlobals Globals { get; init; }
    public required ContainerState Container { get; init; }

    /// <summary>Address of this slot's PitPos entry — the write target.</summary>
    public required ulong EntryAddress { get; init; }

    /// <summary>The full 32-byte entry. Only the first 24 are ever written.</summary>
    public required byte[] EntryBytes { get; init; }

    public required SpotEntry CurrentEntry { get; init; }
    public required GateResult Preconditions { get; init; }
    public bool IndexedDestination { get; init; }

    /// <summary>
    /// The 8 bytes at +0x18 this project never writes. Recorded so a report can
    /// show they were untouched, rather than merely asserting it.
    /// </summary>
    public ReadOnlySpan<byte> PaddingTail => EntryBytes.AsSpan(24, 8);
}

/// <summary>
/// Reads the live spot table and slot container.
///
/// Every address comes from the loaded offset profile. If you find yourself
/// wanting a literal here, it belongs in the JSON.
/// </summary>
public sealed class LiveStateReader(GameSession session)
{
    private readonly GameSession _session = session;

    public LiveState Read(PlacementModel model)
    {
        var offsets = _session.Offsets;
        var memory = _session.Memory;
        var spot = offsets.SpotTable;

        // The table globals are POINTERS in the image; read through them.
        var globals = new SpotTableGlobals(
            PitPosTable: memory.ReadUInt64(_session.Resolve(spot.IndexedDestination ?? spot.PitPosTable, "placement table")),
            GarPosTable: memory.ReadUInt64(_session.Resolve(spot.GarPosTable, "GarPos table")),
            Mult: memory.ReadInt32(_session.Resolve(spot.Mult, "spot table MULT")),
            Count: memory.ReadInt32(_session.Resolve(spot.Count, "spot table COUNT")));

        var container = ReadContainer(slotIndex: 0);

        // The slot the container reports is authoritative; slot 0 is only where
        // we start looking.
        if (container.SlotIndex != 0)
        {
            container = ReadContainer(container.SlotIndex);
        }

        var entryAddress = spot.IndexedDestination is null
            ? PlacementMath.ComputePitPosEntryAddress(globals.PitPosTable, container.PitIndex)
            : IndexedPlacementMath.EntryAddress(globals.PitPosTable, container.PitIndex);

        var entryBytes = globals.PitPosTable != 0 && container.PitIndex >= 0
            ? memory.ReadBytes(entryAddress, spot.EntryBytes)
            : new byte[spot.EntryBytes];

        return new LiveState
        {
            Globals = globals,
            Container = container,
            EntryAddress = entryAddress,
            EntryBytes = entryBytes,
            CurrentEntry = PlacementMath.DecodeSpotEntry(entryBytes),
            Preconditions = PlacementMath.EvaluatePlacementPreconditions(container, globals, model),
            IndexedDestination = spot.IndexedDestination is not null,
        };
    }

    /// <summary>
    /// Which slot the player's car occupies.
    ///
    /// Slot 0 is only where we start looking; the slot the container reports
    /// about itself is authoritative. Split out of <see cref="Read"/> so callers
    /// that want a slot but not a placement plan do not have to invent a
    /// <see cref="PlacementModel"/> to get one.
    /// </summary>
    public int ResolveSlotIndex()
    {
        var reported = _session.Memory.ReadInt32(FieldAddress(0, "slotIndex"));
        return reported == 0
            ? 0
            : _session.Memory.ReadInt32(FieldAddress(reported, "slotIndex"));
    }

    /// <summary>Re-read only the fields that change while waiting for Drive.</summary>
    public int ReadControlOwner(int slotIndex) =>
        _session.Memory.ReadInt32(FieldAddress(slotIndex, "controlOwner"));

    public int ReadPitState(int slotIndex) =>
        _session.Memory.ReadInt32(FieldAddress(slotIndex, "pitState"));

    /// <summary>
    /// Sample the vehicle the instant control flips to the player.
    ///
    /// Must be called BEFORE any settle delay — afterwards the driver has
    /// control and can legally shift or move, which would make gear and motion
    /// state meaningless as assertions about what the engine produced.
    /// </summary>
    public ArrivalState ReadArrival(int slotIndex)
    {
        try
        {
            var motion = new float[9];
            var motionBase = FieldAddress(slotIndex, "vehMotionState");
            for (var i = 0; i < motion.Length; i++)
            {
                motion[i] = _session.Memory.ReadSingle(motionBase + (ulong)(i * 4));
            }

            return new ArrivalState
            {
                ControlOwner = _session.Memory.ReadInt32(FieldAddress(slotIndex, "controlOwner")),
                Gear = _session.Memory.ReadInt32(FieldAddress(slotIndex, "vehGear")),
                PitState = _session.Memory.ReadInt32(FieldAddress(slotIndex, "pitState")),
                MotionState = motion,
                Valid = true,
            };
        }
        catch (MemoryAccessException)
        {
            // A failed read is not fatal. The placement itself is unaffected; it
            // is simply unverified, and saying so is better than guessing.
            return new ArrivalState { Valid = false };
        }
    }

    private ContainerState ReadContainer(int slotIndex)
    {
        var memory = _session.Memory;
        return new ContainerState(
            SlotIndex: memory.ReadInt32(FieldAddress(slotIndex, "slotIndex")),
            PitIndex: memory.ReadInt32(FieldAddress(slotIndex, "pitIndex")),
            GarageIndex: memory.ReadInt32(FieldAddress(slotIndex, "garageIndex")),
            ControlOwner: memory.ReadInt32(FieldAddress(slotIndex, "controlOwner")),
            VehicleLength: memory.ReadSingle(FieldAddress(slotIndex, "vehicleLength")),
            VehicleWidth: memory.ReadSingle(FieldAddress(slotIndex, "vehicleWidth")),
            LateralSignSource: memory.ReadSingle(FieldAddress(slotIndex, "lateralSign")),
            RestOffsetPrimary: ReadOptional(slotIndex, "restOffsetPrimary"),
            RestOffsetSecondary: ReadOptional(slotIndex, "restOffsetSecondary"));
    }

    /// <summary>
    /// Read a field that older profiles do not carry, as 0 when it is absent.
    ///
    /// Fields added after a profile was written must not make that profile
    /// unloadable. A zero here means "this build's profile predates the rest
    /// offsets", which callers detect through
    /// <see cref="ContainerState.HasEnginePlacementDistance"/> and fall back on.
    /// </summary>
    private float ReadOptional(int slotIndex, string field)
    {
        try
        {
            return _session.Memory.ReadSingle(FieldAddress(slotIndex, field));
        }
        catch (Exception ex) when (ex is OffsetProfileException or MemoryAccessException)
        {
            return 0f;
        }
    }

    private ulong FieldAddress(int slotIndex, string field) =>
        _session.Offsets.Containers.FieldAddress(_session.ModuleBase, slotIndex, field);
}
