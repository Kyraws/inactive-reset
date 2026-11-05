# Reanchor motion probes

Maintainer-only experiments; current application behavior is documented in [Placement](../../docs/PLACEMENT.md).

## Read-only suspension observations

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-layout <mapped-image> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-snapshot <full-build-sha256> <checkpoint.json> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-snapshot <full-build-sha256> <checkpoint.json> <new-report.jsonl> --window
```

`suspension-layout` is offline. It resolves 29 observation instruction contracts
and checks their relationships with the existing motion layout. The input must
use mapped PE coordinates (file offset equals RVA), not ordinary disk section
offsets. The report's hash identifies the input bytes, not a live executable.
Anchor `sourceRva` metadata is historical provenance only; discovery searches
executable sections and never uses it as a fallback address.

`suspension-snapshot` opens read-only process rights. It requires the requested
full executable hash, the checkpoint's track/car (and build when present), active
player control in local Practice, EAC absence and current motion/suspension code
proofs. It reads twelve linked bodies, chassis membership, aggregate/origin state,
four wheel identities, attachment descriptors, output gates and derived values.
Pointers become capture-local body IDs; output is diagnostic JSON, not a restore
payload. Output files must be new. There is no arm, pause, write, reset or learning
path. Environment overrides for offsets/data are the same as the other probes.

SDK time must first progress. The snapshot then requires equal SDK time around
the native reads, at most 25 ms, and unchanged topology, mode/gates and wheel
geometry flags. It retries timing refusals at most three times in the same
read-only attachment; topology/context failures stop immediately. These checks
**do not establish atomic native state**. UTC, monotonic timestamps/frequency and
both SDK samples are retained. Constraint/solver history and tyre contact state
are explicitly unavailable. Native values remain observations even when output
gates suppress the corresponding SDK channel to zero.

Body and wheel state are acquired in bounded bulk blocks, with monotonic
start/end timestamps. Body blocks are reread after collection; `ChangedBodyIds`
identifies any differences (aggregate ID -1). `BodyBlocksUnchanged` reports
byte equality across those reads, not an atomic tick guarantee. Window samples
that pass SDK-time/topology checks may still report changed native blocks and
must not be treated as coherent restoration data. The reread adds no game writes
or pause. The diagnostic does not extrapolate body poses to manufacture agreement.

`--window` observes for 45 seconds in one read-only attachment, with a 200-ms
delay between observations. Context/menu checks add overhead; no sampling rate
is promised. Incremental JSONL retains a header, accepted samples, exhausted
timing refusals and a completion/abort record. Accepted samples retain internal
timing retries. Context/topology errors and backwards SDK time abort; timing
refusals alone may continue. Interrupted files retain complete lines without a
successful completion record.

Semantics come from the historical runtime-derived `e7ad9129...` carrier studied
on 2026-11-04. Offline carrier matching and synthetic refusal/coherence tests
passed. One stationary read-only snapshot passed on build `66942337`, Bahrain
Paddock / TF Sport 2026 #2:LM on 2026-11-04; body geometry reproduced native
deflection and SDK wheel Y closely. A subsequent moving window retained 122
observations, 116 moving, up to 172.2 km/h. Body-geometry/native deflection differed
by up to 12.5 mm despite accepted time brackets: moving read evidence, not a
coherent restoration-state validation. Native update phasing/read skew,
solver/contact completeness and restoration remain unresolved.
Matching code on another build does not
establish complete physics compatibility. Follow-up bulk/reread captures showed
that the largest geometry outliers coincided with changed native body blocks;
even unchanged rereads do not synchronize native state to SDK telemetry.
The normal SDK recorder and restoration capabilities are unchanged.

## Owned-pause suspension observation

### Embedded tyre model observations

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-tyre-model-layout <mapped-image> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-tyre-model-state <full-build-sha256> <checkpoint.json> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-paused-tyre-model-state <full-build-sha256> <checkpoint.json> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-tyre-table-layout <mapped-image> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-tyre-table-state <full-build-sha256> <checkpoint.json> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-paused-tyre-table-state <full-build-sha256> <checkpoint.json> <new-report.json>
```

The layout command is offline only. The state command attaches read-only and
takes one bounded observation, with no retry or pause. It shares the existing
Practice/EAC/build/car/track/driver/player/progressing-time gates and requires
three uniquely discovered functions to match their complete proof bytes before
and after capture. Existing motion offsets must agree with that code convention.

