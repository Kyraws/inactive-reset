# The heading error — resolved 2026-08-22

**Status: fixed and verified by driving.** Placement at `cp-023328` on build
`0F6DCAC1` now lands **0.000500 m** horizontal and **+0.000883 m** vertical, with
arrival verified — down from 0.710 m.

**Status: understood and corrected.** The miss was never uncorrectable. It is a
constant displacement vector in the vehicle frame, and the model had a term for
only two of its three components — so half a metre of lateral error had nowhere
to go. `PlacementModel.RestLateralOffset` (`L`) is that missing term.

Everything below the resolution section is the original 2026-08-11 diagnosis,
kept because its measurements and its rule-outs are still correct. Its
*conclusion* — that this is a rotation and therefore cannot be calibrated away —
is superseded. Read the resolution first.

## Resolution

The model was:

    rest = destination + D*heading(yaw) + (0, H, 0)

The engine's actual displacement from a written destination has an off-axis
component. With no lateral term, `D` and `H` could not represent it at any
value, which is exactly why re-calibrating them never helped and why the
advisory forbade trying.

The model is now:

    rest = destination + D*heading(yaw) + L*lateral(yaw) + (0, H, 0)

### The measurement

On 2026-08-22, `cp-023328` was placed on build `0F6DCAC1` with `D` deliberately
**doubled** (5.09529 instead of the calibrated 2.54765), in a scratch copy of
the calibration.

| | baseline `D` = 2.5476 | probe `D` = 5.0953 |
|---|---|---|
| forward | +0.502 m | **-2.0459 m** |
| lateral | +0.503 m | **+0.5033 m** |
| horizontal | 0.710 m | 2.107 m |

The forward component moved by exactly `-deltaD` (2.54765, agreeing to 0.2 mm).
The lateral component did not move at all.

`D` is *this tool's* constant, not the engine's — it only decides where the
destination is written. So the engine's displacement from a given destination is
invariant under a 2.5 m change in where that destination sits. The miss is a
constant vehicle-frame vector, and correcting it in the model is valid across
positions.

Constants measured for `0F6DCAC1` at Barcelona in the #50:

    D = 3.049423    L = 0.503253    H = 0.373062 (unchanged)

### Rotation versus translation is unobservable, and does not matter

The original diagnosis asked which one this is. That question has no answer from
outside the engine.

A heading error of `theta` combined with the engine's own fixed range produces a
displacement that is a **constant vector in the vehicle frame** — identical in
every measurable way to a constant vehicle-frame translation. Separating them
requires varying the engine's range, which is not a thing this tool can do. The
original document's `2.7412 m at +9.749 deg` and an equivalent forward/lateral
pair are two descriptions of the same displacement; only the second is one the
model can represent.

So the original warning was half right. Absorbing the miss into `D` and `H`
**alone** really would have been wrong — not because it smears a rotation, but
because those two terms cannot express an off-axis displacement, so the fit
would have been forced and wrong. Adding `L` is not a fudge factor; it is the
model finally having the same degrees of freedom as the thing it models.

### What did not change

- **`H` is correct** and stays at 0.373062. The probe run showed a 0.046 m
  vertical residual, but that was terrain under the probe's deliberately
  displaced destination, not a standing error: with the corrected constants the
  vertical error is 0.9 mm. Do not absorb terrain into `H`.
- **`D` and `L` are per track AND per vehicle AND per build.** `D` demonstrably
  moved between `1AC2F605` and `1.4.1.3` with the engine tunables byte-identical.
  Re-measure both when the game patches.
- **Old profiles are unaffected.** `format_version` 1 has no `lateral_offset_L`;
  it reads as 0 and those profiles keep their old, characterised miss rather than
  silently acquiring a correction measured for a different build.

Pinned by `tests/InactiveReset.Tests/RestLateralOffsetTests.cs`, which fails on
the pre-2026-08-22 behaviour.

---

# Original diagnosis, 2026-08-11 (superseded conclusion)

Placement lands the car about **0.57 m** from the target on `1AC2F605`.

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

## It is a rotation, not a translation — SUPERSEDED, see Resolution

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

## Do not fix it by re-calibrating — SUPERSEDED, see Resolution

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
