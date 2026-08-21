using InactiveReset.Core;
using InactiveReset.Ui;

namespace InactiveReset.Cli;

/// <summary>
/// Command-line front end.
///
/// Every verb here is a thin wrapper over a Core operation, so that the UI can
/// call exactly the same code. When the two front ends implement the same
/// action twice they drift. That has already happened here once: one front end
/// silently kept an older gate-write-restore sequence.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (GateException ex)
        {
            Error("GATE REFUSED", ex.Message);
            return 2;
        }
        catch (OffsetProfileException ex)
        {
            Error("OFFSETS", ex.Message);
            return 3;
        }
        catch (StaleOffsetException ex)
        {
            Error("STALE OFFSET", ex.Message);
            return 4;
        }
        catch (MemoryAccessException ex)
        {
            Error("MEMORY", ex.Message);
            return 5;
        }
        catch (SharedMemoryException ex)
        {
            Error("SHARED MEMORY", ex.Message);
            return 6;
        }
        catch (CheckpointException ex)
        {
            Error("CHECKPOINT", ex.Message);
            return 7;
        }
        catch (CalibrationException ex)
        {
            Error("CALIBRATION", ex.Message);
            return 8;
        }
        catch (GameInstallException ex)
        {
            Error("GAME NOT FOUND", ex.Message);
            return 9;
        }
        catch (Exception ex)
        {
            Error("ERROR", ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        // Help anywhere, not just first: `place --help` is what someone types
        // when they want to know what `place` takes, and reading it as a
        // checkpoint name would be a confusing way to answer.
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "-?" or "/?" or "help"))
        {
            Usage();
            return args.Length == 0 ? 64 : 0;
        }

        var offsets = OffsetDirectory(args);
        var data = DataDirectory(args);
        var rest = args.Skip(1).ToArray();

        return args[0] switch
        {
            "status" => Status(offsets, data),
            "rules" => Rules(offsets, data, rest),
            "lap" => Lap(offsets, data, rest),
            "list" => List(data),
            "plan" => Plan(offsets, data, rest),
            "place" => Place(offsets, data, rest),
            "watch" => Watch(offsets, data),
            "capture" => Capture(offsets, data, rest),
            "serve" => Serve(offsets, data, rest),
            "launch" => Launch(data, rest),
            // `dump` and `reanchor` are maintainer tools and deliberately absent.
            // They live in src\InactiveReset.Reanchor, which is never published:
            // users consume offset profiles, they do not produce them.
            "dump" or "reanchor" => MaintainerOnly(args[0]),
            _ => Unknown(args[0]),
        };
    }

    /// <summary>
    /// `dump` and `reanchor` used to live here. They now live in the
    /// unpublished maintainer project, so tell anyone who typed them what to do
    /// instead -- silence, or a bare "unknown verb", would read as a regression.
    /// </summary>
    private static int MaintainerOnly(string verb)
    {
        Error("MAINTAINER-ONLY",
            $"""
            `{verb}` is not part of the shipped tool.

              Offset profiles are published, not derived on your machine. If LMU
              updated, run the app: it will offer to fetch the profile for the
              new build. If no profile exists yet, it has not been published yet.

              Producing profiles is src\InactiveReset.Reanchor, which ships to
              nobody.
            """);
        return 2;
    }

    /// <summary>
    /// The console consent prompt for the one outbound connection this tool makes.
    ///
    /// Asks unless the user previously answered "always". Refuses silently when
    /// stdin is redirected: a script piping into this must never be taken to have
    /// agreed to anything on the user's behalf.
    /// </summary>
    private static Func<MissingProfile, bool> AskToFetch(string dataDirectory) => request =>
    {
        var consent = new FetchConsent(dataDirectory);
        if (consent.Granted)
        {
            Console.WriteLine($"  fetching offset profile for build {request.Short} ...");
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                $"  no offset profile for build {request.Short}, and stdin is not a terminal");
            Console.Error.WriteLine("  so you cannot be asked. Download it manually:");
            Console.Error.WriteLine($"    {request.Url}");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"  This Le Mans Ultimate build ({request.Short}) is new to this machine.");
        Console.WriteLine("  An offset profile describes where things live in it, and without one");
        Console.WriteLine("  the tool cannot safely read or write anything.");
        Console.WriteLine();
        Console.WriteLine($"    download from: {request.Url}");
        Console.WriteLine($"    save to:       {request.OffsetDirectory}");
        Console.WriteLine();
        Console.WriteLine("  This is the ONLY time this tool connects to the internet.");
        Console.Write("  Fetch it? [y]es / [N]o / [a]lways: ");

        var answer = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
        if (answer is "a" or "always")
        {
            consent.Grant();
            Console.WriteLine($"  remembered in {consent.Path_}");
            return true;
        }
        return answer is "y" or "yes";
    };

    // ---- serve -------------------------------------------------------------

    private static int Serve(string offsetDirectory, string dataDirectory, string[] args)
    {
        var port = 8710;
        var portIndex = Array.IndexOf(args, "--port");
        if (portIndex >= 0 && portIndex + 1 < args.Length
            && int.TryParse(args[portIndex + 1], out var parsed))
        {
            port = parsed;
        }

        var server = new Server(offsetDirectory, dataDirectory, port);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

        var serving = server.RunAsync(cancellation.Token);

        if (!args.Contains("--no-open"))
        {
            OpenUi(server.Url, window: args.Contains("--window"));
        }

        try
        {
            serving.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C. Normal shutdown.
        }
        return 0;
    }

    /// <summary>
    /// Show the UI. <c>--window</c> asks Edge/WebView2 to open it as its own
    /// app window rather than a browser tab; otherwise it goes to the default
    /// browser, which is handy on a second screen while driving.
    /// </summary>
    private static void OpenUi(string url, bool window)
    {
        try
        {
            if (window)
            {
                // --app= gives a chromeless window with no tabs or address bar,
                // using the Edge/WebView2 runtime already present on Windows.
                // No extra dependency, and it behaves like a native window.
                var edge = new System.Diagnostics.ProcessStartInfo("msedge.exe", $"--app={url}")
                {
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(edge);
                return;
            }
        }
        catch (Exception)
        {
            // Edge missing or blocked. Fall through to the default browser.
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"open {url} manually ({ex.Message})");
        }
    }

    // ---- capture -----------------------------------------------------------

    private static SharedMemoryReader OpenSharedMemory(string offsetDirectory)
    {
        var path = Path.Combine(offsetDirectory, "shared-memory.json");
        if (!File.Exists(path))
        {
            throw new SharedMemoryException(
                $"missing {path}. Regenerate it with tools/dump-sdk-offsets.");
        }
        return new SharedMemoryReader(SharedMemoryOffsets.Load(path));
    }

    /// <summary>Live view of what a capture would record. Reads nothing but shared memory.</summary>
    private static int Watch(string offsetDirectory, string dataDirectory)
    {
        using var reader = OpenSharedMemory(offsetDirectory);
        Console.WriteLine("watching LMU shared memory - Ctrl+C to stop\n");

        while (true)
        {
            var snapshot = reader.Read();
            if (snapshot is null)
            {
                Console.Write("\r  no player vehicle (in menus?)".PadRight(110));
            }
            else
            {
                var target = Geometry.BuildTargetFromRecordedPose(snapshot.Pose);
                Console.Write(
                    $"\r  {snapshot.TrackName} | lap {snapshot.LapDistance,7:F1} m | "
                    + $"pos [{snapshot.Pose.Position}] | yaw {target.Yaw,8:F5} | "
                    + $"gear {snapshot.Gear} | {(target.Valid ? "capturable" : "NOT capturable")}"
                        .PadRight(20));
            }
            Thread.Sleep(100);
        }
    }

    private static int Capture(string offsetDirectory, string dataDirectory, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: capture <name>");
            return 64;
        }

        using var reader = OpenSharedMemory(offsetDirectory);
        var result = new CaptureService(reader).Capture(args[0]);
        var checkpoint = result.Checkpoint;

        Console.WriteLine($"  track/vehicle {checkpoint.TrackName} / {checkpoint.VehicleName}");
        Console.WriteLine($"  position      [{checkpoint.Pose.Position}]");
        Console.WriteLine($"  yaw           {result.Target.Yaw:F6} rad "
                        + $"(heading magnitude {result.Target.HeadingHorizontalMagnitude:F6})");
        Console.WriteLine($"  lap distance  {checkpoint.LapDistance:F1} m   gear {checkpoint.Gear}");

        if (!result.Usable)
        {
            Console.Error.WriteLine("\n  REFUSED - this pose cannot become a placement target:");
            foreach (var failure in result.Target.Failures)
            {
                Console.Error.WriteLine($"      - {failure}");
            }
            return 1;
        }

        var path = CaptureService.Save(checkpoint, Path.Combine(dataDirectory, "checkpoints"));
        Console.WriteLine($"\n  saved         {path}");

        var calibrations = CalibrationProfile.LoadAll(Path.Combine(dataDirectory, "profiles"));
        var hasCalibration = calibrations.Any(c =>
            string.Equals(c.TrackName, checkpoint.TrackName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.VehicleName, checkpoint.VehicleName, StringComparison.OrdinalIgnoreCase));
        if (!hasCalibration)
        {
            Console.WriteLine("\n  NOTE: there is no calibration for this track/vehicle pair, so "
                            + "`place` will refuse. Calibration is per track AND per vehicle.");
        }
        return 0;
    }

    // ---- list --------------------------------------------------------------

    private static int List(string dataDirectory)
    {
        var calibrations = CalibrationProfile.LoadAll(Path.Combine(dataDirectory, "profiles"));
        var checkpoints = Checkpoint.LoadAll(Path.Combine(dataDirectory, "checkpoints"));

        Console.WriteLine("== calibrations ==");
        if (calibrations.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        foreach (var calibration in calibrations)
        {
            Console.WriteLine($"  {calibration.TrackName} / {calibration.VehicleName}");
            Console.WriteLine($"      D = {calibration.ForwardDistance:F5}  H = {calibration.VerticalOffset:F6}"
                            + $"  samples {calibration.SampleCount}");
            if (calibration.Locked)
            {
                Console.WriteLine("      LOCKED - will refuse to place");
            }
            if (calibration.Advisory is not null)
            {
                Console.WriteLine("      advisory:");
                foreach (var line in Wrap(calibration.Advisory, 92))
                {
                    Console.WriteLine($"        {line}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("== checkpoints ==");
        if (checkpoints.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        foreach (var group in checkpoints.GroupBy(c => $"{c.TrackName} / {c.VehicleName}"))
        {
            Console.WriteLine($"  {group.Key}");
            foreach (var checkpoint in group.OrderBy(c => c.Name))
            {
                Console.WriteLine($"      {checkpoint.Name,-14} lap distance {checkpoint.LapDistance,8:F1} m");
            }
        }
        return 0;
    }

    // ---- plan / place ------------------------------------------------------

    private static int Plan(string offsetDirectory, string dataDirectory, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: plan <checkpoint>");
            return 64;
        }

        using var session = GameSession.Attach(offsetDirectory, forWriting: false, AskToFetch(dataDirectory));
        var plan = BuildPlan(session, dataDirectory, args[0]);
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

        using var session = GameSession.Attach(offsetDirectory, forWriting: true, AskToFetch(dataDirectory));
        var plan = BuildPlan(session, dataDirectory, args[0]);
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

        var outcome = new PlacementService(session).Place(
            plan, OnProgress,
            clearPitFlag: !args.Contains("--keep-pit-flag"),
            setSector: setSector);
        if (!outcome.Completed)
        {
            Console.Error.WriteLine($"\nFAILED: {outcome.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  achieved      [{outcome.Achieved}]");
        PrintOutcome(outcome);
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
    private static void PrintOutcome(PlacementOutcome outcome)
    {
        var penalty = PlacementReport.ForPenalty(outcome);
        var lines = penalty is null
            ? PlacementReport.For(outcome)
            : [penalty, .. PlacementReport.For(outcome)];

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

    private static PlacementPlan BuildPlan(GameSession session, string dataDirectory, string name)
    {
        var checkpoint = Checkpoint.Require(Path.Combine(dataDirectory, "checkpoints"), name);
        var calibration = CalibrationProfile.Require(
            Path.Combine(dataDirectory, "profiles"), checkpoint.TrackName, checkpoint.VehicleName);
        return new PlacementService(session).Plan(checkpoint, calibration);
    }

    private static void PrintPlan(PlacementPlan plan)
    {
        Console.WriteLine("== plan ==");
        Console.WriteLine($"  checkpoint    {plan.Checkpoint.Name}  ({plan.Checkpoint.TrackName} / {plan.Checkpoint.VehicleName})");
        Console.WriteLine($"  calibration   D = {plan.Model.RestForwardDistance:F5}  H = {plan.Model.RestVerticalOffset:F6}");
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

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var words = text.Split(' ');
        var line = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(word);
        }
        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    // ---- launch ------------------------------------------------------------

    /// <summary>
    /// Start the game, either way.
    ///
    /// The mode is a required, spelled-out argument rather than a flag with a
    /// default. Both launches are legitimate and they are not interchangeable:
    /// one produces a session this tool can use and one deliberately does not,
    /// so defaulting would silently pick a side of that.
    /// </summary>
    private static int Launch(string dataDirectory, string[] args)
    {
        var gameDirectory = Flag(args, "--game-dir");

        var set = Flag(args, "--set-game-dir");
        if (set is not null)
        {
            var saved = GameLauncher.SetInstallDirectory(dataDirectory, set);
            Console.WriteLine($"install directory saved: {saved.Root}");
            return 0;
        }

        var verb = args.FirstOrDefault(a => !a.StartsWith('-'));

        // `launch where` answers "which install would you use?" without starting
        // anything -- the question you ask when a machine has two of them.
        if (verb is "where")
        {
            var install = GameLauncher.Locate(dataDirectory, gameDirectory);
            Console.WriteLine($"  install   {install.Root}");
            Console.WriteLine($"  found by  {install.Source}");
            Console.WriteLine($"  direct    {(File.Exists(install.DirectExe) ? "ok" : "MISSING")}  {install.DirectExe}");
            Console.WriteLine($"  eac       {(File.Exists(install.ProtectedExe) ? "ok" : "MISSING")}  {install.ProtectedExe}");

            var state = GameLauncher.Running();
            Console.WriteLine($"  running   {(state.Running ? $"pid {state.ProcessId}, {(state.Protected ? "with EasyAntiCheat - NOT attachable" : "direct - attachable")}" : "no")}");
            return 0;
        }

        var mode = verb switch
        {
            "direct" => LaunchMode.Direct,
            "eac" => LaunchMode.Protected,
            null => (LaunchMode?)null,
            _ => null,
        };

        if (mode is null)
        {
            Error("LAUNCH",
                $"""
                say which launch you want.

                  inactive-reset launch direct   no anticheat; this tool can attach
                  inactive-reset launch eac      the normal protected launch
                  inactive-reset launch where    which install would be used
                """);
            return 64;
        }

        var result = GameLauncher.Launch(mode.Value, dataDirectory, gameDirectory);
        Console.WriteLine($"started pid {result.ProcessId}: {Path.GetFileName(result.ExecutablePath)}");
        Console.WriteLine($"  {result.Message}");
        return 0;
    }

    // ---- status ------------------------------------------------------------

    private static int Status(string offsetDirectory, string dataDirectory)
    {
        using var session = GameSession.Attach(offsetDirectory, forWriting: false, AskToFetch(dataDirectory));

        Console.WriteLine("== build ==");
        foreach (var gate in session.Gates)
        {
            Console.WriteLine($"  {gate.Name,-12} {(gate.Passed ? "ok" : "FAILED"),-7} {gate.Detail}");
        }
        Console.WriteLine($"  {"module base",-12} {"",-7} 0x{session.ModuleBase:X}");
        Console.WriteLine($"  {"game version",-12} {"",-7} {session.Offsets.FileVersion}");

        Console.WriteLine();
        Console.WriteLine("== rules ==");
        PrintRules(new RulesController(session).ReadAll());

        Console.WriteLine();
        try
        {
            var slot = new LiveStateReader(session).ResolveSlotIndex();
            PrintLapValidity(new LapValidityController(session).Read(slot));
        }
        catch (Exception ex)
            when (ex is MemoryAccessException or OffsetProfileException or StaleOffsetException)
        {
            // In the menus there is no slot to read, and an older profile has no
            // countLapFlag entry. Neither says anything about the build gates
            // above, so neither should look like a status failure.
            Console.WriteLine("== lap validity ==");
            Console.WriteLine($"  unavailable: {ex.Message}");
        }
        return 0;
    }

    // ---- lap validity ------------------------------------------------------

    /// <summary>
    /// Show, and optionally repair, whether the current and next lap will be
    /// timed.
    ///
    /// Read-only by default on purpose. The diagnosis this implements -- that a
    /// placed car keeps its pit flag and so gets its first flying lap demoted to
    /// an out-lap -- is derived from the disassembly but has not been watched
    /// happening. Reading it costs nothing and settles the question.
    /// </summary>
    private static int Lap(string offsetDirectory, string dataDirectory, string[] args)
    {
        var sectorIndex = Array.IndexOf(args, "--set-sector");
        int? setSector = sectorIndex >= 0 && sectorIndex + 1 < args.Length
                         && int.TryParse(args[sectorIndex + 1], out var parsedSector)
            ? parsedSector
            : null;

        var wantsWrite = args.Contains("--clear-pit-flag") || setSector is not null;
        if (wantsWrite && !args.Contains("--accept-write"))
        {
            Console.Error.WriteLine(
                "refusing: this writes to the game. Pass --accept-write to confirm.");
            return 64;
        }

        using var session = GameSession.Attach(offsetDirectory, forWriting: wantsWrite, AskToFetch(dataDirectory));
        var slot = new LiveStateReader(session).ResolveSlotIndex();
        var lap = new LapValidityController(session);
        var state = lap.Read(slot);

        PrintLapValidity(state);

        if (!wantsWrite)
        {
            Console.WriteLine();
            Console.WriteLine("  pass --clear-pit-flag --accept-write to clear the pit flag");
            Console.WriteLine("  pass --set-sector N --accept-write to set the sector index");
            return 0;
        }

        Console.WriteLine();

        if (setSector is not null)
        {
            var sector = lap.SetSector(slot, setSector.Value);
            Console.WriteLine($"  sector        0x{sector.Address:X}  {sector.Before} -> {sector.After}"
                            + (sector.Changed ? "" : "  (unchanged)"));
        }

        if (args.Contains("--clear-pit-flag"))
        {
            var result = lap.ClearPitFlag(slot);
            Console.WriteLine($"  pit flag      0x{result.Address:X}  {result.Before} -> {result.After}"
                            + (result.Changed ? "" : "  (unchanged)"));
            Console.WriteLine($"                {result.Message}");
        }
        return 0;
    }

    private static void PrintLapValidity(LapValidityState state)
    {
        Console.WriteLine("== lap validity ==");
        Console.WriteLine($"  {"slot",-14} {state.SlotIndex}");
        Console.WriteLine($"  {"countLapFlag",-14} {state.CountLapFlag} ({state.Counting})"
                        + $"   0x{state.CountLapFlagAddress:X}");
        Console.WriteLine($"  {"lapCountsNext",-14} {(state.LapCountsNext ? 1 : 0)}"
                        + $"   0x{state.LapCountsNextAddress:X}");
        Console.WriteLine($"  {"pitFlag",-14} {(state.PitFlag ? 1 : 0)}"
                        + $"   0x{state.PitFlagAddress:X}"
                        + (state.PitFlag ? "   <- demotes the next crossing" : ""));
        Console.WriteLine($"  {"pitState",-14} {state.PitState}");
        Console.WriteLine($"  {"lapNumber",-14} {state.LapNumber}");
        Console.WriteLine($"  {"sector",-14} {state.Sector}");
        Console.WriteLine($"  {"lapStartEt",-14} {state.LapStartEt:F3}");
        Console.WriteLine();
        Console.WriteLine($"  this lap      {(state.CurrentLapIsTimed ? "timed" : "NOT timed")}");
        Console.WriteLine($"  next lap      {(state.NextLapWillBeTimed ? "will be timed" : "will NOT be timed")}");
        Console.WriteLine($"                {state.Summary}");
    }

    // ---- rules -------------------------------------------------------------

    private static int Rules(string offsetDirectory, string dataDirectory, string[] args)
    {
        var wantsWrite = args.Any(a => a is "--off" or "--on");
        using var session = GameSession.Attach(offsetDirectory, forWriting: wantsWrite, AskToFetch(dataDirectory));
        var rules = new RulesController(session);

        if (!wantsWrite)
        {
            PrintRules(rules.ReadAll());
            Console.WriteLine();
            Console.WriteLine("  pass --off or --on with --pit-speeding and/or --track-limits");
            return 0;
        }

        var enable = args.Contains("--on");
        var pit = args.Contains("--pit-speeding");
        var limits = args.Contains("--track-limits");
        if (!pit && !limits)
        {
            pit = limits = true;   // "--off" alone means everything we know how to turn off
        }

        var results = new List<RuleWriteResult>();
        if (pit)
        {
            results.Add(rules.SetPitSpeedingPenalty(enable));
        }
        if (limits)
        {
            results.AddRange(rules.SetTrackLimits(enable));
        }

        foreach (var result in results)
        {
            Console.WriteLine(result.Changed
                ? $"  {result.Name,-30} 0x{result.Address:X}  {result.Before} -> {result.After}"
                : $"  {result.Name,-30} 0x{result.Address:X}  already {result.After}");
        }

        Console.WriteLine();
        PrintRules(rules.ReadAll());

        if (session.Process.HasExited)
        {
            Console.Error.WriteLine("\nWARNING: the game process has exited.");
            return 1;
        }
        Console.WriteLine("\ngame alive.");
        return 0;
    }

    private static void PrintRules(IReadOnlyList<RuleState> states)
    {
        var width = states.Count == 0 ? 0 : states.Max(s => s.Name.Length);
        foreach (var state in states)
        {
            Console.WriteLine($"  {state.Name.PadRight(width)} = {state.Display,-4} {(state.Resolved ? state.Effect : "NOT re-derived for this build - not read")}");
            if (state.Domain is not null)
            {
                Console.WriteLine($"  {new string(' ', width)}        {state.Domain}");
            }
        }
    }

    // ---- plumbing ----------------------------------------------------------

    /// <summary>
    /// Where the per-build JSON profiles live. Defaults to an `offsets` folder
    /// beside the executable, then beside the repo root, so the tool works both
    /// from a publish folder and from `dotnet run`.
    /// </summary>
    private static string OffsetDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--offsets");
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        return FindUpwards("offsets");
    }

    /// <summary>Where calibration profiles and checkpoints live.</summary>
    private static string DataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }
        return FindUpwards("data");
    }

    /// <summary>The value after <paramref name="name"/>, or null if it is absent.</summary>
    private static string? Flag(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindUpwards(string folder)
    {
        var here = AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(here); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, folder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(here, folder);
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'");
        Usage();
        return 64;
    }

    private static void Error(string kind, string message)
    {
        Console.Error.WriteLine($"{kind}: {message}");
    }

    private static void Usage()
    {
        Console.WriteLine("""
            Inactive Reset - practice tool for Le Mans Ultimate
            Offline single-player Practice only.

              inactive-reset status
                  Build gates, which offset profile matched, and current rules.

              inactive-reset rules
                  Show the penalty rules and whether each can be changed live.

              inactive-reset rules --off [--pit-speeding] [--track-limits]
              inactive-reset rules --on  [--pit-speeding] [--track-limits]
                  Turn penalties off or back on. With neither flag, both.

              inactive-reset lap
                  Whether this lap and the next one will be timed, and why.
                  Placing a car sets the engine's pit flag, and until that
                  clears the next start/finish crossing is demoted to an
                  out-lap - which costs a whole extra lap. READS ONLY.

              inactive-reset lap --clear-pit-flag --accept-write
                  Clear the pit flag. One byte, verified by read-back.

              inactive-reset list
                  Calibrations and recorded checkpoints.

              inactive-reset watch
                  Live view of position, heading and whether the current pose
                  could be captured. Reads LMU's shared memory only.

              inactive-reset capture <name>
                  Record the car's current pose as a checkpoint. Drive to the
                  spot you want to practise from, then run this.

              inactive-reset plan <checkpoint>
                  Everything a placement would do, including the exact 24 bytes.
                  WRITES NOTHING.

              inactive-reset place <checkpoint> --accept-write [--keep-pit-flag]
                  Place the car. Park in the garage first, then press Drive when
                  it prints ARMED.

                  Afterwards the car briefly carries pit state. Drive away
                  GENTLY - pit state clears with distance travelled, not with
                  time, so standing still will never clear it. CLEAR means it
                  has cleared and you can accelerate.

                  Once clear, the pit flag is cleared too so your first flying
                  lap is timed instead of being treated as an out-lap. The
                  before/after values are printed, so a run that reports 0 -> 0
                  says the flag was not the problem. --keep-pit-flag skips it.

              inactive-reset launch direct
                  Start the game with no anticheat in the process tree. This is
                  the only kind of session this tool can attach to. Steam must
                  already be running.

              inactive-reset launch eac
                  Start the game the normal, protected way, for online racing.
                  This tool will refuse to attach to that session, on purpose.

              inactive-reset launch where
                  Which install would be used, and whether the game is up.
                  Steam's own library config is read to find it; override with
                  --game-dir, or save one with --set-game-dir <dir>.

              inactive-reset serve [--window] [--port N] [--no-open]
                  Same features in a local web UI, on 127.0.0.1 only.
                  --window opens it as its own app window instead of a tab.

            Not here on purpose:
              dump, reanchor    Maintainer tools, in the unpublished
                                InactiveReset.Reanchor project. When LMU
                                updates you do not re-derive anything -- the
                                app offers to fetch the new profile.

            Options:
              --offsets <dir>   folder of per-build offset profiles
              --data <dir>      folder holding profiles/ and checkpoints/

            Note: track-limits penalties are latched at session start, so the
            tool writes the engine's derived flags rather than the setting.
            Changing the setting itself, in-game or over REST, does nothing
            to a session already running.
            """);
    }
}