Each of four wheels records its capture-local linked spin-body ID, separate
embedded tyre-body pose/motion/inertia plus force/torque, the full inline control
word, its masked slot index and eight slots. Each slot separates twelve raw
16-bit words from nine finite doubles labelled by byte offset. Word meanings
and scalar units remain partly unresolved; words are never dereferenced.
Live pointers and untyped model blocks are not exported. Embedded body IDs
12..15 are wheel-local, following linked IDs 0..11; they are not persistent IDs.

Topology is discovered before state reads. State blocks and topology/clocks are
reread exactly; short reads, nonfinite state, invalid body matrices/inertia,
overlapping allocations, foreign spin bodies and all changes refuse capture.
Decoding uses verified local copies after the equal-SDK-time/25-ms bracket.
Native/SDK phase differences are retained, not corrected. Reports never replace
existing files; acquisition refusals retain their role/offset when available.

The paused variant **writes only the pause flag** and requires explicit owner
authorization, fresh driving readiness and no competing writer. It extends the
owned-pause joint/body diagnostic with two tyre acquisitions. Every raw tyre
block, including unexported bytes and native membership, must agree across the
pair. There are no paused retries; existing owned cleanup and flag-zero readback
remain mandatory. Additional code/API work contributes to pause duration; no
hard maximum is promised. Cancellation or acquisition failure attempts cleanup.

Offline validation: 110 focused Release tests passed; three exact function
proofs resolved on the historical carrier. One owner-authorized paused variant
passed live on 2026-11-04 at 153.02 km/h, build `66942337`, Bahrain Paddock /
TF Sport 2026 #2:LM: four embedded bodies/32 slots, 22+24+3 checks, exact paired
state and flag-zero cleanup after a recorded 570.7-ms acquire-to-release interval.
Later SDK-only progression was observed. Native time remained 2.5 ms ahead of
SDK. The unpaused tyre command has not been live-tested. Allocated contact tables,
other model history and safe reconstruction remain
outside this capture: complete-model/contact-state flags remain false. The
production SDK recorder, reset gates, recovery and learned-rest are unchanged.

The separate **table variants** add three full-function proofs for allocation,
cell evolution and coefficient initialization. After verifying exact live counts
(72 state entries, 504 records, 648 descriptor pointers per tyre), they copy both
state arrays, the record allocation and descriptor table in bounded bulk reads.
All ranges must be canonical, aligned and non-overlapping across wheels and the
existing observed graph. Every descriptor must point to its exact expected
state-array or record entry; the diagnostic never follows arbitrary descriptor
targets. Each tyre exports two 72-row arrays and 504 numeric record prefixes,
twelve doubles per row, with 72 groups of nine local region/index references.
Unclassified record metadata and native addresses are not exported.

All table headers, full buffers (including unexported bytes) and descriptor
pointers are compared exactly on reread and, when paused, across the pair. Slot
indices are checked against the proved 72 groups; unknown trailing slot words
remain uninterpreted. Decoding/graph validation uses verified local copies
outside the timing bracket. Counts, graph, flags/metadata, short reads, invalid
numeric state and code failures stop; no retry is added. The table byte budget
is 624,384 bytes per four-tyre pass before rereads and existing state/headers;
the 25-ms gate remains unchanged. Pause duration still has no hard maximum.

Table validation: 140 focused Release tests pass, and actual-carrier discovery
passes 3 existing tyre-model + 3 new table checks. One newly owner-authorized
paused table capture passed live on 2026-11-04 at 168.76 km/h on the same build/
car/track: 31,104 finite allocated scalars and 2,592 descriptor targets per
observation, exact paired state, 22+24+3+3 checks, flag-zero cleanup after a
recorded 610.3-ms acquire-to-release interval, and later SDK progression. Native
time stayed 5 ms ahead of SDK. The unpaused table command remains untested live.
Further pause execution requires new owner authorization/readiness. CompleteModelStateAvailable and
TyreContactStateAvailable remain false: record prefixes are not complete contact
history, and no model configuration restore or suspension reconstruction is
validated. Production restoration behavior is unchanged.

