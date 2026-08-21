using System.Text.Json.Nodes;
using InactiveReset.Core;

namespace InactiveReset.Ui;

/// <summary>
/// Adapts the Core services for the web front end.
///
/// A placement takes as long as the driver takes to press Drive, so it cannot
/// be a request/response: it runs on a background task and the page polls
/// <c>/api/state</c> for the current phase. Everything else is synchronous.
/// </summary>
public sealed class PlacementRunner(string offsetDirectory, string dataDirectory)
{
    // System.Threading.Lock is .NET 9+; this targets .NET 8.
    private readonly object _gate = new();
    private readonly List<string> _log = [];

    private PlacementPhase _phase = PlacementPhase.Idle;
    private string _phaseMessage = "";
    private PlacementOutcome? _lastOutcome;
    private Task? _running;

    private FetchConsent Consent => new(dataDirectory);

    /// <summary>
    /// Fetch a missing profile only if the user has already agreed.
    ///
    /// The window cannot block on a console prompt, so consent is a stored
    /// answer rather than a question asked mid-attach. Until it is given, attach
    /// fails with the ordinary "no profile for this build" error and the page
    /// offers the choice explicitly -- which keeps the rule identical to the
    /// CLI's: this tool does not reach the internet until told it may.
    /// </summary>
    private bool FetchIfAllowed(MissingProfile request) => Consent.Granted;

    /// <summary>
    /// Grant consent, then report whether a profile can now be had. Called by
    /// the page's "allow and fetch" action.
    /// </summary>
    public JsonObject AllowFetch()
    {
        var result = new JsonObject();
        try
        {
            Consent.Grant();
            using var session = GameSession.Attach(offsetDirectory, forWriting: false, FetchIfAllowed);
            result["ok"] = true;
            result["message"] = $"profile ready for build {session.ExecutableSha256[..8]}";
        }
        catch (Exception ex)
        {
            result["ok"] = false;
            result["message"] = ex.Message;
        }
        return result;
    }

    private string Checkpoints => Path.Combine(dataDirectory, "checkpoints");
    private string Profiles => Path.Combine(dataDirectory, "profiles");

    // ---- state -------------------------------------------------------------

    public JsonObject State()
    {
        var state = new JsonObject();

        try
        {
            using var session = GameSession.Attach(offsetDirectory, forWriting: false, FetchIfAllowed);
            state["connected"] = true;
            state["pid"] = session.Process.Id;
            state["build"] = session.ExecutableSha256[..8];
            state["moduleBase"] = $"0x{session.ModuleBase:X}";
            state["gameVersion"] = session.Offsets.FileVersion;

            var gates = new JsonArray();
            foreach (var gate in session.Gates)
            {
                gates.Add(new JsonObject
                {
                    ["name"] = gate.Name,
                    ["passed"] = gate.Passed,
                    ["detail"] = gate.Detail,
                });
            }
            state["gates"] = gates;

            var all = new RulesController(session).ReadAll();

            var rules = new JsonArray();
            foreach (var rule in all)
            {
                rules.Add(new JsonObject
                {
                    ["name"] = rule.Name,
                    ["value"] = rule.Value,
                    ["effect"] = rule.Effect,
                    ["writableLive"] = rule.WritableLive,
                    ["domain"] = rule.Domain,
                    ["rva"] = $"0x{rule.Rva:X7}",
                    ["address"] = $"0x{rule.Address:X}",
                });
            }
            state["rules"] = rules;

            // Track limits is the only penalty left as a choice. Steward
            // penalties are absent because nothing here can change them, and an
            // unusable control is worse than none.
            var pit = all.FirstOrDefault(r => r.Name.Contains("Pit-speeding"));
            var limits = all.FirstOrDefault(r => r.Name.Contains("lap invalidation"));

            state["penalties"] = new JsonObject
            {
                ["trackLimits"] = new JsonObject
                {
                    ["label"] = "Track Limits",
                    ["detail"] = "penalties and lap invalidation for going off track",
                    ["on"] = limits is not null && limits.Value != 0,
                    ["known"] = limits is not null,
                },
            };

            // The pit-speeding penalty is NOT a toggle. Every placement disables
            // it, because there is no case where you want a stop/go for a
            // position the tool put the car in. Shown as status so the true
            // state is still visible -- read back from memory, like everything
            // else here, rather than assumed from what was last written.
            //
            // NOTE: this is the penalty, not the car's pit limiter, which this
            // tool never touches. See CONTEXT.md; the two were confused for as
            // long as this control was labelled "Pit Limiter".
            state["pitSpeedingPenalty"] = new JsonObject
            {
                ["label"] = "Pit-speeding penalty",
                ["detail"] = "stop/go for speeding in the pit lane; disabled at every placement",
                ["on"] = pit is not null && pit.Value != 0,
                ["known"] = pit is not null,
            };

            state["lapValidity"] = LapValidity_(session);
        }
        catch (Exception ex)
        {
            state["connected"] = false;
            state["error"] = ex.Message;

            // A new LMU build is patch day, not a fault. Tell the page exactly
            // that, so it can offer the one action that helps instead of showing
            // the same dead end as "game not running".
            if (ex is MissingProfileException missing)
            {
                state["missingProfile"] = new JsonObject
                {
                    ["build"] = missing.Request.Short,
                    ["url"] = missing.Request.Url,
                    ["consentGiven"] = Consent.Granted,
                };
            }
        }

        state["game"] = Game_();
        state["session"] = Session_();
        state["checkpoints"] = Checkpoints_();
        state["calibrations"] = Calibrations_();

        lock (_gate)
        {
            state["phase"] = _phase.ToString();
            state["phaseMessage"] = _phaseMessage;
            state["busy"] = _running is { IsCompleted: false };
            state["log"] = new JsonArray(_log.TakeLast(40).Select(l => (JsonNode)l!).ToArray());
            state["outcome"] = _lastOutcome is null ? null : new JsonObject
            {
                ["completed"] = _lastOutcome.Completed,
                ["lines"] = OutcomeLines(_lastOutcome),
            };
        }
        return state;
    }

