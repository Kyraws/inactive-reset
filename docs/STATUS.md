# Status and known limitations

Reviewed on 2026-10-07. This is the current technical status, not a compatibility
promise. [Architecture](ARCHITECTURE.md) describes the implementation;
[Placement and rules](PLACEMENT.md) records the execution sequence.

## Validation evidence

EAC access and first-use guidance were checked on 2026-10-23, build 14200 /
`66942337`. Protected launch, REST local-session editing/start, official-event
refresh and SDK checkpoint capture passed; internal memory actions remain
blocked. Original game settings restored exactly. Automated and rendered checks
cover both launch choices, multiplayer locks and disabled-action explanations,
including stale SDK data at the menu. See [Local sessions](SESSIONS.md) for scope;
these checks did not join or modify an online race.

- Historical ordinary-path placement measurements, including correction of the
  lateral-offset model, are retained in the [heading investigation](archive/HEADING_BUG.md).
  Those results do not establish accuracy for every car, setup, track, or build.
- The [66942337 investigation](archive/66942337_UPDATE_CHECK.md) records successful
  attachment and read-only planning after integration. Its live placement used a
  maintainer experimental command and manually supplied constants. The record
  does not establish that the integrated production placement was live-tested.
- Automated tests cover arithmetic, profile handling, discovery, cache rejection,
  indexed selection gates, spot-transaction rollback and restoration, and reporting.
  Some dump-based tests need local runtime captures and can be skipped without
  them. Neither a matching instruction pattern nor a passing test suite proves
  live placement behaviour.
  Each recorded build now has an independent skip. See [Testing](TESTING.md).

## Priority fixes

| Priority | Finding | Evidence and next step |
| --- | --- | --- |
| High | Concurrent application instances can compete over game writes | Runner locks are process-local; add a shared writer lock before allowing multiple windows or CLI writers against the same game |
| High | UI placements do not save learning or observations | `PlacementRunner.RunPlacement` loads learned values but never calls `RestLearning.Apply`; align orchestration with the CLI after validating settled motion |
| Medium | Capture and measured rest can include motion | Shared-memory reads are not a single locked snapshot; rest is sampled after a 400 ms delay with player control; validate capture coherence and learning while moving |
| Medium | A manual ordinary-path calibration can be reused across builds | `PlacementService.Plan` only requires matching build for indexed placement; review staleness policy |
| Low | Invalid CLI sector input silently falls back to the final sector | `Program.Place` uses the default on failed parsing; reject malformed or missing values |
| Low | Rule identity depends on display text | `PlacementRunner.State` selects rules with label substrings; separate identity from presentation if changing this flow |
| Low | Each UI state poll reattaches and hashes the executable | `PlacementRunner.State`; measure before optimising, retaining write-time checks |
| Low | App build reports a WindowsBase reference conflict | Release solution build emits MSB3277 through WebView2's WPF assembly, although the app uses WinForms; review unused package references |

## Audit fixes (2026-10-16)

CLI placement, rule and lap writes now use the shared active local Practice
check. Both placement front ends require live track/car identity. Checkpoint
names are validated as Windows filenames and confined to their save folder;
library captures use temporary files and refuse concurrent replacement.
Malformed checkpoints are skipped individually and reported in Activity or CLI
listing. Exception cleanup waits for a safe Flag Rules restoration point and
rechecks session ownership after waiting. Scheduled UI polling skips ticks
while a refresh is active, keeping slow responses visible without overlapping
background requests. Regression checks cover these changes.

Live audit validation on 2026-10-17 used direct-launch LMU 1.4.2.0 (`66942337`),
Barcelona and United Autosports 2026 #23:ELMS. A standing garage placement
returned within 4.3 mm horizontally and 1.1 mm vertically of the capture.
Cancellation before Drive and cleanup after Drive/garage return restored the
full original spot entry, padding and readable rules. Main-menu writes and a
wrong-track checkpoint were refused. Moving restoration and deliberate live
memory failures were not tested in this pass.

Actual WebView2 validation found and fixed diagnostic-response serialization
and missing warnings in the Lab Activity/Issues-only views. All four browser
suites passed (182 checks); Original/Redline layouts and the production page
against the live game were inspected. These checks cover the recorded setup,
not every game build, vehicle or track.

