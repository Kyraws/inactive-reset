namespace InactiveReset.Core;

/// <summary>
/// How alarming a line is. Three levels, not two: <see cref="Good"/> exists
/// because some results are actively reassuring and should read that way.
///
/// The sector write is the case that forced this. It is the reason a placed lap
/// counts at all, and a driver who cannot see it succeed is flying blind on the
/// one thing they care about.
/// </summary>
public enum OutcomeSeverity
{
    /// <summary>A fact. Neither good nor bad.</summary>
    Normal,

    /// <summary>Something worked that could have failed.</summary>
    Good,

    /// <summary>Something did not work, or did not happen. Never fatal on its own.</summary>
    Warning,
}

/// <summary>
/// One line of a placement report.
/// </summary>
/// <param name="Label">Short name of the thing being reported. Stable enough to key on.</param>
/// <param name="Value">The result, already formatted for reading.</param>
/// <param name="Severity">How this should be presented.</param>
/// <param name="Sentence">
/// The consequence, in words a driver can act on, or null when the value speaks
/// for itself. This is where the project's hard-won distinctions live -- "gave
/// up waiting" is not "cleared", and saying so is the whole point.
/// </param>
public sealed record OutcomeLine(
    string Label,
    string Value,
    OutcomeSeverity Severity,
    string? Sentence = null);

/// <summary>
/// Turns a <see cref="PlacementOutcome"/> into the lines a human should read.
///
/// <para><b>Why this exists.</b> Both front ends used to assemble these
/// sentences themselves -- the CLI in about forty-five lines of conditionals,
/// the web page in a hand-built table of cells. They drifted, exactly as the
/// header on <see cref="PlacementService"/> warns front ends do: the page
/// reported the pit flag, which the measurements call a secondary guard, and
/// never reported the sector write, which the same measurements call the fix.
/// A driver placing from the UI was shown the guard and not the fix.</para>
///
/// <para>Deciding what an outcome MEANS is a single decision, so it is made
/// once, here. Front ends render; they do not interpret.</para>
///
/// <para>Pure, and deliberately so -- it is the part of the placement that can
/// be tested without a running game.</para>
/// </summary>
public static class PlacementReport
{
    /// <summary>
    /// Below this, the placement landed where it was aimed. Above it, the
    /// documented heading error dominates -- see docs/HEADING_BUG.md. The
    /// threshold is not a tolerance on the mechanism, which repeats to ~1.5 mm.
    /// </summary>
    private const float GoodHorizontalErrorMetres = 0.05f;

    public static IReadOnlyList<OutcomeLine> For(PlacementOutcome outcome)
    {
        var lines = new List<OutcomeLine>();

        if (!outcome.Completed)
        {
            // A failed placement is not a silent one. The messages carry what was
            // attempted and, crucially, that the bytes went back -- which is the
            // fact a driver most needs after a failure.
            lines.Add(new OutcomeLine(
                "result", "FAILED", OutcomeSeverity.Warning, outcome.Message));
            return lines;
        }

        lines.Add(new OutcomeLine("result", "placed", OutcomeSeverity.Good));

        lines.Add(new OutcomeLine(
            "horizontal error",
            $"{outcome.HorizontalErrorMetres:F3} m",
            outcome.HorizontalErrorMetres < GoodHorizontalErrorMetres
                ? OutcomeSeverity.Good
                : OutcomeSeverity.Normal,
            outcome.HorizontalErrorMetres < GoodHorizontalErrorMetres
                ? null
                : "a known, unfixed heading error puts every placement out by a "
                  + "constant amount in the vehicle frame; this is expected, not a "
                  + "failed placement. The size is NOT constant across builds -- it "
                  + "was ~0.57 m on 1AC2F605 and ~0.71 m on 0F6DCAC1, measured with "
                  + "the same calibration and identical engine tunables. Compare "
                  + "against the worst error recorded in the calibration, not a "
                  + "remembered number."));

        lines.Add(new OutcomeLine(
            "vertical error", $"{outcome.VerticalErrorMetres:F3} m", OutcomeSeverity.Normal));

        AddSector(lines, outcome);
        AddPitState(lines, outcome);
        AddPitFlag(lines, outcome);
        AddArrival(lines, outcome);

        return lines;
    }

