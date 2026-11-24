using InactiveReset.Core;

namespace InactiveReset.Cli;

internal static partial class Program
{
    private static int Plan(string offsetDirectory, string dataDirectory, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: plan <checkpoint>");
            return 64;
        }

        using var session = GameSession.Attach(offsetDirectory, forWriting: false);
        var plan = BuildPlan(session, offsetDirectory, dataDirectory, args[0]);
        PrintPlan(plan);
        Console.WriteLine("\nNOTHING WAS WRITTEN.");
        return plan.CanProceed ? 0 : 1;
    }

    private static int Place(string offsetDirectory, string dataDirectory, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: place <checkpoint> --accept-write");
            return 64;
        }
        if (!args.Contains("--accept-write"))
        {
            Console.Error.WriteLine(
                "refusing: `place` writes 24 bytes to the game. Pass --accept-write to confirm.");
            return 64;
        }

        var menu = new LmuSessionClient(dataDirectory: dataDirectory);
        menu.RequirePracticeAsync().GetAwaiter().GetResult();
        using var session = GameSession.Attach(offsetDirectory, forWriting: true);
        var plan = BuildPlan(session, offsetDirectory, dataDirectory, args[0]);
        PrintPlan(plan);

        if (!plan.CanProceed)
        {
            Console.Error.WriteLine("\nrefusing: preconditions not met.");
            return 1;
        }

        Console.WriteLine();
        // A plain delegate, not Progress<T>, which delivers reports out of
        // order. The phases are a sequence and must read as one.
        void OnProgress(PlacementProgress p) =>
            Console.WriteLine($"  [{p.Phase}] {p.Message}");

        var sectorIndex = Array.IndexOf(args, "--sector");
        int? setSector = args.Contains("--keep-sector") ? null
            : sectorIndex >= 0 && sectorIndex + 1 < args.Length
              && int.TryParse(args[sectorIndex + 1], out var chosen)
                ? chosen
                : LapValidityController.LastSector;

        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine("Cleanup requested. If already placed, return to the garage or wait for pit state to clear.");
        }
        Console.CancelKeyPress += Cancel;
        PlacementOutcome outcome;
        try
        {
            outcome = new PlacementService(session).Place(plan, offsetDirectory, menu, OnProgress,
                cancellation: cancellation.Token,
                clearPitFlag: !args.Contains("--keep-pit-flag"), setSector: setSector);
        }
        finally { Console.CancelKeyPress -= Cancel; }
        if (!outcome.Completed)
        {
            Console.Error.WriteLine($"\nFAILED: {outcome.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  achieved      [{outcome.Achieved}]");

        PrintOutcome(outcome, plan.Calibration, session.ExecutableSha256);
        return 0;
    }

    /// <summary>
    /// Render the placement report.
    ///
    /// Deliberately dumb. What an outcome MEANS -- whether a value is
    /// reassuring, what the driver should do about it -- is decided once in
    /// <see cref="PlacementReport"/> and rendered identically here and in the web
    /// page. This used to be forty-five lines of conditionals with a second,
    /// divergent copy in the UI, and the copies drifted: the page never reported
    /// the sector write at all.
    ///
    /// Note that it no longer inspects <c>args</c>. "Left alone" and "tried and
    /// failed" are now carried by the outcome itself, which is where that fact
    /// always belonged.
    /// </summary>
    private static void PrintOutcome(
        PlacementOutcome outcome, CalibrationProfile? calibration, string? buildSha256)
    {
        var penalty = PlacementReport.ForPenalty(outcome);
        var lines = penalty is null
            ? PlacementReport.For(outcome, calibration, buildSha256)
            : [penalty, .. PlacementReport.For(outcome, calibration, buildSha256)];

        foreach (var line in lines)
        {
            var marker = line.Severity switch
            {
                OutcomeSeverity.Good => "+",
                OutcomeSeverity.Warning => "!",
                _ => " ",
            };
            Console.WriteLine($"  {marker} {line.Label,-22}{line.Value}");
            if (line.Sentence is not null)
            {
                foreach (var wrapped in Wrap(line.Sentence, 62))
                {
                    Console.WriteLine($"    {new string(' ', 22)}{wrapped}");
                }
            }
        }
    }

    private static PlacementPlan BuildPlan(
        GameSession session, string offsetDirectory, string dataDirectory, string name)
    {
        var (checkpoint, identity) = PlacementService.ResolveDestination(
            offsetDirectory, Path.Combine(dataDirectory, "checkpoints"), name);
        return new PlacementService(session).Plan(checkpoint, dataDirectory, identity);
    }

    private static void PrintPlan(PlacementPlan plan)
    {
        Console.WriteLine("== plan ==");
        Console.WriteLine($"  checkpoint    {plan.Checkpoint.Name}  ({plan.Checkpoint.TrackName} / {plan.Checkpoint.VehicleName})");
        Console.WriteLine($"  loaded car    {plan.PlacementIdentity.VehicleName}");
        var source = plan.RestSource switch
        {
            RestSource.Profile => "measured calibration",
            _ => "derived from the running engine",
        };
        Console.WriteLine($"  calibration   D = {plan.Model.RestForwardDistance:F5}  H = {plan.Model.RestVerticalOffset:F6}"
                        + $"  L = {plan.Model.RestLateralOffset:F6}");
        Console.WriteLine($"                {source}");
        if (plan.Calibration.Advisory is not null)
        {
            Console.WriteLine("  ADVISORY:");
            foreach (var line in Wrap(plan.Calibration.Advisory, 88))
            {
                Console.WriteLine($"      {line}");
            }
        }
        Console.WriteLine($"  target rest   [{plan.Target.RestPosition}]  yaw {plan.Target.Yaw:F6}");
        Console.WriteLine($"  computed      {plan.ComputedEntry}");
        Console.WriteLine($"  24 bytes      {Convert.ToHexString(plan.Payload)}");
        Console.WriteLine($"  re-check      [{plan.ForwardRecheck}]  residual {plan.ResidualMetres:F6} m");

        Console.WriteLine();
        Console.WriteLine("== live ==");
        var live = plan.Live;
        Console.WriteLine($"  slot/pit/gar  {live.Container.SlotIndex} / {live.Container.PitIndex} / {live.Container.GarageIndex}");
        Console.WriteLine($"  controlOwner  {live.Container.ControlOwner}  (1 = Ai/garage, 0 = player)");
        Console.WriteLine($"  vehicle L/W   {live.Container.VehicleLength:F4} / {live.Container.VehicleWidth:F5}");
        Console.WriteLine($"  lateral sign  {Geometry.LateralSign(live.Container.LateralSignSource):+0;-0}  "
                        + $"(source {live.Container.LateralSignSource:F6})");
        Console.WriteLine($"  MULT / COUNT  {live.Globals.Mult} / {live.Globals.Count}");
        Console.WriteLine($"  entry at      0x{live.EntryAddress:X}");
        Console.WriteLine($"  padding tail  {Convert.ToHexString(live.PaddingTail)}  (never written)");

        Console.WriteLine();
        Console.WriteLine(plan.Live.Preconditions.Passed
            ? "  preconditions ALL PASSED"
            : "  preconditions FAILED:");
        foreach (var failure in plan.Live.Preconditions.Failures)
        {
            Console.WriteLine($"      - {failure}");
        }
        foreach (var failure in plan.Target.Failures)
        {
            Console.WriteLine($"      - {failure}");
        }
    }
}