### Original owned-pause body observation

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-paused-snapshot <full-build-sha256> <checkpoint.json> <new-report.json>
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-paused-constraint-state <full-build-sha256> <checkpoint.json> <new-report.json>
```

This separate maintainer command **writes the physics-pause flag**. Obtain
explicit owner approval before execution and run with no other placement/probe
writer. It shares the read-only command's Practice/EAC/build/car/code gates and
requires active driving with progressing SDK time before acquiring its own
pause. An existing driver pause is refused. It sets/verifies the pause, waits
25 ms, takes a native/SDK observation, waits another 25 ms and rereads. All
observed native state except host read timestamps must agree; SDK time must
remain unchanged. There is no paused window mode or retry.

The new JSON report retains observations, SDK phase, identity and timed pause/
cleanup events, including failures. Ctrl+C requests cancellation and owned
cleanup. Unlike a partial motion write, a read failure always attempts resume;
changed context or failed readback can prevent cleanup and require recovery.
Only successful cleanup plus final flag-zero readback yields success. Menu/API
checks and runtime overhead add to the pause duration; there is no hard maximum.
No body, suspension, gear, assist, reset or learning writes occur. Stable paused
reads do not establish complete solver/contact state or restoration support.
The constraint-state variant additionally resolves the 22 inventory and 24 joint
state proofs before pausing and rechecks them afterward while still paused.
It captures all supported joint blocks/topology twice, requiring exact bytes
and addresses to agree across the pair, including unexported callback/header
bytes. Each joint acquisition retains the equal-SDK-time/25-ms gate. No retries
are allowed under pause. Both decoded inventories and acquisition brackets are
retained; full raw blocks/pointers are compared locally rather than exported.
Neither variant applies speed or body translation; those remain in the existing
production reset. This diagnostic establishes no new restoration capability.
Offline focused tests passed. One owner-approved live acquisition passed on
2026-11-04, build `66942337`, Bahrain Paddock / TF Sport 2026 #2:LM, at 169.20 km/h.
Two observations matched, SDK/native clocks aligned and geometry/native
deflection residual was at most 0.023335 mm. Recorded acquire-to-release interval
was 550.6 ms; final flag-zero readback and later read-only simulation progression
passed. This does not establish suspension restoration or other-build support.

## Read-only constraint inventory

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-constraint-inventory <full-build-sha256> <checkpoint.json> <new-report.json>
```

Uses read-only process rights and the existing Practice, active-driver, EAC,
full-build, checkpoint track/car and motion/suspension code gates. It additionally
checks 22 inventory code/table/name proofs, including four constructor/type-name
bindings, and rereads joint/body/group membership and vtables. Timing-only SDK
bracket refusals retry at most three times; topology/pointer/code failures stop.
Collection requires physics-backed mode, progressing SDK time before entry, equal before/after SDK
time and at most 25 ms for the accepted native read. It never pauses physics.

Reports capture-local joint/group IDs, vtable and function RVAs, identified or
unresolved types, group row counts/membership and verified IsoBallJoint_0 body
references. Other types' body references/state remain unavailable. Names alone
do not enable their state capture. Vectors are bounded to 256 joints/64 groups,
with 256 rows/members per group. The solver member table can have two-byte
alignment because native construction packs it after two-byte indices;
individual object pointers still require eight-byte alignment.

One read-only inventory passed on 2026-11-04, build `66942337`, Bahrain Paddock /
TF Sport 2026 #2:LM. The name-proved sample at 230.85 km/h found 30 joints:
5 IsoBallJoint_2, 4 PureHinge, 20 Bar_2 and 1 AnisoOriJoint_0; three groups had
6/20/20 rows. Accepted read: 2.458 ms, SDK time 236.7525 s before/after, unchanged
topology rereads; this capture used 21 proof checks. The final explicit mode gate
raises discovery to 22 checks; 45 focused tests and offline carrier discovery
passed. Its live recheck was refused outside active driving control.
This is a non-atomic inventory, not complete constraint/contact state or a
restoration payload. Production recording, reset and learned-rest are unchanged.

