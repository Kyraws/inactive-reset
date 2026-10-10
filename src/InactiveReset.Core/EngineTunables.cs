namespace InactiveReset.Core;

/// <summary>Where a set of engine tunables came from.</summary>
public enum TunableSource
{
    /// <summary>Read from the running process and inside range. The normal case.</summary>
    Live,

    /// <summary>The live read failed its range check; the profile snapshot was used.</summary>
    Fallback,
}

/// <summary>
/// The engine's placement tunables, read from the running game.
///
/// WHY THIS EXISTS. Until the 1AC2F605 build these were <c>.rdata</c> literals
/// and the profile could reasonably hold copies of them. In this build
/// GetPitDestination reads them from a MUTABLE <c>.data</c> block, so Studio 397
/// can retune placement without moving any address at all. They did exactly
/// that -- the yaw offset went 35 deg to 45 deg and the search start 0.2 to 0.55
/// -- and every build gate stayed green while placement went a metre wrong,
/// because `reanchor` re-derives *addresses* and these were never addresses.
///
/// Addresses come from the discovered profile. Values are read per placement
/// and range-checked, with a checked profile snapshot as fallback. Bounds reject
/// implausible values; they do not prove an address or placement model correct.
/// Automatic discovery identifies the tuning block through its search and yaw
/// consumers, so changing the values alone does not prevent discovery.
/// </summary>
public sealed record EngineTunables
{
    /// <summary>Radians, already converted from the degrees the engine stores.</summary>
    public required float YawOffsetRadians { get; init; }

    public required float SearchStartFactor { get; init; }
    public required float SearchStepFactor { get; init; }
    public required float SearchMaxFactor { get; init; }

    public required TunableSource Source { get; init; }

    /// <summary>
    /// Why the live read was rejected, when it was. Empty on the happy path.
    /// Surfaced by `status` so a silent fallback cannot masquerade as a live read.
    /// </summary>
    public IReadOnlyList<string> Rejections { get; init; } = [];

    private const float DegreesToRadians = 0.017453292519943295f;

    /// <summary>
    /// Read the tunables from the running process, falling back to the profile
    /// snapshot when the live values do not survive their range check.
    ///
    /// Throws only when BOTH fail, which means the profile is describing a build
    /// this is not -- a refusal, never a guess.
    /// </summary>
    public static EngineTunables Read(
        ProcessMemory memory, ulong moduleBase, EngineModelSpec spec)
    {
        var rejections = new List<string>();

        var degrees = TryRead(memory, moduleBase, spec.YawOffsetDegrees, "yaw offset", rejections);
        var start = TryRead(memory, moduleBase, spec.SearchStartFactorRva, "search start", rejections);
        var step = TryRead(memory, moduleBase, spec.SearchStepFactorRva, "search step", rejections);
        var max = TryRead(memory, moduleBase, spec.SearchMaxFactorRva, "search max", rejections);

        return Validate(degrees, start, step, max, spec, rejections);
    }