## Direct reset validation (2026-10-16)

An experimental on-track reset and Ctrl+Alt+R hotkey are implemented with a locally discovered and verified reset layout; live evidence below is for the researched `66942337` build. Short stopped-car tests succeeded, but the first moving long-distance reset fell below the road and triggered automatic pit recovery. The repair now mirrors vehicle cached pose, road/wheel cache invalidation and the separate collision body, and observes two seconds after resume instead of reporting success from one frame. The physical repair rolled smoothly in a moving roughly 819-metre reset after explicit one-byte pause recovery. Its first validation wrongly rejected LMU refreshing the road pointer while physics was paused. The corrected cache/rounding checks pass 146 focused tests. The final automatic Ctrl+Alt+R test returned from 110.36 km/h in second gear, roughly 525 metres away, to the saved 39.86 km/h in first gear. It resumed automatically after 763 ms; passive telemetry retained road contact and control for five seconds, and the owner confirmed smooth rolling. The timing-optimized build subsequently passed an owner-operated Barcelona test in Team Qatar by Iron Lynx 2026 #62:ELMS at a captured 126.68 km/h in second gear. The app reported 125.12 km/h, automatic resume, a 470 ms pause and completion after its observation; the owner confirmed smooth rolling and a shorter pause. Fixed waits fell from 200 to 50 ms, reads are batched and menu requests overlap read-only work; all per-write checks remain. 149 focused tests pass. Other combinations, repeated resets and faster target speeds remain unverified. See [Placement](PLACEMENT.md#experimental-reset-on-track).

## Offset resilience update (2026-10-16)

Reset/speed/gear paths now share an immutable profile derived from 79 native operands, 102 agreeing references and two drivetrain contracts. Resolver version 5 includes this optional capability in the existing fully validated cache. All production reset address literals and fixed-build checks have been replaced; engine speed setters are decoded and validated at runtime. Missing or changed reset patterns disable that feature while leaving successful placement discovery available. Signed one-byte displacements are validated rather than interpreted as large positive offsets.

The full suite passed 426 tests; one older-capture test was skipped because its capture was unavailable. All 56 checkpoint UI checks passed. Tests cover moved code/globals, changed body operands, altered cache fields/probes/hash/version/module size, changed live probes, and missing/duplicate signatures. This refactor has not yet been live-tested; LMU was closed. Major engine/SDK changes still need compatibility work, and synthetic relocations are not a guarantee for an unseen patch.

## Behaviour that still needs live checks

The 2026-10-10 API/cache changes add installed-menu route and port recovery and
whole-profile validation against current mapped code. The current installed menu
passes read-only helper discovery; synthetic tests cover renamed routes, package
replacement and cache alteration. No live session changes were performed for
this update. Named helper/request shapes and response schemas remain support
boundaries; a changed engine or launch sequence still needs explicit validation.

The 2026-10-10 discovery changes resolve tuning addresses from search/yaw
consumers and follow changed pit-speed field and branch displacements. Offline
checks cover the three available mapped builds and a retuned/relocated 66942337
copy. Instruction shapes and tuning-field relationships remain supported-layout
assumptions; this is not evidence of compatibility with an unseen patch or live
placement after an engine change.

The new cancellation/close workflow through live garage and session transitions;
lap timing near start/finish; sector splits after a forced sector
write; capture at speed; and placement/learning across varied cars, tracks,
setups, and surfaces. Record full executable hash and test conditions for each
result rather than inferring support from a version string.

Checkpoint speed capture/display and automatic experimental motion restoration are
implemented. Normal teleport requires a completely verified reset layout,
straight moving captures (at least 0.5 km/h) and the capture vehicle. It uses an owned
physics pause to apply linked-body velocity, wheel rotation and captured gear/engine state, verifies the
complete state, then resumes. Garage motion restoration waits for released applied pedals after a minimum
three-second handoff delay, then uses the direct reset to handle slope creep
and clear inherited pit state. It temporarily
releases/restores an enabled Hold Brakes preference through LMU’s options API.
It excludes rolling outcomes from stationary
learning and leaves possible partial motion updates paused for recovery.

