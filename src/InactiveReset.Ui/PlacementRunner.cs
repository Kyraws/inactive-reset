using System.Text.Json;
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

    // Kept beside the outcome so the report can judge staleness later, when the
    // session and plan that produced it are long gone.
    private CalibrationProfile? _lastCalibration;
    private string? _lastBuildSha256;
    private Task? _running;
    private CancellationTokenSource? _placementCancellation;
    private bool _stopping;
    private readonly LmuSessionClient _sessionMenu = new(dataDirectory: dataDirectory);

    private string Checkpoints => Path.Combine(dataDirectory, "checkpoints");
    // Both calibration directories are resolved from the data directory itself;
    // see CalibrationProfile.LoadAllForData.

    // ---- state -------------------------------------------------------------

    public JsonObject State()
    {
        var state = new JsonObject();

        try
        {
            using var session = GameSession.Attach(offsetDirectory, forWriting: false);
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
                    ["display"] = rule.Display,
                    ["resolved"] = rule.Resolved,
                    ["effect"] = rule.Resolved
                        ? rule.Effect
                        : "address not re-derived for this build - not read",
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
                    ["on"] = limits is not null && limits.Resolved && limits.Value != 0,
                    // An unresolved address is not a known "off": the control must
                    // be unusable, not confidently wrong.
                    ["known"] = limits is not null && limits.Resolved,
                },
            };

            // The pit-speeding penalty is NOT a toggle. Placement temporarily
            // disables Flag Rules, then restores them when pit state clears.
            // Show the live value, not an assumption about the last write.
            //
            // NOTE: this is the penalty, not the car's pit limiter, which this
            // tool never touches. See CONTEXT.md; the two were confused for as
            // long as this control was labelled "Pit Limiter".
            state["pitSpeedingPenalty"] = new JsonObject
            {
                ["label"] = "Pit-speeding penalty",
                ["detail"] = "Flag Rules temporarily off during placement, restored after pit state clears",
                ["on"] = pit is not null && pit.Resolved && pit.Value != 0,
                ["known"] = pit is not null && pit.Resolved,
            };

            state["lapValidity"] = LapValidity_(session);
            try
            {
                var live = new LiveStateReader(session);
                var owner = live.ReadControlOwner(live.ResolveSlotIndex());
                if (owner is 0 or 1) state["inGarage"] = owner == 1;
            }
            catch (Exception ex) when (ex is MemoryAccessException or GateException) { }
            try
            {
                state["tyreRules"] = JsonSerializer.SerializeToNode(
                    TyrePhysicsReader.Read(session, offsetDirectory),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex) when (ex is MemoryAccessException or OffsetProfileException or StaleOffsetException or GateException or InvalidOperationException or FormatException or OverflowException or IOException)
            {
                state["tyreRulesError"] = ex.Message;
            }
        }
        catch (Exception ex)
        {
            state["connected"] = false;
            state["error"] = ex.Message;

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
                ["lines"] = OutcomeLines(_lastOutcome, _lastCalibration, _lastBuildSha256),
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
    private static JsonArray OutcomeLines(
        PlacementOutcome outcome, CalibrationProfile? calibration, string? buildSha256)
    {
        var penalty = PlacementReport.ForPenalty(outcome);
        var lines = penalty is null
            ? PlacementReport.For(outcome, calibration, buildSha256)
            : [penalty, .. PlacementReport.For(outcome, calibration, buildSha256)];

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
                ["tyres"] = JsonSerializer.SerializeToNode(snapshot.Tyres,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
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
                ["id"] = checkpoint.SelectionId(Checkpoints),
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
        foreach (var calibration in CalibrationProfile.LoadAllForData(dataDirectory))
        {
            array.Add(new JsonObject
            {
                ["track"] = calibration.TrackName,
                ["vehicle"] = calibration.VehicleName,
                ["forwardDistance"] = calibration.ForwardDistance,
                ["verticalOffset"] = calibration.VerticalOffset,
                ["lateralOffset"] = calibration.LateralOffset,
                ["locked"] = calibration.Locked,
                ["advisory"] = calibration.Advisory,
                ["build"] = calibration.ExecutableSha256 is { Length: >= 8 } hash ? hash[..8] : null,
            });
        }
        return array;
    }


    public Task<JsonObject> SessionSetupAsync() => _sessionMenu.SetupAsync();
    public Task<JsonObject> SessionCatalogAsync() => _sessionMenu.CatalogAsync();
    public async Task<JsonObject> SessionNavigationAsync()
    {
        var result = await _sessionMenu.NavigationAsync();
        var game = GameLauncher.Running();
        result["protected"] = game.Protected;
        result["pid"] = game.ProcessId;
        lock (_gate)
        {
            result["operationBusy"] = _running is { IsCompleted: false };
            result["operationMessage"] = _phaseMessage;
        }
        return result;
    }

    public Task<JsonObject> SessionActionAsync(JsonObject body)
    {
        TaskCompletionSource<JsonObject> completion;
        CancellationToken token;
        lock (_gate)
        {
            if (_stopping) throw new GateException("The app is closing.");
            if (_running is { IsCompleted: false }) throw new GateException("Wait for the current operation to finish before changing sessions.");
            var game = GameLauncher.Running();
            if (!game.Attachable) throw new GateException("Launch LMU for local play without EAC before setting up a session.");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _running = completion.Task;
            _placementCancellation?.Dispose();
            _placementCancellation = new CancellationTokenSource();
            token = _placementCancellation.Token;
        }
        _ = RunSessionActionAsync(body, completion, token);
        return completion.Task;
    }

    private async Task RunSessionActionAsync(JsonObject body, TaskCompletionSource<JsonObject> completion, CancellationToken cancellation)
    {
        var starting = body["action"]?.GetValue<string>() == "start";
        try
        {
            JsonObject result;
            if (starting)
            {
                lock (_gate) { _log.Clear(); _lastOutcome = null; }
                await _sessionMenu.StartAsync(body["menuConfirmed"]?.GetValue<bool>() == true,
                    message => OnProgress(new(PlacementPhase.Gating, message)), cancellation);
                OnProgress(new(PlacementPhase.Done, "Session loaded. Switch to LMU and press Drive."));
                result = new() { ["ok"] = true };
            }
            else if (body["action"]?.GetValue<string>() == "menu")
            {
                await _sessionMenu.ReturnToMenuAsync(cancellation);
                result = await _sessionMenu.SetupAsync(cancellation);
            }
            else result = await _sessionMenu.ChangeAsync(body, cancellation);
            completion.SetResult(result);
        }
        catch (OperationCanceledException)
        {
            if (starting) OnProgress(new(PlacementPhase.Cancelled, "Monitoring stopped. A session already accepted by LMU may still load."));
            completion.SetResult(new() { ["error"] = "Operation cancelled. Check LMU before starting again." });
        }
        catch (Exception ex)
        {
            if (starting) OnProgress(new(PlacementPhase.Failed, ex.Message));
            completion.SetResult(new() { ["error"] = ex.Message });
        }
    }

    // ---- actions -----------------------------------------------------------

    public JsonObject SetRules(JsonObject body)
    {
        lock (_gate)
        {
            if (_stopping || _running is { IsCompleted: false }) throw new GateException("Wait for the current operation before changing rules.");
            _sessionMenu.RequirePracticeAsync().GetAwaiter().GetResult();
            return SetRulesCore(body);
        }
    }

    private JsonObject SetRulesCore(JsonObject body)
    {
        var enable = body["enable"]?.GetValue<bool>() ?? false;
        var pit = body["pitSpeeding"]?.GetValue<bool>() ?? false;
        var limits = body["trackLimits"]?.GetValue<bool>() ?? false;

        using var session = GameSession.Attach(offsetDirectory, forWriting: true);
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
        var tyreOptions = ReadTyreOptions(body);
        var name = body["checkpoint"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckpointException("a checkpoint name is required");
        }

        lock (_gate)
        {
            if (_stopping) return new JsonObject { ["error"] = "the app is closing" };
            if (_running is { IsCompleted: false })
            {
                return new JsonObject { ["ok"] = false, ["error"] = "a placement is already running" };
            }
            _log.Clear();
            _lastOutcome = null;
            _lastCalibration = null;
            _lastBuildSha256 = null;
            _phase = PlacementPhase.Gating;
            _phaseMessage = "starting";
            _placementCancellation?.Dispose();
            _placementCancellation = new CancellationTokenSource();
            var token = _placementCancellation.Token;
            _running = Task.Run(() => RunPlacement(name, tyreOptions, token));
        }
        return new JsonObject { ["ok"] = true };
    }

    private static TyreResetOptions? ReadTyreOptions(JsonObject body)
    {
        if (body["tyres"] is not JsonObject value) return null;
        using var document = JsonDocument.Parse(value.ToJsonString());
        return TyreResetOptions.FromJson(document.RootElement);
    }

    public JsonObject StartTyres(JsonObject body)
    {
        var options = ReadTyreOptions(body) ?? new TyreResetOptions();
        lock (_gate)
        {
            if (_stopping) return new JsonObject { ["error"] = "the app is closing" };
            if (_running is { IsCompleted: false })
                return new JsonObject { ["error"] = "an operation is already running" };
            _log.Clear();
            _lastOutcome = null;
            _phase = PlacementPhase.Gating;
            _phaseMessage = "preparing tyres";
            _placementCancellation?.Dispose();
            _placementCancellation = new CancellationTokenSource();
            var token = _placementCancellation.Token;
            _running = Task.Run(() => RunTyres(options, token));
        }
        return new JsonObject { ["ok"] = true };
    }

    private void RunTyres(TyreResetOptions options, CancellationToken cancellation)
    {
        try
        {
            _sessionMenu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
            using var session = GameSession.Attach(offsetDirectory, forWriting: true);
            using var tyres = new TyreResetPreparation(session, offsetDirectory, options);
            var reader = new LiveStateReader(session);
            var slot = reader.ResolveSlotIndex();
            cancellation.ThrowIfCancellationRequested();
            tyres.Stage();
            OnProgress(new(PlacementPhase.Armed, "TYRES PREPARED - press Drive"));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (reader.ReadControlOwner(slot) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                tyres.CheckContext();
                if (timer.Elapsed.TotalSeconds > 120) throw new GateException("timed out waiting for Drive; pending tyre records restored");
                Thread.Sleep(20);
            }
            tyres.ObserveDrive();
            OnProgress(new(PlacementPhase.Settling, "checking fitted tyres"));
            Thread.Sleep(400);
            var result = tyres.AfterDrive();
            OnProgress(new(result.Verified ? PlacementPhase.Done : PlacementPhase.Failed, result.Message));
        }
        catch (OperationCanceledException)
        {
            OnProgress(new(PlacementPhase.Cancelled, "cancelled; pending tyre records restored where the garage session is unchanged"));
        }
        catch (Exception ex) { OnProgress(new(PlacementPhase.Failed, ex.Message)); }
    }

    public JsonObject CancelPlace()
    {
        lock (_gate) _placementCancellation?.Cancel();
        return new JsonObject { ["ok"] = true };
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            _placementCancellation?.Cancel();
            return _running ?? Task.CompletedTask;
        }
    }

    private void RunPlacement(string name, TyreResetOptions? tyreOptions, CancellationToken cancellation)
    {
        try
        {
            _sessionMenu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
            using var session = GameSession.Attach(offsetDirectory, forWriting: true);
            SessionIdentity? live = null;
            try
            {
                var offsets = SharedMemoryOffsets.Load(Path.Combine(offsetDirectory, "shared-memory.json"));
                using var reader = new SharedMemoryReader(offsets);
                live = SessionIdentity.From(reader);
            }
            catch (SharedMemoryException)
            {
            }

            var checkpoint = Checkpoint.Require(Checkpoints, name, live);
            var identity = live ?? new SessionIdentity(checkpoint.TrackName, checkpoint.VehicleName);
            identity.RequireMatches(checkpoint);
            var calibration = CalibrationProfile.Find(dataDirectory, identity.TrackName, identity.VehicleName)
                ?? CalibrationProfile.Placeholder(identity.TrackName, identity.VehicleName);
            var learned = new LearnedRestStore(dataDirectory).Load(session.ExecutableSha256,
                identity.TrackName, identity.VehicleName, checkpoint.LearningKey(identity.VehicleName));

            var service = new PlacementService(session);
            var plan = service.Plan(checkpoint, calibration, learned, live);

            Log($"plan residual {plan.ResidualMetres:F6} m");
            if (!plan.CanProceed)
            {
                foreach (var failure in plan.Live.Preconditions.Failures.Concat(plan.Target.Failures))
                {
                    Log($"blocked: {failure}");
                }
            }

            using var tyres = tyreOptions is null ? null : new TyreResetPreparation(session, offsetDirectory, tyreOptions);
            var outcome = service.Place(plan, OnProgress, cancellation: cancellation, tyres: tyres);
            lock (_gate)
            {
                _lastOutcome = outcome;
                _lastCalibration = calibration;
                _lastBuildSha256 = session.ExecutableSha256;
            }
            Log(outcome.Completed
                ? $"placed, error {outcome.HorizontalErrorMetres:F4} m"
                : $"{(outcome.Cancelled ? "cancelled" : "failed")}: {outcome.Message}");
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
