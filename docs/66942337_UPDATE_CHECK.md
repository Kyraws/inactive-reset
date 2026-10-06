# LMU build 66942337 update check (2026-10-06)

Status after application integration: Inactive Reset now discovers offsets
locally and selects the indexed destination path automatically. Attachment and
read-only planning passed against this running build. The integrated placement
path has not yet had a live placement test. The experiments below document the
earlier maintainer-only path and its original promotion requirements.

Installed direct-launch executable SHA-256:
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`.
Read-only mapped dump: `artifacts/LMU_runtime_66942337.bin` (0x3D41000
bytes, zero unreadable bytes). The live mapped PE identity matched disk.

The first static PitPos resolution failed because it encoded the preceding
build's slot-index offset `0x471D4`. The slot index is `0x471E4` here.
`FindPitPos` now uses the independently resolved index and distinguishes the
indexed destination path from the old ordinary-slot path. Regression tests
cover both layouts. On the old working dump it still resolves ordinary-slot
PitPos `0x01DBD990`; on this dump it resolves the special-slot PitPos global
`0x01DFD968` with 11 agreeing readers. The indexed destination table global
is `0x01DFD9B0`, pit state/flag offsets are `0x8FE0/0x8FF0`, and the pit-speed
gate identifies Flag Rules `0x01E13E68`. The independent pit-penalty path
resolver also finds that Flag Rules global and passes its mutation checks.

In a direct-launch offline Barcelona Practice session with the exact Richard
Mille AF Corse 2025 #50:ELMS car, live tables and indices were plausible:
slot 0, pit index 8, garage index 0, owner/mode `1/0` in the garage, Flag
Rules `2`. A read-only unmodified Drive produced mode-2 player control and
rest pose `(119.386047,0.758482,200.777328)`, yaw `-0.207134`. This differs
from the prior build's matching-car baseline by ~0.2 mm horizontally and
0.03 mm vertically.

A guarded maintainer-only placement to checkpoint `cp-023328` then produced
player pose `(-87.679024,-1.523374,-146.855820)`, yaw `0.515187`, at the
first sampled arrival: 2 mm horizontal and +7 mm vertical checkpoint error.
The original 32 spot-entry bytes were verified after Drive. Pit state cleared
after 39.6 seconds of driving, then the original Flag Rules value `2` was
verified. A separate read-only check found owner/mode `0/0`, pit state/flag
`0/0`, Flag Rules `2`, and the original mode-2 entry. The driver reported that
the placement worked normally. This was an experimental command with manually
supplied calibration, **not** the shipped Inactive Reset placement path.

The new build must remain disabled for normal use until the indexed-table
path and automatic per-build/car/track calibration are integrated into the
production placement service and the generated offset candidate passes all
required gates. Static matches and one successful live experiment do not
authorize promoting an old ordinary-slot profile.

The 0F6DCAC1→66942337 offline reanchor wrote
`artifacts/66942337.offline-candidate.json` and exited nonzero as intended:
`placementValidated=false`, special-slot PitPos `0x01DFD968` confidence `U`
for the old ordinary-slot profile field. Flag Rules was structurally resolved
at `0x01E13E68` with reader `0x00CE846A` and confidence `E`. The complete
.NET suite passed 158/158 tests after the resolver change.