    /// <summary>
    /// The placement report, verbatim.
    ///
    /// This method does not decide what anything means, and must not start. The
    /// page renders whatever lines arrive, so a line added in
    /// <see cref="PlacementReport"/> reaches the driver without either front end
    /// being touched. That is the whole point: the previous hand-built version
    /// of this block silently omitted the sector write -- the one field that
    /// decides whether a placed lap counts -- for as long as it existed.
    /// </summary>
    private static JsonArray OutcomeLines(PlacementOutcome outcome)
    {
        var penalty = PlacementReport.ForPenalty(outcome);
        var lines = penalty is null
            ? PlacementReport.For(outcome)
            : [penalty, .. PlacementReport.For(outcome)];

        return new JsonArray(lines.Select(line => (JsonNode)new JsonObject
        {
            ["label"] = line.Label,
            ["value"] = line.Value,
            ["severity"] = line.Severity.ToString().ToLowerInvariant(),
            ["sentence"] = line.Sentence,
        }).ToArray());
    }

    /// <summary>
    /// Whether the current and next lap will be timed.
    ///
    /// Read back from memory on every refresh, like the rules are — the state
    /// the engine is actually in, not what the tool last asked for. Null when
    /// there is no slot to read (menus) or when the loaded profile predates
    /// these fields; both are absences, not failures, so neither is allowed to
    /// take the rest of the state down with it.
    /// </summary>
    private static JsonNode? LapValidity_(GameSession session)
    {
        try
        {
            var slot = new LiveStateReader(session).ResolveSlotIndex();
            var lap = new LapValidityController(session).Read(slot);
            return new JsonObject
            {
                ["countLapFlag"] = lap.CountLapFlag,
                ["counting"] = lap.Counting.ToString(),
                ["lapCountsNext"] = lap.LapCountsNext,
                ["pitFlag"] = lap.PitFlag,
                ["pitState"] = lap.PitState,
                ["currentLapTimed"] = lap.CurrentLapIsTimed,
                ["nextLapTimed"] = lap.NextLapWillBeTimed,
                ["summary"] = lap.Summary,
            };
        }
        catch (Exception ex)
            when (ex is MemoryAccessException or OffsetProfileException or StaleOffsetException)
        {
            return null;
        }
    }

