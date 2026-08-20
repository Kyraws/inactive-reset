# The 11.633 degree heading error

Known and deliberately not fixed. Placement lands the car about **0.57 m** from
the target. This document exists so nobody
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
actually drives. The model applies that conversion in
`PlacementMath.PredictDriveDestination` and inverts it in
`PlacementMath.InvertToPitPosEntry`:

    destination.orientation.y = pitPos.orientation.y - sign * yawOffsetMode2
    entry.orientation.y       = desiredYaw          + sign * yawOffsetMode2

with `yawOffsetMode2 = 0.6108652 rad = 35.00 deg` at the time these samples were
taken, documented as `GetPitDestination` mode 2.

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
- **`0.406723` is a coincidence, now settled for good.** `.rdata` contains
  `0.610865` in the old build and not the new, and `0.406723` in the new and not
  the old, and `0.406723 rad = 23.3035 deg` sits within 0.064 deg of the offset
  this document's sample implies. It is still a coincidence, and the evidence is
  now stronger than the original instruction-form argument:
  - `0x0181AC00` (old) sits inside an **ascending run of pooled float literals**
    — `0.5999`, `0.6`, `0.6024`, `0.6108652`, `0.62`, `0.625`, `0.6366197` (2/pi),
    `0.64`. A compiler constant pool, nothing more.
  - `0x018E93F4` (new) is **not** a pool. Its neighbours are denormals and
    `1e+29` garbage, it is not 16-byte aligned, and **nothing in `.text`
    references it.** A full RIP-relative scan across `.text`, covering all four
    post-displacement immediate sizes, returns zero readers.

  Do not re-open this. In ~2.2M float-aligned positions of `.rdata`, a hit within
  a few parts per million of any chosen target is expected.

## The engine constants changed in the 1AC2F605 build

**Resolved statically on 2026-08-20, against `LMU_runtime_44C3EE9C.bin`.** This
is not the same thing as the 11.633 deg error above being explained — see
"What this does and does not settle".

`GetPitDestination` moved from `0x00D4C700` to **`0x00D4C310`**. It was located
by call-site correspondence: both builds have exactly 21 callers of
`AssignSpotIndices`, in the same order, and the old call at `0x00D4C795` is entry
13 at `func+0x95`; the new entry 13 is `0x00D4C3A5`.

The old build applies the offset as a plain `.rdata` literal:

    0x00D4C8C6  mulss xmm6, [0x0181AC00]   ; xmm6 = sign, *= 0.6108652
    0x00D4C8D9  subss xmm0, xmm6           ; yaw -= sign * 35 deg

`xmm6` really is `sign`: `0x00D4C7ED..0x00D4C80D` is
`src == 0 ? 0 : src / |src|`. **The model was correct for that build.**

The new build does it differently:

    0x00D4C525  movss xmm1, [0x03B3621C]   ; 45.0      <-- DEGREES, in .data
    0x00D4C531  mulss xmm1, [0x0181A8D4]   ; *= 0.01745329  (pi/180)
    0x00D4C546  mulss xmm1, xmm7           ; *= sign
    0x00D4C558  subss xmm0, xmm1           ; yaw -= sign * 45 deg

The offset is **no longer a radian literal**. It is a value in **degrees**, held
in a mutable `.data` tunable block and converted at runtime — which is why
`0.6108652` is absent from this build's `.rdata` entirely.

The same block carries the pit-spot search factors, and the search loop has the
identical `comiss`/`ja` shape in both builds, so the roles map directly:

| | 266D1AF6 | 1AC2F605 | RVA |
|---|---|---|---|
| yaw offset | 0.6108652 rad (35 deg) | **45 deg** = 0.7853982 rad | `0x03B3621C` |
| search start | 0.2 * width | **0.55 * width** | `0x03B3620C` |
| search step | 0.1 * width | 0.1 * width | `0x03B36210` |
| search max | 1.5 * width | 1.5 * width | `0x03B36214` |

Both the yaw offset and the search start changed. `offsets/1AC2F605.json` has
been updated, at confidence **`I`** — read from a dump, not yet confirmed
against a live placement.

Because the values live in `.data` and were read from a **runtime** dump, they
are post-config values, not file defaults. That also means a future version
could read them live instead of snapshotting them, which is why the RVAs are
recorded in the profile. That change has not been made.

## What this does and does not settle

**Does:** the profile was describing the *previous* build. Any placement made on
the current build used a 10 deg wrong yaw offset and a search start wrong by
0.35 * width — together well over a metre, far more than the 0.57 m documented
here.

**Does not:** the measurements in this document are dated **2026-08-07**, four
days before the 2026-08-11 patch, so they describe the **266D1AF6** build — the
one whose constants the model got right. The 11.633 deg error therefore remains
unexplained, and the "it is a rotation, not a translation" reasoning above still
stands on its own evidence.

One caution about that reasoning, for whoever picks this up: the sample is not a
pure rotation. The model predicts 2.5480 m and the engine produced 2.7412 m, so
the radial distance moved by +0.1932 m as well as the heading by 11.633 deg. A
pure yaw-offset error cannot change the length of the destination-to-rest vector.
Either `D` is also wrong, or the destination point itself is not where the model
puts it — and a changed search start moves exactly that. One sample cannot
separate the two, which is the strongest argument for the two-range protocol
below.

## How to verify a fix

Do not trust a single checkpoint. A wrong rotation can be made to look right at
one range.

1. Place at two checkpoints whose distance from the pit spot differs
   materially. A translation error stays constant; a rotation error scales with
   range.
2. Re-run `calibrate`, which writes nothing. Its residual should collapse.
3. Check the vertical error stays near zero — if a "fix" moves it, the fix is
   wrong.