Separate native experiments on the Ferrari 296 LMGT3 at Monza progressed from
a cached-motion write with no useful movement, through unpaused linked-body
motion with an initial speed deficit and a partial-write failure, to complete
manual-pause and automatic-pause batches. The automatic prototype verified all
514 bytes and resumed without driver pause input; first SDK speed was 8.54 km/h
for an 8.94 km/h request, peak 8.56 km/h, final 7.72 km/h after about two seconds
and 4.47 m travelled with released pedals. The owner confirmed smooth movement.
A subsequent EXITING-state experiment verified all 514 bytes, automatic resume
and smooth movement with pit state unchanged. The first integrated handoff
was blocked before motion writes by EXITING; the delayed version was blocked
by full applied brake while physical brake input was zero. LMU’s Hold Brakes
was On. With it manually Off, the owner confirmed smooth integrated restoration.
The subsequent automatic workaround was tested live with Hold Brakes initially
On: the app verified temporary release before Drive, restored captured speed
after the control wait, and restored Hold Brakes to On immediately afterward.
The settings API confirmed On and the owner confirmed smooth movement.
Exact speed, drivetrain/tyre-contact state and broader support remain open.
See [Developer tools](../tools/README.md).

Captured tyre state, fuel and general damage restoration are not implemented.
The compact UI provides per-wheel tyre settings; live readings remain in the
game, and protection/discovery diagnostics appear in Activity. Tyre reset addresses are discovered from the current executable,
code-probed, cached by full hash, and refused when the engine structure changes.
See [Tyres](TYRES.md) for verified fields, engine setters and remaining
thermal/damage investigation.
New captures store surface, carcass and inner-layer tyre temperatures in Celsius,
raw SDK wear (0..1), fitted compound names/indices and vehicle class. SDK surface
temperature channels are physical left/centre/right, not inside/outside. The wear
value is not presented as a remaining-tread or grip percentage. Older captures
without tyre data remain supported. A separate garage reset prepares the four
fitted tyres for Drive with independent FL/FR/RL/RR condition and optional initial
temperature. Per-wheel settings have automated validation but await a live test
using different values on each wheel. It is opt-in during placement and keeps fitted compounds. Fresh
condition and 70°C internal layers were measured live on build 66942337; surfaces
cool immediately afterward. The shared-memory output itself remains read-only.
The requested compound policy is Medium/Wet for GT3, LMP2 and LMP3, and
Soft/Medium/Hard/Wet for GTE and Hypercar. Actual compound availability and engine
indices must be resolved for the loaded vehicle before implementing selection.
Checkpoints store pose and metadata; placement does not resume the captured
vehicle condition. Motion restoration is limited to the experimental option above.

The arbitrary upper speed ceiling has been removed. Offline payload checks cover
speeds through 350 km/h; live motion tests have reached 148.52 km/h. The new
gear/engine extension passed a first-gear placement test: requested 39.86 km/h,
app observed 38.25 km/h and gear 1 at 2682 RPM. The owner confirmed smooth
rolling; Hold Brakes returned On and Flag Rules returned to 2 in the garage.
Its first attempt was rejected before motion writes by a mismatched stability
snapshot; both snapshots now use the same body/wheel/gear reader. Hold Clutch
was On and filtered clutch remained 1, so sustained mechanical clutch coupling
was not established in that test. A subsequent third-gear test at 148.52 km/h restored 144.83 km/h
with filtered clutch engaged, five seconds of released pedals and owner-confirmed
smooth rolling. Peak sampled engine speed over those five seconds was 5,968 RPM
against the loaded 8,000 RPM limit; an initial RPM transient remains. Pit state
cleared and original Hold Brakes/Flag Rules were restored. Other gears remain
unverified.
The integrated 39.86 km/h capture restored an initial 38.42 km/h, with smooth
coasting confirmed by the owner and original Flag Rules restored in the garage.
The 58.19 km/h capture restored an initial 56.32 km/h; the owner confirmed
smooth rolling. Pit state then cleared normally, original Flag Rules were
verified restored, and Hold Brakes read On afterward.
The integrated 19.55 km/h capture restored an initial 18.79 km/h, followed by
smooth neutral coasting with released pedals; the owner confirmed smooth motion.
Original Flag Rules were verified restored on garage return. Higher speeds
remain unverified, and the roughly 3–4% initial speed loss remains unexplained.
