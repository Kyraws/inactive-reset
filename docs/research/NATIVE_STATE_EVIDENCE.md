# Native-state observation evidence

Historical diagnostic results and their recorded limits. See [Status](../STATUS.md) for current support.

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
recorder and reset behavior are unchanged. See [probe documentation](../../tools/InactiveReset.Reanchor/README.md#read-only-suspension-observations).
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