    /// <summary>
    /// The decision, with no process attached: given four values that may or may
    /// not have been readable, decide whether to trust them.
    ///
    /// Separated from <see cref="Read"/> purely so it can be tested. The range
    /// checks are the whole safety argument of this type, and an argument that
    /// only runs against a live game is an argument nobody ever checks.
    /// </summary>
    public static EngineTunables Validate(
        float? degrees, float? start, float? step, float? max,
        EngineModelSpec spec, List<string> rejections)
    {
        if (degrees is not null && start is not null && step is not null && max is not null)
        {
            Check(degrees.Value, spec.YawOffsetDegreesMin, spec.YawOffsetDegreesMax,
                  "yaw offset (deg)", rejections);
            Check(start.Value, spec.SearchFactorMin, spec.SearchFactorMax,
                  "search start factor", rejections);
            Check(step.Value, spec.SearchFactorMin, spec.SearchFactorMax,
                  "search step factor", rejections);
            Check(max.Value, spec.SearchFactorMin, spec.SearchFactorMax,
                  "search max factor", rejections);

            // Relational sanity. Four individually plausible numbers can still be
            // an incoherent set, and an incoherent set means a wrong address just
            // as surely as an out-of-range one does.
            if (rejections.Count == 0 && !(step.Value < max.Value))
            {
                rejections.Add(
                    $"search step {step.Value} is not less than max {max.Value} - " +
                    "the clearance search would not terminate as modelled");
            }
            if (rejections.Count == 0 && start.Value > max.Value)
            {
                rejections.Add(
                    $"search start {start.Value} exceeds max {max.Value} - " +
                    "the first candidate would already be out of bounds");
            }

            if (rejections.Count == 0)
            {
                return new EngineTunables
                {
                    YawOffsetRadians = degrees.Value * DegreesToRadians,
                    SearchStartFactor = start.Value,
                    SearchStepFactor = step.Value,
                    SearchMaxFactor = max.Value,
                    Source = TunableSource.Live,
                };
            }
        }

        return Fallback(spec, rejections);
    }

    /// <summary>
    /// The profile snapshot, itself range-checked. A fallback that is not checked
    /// is just a second way to be confidently wrong.
    /// </summary>
    private static EngineTunables Fallback(EngineModelSpec spec, List<string> rejections)
    {
        var fallbackDegrees = spec.FallbackYawOffsetRadians / DegreesToRadians;
        var bad = new List<string>();

        Check(fallbackDegrees, spec.YawOffsetDegreesMin, spec.YawOffsetDegreesMax,
              "fallback yaw offset (deg)", bad);
        Check(spec.FallbackSearchStartFactor, spec.SearchFactorMin, spec.SearchFactorMax,
              "fallback search start factor", bad);
        Check(spec.FallbackSearchStepFactor, spec.SearchFactorMin, spec.SearchFactorMax,
              "fallback search step factor", bad);
        Check(spec.FallbackSearchMaxFactor, spec.SearchFactorMin, spec.SearchFactorMax,
              "fallback search max factor", bad);

        if (bad.Count > 0)
        {
            throw new StaleOffsetException(
                "the engine tunables could not be read live AND the profile fallback is "
                + "out of range - this profile does not describe the running build.\n"
                + "  live:     " + string.Join("; ", rejections) + "\n"
                + "  fallback: " + string.Join("; ", bad));
        }

        return new EngineTunables
        {
            YawOffsetRadians = spec.FallbackYawOffsetRadians,
            SearchStartFactor = spec.FallbackSearchStartFactor,
            SearchStepFactor = spec.FallbackSearchStepFactor,
            SearchMaxFactor = spec.FallbackSearchMaxFactor,
            Source = TunableSource.Fallback,
            Rejections = rejections,
        };
    }

    private static float? TryRead(
        ProcessMemory memory, ulong moduleBase, Rva rva, string what, List<string> rejections)
    {
        if (rva.Confidence == Confidence.Unresolved)
        {
            rejections.Add($"{what} address is not re-derived for this build (RVA {rva})");
            return null;
        }
        try
        {
            return memory.ReadSingle(moduleBase + rva.Value);
        }
        catch (MemoryAccessException ex)
        {
            rejections.Add($"{what} at RVA {rva} could not be read: {ex.Message}");
            return null;
        }
    }

    private static void Check(float value, float min, float max, string what, List<string> into)
    {
        if (!float.IsFinite(value))
        {
            into.Add($"{what} read as {value}, which is not a finite number");
            return;
        }
        // A subnormal survives an ordinary range test -- 1.4e-45 is dutifully
        // "between 0 and 90" -- but no human ever typed one into a tuning file.
        // It is the signature of reading a float out of bytes that are not one.
        if (float.IsSubnormal(value))
        {
            into.Add($"{what} read as the subnormal {value}, which is not a tuning value");
            return;
        }
        if (value < min || value > max)
        {
            into.Add($"{what} read as {value}, outside the plausible range [{min}, {max}]");
        }
    }
}
