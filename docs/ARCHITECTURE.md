# Architecture

See [Status](STATUS.md) for known gaps and [research evidence](research/README.md)
for build-specific measurements. The solution targets Windows x64 / .NET 8.

## Components

| Location | Responsibility |
| --- | --- |
| `src/InactiveReset.Core` | Memory access, discovery, telemetry, checkpoint/calibration formats, placement, motion, tyres, rules and LMU menu API |
| `src/InactiveReset.Ui` | Loopback HTTP server, embedded Redline workspace, maps/preset/library stores and background operation runner |
| `src/InactiveReset.App` | Windows Forms/WebView2 host, keyboard and Raw Input bindings |
| `src/InactiveReset.Cli` | Console commands and optional browser server |
| `tools/InactiveReset.Reanchor` | Maintainer-only analysis, mapped-module capture and experimental probes |
| `tests/` | C# regressions/integration tests, WebView2 suites, package smoke and live checklist |
| `tools/` | SDK layout generation, discovery-resource generation, Ghidra evidence and native observers |

The UI and CLI share Core and `PlacementReport`, but orchestrate independently.
CLI placement saves `RestLearning`/observations; UI placement currently only loads
learning. Front-end behavior must be verified separately.

## Launch, API and telemetry

`GameLauncher` locates LMU through an explicit/saved override or Steam manifests.
Steam must be running. Direct launch supports local play; protected launch uses
`start_protected_game.exe` for online access. Close LMU before changing modes.

`LmuSessionClient` recovers supported API routes from installed `Bin/UI.zip` and
reads the port from `Settings.JSON`. Requests use loopback with no proxy/redirects.
Single-player setup works in either launch mode at the ready main menu, without
memory attachment. Startup-prompt dismissal remains manual; there is no confirmation
checkbox. Unsupported or ambiguous request helpers fail closed. See [Sessions](SESSIONS.md).

SDK telemetry is read-only and independent of memory attachment. Capture requires
an active session and valid current pose; menu/loading guards reject leftover data.
`GameSession.Attach` refuses machine-wide EAC/protected-launch detection, checks
disk/mapped PE identity, hashes the executable and validates discovered code probes.
Memory actions additionally require active local single-player Practice. EAC absence
alone is insufficient. Planning requires live track/car identity.

## Discovery and compatibility

`AutomaticOffsets` derives an immutable profile from the current mapped module:
structural resolvers identify tables, indices, stride, ownership, dimensions,
placement tunables and rules; embedded instruction anchors resolve other fields.
Missing, ambiguous or disagreeing evidence refuses use. No profile download occurs.

Caches in `offsets/<full-executable-sha256>.auto.json` are compared in full with the
verified derivation, including model/reset metadata. Matching build, resolver version
and executable-section fingerprint permit in-memory reuse. Tampered fields are
repaired; capture/discovery/probe failures preserve the old disk cache and refuse
attachment. New caches replace atomically. The `placementValidated` marker records
provenance; production automatic attachment does not use it as a live-test gate.

Optional `MotionLayout` independently derives 79 reset values from 102 operand
references and two drivetrain contracts. It validates agreeing consumers and
supported relationships; native engine-speed setters are decoded and verified
at runtime. Missing reset evidence disables reset/speed restoration independently.
Tyre discovery has its own verified code operands/probes and full-build cache.
The SDK ABI is generated separately from installed public headers. Relocation
checks do not establish unseen-patch physics semantics or live compatibility.

Historical profiles/layouts live in `tests/Fixtures/offsets/`. They are regression
evidence, never automatic fallback addresses. `offsets/shared-memory.json` remains
the required runtime SDK layout. See [Testing](TESTING.md) and [Tyres](TYRES.md).

## Placement and motion

Checkpoints record track/car provenance, pose, lap distance and gear. Optional
`motion.local_velocity_mps` supplies speed; legacy `motion.local_velocity` still
loads. Older captures can lack motion. Pose reuse requires the loaded track and
uses the loaded car's constants; speed/gear additionally require the capture car
and eligible straight, approximately level motion. Standing teleport skips motion.

Planning prefers learned corrections, then eligible manual calibration, then live
engine-derived rest values. Indexed calibration requires a matching executable
hash; the ordinary path currently accepts unlocked cross-build calibration.
Ordinary PitPos inversion includes yaw, clearance search and fitted signed lateral
settling. Indexed placement writes `pitIndex*3+2` and omits ordinary clearance
search. The borrowed vertical constant is not a general physical model.

Garage placement temporarily writes 24 bytes of a 32-byte spot entry, preserving
its tail. It suppresses/verifies Flag Rules, waits for Drive, samples arrival and
400 ms rest, restores spot bytes, and repairs lap state. Eligible moving handoff
uses verified paused direct reset after stable control/released pedals. Partial
motion writes can leave physics paused for recovery. Rule cleanup waits for safe
pit clearance or garage return and rechecks context; cancellation/close wait for
that cleanup. See [Placement](PLACEMENT.md) for the authoritative sequence.

Player-controlled teleport uses experimental direct reset: owned physics pause,
linked/collision-body and wheel updates, road-cache refresh, lap repair, complete
verification, resume and observation. It refuses cached collision pairs. Garage
tyre preparation is a separate guarded handoff; on-track reset preserves fitted
tyres and refuses conditioning. Fuel, damage and captured tyre restoration are absent.
Rolling outcomes cannot train stationary learning.

## Workspace, storage and build

`/` and `/lab` serve the same workspace. `Lab.workspace.*` implements the builder,
map/library and preset drawers using `Session.js`, `Practice.js` and shared controls.
`CircuitMapStore` caches validated geometry; `CheckpointLibrary` confines file
operations and preserves full payloads. Capture/library mutations share the runner's
operation lock with placement. Locks are process-local; concurrent writers remain
an open issue. The server binds `127.0.0.1` and requires JSON POSTs for actions.

| Location | Contents |
| --- | --- |
| `data/checkpoints/`, `.deleted/` | User captures and recoverable deletions |
| `data/profiles/`, `profiles-default/` | User and tracked shipped calibrations |
| `data/learned-rest/`, `observations/` | Build/checkpoint corrections and CLI observations |
| `data/ui-lab.json`, `input-bindings.json` | Personal presets/preferences and native bindings |
| `data/official-sessions.json`, `circuit-maps/` | Official-event and map caches |
| `offsets/*.auto.json` | Ignored build-discovery caches |
| `artifacts/`, `dist/` | Build/test/research output and release executables/ZIP |

Executables search upward for data/offsets unless overridden. `build.cmd` delegates
to `scripts/build.ps1`; the root solution maps configurations to x64 and
`Directory.Build.props` centralizes settings/output. `package` tests Release,
publishes App/CLI, stages SDK layout/calibrations/legal files and linked guides,
then smoke-tests the extracted ZIP outside the checkout. Maintainer executables,
user data and local offset caches are excluded. See [Contributing](CONTRIBUTING.md).