    /// <summary>
    /// Track, car and where it currently is, from LMU's own shared memory.
    ///
    /// Null while the game is in menus, which is the normal state between
    /// sessions rather than an error worth showing.
    /// </summary>
    /// <summary>
    /// Whether the game is up, how it was started, and whether we could start
    /// it. Reported even when <c>connected</c> is false -- especially then,
    /// since "not running" and "running, but protected" are the two cases the
    /// launch buttons exist for and they must not look alike.
    /// </summary>
    private JsonNode? Game_()
    {
        var running = GameLauncher.Running();
        var game = new JsonObject
        {
            ["running"] = running.Running,
            ["protected"] = running.Protected,
            ["attachable"] = running.Attachable,
            ["pid"] = running.ProcessId,
        };

        try
        {
            var install = GameLauncher.Locate(dataDirectory);
            game["install"] = install.Root;
            game["foundBy"] = install.Source;
            game["canLaunchDirect"] = File.Exists(install.DirectExe);
            game["canLaunchProtected"] = File.Exists(install.ProtectedExe);
        }
        catch (GameInstallException ex)
        {
            // Not being able to find the install disables the launch buttons.
            // It says nothing about a session that is already running, so it
            // must not be reported as a connection failure.
            game["installError"] = ex.Message;
            game["canLaunchDirect"] = false;
            game["canLaunchProtected"] = false;
        }

        return game;
    }

    /// <summary>
    /// Start the game one way or the other.
    ///
    /// The mode is required in the body and not defaulted, for the same reason
    /// the CLI verb requires it: the two launches are not interchangeable, and
    /// only one of them produces a session this tool can use.
    /// </summary>
    public JsonObject Launch(JsonObject body)
    {
        var mode = body["mode"]?.GetValue<string>() switch
        {
            "direct" => LaunchMode.Direct,
            "eac" => LaunchMode.Protected,
            _ => throw new InvalidOperationException("mode must be 'direct' or 'eac'"),
        };

        var result = GameLauncher.Launch(mode, dataDirectory);

        lock (_gate)
        {
            _log.Add($"launched {Path.GetFileName(result.ExecutablePath)} (pid {result.ProcessId})");
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["mode"] = mode == LaunchMode.Direct ? "direct" : "eac",
            ["pid"] = result.ProcessId,
            ["message"] = result.Message,
        };
    }

