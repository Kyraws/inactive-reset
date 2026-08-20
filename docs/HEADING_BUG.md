# The 11.633 degree heading error

Carried over from the predecessor deliberately, not fixed. Placement lands the
car about **0.57 m** from the target. This document exists so nobody
re-diagnoses it from scratch, and — more importantly — so nobody "fixes" it the
wrong way.

## Symptom

Placement is **precise but not accurate**. Two runs at the same checkpoint agree
to ~1.5 mm; every run misses the target by ~0.57 m.

| Test | Error |
|---|---|
| `place cp-023328` | 0.5663 m |
| `place cp-023328` again | 0.5662 m |
| `place cp-023419` (different corner, heading 30 deg apart) | 0.5777 m |
| `calibrate` — the engine's OWN placement, **nothing written** | 0.5694 m |

Decomposed in the vehicle's frame the bias is constant:

    cp-023328   forward +0.135 m   lateral -0.550 m
    cp-023419   forward +0.152 m   lateral -0.557 m

## It is a rotation, not a translation

The last row of the table is the important one. That sample wrote **nothing** —
it observed the engine's own garage-to-Drive placement from its own unmodified
`PitPos` entry. The bias appears with none of this project's code involved, so
it cannot be caused by the bytes we write.

Taking that sample relative to the model's own "drive to" point:

    model predicts   2.5480 m at heading  -1.883 deg
    engine produced  2.7412 m at heading  +9.749 deg

    vertical error   -0.0018 m      -> the vertical constant H is CORRECT
    distance error   +0.1932 m
    heading error   +11.633 deg     -> the fault

`2.7412 * sin(11.633 deg) = 0.5527 m`, which is the entire observed "lateral"
miss. The forward component follows too: `cos(11.633 deg) = 0.98`, a 2%
shortening at this range.

**It looks like a constant vehicle-frame offset only because every sample was
taken at the same ~2.7 m range.** At one distance a small rotation and a fixed
translation are indistinguishable. They are not the same thing.

## Do not fix it by re-calibrating

Fitting new forward/vertical constants to this data would absorb a rotation
into two translation terms. The result would be accurate at 2.7 m and wrong at
every other distance, and it would look like a success. The vertical error is
already 1.8 mm, which is the direct evidence that the vertical constant is not
the problem.

This is the same failure mode as the old plugin's `teleport.lift_meters` fudge
factor, which is why continuous pose writes were abandoned there.

## Where the fault is

Between the stored `PitPos` orientation triple and the heading the engine
actually drives. In the predecessor that conversion is:

    route_c.cpp:127   destination.orientation.y = pitPos.orientation.y - sign * yawOffsetMode2
    route_c.cpp:153   entry.orientation.y       = desiredYaw          + sign * yawOffsetMode2

with `yawOffsetMode2 = 0.6108652 rad = 35.00 deg`, documented as
`GetPitDestination` mode 2.

Working the observed sample backwards:

    PitPos ori.y            = 0.578000 rad = 33.116 deg
    model  0.578 - 0.610865 = -0.032865 rad = -1.883 deg   (matches the tool)
    engine observed heading = +0.170160 rad = +9.749 deg
    implied engine offset   =  0.407840 rad = 23.367 deg

So the engine behaves as though the offset were ~23.4 deg, not 35.0 deg.

## Ruled out

- **`GetSpotTransform` and its sibling are byte-identical** across the patch
  (modulo relocated displacements), and the float constants they read are
  unchanged (`-5`, `0.075`). The transform code did not change.
- **`kSlotReset` is a pure refactor.** It grew 0x2FB -> 0x306 bytes, but the
  notify block was merely extracted into `0x00EB8CA0`, which writes the same
  fields with the same values (`0x6F80 + 0x2040 = 0x8FC0`). Instruction count
  is 169 in both builds.
- **A promising float coincidence was checked and rejected.** `.rdata` contains
  `0.610865` in the old build and not the new, and `0.406723` in the new and
  not the old — which looks conclusive. It is not: the only instruction reading
  the old constant is a `mulss` at `0x00D4C8C6`, which does not match the
  documented `-=` form. Different constant, coincidental value.

## The open lead

`kSlotRestart` grew 0x7AB -> 0x7EB bytes. Most of that is logger line numbers
(four immediates all shifted by exactly -20, i.e. 20 source lines deleted
above), but there is one real change: a **branch polarity flip** (`je` -> `jne`)
plus a new guard/logging block around a per-slot bitmask test.

The project's own constant is named `yawOffsetMode2` — mode **2**. If the
flipped branch changes which *mode* is requested from `GetPitDestination`, that
would produce exactly what we see: identical transform code, a different offset
applied, a constant angular error. That hypothesis is untested.

## How to verify a fix

Do not trust a single checkpoint. A wrong rotation can be made to look right at
one range.

1. Place at two checkpoints whose distance from the pit spot differs
   materially. A translation error stays constant; a rotation error scales with
   range.
2. Re-run `calibrate`, which writes nothing. Its residual should collapse.
3. Check the vertical error stays near zero — if a "fix" moves it, the fix is
   wrong.
