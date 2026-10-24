# Testing

The repeatable suite runs on Windows x64 with .NET 8 and the WebView2 Runtime.
From the repository root in PowerShell:

```powershell
.\build.cmd test -Configuration Release
```

This runs C# unit/HTTP integration tests and all five actual-WebView2 suites,
plus a production-page and layout smoke check. The isolated test host uses fake
menu responses and refuses native memory attachment, so an installed/running
LMU is not required. Test data uses unique temporary folders.

For focused C# checks or browser checks only:

```powershell
dotnet test InactiveReset.sln -c Release --filter "Category!=LocalGame"
.\build.cmd browser -Configuration Release
```

Run `.\build.cmd test-local -Configuration Release` for read-only mapped-image
and installed-menu compatibility checks. Missing fixtures are reported as skips;
those skips do not establish build compatibility. Direct `dotnet test` without
a filter still includes both categories.

The Redline suite covers builder/weather, map preview versus selection, checkpoint
file actions, preset management, Controls and summary resizing. Populated layouts
are checked at 1040×680 and 1280×800, including ten events and both practice buttons.
Assertions detect overflow and clipped actions; rendered screenshots are retained.
Backend/HTTP regressions cover map validation/cache and checkpoint file integrity.

`.\build.cmd package` runs the repeatable suite, publishes and packages the app,
then extracts the ZIP outside the checkout. Its smoke check runs CLI help/list
and the local HTTP server, reads its page/assets and checks required files and
exclusion of user checkpoints/build caches. It does not launch or write to LMU.

## Assertions and diagnostics

Keep exact assertions for safety contracts: original bytes/padding, restored
settings, refused writes, identity/build checks, and cancellation/session
ownership. For concurrent saves, accept either complete winner rather than
requiring a particular thread order.

UI checks focus on reachable controls, state transitions, visible errors and
small rounding allowances for layout. They wait for readiness with bounded
deadlines, not an exact response time. Do not add pixel-perfect baselines,
exact total test-count requirements, arbitrary coverage percentages, or fixed
millimetre limits across all game content. Wording changes alone should not
break a test unless the wording is a required instruction or API contract.

