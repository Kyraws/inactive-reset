# Placement and lap-timing evidence

Historical measurements from the former heading/lap investigations and
architecture/targets. Current algorithms are in `PlacementMath`, `RestModel`,
`RestLearning` and `LapValidity`; see [Placement](../PLACEMENT.md) for workflow.

## Ordinary-path model, August 2026

Barcelona / Richard Mille AF Corse #50 placements missed by about 0.567 m on
`1AC2F605` and 0.710 m on `0F6DCAC1`. The uncorrectable 11.633-degree rotation
diagnosis was superseded: the model lacked lateral displacement. Two checkpoints
at the same destination-to-rest range cannot distinguish rotation from translation.

On 2026-08-22, doubling the tool's D from 2.54765 to 5.09529 m changed forward
error from +0.502 to -2.0459 m while lateral error stayed +0.5033 m. The forward
change matched -deltaD within 0.2 mm. The corrected model is:

```text
rest = destination + D*heading(yaw) + sign*L*lateral(yaw) + (0,H,0)
```

For `0F6DCAC1`, Barcelona, Richard Mille AF Corse 2025 #50:ELMS, measured
D=3.049423, L=0.503253, H=0.373062 m produced 0.000500 m horizontal and
+0.000883 m vertical error at `cp-023328`. The shipped calibration retains
this provenance. Old profiles without L read it as zero.

The engine transform uses `destination - M*(0,b,a)`, with forward distance
`-a` from two vehicle floats. Ordinary-path settling was fitted as
0.104855*vehicleLength forward and 0.106398*vehicleLength laterally:

| Car | Length (m) | Scope | Residual |
| --- | ---: | --- | ---: |
| Oreca 07 LMP2 | 4.6868 | Fitted | 1.4 mm |
| Ferrari 296 GT3 | 4.7457 | Fitted | 0.2 mm |
| Ferrari 499P | 5.0233 | Fitted | 1.2 mm |
| BMW M Hybrid V8 | 5.0233 | Blind validation | 2.4 mm |

One track/session/setup does not establish a universal model. H remains borrowed:
engine vertical terms of 0.362/0.322 m did not explain it. Do not absorb terrain
into H or reuse ordinary-path coefficients for indexed placement. The earlier yaw
literal 35° became a mutable 45° value; search start changed 0.2→0.55 times
width while step/max stayed 0.1/1.5. Later behavior changed despite identical
tunables. Address matches and inverse/forward consistency do not prove accuracy.

## Sign and learning

Unsigned L caused 1.069–1.084 m lateral misses in sign -1 Genesis Magma Racing
2026 #17:LM at Daytona and Barcelona, while sign +1 BMW misses were +0.0027 to
-0.0003 m. The error was -2L, not banking: ground changed only 0.036 m across
1.07 m (1.9°). Use loaded-car identity and geometry, not checkpoint provenance,
to choose constants. Same-track pose reuse and capture-car motion eligibility
are separate current policies.

Corrections use `D += forwardError`, `L += sign*lateralError`. A measured
3.2 mm miss fell to 0.27 mm on the next learned placement. Corrected Genesis
tests on 2026-08-22: flat Daytona `day3` missed 0.0035 m horizontally / 0 m
vertically; banked `day4`, 2.6 m higher, missed 0.0606 / 0.011 m, including
0.0591 m lateral and 0.0137 m forward settling.

A wall caused a 1.794 m miss, 1.7937 m along the clearance-search axis: nine
whole steps. The original quarter-step learning cap wrongly rejected banking.
The revised guard rejects errors within 0.15 step of whole candidates >=1,
then errors larger than 0.6 step. Preserve observations and rejection reasons.
Current CLI placement saves learning; UI placement only loads it.

## Lap acceptance, 2026-08-12

Build `1AC2F605`, Barcelona, Richard Mille AF Corse 296 GT3. Dated container
offsets: sector +0x1CEA8, countLapFlag +0x1CFF0, next-lap latch +0x1CFF4,
lapStartEt +0x1CFF8, pit flag/state +0x8FD0/+0x8FC0.

| Observation | Before crossing | After crossing |
| --- | --- | --- |
| Clean lap | lap 3, sector 2, ET 863.494 | lap 4, sector 0, ET 1111.035 |
| Placed at 4311 m, no sector repair | lap 5, sector 0, ET 1170.652, count 1 | All unchanged |
| Same placement, sector set to 2, pit flag already 0 | lap 5, ET 1170.652 | lap 6, sector 0, ET 1281.541, count 2 |
| Placed at 477 m, forced sector 2 | lap 7, ET 1316.048, count 1 | lap 8, sector 0, ET 1458.984, count 2 |

`Slot_Reset` initialized sector 0; final-sector placement crossed no sector lines,
so start/finish was rejected entirely. The initial out-lap-demotion diagnosis
described a real mechanism but not this failure. lapStartEt distinguished a
rejected crossing from a demoted one. Forced sector did not move backward at
intermediate lines; accurate splits remain unverified.

Count flags 0/1/2 mean neither / lap only / lap and time. At accepted crossings,
the next-lap latch selects 1 or 2, rearms, and lapStartEt updates. A set pit flag
can then demote 2 to 1 outside races. The flag cleared unaided in 33 seconds
(pit state 5→0); clearing it removes a race against reaching the line, rather
than fixing a wholly rejected crossing. Pit-entry/exit handlers used the same
flag through controller+0x2050, controller=container+0x6F80.

Current direct reset repairs pit state inside an owned pause and marks the partial
lap untimed. Garage placement has a different cleanup sequence; historical command
ordering is superseded by the current guide.
