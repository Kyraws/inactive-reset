namespace InactiveReset.Core;

/// <summary>One rule as it currently stands in the running game.</summary>
public sealed record RuleState(
    string Name,
    int Value,
    Consumption Consumption,
    bool WritableLive,
    string? Domain,
    string? Note,
    ulong Rva = 0,
    ulong Address = 0)
{
    /// <summary>
    /// Why a write here would or would not do anything. This is the sentence
    /// that saves people the confusion of changing a setting and watching
    /// nothing happen.
    /// </summary>
    public string Effect => Consumption switch
    {
        Consumption.ReadLive =>
            "read on every evaluation - a write applies immediately",
        Consumption.ExpandedOnce =>
            "read once at session start - writing this does NOTHING mid-session",
        _ => "consumption unknown - display only",
    };
}

/// <summary>
/// Reads and writes the penalty rules of a running session.
///
/// The whole subtlety lives in <see cref="Consumption"/>. Two rules that look
/// identical in the settings menu are reached completely differently:
///
///   Pit-speeding  -> Flag Rules is dereferenced on every penalty evaluation,
///                    so writing the setting itself works.
///   Track limits  -> the setting is read once at session start and expanded
///                    into derived flags. Writing the setting mid-session does
///                    nothing at all; the derived flags must be written.
///
/// Getting that backwards is why changing rules through the game's own menu or
/// its REST API appears to have no effect on a running session.
/// </summary>
public sealed class RulesController(GameSession session)
{
    private readonly GameSession _session = session;

    private RulesSpec Spec => _session.Offsets.Rules;

    // ---- read --------------------------------------------------------------

    public IReadOnlyList<RuleState> ReadAll()
    {
        var states = new List<RuleState>
        {
            Read("Pit-speeding gate (Flag Rules)", Spec.FlagRules),
            Read("Track limits (stored setting)", Spec.TrackLimits.Config),
        };

        for (var i = 0; i < Spec.TrackLimits.DerivedFlags.Count; i++)
        {
            var field = Spec.TrackLimits.DerivedFlags[i];
            var label = field.Note?.Contains("Invalidated", StringComparison.OrdinalIgnoreCase) == true
                ? $"Track limits flag {i} (lap invalidation)"
                : $"Track limits flag {i}";
            states.Add(Read(label, field));
        }

        states.Add(Read("Steward penalties", Spec.StewardPenalties));
        return states;
    }

    private RuleState Read(string name, RuleField field)
    {
        var address = _session.ModuleBase + field.Rva.Value;
        int value;
        try
        {
            value = field.Type == FieldType.Byte
                ? _session.Memory.ReadByte(address)
                : _session.Memory.ReadInt32(address);
        }
        catch (MemoryAccessException)
        {
            value = -1;
        }

        return new RuleState(name, value, field.Consumption,
                             field.IsWritableLive, field.Domain, field.Note,
                             field.Rva.Value, address);
    }

    // ---- write -------------------------------------------------------------

    /// <summary>
    /// Disable the stop/go for pit-lane speeding.
    ///
    /// This writes the Flag Rules setting itself, which the penalty code reads
    /// on every evaluation, so it takes effect on the next pit entry.
    ///
    /// The game's REST setter clamps this value to a floor of 1 and therefore
    /// cannot disable the penalty at all. The floor lives in the setter, not in
    /// the consumer -- so a direct write reaches 0 and is honoured.
    /// </summary>
    public RuleWriteResult SetPitSpeedingPenalty(bool enabled)
    {
        var field = Spec.FlagRules;
        var value = enabled
            ? field.DefaultValue ?? 2
            : field.DisableValue ?? 0;
        return Write("Pit-speeding gate", field, value);
    }

    /// <summary>
    /// Disable track-limits penalties and lap invalidation.
    ///
    /// This writes the DERIVED flags, not the setting. Session init writes a
    /// word 0x0101 covering the first two flags, and the engine's own "disabled"
    /// branch clears only the first -- which is precisely why the second, the
    /// one gating "Off Track - Lap Invalidated", has to be written by hand.
    /// </summary>
    public IReadOnlyList<RuleWriteResult> SetTrackLimits(bool enabled)
    {
        var value = enabled ? Spec.TrackLimits.EnableValue : Spec.TrackLimits.DisableValue;
        var results = new List<RuleWriteResult>();
        for (var i = 0; i < Spec.TrackLimits.DerivedFlags.Count; i++)
        {
            results.Add(Write($"Track limits flag {i}", Spec.TrackLimits.DerivedFlags[i], value));
        }
        return results;
    }

    private RuleWriteResult Write(string name, RuleField field, int value)
    {
        if (!_session.Memory.CanWrite)
        {
            throw new MemoryAccessException(
                "this session was attached read-only; reattach for writing");
        }
        if (field.Consumption == Consumption.Unknown)
        {
            throw new InvalidOperationException(
                $"{name}: how the engine consumes this is unknown, so a write has " +
                "unpredictable effect. Refusing.");
        }

        var address = _session.ModuleBase + field.Rva.Require(name);
        var before = field.Type == FieldType.Byte
            ? _session.Memory.ReadByte(address)
            : _session.Memory.ReadInt32(address);

        if (before == value)
        {
            return new RuleWriteResult(name, address, before, value, Changed: false);
        }

        ReadOnlySpan<byte> payload = field.Type == FieldType.Byte
            ? stackalloc byte[] { (byte)value }
            : BitConverter.GetBytes(value);

        _session.Memory.WriteVerified(address, payload);

        var after = field.Type == FieldType.Byte
            ? _session.Memory.ReadByte(address)
            : _session.Memory.ReadInt32(address);

        return new RuleWriteResult(name, address, before, after, Changed: true);
    }
}

public sealed record RuleWriteResult(
    string Name, ulong Address, int Before, int After, bool Changed);