## Read-only constraint state

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll suspension-constraint-state <full-build-sha256> <checkpoint.json> <new-report.json>
```

Shares all inventory gates and read-only
rights, with 24 additional exact vtable/function checks for IsoBallJoint_2,
PureHinge, Bar_2 and AnisoOriJoint_0. Captures local body IDs, local anchor/axis
relationships, raw flags, offset-labelled parameters, row coefficients/weighted
coefficients and retained corrections/input/limits. Infinite limits/configuration
are invariant strings; NaNs and non-finite corrections/coefficients are refused.
Unknown types, foreign bodies, inconsistent group rows/membership and changed or
short object blocks are refused. Each joint must occur in exactly one group.

Full object-block rereads and SDK brackets do not prove atomicity or alignment
with a body snapshot. `ConstraintStateAvailable` remains false: complete solver
scratch/contact/tyre history and reconstruction are absent. Native accessors are
never invoked; no pause or game-memory write occurs. Offline carrier checks
(22 inventory + 24 state) passed. A fresh live capture
must independently establish driving control. Previous pause authorization does
not cover another pause. Production reset/recovery/learning are unchanged.

A terminal changed/truncated verification reread saves a rejected report
(`mode=read-only-constraint-refusal`, `accepted=false`) and exits with failure.
It identifies the first failing block, relative byte offset and mismatch counts;
it contains no accepted joint state or SDK-after bracket. Early gate failures
still produce no report. Refusal
attribution regressions initially brought the focused suite to 62 passing tests.

Native acquisition now finishes all byte rereads before local joint decoding.
The SDK-after/25-ms bracket excludes local decoding and row/scalar interpretation;
those checks still must pass before saving an accepted result. All byte-equality
gates remain. Focused tests now total 64 passed. The first live check after this
change refused before attachment at the active-driver gate. Later moving reads
still advanced, motivating the batching change below.

Joint-state copying now starts after topology discovery, with two tight passes
over all state blocks before topology rereads. The command permits at most three
fresh attempts within the existing timing-attempt budget. Only equal-length
mismatches entirely inside proved numeric row spans may retry; pointers, flags,
headers, topology, code and short-read failures still stop. Each attempt rechecks
Practice/driver/player context and acquires a fresh complete graph. Every byte
reread and acceptance gate remains; attempts never merge state. Refused attempts
are retained in successful/final reread-refusal reports.

One live capture passed on 2026-11-04, build `66942337`, Bahrain Paddock /
TF Sport 2026 #2:LM, at 198.42 km/h in 2.7453 ms. Attempt 1 was discarded for
numeric row advancement; attempt 2 passed equal SDK time, all block/topology
rereads and 22+24 code checks. All 30 joints/46 rows were captured with nonzero
finite corrections. Final focused tests: 70 passed. This establishes one
bracketed read-only observation, not atomicity, body-phase alignment, repeated
reliability, complete contact state or restoration. No pause or game writes.

## Experimental speed restoration

New checkpoints store optional `motion.local_velocity_mps` and the existing
capture gear. Speed is the velocity magnitude converted to km/h. These are
recorded values only; normal teleport restores eligible captured speed and gear; standing teleport skips it.

From the repository root after building Release:

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll velocity-probe <full-build-sha256> <checkpoint.json>
```

The default command is read-only with respect to game memory. It requires a
moving checkpoint captured at 0.5 km/h or faster (no upper speed ceiling), the same track and capture vehicle, active
offline Practice without EAC, and a stopped player-controlled car in neutral
with pit state clear. It uses locally discovered offsets and the exact executable
hash supplied. Paths default to repository `offsets/` and `data/`;
`INACTIVE_RESET_OFFSETS` and `INACTIVE_RESET_DATA` override them.

Append `--arm` to attempt ONE 24-byte write to the candidate world/local
velocity fields and record two seconds of SDK speed, position, elapsed time and
motion-block readings under `artifacts/velocity-probe/`. Observe natural driving
first to cross-check the motion fields against SDK velocity. Stop the car in a
clear area before an armed test. The probe does not restore gear, engine speed,
wheel rotation or angular velocity. It does not loop writes or restore stale
velocity after the car starts moving. A readback failure may mean the write was
partially applied or physics immediately changed it; the observation log keeps
that failure and subsequent samples.

A completed observation file or zero exit status is not proof of a successful
rolling reset. Review elapsed-time progression and actual position change as well
as reported speed. The first live test on build `66942337` (Monza Curva Grande,
Richard Mille AF Corse 2026 #50:ELMS, rear against a wall, clear space ahead,
neutral, pedals released) verified the write but found near-zero velocity again
by the 24 ms sample. SDK speed stayed below 0.0024 km/h and position moved less
than 0.00004 m over two seconds. A velocity write alone did not restore motion
in that test. That prototype did not establish physics authority or drivetrain restoration. The probe never feeds placement learning or enables a production
option. The existing telemetry reader is not a locked SDK snapshot, so capture
coherence while moving also needs validation.