    private JsonNode? Session_()
    {
        try
        {
            var path = Path.Combine(offsetDirectory, "shared-memory.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var reader = new SharedMemoryReader(SharedMemoryOffsets.Load(path));
            var snapshot = reader.Read();
            if (snapshot is null)
            {
                return null;
            }

            var target = Geometry.BuildTargetFromRecordedPose(snapshot.Pose);
            return new JsonObject
            {
                ["track"] = snapshot.TrackName,
                ["vehicle"] = snapshot.VehicleName,
                ["lapDistance"] = snapshot.LapDistance,
                ["gear"] = snapshot.Gear,
                ["x"] = snapshot.Pose.Position.X,
                ["y"] = snapshot.Pose.Position.Y,
                ["z"] = snapshot.Pose.Position.Z,
                ["yaw"] = target.Yaw,
                ["capturable"] = target.Valid,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private JsonArray Checkpoints_()
    {
        var array = new JsonArray();
        foreach (var checkpoint in Checkpoint.LoadAll(Checkpoints).OrderBy(c => c.Name))
        {
            array.Add(new JsonObject
            {
                ["name"] = checkpoint.Name,
                ["track"] = checkpoint.TrackName,
                ["vehicle"] = checkpoint.VehicleName,
                ["lapDistance"] = checkpoint.LapDistance,
            });
        }
        return array;
    }

    private JsonArray Calibrations_()
    {
        var array = new JsonArray();
        foreach (var calibration in CalibrationProfile.LoadAll(Profiles))
        {
            array.Add(new JsonObject
            {
                ["track"] = calibration.TrackName,
                ["vehicle"] = calibration.VehicleName,
                ["forwardDistance"] = calibration.ForwardDistance,
                ["verticalOffset"] = calibration.VerticalOffset,
                ["locked"] = calibration.Locked,
                ["advisory"] = calibration.Advisory,
            });
        }
        return array;
    }

    // ---- actions -----------------------------------------------------------

    public JsonObject SetRules(JsonObject body)
    {
        var enable = body["enable"]?.GetValue<bool>() ?? false;
        var pit = body["pitSpeeding"]?.GetValue<bool>() ?? false;
        var limits = body["trackLimits"]?.GetValue<bool>() ?? false;

        using var session = GameSession.Attach(offsetDirectory, forWriting: true, FetchIfAllowed);
        var rules = new RulesController(session);

        var changes = new JsonArray();
        if (pit)
        {
            Add(changes, rules.SetPitSpeedingPenalty(enable));
        }
        if (limits)
        {
            foreach (var result in rules.SetTrackLimits(enable))
            {
                Add(changes, result);
            }
        }

        Log(enable ? "penalties enabled" : "penalties disabled");
        return new JsonObject { ["ok"] = true, ["changes"] = changes };

        static void Add(JsonArray array, RuleWriteResult result) => array.Add(new JsonObject
        {
            ["name"] = result.Name,
            ["address"] = $"0x{result.Address:X}",
            ["before"] = result.Before,
            ["after"] = result.After,
            ["changed"] = result.Changed,
        });
    }

    public JsonObject Capture(JsonObject body)
    {
        var name = body["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckpointException("a checkpoint name is required");
        }

        var offsets = SharedMemoryOffsets.Load(Path.Combine(offsetDirectory, "shared-memory.json"));
        using var reader = new SharedMemoryReader(offsets);
        var result = new CaptureService(reader).Capture(name);

        if (!result.Usable)
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["error"] = "this pose cannot become a placement target",
                ["failures"] = new JsonArray(result.Target.Failures.Select(f => (JsonNode)f!).ToArray()),
            };
        }

        var path = CaptureService.Save(result.Checkpoint, Checkpoints);
        Log($"captured '{name}' at {result.Checkpoint.LapDistance:F0} m");
        return new JsonObject { ["ok"] = true, ["path"] = path };
    }

    /// <summary>
    /// Start a placement on a background task. Returns immediately; the page
    /// follows progress through <see cref="State"/>.
    /// </summary>
    public JsonObject StartPlace(JsonObject body)
    {
        var name = body["checkpoint"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckpointException("a checkpoint name is required");
        }

        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                return new JsonObject { ["ok"] = false, ["error"] = "a placement is already running" };
            }
            _log.Clear();
            _lastOutcome = null;
            _phase = PlacementPhase.Gating;
            _phaseMessage = "starting";
            _running = Task.Run(() => RunPlacement(name));
        }
        return new JsonObject { ["ok"] = true };
    }

    private void RunPlacement(string name)
    {
        try
        {
            using var session = GameSession.Attach(offsetDirectory, forWriting: true, FetchIfAllowed);
            var checkpoint = Checkpoint.Require(Checkpoints, name);
            var calibration = CalibrationProfile.Require(
                Profiles, checkpoint.TrackName, checkpoint.VehicleName);

            var service = new PlacementService(session);
            var plan = service.Plan(checkpoint, calibration);

            Log($"plan residual {plan.ResidualMetres:F6} m");
            if (!plan.CanProceed)
            {
                foreach (var failure in plan.Live.Preconditions.Failures.Concat(plan.Target.Failures))
                {
                    Log($"blocked: {failure}");
                }
            }

            var outcome = service.Place(plan, OnProgress);
            lock (_gate)
            {
                _lastOutcome = outcome;
            }
            Log(outcome.Completed
                ? $"placed, error {outcome.HorizontalErrorMetres:F4} m"
                : $"failed: {outcome.Message}");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _phase = PlacementPhase.Failed;
                _phaseMessage = ex.Message;
            }
            Log($"error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called synchronously and in order by the placement service.
    ///
    /// Ordering still matters even without audio: the page renders a step trail
    /// from these phases, and a reordered report would show the driver a state
    /// the placement is not actually in.
    /// </summary>
    private void OnProgress(PlacementProgress progress)
    {
        lock (_gate)
        {
            _phase = progress.Phase;
            _phaseMessage = progress.Message;
        }
        Log($"[{progress.Phase}] {progress.Message}");
    }

    private void Log(string message)
    {
        lock (_gate)
        {
            _log.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        }
    }
}

