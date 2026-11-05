# Status and known limitations

Reviewed 2026-10-24. Implementation and recorded experiments do not guarantee
compatibility with every car, circuit or future build. See
[Architecture](ARCHITECTURE.md), [Placement](PLACEMENT.md) and [Tyres](TYRES.md).

## Current verification

- Release builds with the existing WindowsBase/MSB3277 warning. Latest backend
  run (2026-11-03): **517 passed**. Five WebView2 suites: **259 checks passed**, plus production
  HTTP, first-use/EAC and layout checks. See [Testing](TESTING.md).
- All builder tabs/main views were measured at 1040×680 and 1280×800 with full
  weekend/weather metadata and native binding rows. Capture, tyre, checkpoint
  and binding-footer actions remain visible. Overview tests include ten events,
  long titles, both practice buttons and the EAC notice. Rendered pages inspected.
- Live read-only map validation/cache passed for Portimao (1,424 waypoints),
  Barcelona (1,502) and Monza (1,560). Extra types through 149 are valid; rendering
  selects circuit/pit types 0/1. On-track marker alignment remains untested.
- Controls is the only settings page; Redline/dark appearance and fitted spacing
  are fixed. Old preference files remain readable. No compact utility mode.

## Priority fixes

Telemetry timeline work has reached the SDK field-inventory/continuous-recorder
milestone. The app/serve and standalone `record` command record bounded JSONL with
SDK locking, identity checks and freshness/segment diagnostics. Exact sample selection and recorded pose/speed/gear teleport now use the existing
gated operation runner, with additional recorded build/car checks. This integration
is offline-verified; recorded resets have not been live-tested. Additional native
restoration remains unimplemented. Learned-rest behaviour is unchanged.
Maintainer-only suspension layout/snapshot diagnostics now resolve observation
code contracts and capture body/attachment state through read-only rights.
Offline checks and one stationary live read-only snapshot passed on build
`66942337`, Bahrain Paddock / TF Sport 2026 #2:LM (2026-11-04). Moving native
capture was subsequently observed in 122 samples (116 moving, up to 172.2 km/h),
exposing body-geometry/native deflection differences up to 12.5 mm despite equal
SDK time brackets. Follow-up bulk reads and native body rereads identified
advancing native blocks in the largest outliers; unchanged-block observations
had much smaller geometry residuals. This is not an atomicity or restoration
validation. Complete solver/contact state remains unrecorded. The SDK
recorder and reset behavior are unchanged. See [probe documentation](../tools/InactiveReset.Reanchor/README.md#read-only-suspension-observations).
One owner-approved paused diagnostic subsequently passed at 169.20 km/h on the
same build/car: two native observations agreed, SDK/native clocks aligned, and
maximum geometry/native deflection residual was 0.023335 mm. Pause cleanup
verified flag zero after a recorded 550.6-ms acquire-to-release interval; a
read-only follow-up observed later simulation progression. This was a pause-only
write experiment, not suspension restoration or complete solver/contact capture.
Subsequent offline carrier tracing identified retained joint-row corrections
that affect body velocity, plus native property accessors for one joint type.
A subsequent read-only inventory identified 30 joints across four types and
three solver groups on the same car/build. Current constructor/name proofs
identified 5 IsoBallJoint_2, 4 PureHinge, 20 Bar_2 and 1 AnisoOriJoint_0.
Their row/body/anchor/flag and retained-correction layouts have now been traced
offline. A separate read-only state diagnostic is prepared with 24 additional
exact vtable/code checks; 22+24 offline carrier checks pass.
One revised read-only joint-state capture passed live at 198.42 km/h in 2.7453 ms:
30 joints/46 rows, unchanged block/topology rereads, all corrections finite and
nonzero. A first advancing-row attempt was discarded before a fresh accepted
attempt. Batched reads and bounded numeric-row-only retries pass 70 focused tests.
A further freshly owner-ready bounded read-only repeat was refused: after one
numeric-row retry, a Bar_2 block changed outside the permitted row spans (five
bytes, first offset 0x50). The strict gate stopped without a third attempt;
report 8 retains both refusals. Repeated capture reliability remains unproved.
A subsequent owner-authorized paused joint/body diagnostic passed at
107.15 km/h: two exact joint/topology captures, body-pair agreement, 30 joints /
46 rows, 22+24 code checks, equal SDK time and verified flag-zero cleanup after
543.4 ms. Focused tests: 75 passed. Only the pause flag was written; no suspension
restoration or separate post-resume progression validation. This single newly
authorized paused diagnostic is complete.
Offline follow-up verified the saved bodies' omega/momentum/inertia relationship,
identified anchor vector accessors and runtime-mutated joint parameters, and
measured native time 2.5 ms ahead of SDK time in that paused pair. Reconstruction
ordering remains a proposal; contact/force lifecycle and complete retained-state
requirements still need proof. No new restoration capability was enabled.
A further offline trace found linked-body forces cleared each normal substep,
while embedded tyre motion and indexed model state evolve separately and remain
absent from the current body/joint report. The next diagnostic extension targets
those states read-only; raw model bytes are not a restoration payload.
Separate maintainer read-only and prepared owned-pause tyre-model commands now
cover four embedded bodies, inline control and eight mixed-type slots per tyre.
Three full function proofs pass actual-carrier discovery; 110 focused Release
tests pass. One newly owner-authorized paused body/joint/tyre capture passed live
at 153.02 km/h: exact paired state, 30 joints/46 rows, four embedded bodies /
32 slots, 22+24+3 checks and flag-zero cleanup after 570.7 ms. Later SDK-only
progression was observed. Embedded tyre bodies differ measurably from spin
bodies; native time stayed 2.5 ms ahead of SDK. The unpaused tyre command remains
untested live. Complete contact/model capture and restoration remain unsupported;
this new single pause approval is consumed.
Offline allocation tracing now bounds the missing tyre graph: 504 records,
two 72-entry scalar arrays and 648 descriptor pointers per tyre. Slot indices
map to one entry in each array and seven records; derived coefficients are
distinguished from retained scalar state. Actual live tables remain uncaptured;
the bounded read-only acquisition contract is research, not restoration support.
That contract is now implemented in separate maintainer table commands, with
exact counts/ranges/descriptor mapping, typed numeric prefixes and full-block
rereads. Actual-carrier 3+3 function checks and 140 focused Release tests pass.
One newly owner-authorized paused table capture passed at 168.76 km/h: exact
paired state, 31,104 finite allocated scalars and 2,592 local descriptor targets
per observation, 22+24+3+3 checks, flag-zero cleanup after 610.3 ms and later SDK
progression. Native time remained 5 ms ahead of SDK. The unpaused table command
remains untested live; complete state/restoration remain unsupported. This single
pause approval is consumed; further pause execution needs new authorization.
This is not atomicity or body-phase validation. Complete contact/solver state and a safe on-track
reconstruction sequence remain unverified; body/suspension telemetry alone is
insufficient restoration state. No further pause or restoration is authorized.
The owner's 2026-11-03 Bahrain Paddock / TF Sport #2 recording contains 9,040
fresh driving samples over 181.59 simulation seconds (49.78 Hz), with zero reported
buffer drops and no intervals over 100 ms. Frozen-time periods were segmented.
An application-location gate that incorrectly refused realtime telemetry was removed. See the
[recorder contract](TELEMETRY_TIMELINE.md).

| Priority | Finding | Evidence / next step |
| --- | --- | --- |
| High | Multiple instances can compete over game writes | Runner locks are process-local; add a shared writer lock before allowing concurrent windows/CLI writers |
| High | UI placement does not save learning/observations | `PlacementRunner.RunPlacement` loads learning but never calls `RestLearning.Apply`; validate settled motion before aligning with CLI |
| Medium | Capture/measured rest can include motion | SDK reads are not one locked snapshot; rest is sampled 400 ms after player control |
| Medium | Ordinary-path calibration can cross builds | `PlacementService.Plan` requires a build match only for indexed placement; review staleness policy |
| Low | Invalid CLI sector input falls back to final sector | `Program.Place` should reject malformed/missing sector input |
| Low | Rule identity depends on display text | `PlacementRunner.State` uses label substrings; separate identity from presentation |
| Low | Polling reattaches and hashes the executable | Measure before optimizing; retain write-time checks |
| Low | WindowsBase reference conflict | Review unused WebView2 WPF references causing MSB3277 |

## Recorded live scope

Engine tests below used build 14200 / executable SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`.
Owner-confirmed smooth motion is distinct from sampled telemetry.

| Date | Evidence | Limit |
| --- | --- | --- |
| 2026-10-24 | Three live map responses validated/cached through the production client | No car-to-marker alignment measurement or physics writes |
| 2026-10-23 | Direct/EAC launch, local REST editing/loading/readback, SDK capture, official refresh and return to menu; original preset restored exactly | No online race joined; protected memory writes stayed blocked |
| 2026-10-23 | Ten events/twenty presets matched content; Bahrain Outer Race reproduced time, rules and thirty weather values | Configuration only; weekly HTTP 500 retained cached partial data |
| 2026-10-17 | United Autosports #23, Barcelona standing placement within 4.3 mm horizontal / 1.1 mm vertical; cancellation/return restored spot/padding/rules; menu/wrong-track writes refused | Moving restoration and deliberate memory failures not exercised |
| 2026-10-16 | Barcelona direct resets: stopped tests, about 819 m moving repair, automatic return from 110.36 km/h about 525 m away to 39.86 km/h; owner confirmed rolling | Earlier moving reset fell below road and recovered to pits; repeat/high-speed reliability remains limited |
| 2026-10-16 | Mercedes #62 optimized reset: 126.68 requested, 125.12 km/h observed, second gear, automatic resume after 470 ms pause; owner confirmed rolling | Later offset refactor was tested offline, not by another live reset |
| 2026-10-16 | Garage rolling checks covered neutral/first/third gear and 19.55–148.52 km/h; Hold Brakes/Flag Rules restored | Some starts were about 3–4% slower; initial RPM transient and other gears remain unresolved |
| 2026-10-16 | Ferrari 296 / Monza automatic prototype verified 514 bytes; 8.94 requested, 8.54 initial and 7.72 km/h after about 2 s / 4.47 m | Prototype and integrated handoff have distinct scopes |
| 2026-10-16 | Garage targets FL 100%/60°C, FR 95%/65°C, RL 90%/70°C, RR 85%/75°C passed; on-track standing reset preserved condition and refused conditioning | Exact previous distributed tyre model was not reconstructed |
| 2026-10-16 | Combined Mercedes #62 rolling/tyre handoff: 126.68 requested, 126.45 km/h observed in second gear | One earlier condition check failed for unresolved reasons; two later runs passed |
| 2026-10-16 | Native F8 rebinding, foreground gating and Next selection passed | Physical wheel disconnected; hardware verification deferred |

Details: [Local sessions](SESSIONS.md),
[Official scheduled sessions](LAB.md#official-scheduled-sessions),
[Placement](PLACEMENT.md), [Tyres](TYRES.md). Historical ordinary-path heading
measurements and the maintainer-only `66942337` experiment remain in
[research evidence](research/README.md); neither establishes general production accuracy.

## Remaining support boundaries

- Memory writes require local Practice, live track/car identity and verified
  discovery/probes. EAC absence is insufficient. Capture names stay confined;
  malformed files are skipped individually. Error/cancel cleanup rechecks
  ownership and waits for safe rule restoration.
- Discovery depends on supported instruction/helper shapes and SDK ABI. Route/
  port recovery, whole-profile cache checks and synthetic relocation do not prove
  unseen-patch compatibility. Reset uses 79 decoded values, 102 agreeing references
  and two drivetrain contracts; missing patterns disable that capability separately.
  No live reset followed the offset refactor; its fields matched earlier live values.
- Speed/gear restoration requires the capture car and eligible straight, roughly
  level motion. Turning restoration is absent. Garage restoration waits at least
  three seconds and for released applied pedals, handles slope creep, and temporarily
  releases/restores Hold Brakes. Exact initial speed, clutch coupling and drivetrain/
  contact transients remain open. First-gear testing with Hold Clutch On did not
  establish sustained coupling; later third-gear testing stayed below 8,000 RPM
  after an initial transient.
- Partial motion failure can deliberately leave physics paused for recovery.
  Rolling outcomes do not train stationary learning. Further live work must cover
  repeated resets, varied cars/setups/surfaces, capture coherence, session/close
  transitions, start/finish timing and forced-sector splits.
- Captured tyre-state, fuel and general damage restoration are absent. Garage tyre
  preparation keeps fitted compounds and sets per-wheel condition/optional initial
  heat; physics then evolves normally. Compound selection needs loaded-vehicle
  availability/index resolution. SDK wear is raw 0..1, not grip/tread percentage;
  temperature channels are physical left/centre/right.
- SDK telemetry remains read-only. Offline payload tests reach 350 km/h; recorded
  live motion reached 148.52 km/h. Neither is a general speed guarantee.

Record executable hash, track, car, setup, action and outcome for new live results.
Passing arithmetic/UI tests or finding a matching pattern does not establish
live placement, tyre behavior, cleanup or future compatibility.