The maintainer probe also accepts `--bodies`. This selects a separate experiment
on the researched full `66942337` executable hash and the 12-body/two-chassis
layout observed on the Ferrari 296 LMGT3. It checks the exact code paths that
copy body velocity into telemetry-facing fields and integrate body velocity into
position. A read-only natural-driving sample correlated these values with SDK
speed. A subsequent explicitly armed test verified all 12 body writes and
produced 3.97 m of clean, owner-confirmed forward movement over two seconds. The
8.94 km/h request peaked at 7.65 km/h and decayed to 6.86 km/h. This is evidence
for a low-speed motion prototype on that car/build, with exact speed and complete
rolling restoration still unresolved.

Without `--arm`, this mode only prepares and reports the proposed translation.
With `--bodies --arm`, it adds one shared world-velocity change to each body's
existing linear velocity, preserving their relative velocities. On the researched
car this is 12 separate 24-byte writes, 288 bytes total. The batch is not atomic
and may partly complete if a write fails. It leaves gear, wheel rotation and
angular momentum unchanged. It retains the low-speed and stationary gates and
records body velocities with subsequent telemetry. It is never called by normal
placement and does not establish a complete rolling reset.

The alternative `--wheels` mode prepares the same body translation plus four
wheel angular velocities, angular momenta and scalar spin rates (512 bytes
total). It adds released-pedal, stopped-wheel, wheel-membership, orientation,
radius and inertia gates. It derives approximate rolling spin from captured
longitudinal velocity and current wheel radii; it does not restore tyre-contact
state or the drivetrain. Use it instead of `--bodies`. Without `--arm` it
remains read-only.

One explicitly approved live test stopped at the second body's readback
mismatch, after one verified body write and before any wheel writes. The car
moved smoothly according to the owner, peaking at 6.01 km/h and moving 3.05 m.
The mismatching write may also have applied. This partial batch does not validate
wheel restoration. No retry was performed. Applying disjoint state while physics
advances remains unresolved; neither mode is suitable for production placement.

The `--paused-wheels` alternative requires the driver to stop in neutral,
release both pedals and open the normal pause menu first. It checks the game's
explicit pause state, frozen SDK time and stable body/wheel values, checks the
pause boundary before each write, and verifies the complete applied state while
paused. With `--arm` it attempts the same 512-byte batch and leaves the game
paused. Resume is a separate manual action: the written motion may take effect
then. Normal pause was observed to freeze all sampled state on the researched
car/build; this is not an atomic lock if the driver resumes during the batch.
No process suspension, native calls or automatic Resume are used.

The explicitly approved paused test verified all 12 body writes and 12 wheel
fields, then repeatedly verified the complete state for two seconds before
manual Resume. On Resume, SDK speed first read 8.56 km/h for an 8.94 km/h request,
peaked at 8.56 km/h and coasted to 7.70 km/h over two seconds, travelling 4.59 m
with released pedals. The owner confirmed smooth movement. This establishes one
paused low-speed prototype on the researched car/build; the initial 4.2% speed
deficit, tyre-contact/drivetrain state and placement integration remain open.

The `--auto-wheels` alternative owns the researched physics-pause byte, freezes
physics without opening the pause menu, verifies the 512-byte motion batch and
resumes automatically (514 bytes including pause set/restore). It does not pause
audio. A possible partial motion update stays paused for recovery. Without
`--arm` it only prepares the plan. `--placement-wheels` uses the same sequence
but also permits raw pit state 5 / API EXITING; it never changes pit state.
All probe alternatives are mutually exclusive.

Body, wheel and pause primitives are shared with application rolling
start. The app additionally waits for the post-Drive control handoff and
temporarily releases/restores Hold Brakes; the standalone probe does not manage
that assist and still requires released physical and filtered pedal values.
See [Placement](../../docs/PLACEMENT.md) and [Status](../../docs/STATUS.md) for current
integration evidence and limits. Earlier prototype paragraphs above describe
their individual test stages.

The `--gear-wheels` mode extends automatic body/wheel restoration with captured
gear, calculated engine angular velocity and clutch state using the loaded car’s
ratios and rev limit. It accepts the inherited placement EXITING state and
requires stopped neutral before starting. The additional 64 bytes bring the
automatic batch to 578 bytes including pause/resume. First-gear placement at
39.86 km/h and third-gear placement at 148.52 km/h passed live on the researched
Ferrari/Monza setup; other gears remain unverified. All motion remains paused after a possible partial update. It does not initialize tyre-contact state.
