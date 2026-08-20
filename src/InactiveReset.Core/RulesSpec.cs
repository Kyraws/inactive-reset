using System.Text.Json;

namespace InactiveReset.Core;

/// <summary>
/// How the engine consumes a rules setting. This is the single most important
/// distinction in the project, and getting it wrong is why changing rules
/// through the game menu or the REST API appears to do nothing mid-session.
/// </summary>
public enum Consumption
{
    /// <summary>
    /// The consumer dereferences the setting on every evaluation, so writing it
    /// takes effect immediately. Example: Flag Rules, read at 0x00D01537 each
    /// time a pit-speeding penalty is considered.
    /// </summary>
    ReadLive,

    /// <summary>
    /// Read once at session start and expanded into derived flags. Writing the
    /// setting mid-session does NOTHING -- the derived flags must be written
    /// instead. Example: Track Limits Rules, expanded at 0x00D30E7A.
    /// </summary>
    ExpandedOnce,

    /// <summary>Not determined. Display it; never write it.</summary>
    Unknown,
}

public sealed class RulesSpec
{
    /// <summary>
    /// Gates the stop/go for pit-lane speeding. Read live, so a direct write
    /// applies at once.
    ///
    /// Note the REST setter clamps this to a floor of 1 and therefore CANNOT
    /// disable the penalty; the floor lives in the setter, not the consumer.
    /// A direct write reaches 0 and the consumer honours it.
    /// </summary>
    public required RuleField FlagRules { get; init; }

    /// <summary>
    /// Track limits. The config byte is display-only mid-session; the derived
    /// flags are what the evaluator reads.
    /// </summary>
    public required TrackLimitsSpec TrackLimits { get; init; }

    /// <summary>
    /// Steward penalties. No direct readers were found, which means it is
    /// expanded through a pointer and the derived state was never located.
    /// Displayed, never written.
    /// </summary>
    public required RuleField StewardPenalties { get; init; }

    internal static RulesSpec FromJson(JsonElement node) => new()
    {
        FlagRules = RuleField.FromJson(OffsetProfile.Require(node, "flagRules")),
        TrackLimits = TrackLimitsSpec.FromJson(OffsetProfile.Require(node, "trackLimits")),
        StewardPenalties = RuleField.FromJson(OffsetProfile.Require(node, "stewardPenalties")),
    };
}

public sealed class TrackLimitsSpec
{
    /// <summary>
    /// The stored setting. <see cref="Consumption.ExpandedOnce"/> -- writing it
    /// mid-session has no effect. Read it to show the user what the session
    /// started with.
    /// </summary>
    public required RuleField Config { get; init; }

    /// <summary>
    /// What the evaluator actually reads. Session init writes a WORD 0x0101 to
    /// the first two, setting both; the engine's own "disabled" branch clears
    /// only the first, which is exactly why the second must be written by hand.
    /// The second flag gates "Off Track - Lap Invalidated".
    /// </summary>
    public required IReadOnlyList<RuleField> DerivedFlags { get; init; }

    public required int DisableValue { get; init; }
    public required int EnableValue { get; init; }

    internal static TrackLimitsSpec FromJson(JsonElement node) => new()
    {
        Config = RuleField.FromJson(OffsetProfile.Require(node, "config")),
        DerivedFlags = OffsetProfile.Require(node, "derivedFlags")
            .EnumerateArray().Select(RuleField.FromJson).ToArray(),
        DisableValue = node.GetProperty("disableValue").GetInt32(),
        EnableValue = node.GetProperty("enableValue").GetInt32(),
    };
}

/// <summary>Width of a rules field in memory.</summary>
public enum FieldType { Byte, Int32 }

public sealed class RuleField
{
    public required Rva Rva { get; init; }
    public required FieldType Type { get; init; }
    public required Consumption Consumption { get; init; }

    /// <summary>Value that turns the behaviour off, when we know one.</summary>
    public int? DisableValue { get; init; }

    /// <summary>The value the game ships with, for restoring.</summary>
    public int? DefaultValue { get; init; }

    /// <summary>Address of the instruction that consumes this, for auditing.</summary>
    public string? ReadBy { get; init; }

    /// <summary>Human-readable domain, e.g. "0 none, 1 penalties only, ...".</summary>
    public string? Domain { get; init; }

    public string? Note { get; init; }

    public int SizeInBytes => Type == FieldType.Byte ? 1 : 4;

    /// <summary>
    /// True when writing this field can affect the running session. False for
    /// settings the engine latched at session start.
    /// </summary>
    public bool IsWritableLive => Consumption == Consumption.ReadLive;

    internal static RuleField FromJson(JsonElement node) => new()
    {
        Rva = new Rva(
            OffsetProfile.ParseHex(node.GetProperty("rva").GetString()!),
            OffsetProfile.ParseConfidence(node),
            node.TryGetProperty("note", out var n1) ? n1.GetString() : null),
        Type = node.GetProperty("type").GetString() switch
        {
            "byte" => FieldType.Byte,
            "int32" => FieldType.Int32,
            var other => throw new OffsetProfileException($"unknown field type '{other}'"),
        },
        Consumption = node.TryGetProperty("consumption", out var c)
            ? c.GetString() switch
            {
                "readLive" => Consumption.ReadLive,
                "expandedOnce" => Consumption.ExpandedOnce,
                _ => Consumption.Unknown,
            }
            : Consumption.Unknown,
        DisableValue = node.TryGetProperty("disableValue", out var d) ? d.GetInt32() : null,
        DefaultValue = node.TryGetProperty("defaultValue", out var v) ? v.GetInt32() : null,
        ReadBy = node.TryGetProperty("readBy", out var r) ? r.GetString() : null,
        Domain = node.TryGetProperty("domain", out var dom) ? dom.GetString() : null,
        Note = node.TryGetProperty("note", out var n2) ? n2.GetString() : null,
    };
}