    /// <summary>
    /// The sector write. Reported FIRST among the lap-validity lines because it
    /// is the one that decides whether the next crossing counts as a lap at all.
    /// </summary>
    private static void AddSector(List<OutcomeLine> lines, PlacementOutcome outcome)
    {
        if (outcome.SectorWrite is { } write)
        {
            lines.Add(new OutcomeLine(
                "sector",
                write.Changed ? $"{write.Before} -> {write.After}" : $"already {write.After}",
                OutcomeSeverity.Good,
                "the next start/finish crossing will count as a lap"));
            return;
        }

        if (outcome.SectorFailure is { } failure)
        {
            lines.Add(new OutcomeLine(
                "sector", "NOT RESTORED", OutcomeSeverity.Warning,
                $"{failure}. If this checkpoint is in the final sector, the first "
                + "crossing will be ignored and the lap lost."));
            return;
        }

        lines.Add(new OutcomeLine(
            "sector", "left alone", OutcomeSeverity.Normal,
            "not requested; the first crossing will be IGNORED if this checkpoint "
            + "is in the final sector"));
    }

    /// <summary>
    /// Pit state. "Skipped" and "cleared" are different claims and must never be
    /// shown as one: the first says there was nothing to wait for, the second
    /// says something happened. Conflating them would tell a driver it is safe to
    /// accelerate on the strength of a wait that never ran.
    /// </summary>
    private static void AddPitState(List<OutcomeLine> lines, PlacementOutcome outcome)
    {
        if (outcome.PitWaitSkipped)
        {
            lines.Add(new OutcomeLine(
                "pit state", "not applicable", OutcomeSeverity.Good,
                "the pit-speeding penalty is off, so there was nothing to wait for"));
            return;
        }

        if (outcome.PitStateCleared)
        {
            lines.Add(new OutcomeLine(
                "pit state", "cleared", OutcomeSeverity.Good,
                $"cleared after {outcome.TimeToClear.TotalSeconds:F1} s and "
                + $"{outcome.DistanceToClearMetres:F0} m - safe to accelerate"));
            return;
        }

        // Never soften this one. We stopped looking; that is not the same as the
        // pit state having cleared, and the driver acts on the difference.
        lines.Add(new OutcomeLine(
            "pit state", "STILL SET", OutcomeSeverity.Warning,
            $"gave up waiting after {outcome.TimeToClear.TotalSeconds:F1} s and "
            + $"{outcome.DistanceToClearMetres:F0} m. Accelerating now may earn a stop/go."));
    }

    private static void AddPitFlag(List<OutcomeLine> lines, PlacementOutcome outcome)
    {
        if (outcome.LapValidity is { } result)
        {
            lines.Add(new OutcomeLine(
                "pit flag",
                result.Changed ? $"{result.Before} -> {result.After}" : $"already {result.After}",
                result.After == 0 ? OutcomeSeverity.Good : OutcomeSeverity.Warning,
                result.Message));
            return;
        }

        if (outcome.PitFlagFailure is { } failure)
        {
            lines.Add(new OutcomeLine(
                "pit flag", "NOT CLEARED", OutcomeSeverity.Warning, failure));
            return;
        }

        lines.Add(new OutcomeLine(
            "pit flag", "left alone", OutcomeSeverity.Normal,
            "not requested; expect the first crossing to be an out-lap"));
    }

    private static void AddArrival(List<OutcomeLine> lines, PlacementOutcome outcome)
    {
        if (outcome.Arrival is not { } arrival)
        {
            return;
        }

        if (arrival.Verified)
        {
            lines.Add(new OutcomeLine(
                "arrival", "verified", OutcomeSeverity.Good,
                "every field matched what the engine produces on a correct placement"));
            return;
        }

        lines.Add(new OutcomeLine(
            "arrival", "NOT VERIFIED", OutcomeSeverity.Warning,
            string.Join("; ", arrival.Anomalies)));
    }

    /// <summary>
    /// The pit-speeding penalty write performed at arm time, if there was one.
    /// Reported separately from the placement result because it happens before
    /// the car moves and is not part of what the placement achieved.
    /// </summary>
    public static OutcomeLine? ForPenalty(PlacementOutcome outcome)
    {
        if (outcome.PitSpeedingPenalty is { } write)
        {
            return new OutcomeLine(
                "pit-speeding penalty",
                write.Changed ? $"{write.Before} -> {write.After}" : "already off",
                OutcomeSeverity.Good,
                write.Changed ? "disabled for this placement" : null);
        }

        if (outcome.PitSpeedingPenaltyFailure is { } failure)
        {
            return new OutcomeLine(
                "pit-speeding penalty", "NOT DISABLED", OutcomeSeverity.Warning,
                $"{failure}. Speeding in the pit lane may still earn a stop/go.");
        }

        return null;
    }
}