Results land under `artifacts/TestResults/`: C# TRX, per-suite browser JSON and
failure screenshots, and package smoke JSON. CI runs `build.cmd package` and
uploads diagnostics even after failure. WebView2 is provisioned if absent using
[Microsoft's documented Evergreen bootstrapper](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).

Live game release cases are in [the live checklist](../tests/LIVE_CHECKLIST.md).
Run only cases relevant to the changed capabilities/build and record their
conditions and limits separately from automated results.

## Coverage

EAC access checks verify that REST single-player edits work without any memory
attachment, memory actions are refused before attachment or writes, and
multiplayer still blocks session edits in both modes. Browser checks cover both
launch payloads, mode-switch refusal while running, capture/cycling without a
memory connection, disabled-action explanations, first-use launch choices and
recovery from EAC/online/closed-game states. The WebView2 runner captures these
screens for visual review. Dated live EAC checks are in [Local sessions](SESSIONS.md).
Menu/loading capture guards also prevent saving leftover SDK positions in either
launch mode, independently of whether old telemetry is still readable.
Official-surface regressions cover inheritance from earlier Practice, a later
explicit Qualifying surface overriding that baseline for Race, missing bases
remaining unresolved, legacy-cache refresh and visible starting-grip labels.
Responsive-edit checks cover coalesced rapid clicks, direct clock/default entry,
continued editing during delayed writes, preservation of the actual focused
editor, stale refresh rejection, retry/discard and loss/recovery of menu access.
Core checks verify fresh numeric deltas, idempotent retries, all four native
clock fields, unrelated-preset preservation, input bounds and refused readback.

Official-session checks cover authenticated read-only requests, exclusion of
unexpected event types, alternating configurations within one series, atomic
cache persistence, credential exclusion, partial/total failure and successful
serialization through the real HTTP endpoint. Browser checks cover the simplified
Overview, distinct Qualifying/Race presets, catalog/weather mapping, failed-sync
retention, review without writes and the apply-to-session-page flow. Core tests
check Practice time/surface validation, preservation of unrelated preset fields
and refused readback. Dated live menu checks are recorded in [Lab](LAB.md#official-scheduled-sessions).

| Area | What the checks establish |
| --- | --- |
| Placement maths | Inverse/forward consistency, indexed layout, and regression values from recorded measurements |
| Capture files | Checkpoint serialization, tyre state round-trip, wheel order, temperature units, damage indicators, SDK local velocity, speed conversion and older captures/layouts without speed |
| Checkpoint storage | Unsafe/reserved filenames, concurrent duplicate saves, complete saved payloads, and individual malformed-file diagnostics while valid captures remain available |
| Checkpoint diagnostics | Production checkpoint projection serializes malformed-file warnings with valid library entries; actual-browser Lab checks retain those warnings in Activity and Issues-only views |
| HTTP integration | Actual routes/response serialization, required UI assets, readable invalid-request errors, concurrent preference saves, operation exclusion, cancellation, shutdown and Practice gates using an isolated menu/backend |
| Reset update compatibility | Recorded-layout comparison; code/global relocation and body-operand changes; duplicate/missing consumers; signed displacement decoding; full reset-cache repair/refusal; runtime engine setter validation. Synthetic images validate discovery, not live compatibility with an unseen patch |
| Direct reset | Linked-body pose/inertia transforms, SDK-origin conversion, cached Euler convention, separate collision-body motion, bulk tyre-record flag extraction and truncated-layout rejection, on-track/garage gates, observed-travel checks including the recorded explosion/pit recovery, and hotkey dispatch; live long-distance reliability remains unverified |
| Rolling restoration | Body/wheel payloads through 350 km/h without an upper ceiling, orientation conversion, loaded gear ratio/rev-limit calculations, pause ownership and partial-failure handling, handoff timing, assist preference cleanup, and matching capture-car gates; offline checks do not establish live compatibility |
| Capture selection | Exact IDs, case-insensitive names on the loaded track, same-name captures from different cars, and ambiguity rejection |
| Vehicle identity | Same-track pose reuse, loaded-car calibration checks, and separate cross-car learned corrections |
| Learning | Correction calculations, build separation, and rejection of unsuitable samples |
| Discovery | Structural and instruction-anchor matching, retuned/relocated tuning storage, moved pit-speed fields and branch destinations, disagreement, section bounds, and ambiguity rejection |
| Indexed placement gate | Accepts either normal-slot override state; rejects changed readers, profile, destination mode, pit index, table pointer, or planned entry |
| Offset cache | Full-profile comparison with current-code derivation, repair of altered fields/model data, invalidation on code changes beyond the transform probe, and preservation after failed capture/discovery/probe checks |
| Spot transaction | Payload size, preservation of padding, rollback after partial writes, exceptional cleanup, idempotent restoration, and retry after restore failure |
| Cleanup | Cancellation waits for a safe rule-restore point; no pit-clear timeout; garage/session-end handling; stale spot writes skipped; failed suppression rolls back the exact original value |
| Exceptional cleanup | A failure after Drive waits for safe rule restoration; session replacement during the wait and unreadable safety state do not receive restoration writes |
| Rules and reports | Profile fields, lap-state predictions, and outcome messages |
| Launch detection | Install/manifests and launch-selection logic |
| Local session API | Menu and Practice gates, owned-content selection, relative steps, assist ranges, generated save loading, preset preservation, recovered routes/ports after package changes, refusal of ambiguous/unsupported request helpers, and refusal outside the ready main-menu state |

`OffsetProfileTests` owns profile parsing, confidence and placement-validation
gates, plus the container-relative lap-field contract across both shipped
profiles. Its tracked JSON fixtures are copied into the test output explicitly;
generated `.auto.json` caches are excluded. `LapValidityTests` covers lap behavior
without depending on a particular build's numeric addresses.

Cache failure and repair cases use small portable fixtures. The local cache
integration check covers executable-code changes outside the transform probe.
Recorded placement values remain independent regression evidence; demonstrations
of superseded incorrect models and assertions about fixture constants have been
removed. Preserve measured inputs/outputs and failure paths when consolidating
tests, rather than replacing them with only inverse/forward round trips.

Transaction tests run the production `SpotWriteTransaction` logic with an
injected verified-write operation backed by a byte buffer. Production `Begin`
still performs the process, hash, writable-page, and original-byte checks before
using `ProcessMemory.WriteVerified`. The buffer tests do not exercise Windows
memory APIs or those attachment checks.

`TyreStateTests` checks SDK wheel order, Kelvin-to-Celsius conversion, unmodified
wear/compound metadata, checkpoint round-tripping and the requested class compound
policy. These tests do not establish that tyre physics can be changed in game.

`TyreResetTests` checks input limits on every wheel, FL/FR/RL/RR request ordering,
optional temperature per wheel, legacy uniform requests, snapshots before arming,
per-wheel condition/temperature verification, inventory percentage encoding, rollback
after partial writes, cancellation/session/Drive boundaries, and numeric thermal
verification. Live measurements are recorded separately in [Tyres](TYRES.md).

## Local game captures

The automatic-discovery tests check four recorded builds independently:
`D9B92CA9`, `29CE422A`, `66942337`, and `0F6DCAC1`. Each build has its own test
and skip reason. A missing image skips only that build's check. The separate
discovery/cache integration test needs the `66942337` image.

Expected paths are `artifacts/LMU_runtime_<build>.bin`. The baseline image can
instead be supplied through `INACTIVE_RESET_BASELINE_DUMP` for `0F6DCAC1`.
These captures are private mapped game images and are not distributed or stored
in Git. CI can run the portable maths, cache, transaction, and reporting tests
without them; missing capture checks remain visibly skipped.

For a focused run after building:

```powershell
dotnet test InactiveReset.sln -c Release --no-build --filter "FullyQualifiedName~SpotWriteTransactionTests|FullyQualifiedName~AutomaticOffsetCacheTests"
```

## Browser coverage and live limits

`LmuApiRoutesTests` includes a read-only installed-menu check, independently
skipped when LMU cannot be located. It parses the current installed bundle and
settings without launching LMU or sending API requests. Portable synthetic
fixtures cover route/port changes, dynamic parameters, comments/strings/regex
decoys, payload and method changes, and package replacement. These checks do not
establish live compatibility of a new API contract or launch sequence.

`tests/session-ui.html` checks the current production workspace in an iframe
with a fake API. Serve the checkout root over HTTP and open the page. It covers
startup and busy/online gates, owned content, step semantics, race units,
dependent controls, search focus, section switching, failed-save readback and
automatic main-menu readiness without a confirmation checkbox.
It performs no game writes. The checked-in WebView2 runner executes it during
`build.cmd test`. See [Local sessions](SESSIONS.md) for dated live evidence.

`tests/lab-ui.html` checks the Lab UI with a fake API, including plan review,
relative setting changes, cancellation and stopping after partial failure. It
is included in the browser runner and performs no game writes.

From a source checkout, `tests/checkpoint-ui.html` provides dependency-free
selector checks of `Practice.js` against the current HTML with a small DOM stub. Serve the repository root over
HTTP and open `tests/checkpoint-ui.html` in a browser to run them. They cover
track filtering, name-only rows with distinct IDs, cross-car selection, polling,
track changes, menus, busy state, tyre requests, Activity diagnostics and result
details, preparation choices across workspace navigation, and preservation of
destination/log scroll positions. They do not check visual layout
or native browser rendering by themselves. The browser runner also loads the
real embedded HTTP page with a controlled backend, verifies its actual
initialization/library/Activity filters and disconnected action gates, and
checks both styles at representative window sizes. This catches composition
and response bugs that the script/DOM stub cannot.

The complete `PlacementService.Place` sequence still depends on a running game.
The suite does not establish successful live placement, penalty suppression,
pit-state clearing, lap timing, or the native window's closing behaviour. Tests of
outcome messages do not establish that the game produced those outcomes.

Use the live verification procedure in [Contributing](../CONTRIBUTING.md) for
process and engine behaviour. Record which dump cases ran and which were
skipped when reporting results.

Maintainer speed-probe tests cover forward/reverse wheel-spin sign, rotated
axles and world angular momentum, finite/dimension/radius/inertia/orientation
gates, and released-pedal/stopped-wheel requirements. They do not establish
coherent application to running physics or successful rolling placement. Pause
mode tests additionally reject advancing/non-finite SDK time, absent or changed
pause/player/replay/pit fields and conflicting probe modes. A single paused live
prototype and manual Resume are recorded in [Status](STATUS.md).

Rolling-start sequence tests cover preparation failure cleanup, complete
verification before Resume, keeping partial motion updates paused, capture/build/
vehicle eligibility, and exclusion from stationary learning. Handoff checks
cover the minimum delay, sustained readiness, readiness loss, timeout, cancelled
waits and driving-control/replay/pit-state gates. Hold Brakes coverage checks
original On/Off preservation, ambiguous write failures, rejected changes,
restoration failures, cancellation cleanup, context changes and the exact
Practice-only options payload. The checkpoint
DOM-stub harness checks automatic speed/gear restoration, the standing shortcut, busy
state and unsupported build/capture/car gates. These are not live placement tests.
