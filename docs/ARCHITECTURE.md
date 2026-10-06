# Architecture

## Automatic build discovery (2026-10-06)

`GameSession.Attach` hashes the installed executable and loads a local cache for
that exact hash, or resolves offsets from the decrypted mapped image using
`AutomaticOffsets`. Structural resolvers find the tables, indices, stride,
control state, dimensions, and rule flags. Embedded masked instruction anchors
resolve the remaining vehicle and lap fields, refusing ambiguous or disagreeing
matches. The cache carries a resolver version and a structurally resolved probe;
a changed build, resolver version, or probe causes discovery again. No published
offset profile, previous dump, GitHub connection, or maintainer approval is needed.

Placement selects the ordinary PitPos entry on older builds and the indexed
`pitIndex * 3 + 2` entry on newer builds. The indexed path uses the existing
direct inverse and the engine's live rest-offset fields, without the older
clearance-search displacement. Existing per-build checkpoint learning applies
to its arrival errors. The write and rule restoration flow is shared.

The legacy profile/reanchor discussion below records earlier development.

How Inactive Reset works, what is known to be wrong with it, and the mistakes
already made here so they are not made again.

For what the tool *is*, see [`README.md`](../README.md). For the vocabulary —
especially the pit-speeding penalty vs. the car's own pit limiter — see
[`CONTEXT.md`](../CONTEXT.md).

---

## The two mechanisms

**Teleport.** LMU keeps a *spot table* of pit and garage positions. Press Drive
from the garage and the engine reads your pit-spot entry and places the car
there. So the tool overwrites that entry with a computed position, lets the
engine do the placing, and puts the original bytes back. 24 bytes, verified by
read-back, always restored. **The engine places the car; we only change where it
thinks the pit spot is.**

**Rules.** Two penalties, consumed in two completely different ways. This
distinction is the single most important thing in the codebase:

- **read-live** — the consumer dereferences the setting on *every* evaluation,
  so writing it applies at once. Pit-speeding (`Flag Rules`) is this.
- **expanded-once** — read at session start and exploded into derived flags.
  Writing the *setting* mid-session does nothing; the derived flags must be
  written. Track limits is this.

Getting that backwards is why changing rules in the game menu, or over the REST
API, appears to do nothing to a running session.

**Lap validity.** A third thing entirely, and not a rule. The engine accepts a
start/finish crossing as a lap completion only when the sector index
(`+0x1CEA8`) says you are in the final sector. `Slot_Reset` zeroes it, so a car
placed past the last sector line crosses no sector lines, arrives reading 0, and
the crossing is **discarded outright** — lap counter, lap time and lap-start ET
all unmoved. Placement writes `sector = 2`, which is measured to fix it.

`countLapFlag` (`+0x1CFF0`) and its latch (`+0x1CFF4`) decide whether an
*accepted* lap is timed, and `pitFlag` demotes it via the out-lap rule. Both are
real; neither was the cause. Full account, including the wrong diagnosis that
shipped first, in [`LAP_VALIDITY.md`](LAP_VALIDITY.md).

---

## Layout

    src/InactiveReset.Core   offsets, memory, gates, placement math, capture
    src/InactiveReset.Ui     local HTTP server + the single HTML page
    src/InactiveReset.Cli    console front end
    src/InactiveReset.App    WinExe: WebView2 window around the same UI
    tests/                   regression tests, including byte-exact fidelity vs the C++

| | |
|---|---|
| Offsets, per build | `offsets/<hash8>.json` |
| Shared-memory layout | `offsets/shared-memory.json` — generated, never hand-edited |
| Calibrations, checkpoints | `data/profiles`, `data/checkpoints` (per-user, not tracked) |

CLI and window call the same command layer, so they cannot drift. They also
share one **placement report** (`PlacementReport`), which decides what an outcome
means so that neither front end interprets it alone. They did drift once, exactly
there: the page reported the pit flag and never the sector write.

The **windowed app is the front end actually in use.** The CLI is for diagnosis.

A placement **disables the pit-speeding penalty** at arm time, every time. There
is no case where you want a stop/go for a position the tool put the car in, so it
is not a choice. Track limits remains a toggle.

---

## How it is actually used

Design decisions have been made against this, so it is worth knowing before
proposing anything.

The loop is: return to the garage, place, drive a corner or two, return to the
garage, place again — **many times in one sitting**. A placement requires the car
parked under AI, so the garage return is not optional.

- **Lap timing matters.** Crossing the line and having it count is the point,
  which is what the sector write buys. Being placed mid-lap is not.
- **No stop/go has ever been earned from a placement.** Penalties were always
  switched off first, and now a placement switches the pit-speeding one off
  itself. The pit-state wait in `PlacementService.Place` is therefore normally
  unreachable; it is kept because it costs nothing when the penalty is off.
- **Track limits have to be re-toggled every session,** because the derived flags
  re-arm on session init — see Known defects §2. This was deliberately left
  manual rather than re-applied automatically: a background writer is a bigger
  commitment than it looks, and the UI shows the true state read back from
  memory.

---

## Offsets are data, not code

The reason this exists as a rewrite. Every address lives in a JSON profile keyed
by the executable's SHA-256, with a per-address `confidence` (`E` established,
`I` inferred, `U` unresolved). `Rva.Require()` **throws on `U`**, so a stale
address refuses rather than reading plausible nonsense.

Patch day is the **maintainer's** job, not the user's. `dump` and `reanchor`
live in `src/InactiveReset.Reanchor`, which `build.ps1 ship` never publishes:

    inactive-reset-reanchor dump before.bin
    inactive-reset-reanchor reanchor --old-dump before.bin --base offsets/<old>.json

`reanchor` captures the running game, re-derives every address (masked signature
search for code, majority vote across referencing instructions for data),
refreshes the probe bytes and build identity, and writes a new profile.
Publishing it is a `git push` -- see "How a user survives a game patch".

It stays in this repository rather than a separate one because it *writes* the
profile format the app *reads*. Split across repositories, that schema exists
twice and drifts, which this codebase has already suffered twice elsewhere.

**What `reanchor` cannot do.** It re-derives *where things are*. It cannot
re-derive *what the engine does*. The 2026-08-11 patch moved nothing relevant and
changed two tunable values, and every gate stayed green while placement went a
metre wrong. That class of change is handled by reading the values live -- next
section.

Shared-memory offsets come from `tools/dump-sdk-offsets.cpp`, which asks the
compiler for `offsetof` against LMU's own SDK header. Re-run it if LMU ships a
new `SharedMemoryInterface.hpp` — the header is not redistributable and is not in
this repo; see [`tools/README.md`](../tools/README.md). Note
`vect3ComponentSize` is **8** — positions and orientations are `double`; reading
them as `float` gives plausible nonsense, so the loader asserts it.

---

## Engine tunables are read live, not stored

`GetPitDestination` in this build reads its placement constants -- the yaw offset
and the pit-spot clearance search factors -- from a **mutable `.data` block**,
not from `.rdata` literals. Studio 397 can therefore retune placement without
moving a single address, and did.

So the tool reads them out of the running process at placement time
(`EngineTunables`). The profile holds the addresses, plus a snapshot used only as
a fallback. That splits patch day cleanly:

| What changed | What happens |
|---|---|
| A tunable was **retuned** | Picked up on the next placement. No profile change, no publish, no download. |
| The block **moved** | The read fails its range check and it becomes an ordinary `reanchor` job. |

The **range checks are the whole safety argument**. A stale address does not
fault; it returns four plausible floats, and plausible floats place the car
somewhere plausible and wrong. Bounds are deliberately wide -- they reject
nonsense, not tuning -- and include a subnormal check (no human types `1.4e-45`)
and relational checks (`step < max`, `start <= max`), because four individually
sane numbers can still be an insane set. If the live read *and* the fallback both
fail, `place` refuses; it never guesses.

---

## How a user survives a game patch

Users never run `reanchor`. They download the app, not the repository.

1. LMU updates. The build hash changes and no local profile matches.
2. The app says so specifically -- "new game build", not "something failed" --
   and offers to fetch the profile for that exact build.
3. It asks **once**, with an "always" answer stored in `data/fetch-consent.json`.
   Absent file means not granted.
4. On agreement it downloads
   `raw.githubusercontent.com/Kyraws/inactive-reset/main/offsets/<HASH8>.json`.
   A 404 means "not published yet", unambiguously.

This is the **only outbound connection in the project**; everything else binds to
127.0.0.1. The host is hard-coded with no configurable base URL, because a tool
that writes another process's memory should not have a steerable download URL.
The downloaded profile must parse *and* declare the exact build hash requested,
or it is discarded before it touches disk. Dropping the JSON into `offsets/` by
hand always works and needs no network at all.

---

## Build identity

The on-disk exe is wrapped by **Steamstub** (`.bind` section), not EasyAntiCheat.
That is why analysis needs a runtime dump — and why launching
`Le Mans Ultimate.exe` **directly** gives a fully decrypted image with no
anticheat in the process tree. Launch it that way; the tool refuses if EAC is
present.

The 2026-08-11 patch did **not** bump the file version. Identify builds by hash,
never by version.

---

## What works, verified against the running game

Build gates (process, anticheat, hash, probe) · rules read and write, both
mechanisms · capture from shared memory · calibration and checkpoint handling ·
placement plan with zero residual · guarded 24-byte write and restore · arrival
verification · CLI · web UI · windowed app · `reanchor` · install discovery
through Steam's library configuration.

**Not yet verified:** starting the game. Both entry points are located and
every launch gate has been exercised, but neither `launch direct` nor
`launch eac` has been watched actually starting Le Mans Ultimate.

---

## Driving verification of build 0F6DCAC1, 2026-08-21

The reanchor was verified by driving, because it re-derives ADDRESSES and can
never speak for BEHAVIOUR. Three placements at Circuit de Barcelona in the
Richard Mille AF Corse #50, against the candidate profile, not a published one.

| run | error | fwd | lat | bearing |
|---|---|---|---|---|
| `cp-023328` run 1 | 0.7107 m | +0.502 | +0.503 | 45.1 deg |
| `cp-023328` run 2 | 0.7104 m | +0.502 | +0.503 | 45.1 deg |
| `End of Lap` | 0.7047 m | +0.495 | +0.502 | 45.4 deg |

**The addresses are correct.** Every build gate passed against the live process,
including the probe at the re-derived `0x00A7E2D0`. The 24-byte write applied and
restored, the pit flag and sector writes read back, arrival verified. The two
identical runs repeat to **0.28 mm** — better than the 1.5 mm recorded on the
previous build, and not something a wrong address can produce.

**The behaviour changed anyway.** The placement error is constant in the VEHICLE
frame — two points 4 km apart on track, headings +29.6 deg and -23.4 deg, same
offset to within 7 mm — but its magnitude moved from **0.567 m on `1AC2F605` to
0.710 m on `0F6DCAC1`**, and the lateral component appears to have flipped sign.

This is not the 2026-08-11 failure repeating. That patch retuned engine
constants; this one did not. `yawOffsetDegrees` (45.0), `searchStartFactor`
(0.55), `searchStepFactor` (0.1) and `searchMaxFactor` (1.5) are **byte-identical
across both builds**. The calibration is the same file. Everything the tool reads
held still and the car still landed 25% further out, so what moved is the
engine's own placement geometry.

It is also the exact hazard the reanchor documentation claims: every gate green,
every address right, placement quietly worse.

### Why the two-checkpoint test does not discriminate

The plan was to place at two checkpoints "whose range from the pit spot differs",
so that a rotation error (which scales with range) could be told from a
translation error (which does not). **Checkpoints cannot do that.** Range is set
by the calibration constant `D` in `rest = destination + D*heading + (0,H,0)`, so
every checkpoint places at the same distance; measured, both are 1.9269 m. Nor
does varying the heading help: a rotation of the drift vector and a fixed
vehicle-frame translation are both constant in the vehicle frame.

The only knob that changes the range is `D` itself. Placing with `D` deliberately
altered would separate them — a rotation error scales with it, a translation
error does not. That is a diagnostic probe and NOT the re-calibration the
advisory forbids, which is about absorbing the miss into shipped constants.

### It is not ride height

Ride height moves the car vertically. The vertical error is 0.013 - 0.052 m, so
`H` is right; the miss is entirely horizontal.

### The pi/4 in the written orientation is correct

`ori[1]` differs from the target yaw by exactly 0.785398 rad in every plan. That
is the tool compensating for the engine's `yawOffsetDegrees = 45.0`, by design,
and it is unchanged across both builds. It is not the defect, despite the
measured error bearing also sitting near 45 deg.

## Reanchor: what it can re-derive, and what it cannot

Five techniques. Each refuses rather than guesses, and what each one cannot do
is a property of the patch, not a bug.

| technique | finds | fails when |
|---|---|---|
| masked signature | a function that MOVED | the function was RECOMPILED |
| reference vote | a datum, via the instructions referencing it | those instructions were recompiled |
| value anchor | a datum, via the CONTENT around it | the content is volatile runtime state |
| reference profile | a datum, via how MANY instructions read it and what they look like | it has only a reference or two, or a neighbour is read by the same code |
| data fingerprint | a RECOMPILED function, via the globals it reads | it reads no globals, or its globals are still unresolved |

Measured on build `0F6DCAC1` (2026-08-20, `1.4.0.0` → `1.4.1.3`, module shrank
by `0x3B000`): signature and reference matching alone left **20 of 40**
addresses unresolved. Value anchoring recovered all four engine tunables — a
run of floats (`0.55, 0.1, 1.5, 25, 45`) still present verbatim and unique in
the new image — taking it to 16. `--infer-adjacent`, which is opt-in and writes
confidence `I`, takes it to 8.

The deltas are **not uniform**: one region moved `-0x2B550`, another `-0x2C010`.
A single global shift would have been wrong for half the profile, which is why
adjacency inference demands several agreeing neighbours and is never the
default.

### Data before code, and why the order is not arbitrary

The first three tiers all find code first and read data off it. The last two
invert that, and the inversion is the point.

A recompiled function cannot be found by its bytes — that is arithmetic, not a
missing feature. But it still does the same job, so it still reads the same
globals. Re-derive the globals first, and the function becomes findable as the
one place in the new image that reads all of them together.

So `reanchor` runs its tiers to a **fixpoint** rather than in one pass: every
address that resolves is a seed for the next round, and the run ends when a
round resolves nothing new. What is left over is the refusal list.

On `0F6DCAC1` this order is what unblocked the build. Four data addresses
resolved by reference profile — `spotTable.garPosTable`, `containers.table`,
`rules.trackLimits.config` and, by delta corroboration,
`rules.trackLimits.derivedFlags.0`. Feeding the four spotTable addresses back in
as a fingerprint then located `getSpotTransform`, which is also `probe`, at
`0x00A7E2D0`: 4 of 4 targets, length `0x279` → `0x278`.

### These tiers are statistical, so agreement is the only evidence

A reference profile scores candidates; it does not prove one. Measured on this
patch, correct answers beat their runner-up by ratios from 1.5x to 3x — and the
runner-up for `garPosTable` was `pitPosTable`, eight bytes away and read by the
same functions. **No threshold separates right from lucky.** An address earns
`E` by two INDEPENDENT techniques agreeing, never by one technique's own
confidence in itself.

Delta arithmetic is not one of those independent techniques. `--infer-adjacent`
applies a neighbour's delta, so "the inferred address matches the regional
delta" is a restatement, not a corroboration.

### Counting references means deduplicating them

`BuildReferenceIndex` reports a REX-prefixed reference **twice**: `48 8B 05 disp`
decodes at its own address with length 7, and `8B 05 disp` decodes one byte
later with length 6, and both resolve to the same target. Harmless for a
majority vote, where it scales every tally alike. Not harmless where the count
IS the evidence — undeduplicated, one reference looks like two and clears any
minimum a caller sets. `RemapDataByReferenceProfile` collapses sites within four
bytes of each other before counting.

### Sample the references, do not count them all

Every reference site costs a masked search of the whole new image, so reference
voting costs sites x 64 MB. `containers.pointer` has 860 sites and only 121 of
them produced a unique match on `0F6DCAC1` — 739 searches of 64 MB, for nothing.
A single address took minutes and a full run never finished.

The vote exists to catch a **dissenter**, and a sample catches one as well as an
exhaustive count does: eight agreeing votes and eight hundred say the same
thing. So sites are ordered by how distinctive their surroundings are — a window
of varied bytes pins a location down, a window of repeated bytes does not — then
capped, and the search stops as soon as enough agree with no dissent.

Whole-profile run time went from hours to **four minutes**, which is what makes
it usable inside a hypothesis loop rather than once per patch.

`DataMatch.Sites` therefore counts references EXAMINED, not references
available. A small number there is the search being efficient, not the evidence
being thin.

### A length swing is a wrong answer, not a weak one

The fingerprint tier refuses when the function it lands on differs sharply in
length from the old one, rather than resolving with lower confidence. The
distinction is not pedantic.

Measured on `0F6DCAC1`: early in round 2, only two of `probe`'s four globals had
been translated, and fingerprinting on those two landed `0x1BC0` away from the
truth, in a function of length `0x1BA` against the old `0x279`. Later in the
same round `getSpotTransform` — **the same address** — had all four globals and
landed correctly.

Had the swung match been accepted as `I`, the damage would not have stopped at
one wrong address. The probe-byte refresh fires on anything not marked `U`, so
the tool would have read the new image at an address it had got wrong and stored
those bytes as what the build gate expects. The gate would then compare the new
image against itself and could never fail — the exact defect "the probe must
never vouch for itself" exists to prevent, arriving through a new door.

Refusing also produces the better answer, because a later round has more seeds
than an earlier one. Deferred, `probe` resolved correctly in round 3.

### String anchoring — works, not yet a tier

LMU is a **logging build**: functions reference their own names and build-server
source paths, e.g. `'Entered Slot::Restart(%d)'` beside
`'D:\bamboo-agent-home\...\rFactorSource\Source\slot.cpp'`. Strings survive
recompilation completely, so this finds exactly what masked signature search
cannot.

Find the unique string, find the `lea r64,[rip+disp]` referencing it, walk back
to the nearest `INT3`-padded prologue. It self-validates: run it against the old
image, where the answer is known, before trusting it on the new one.

It resolved `slotRestart` and `assignSpotIndices` on `0F6DCAC1`, and
`assignSpotIndices` was later reproduced independently by the data-fingerprint
tier — same address, `0x00CF3880`, from unrelated evidence.

`slotRestart = 0x00CFA230` remains a string-anchoring result that no implemented
tier reproduces; the fixpoint run still reports it unresolved. It must NOT be
hand-written into a profile. Port the technique and let the tool derive it, or
the profile carries an address nothing can re-check next patch.

The script is `tools/reanchor-research/stranchor.py`.

### The capture state is part of the dump

A dump taken in the menus and a dump taken in a session are not
interchangeable, and confusing them wastes a cycle. Code is byte-identical
between them, so code techniques are unaffected — but the spot table and the
container array are **zeroed in menus**, which makes every content-based data
technique useless and makes a stale address look resolved.

Record the state in the filename. The captures in
`G:\LMU_Plugin\analysis\dumps\` follow
`LMU_runtime_<build>_<state>.bin`; the README there is stale and still describes
only the older `266D1AF6` set.

For validating container work specifically, capture with a **large, multi-class
grid**. A single-car session exercises slot 0 and nothing else, so a wrong
stride is invisible; a 38-car field makes `slotIndex == i` hold across 40 slots
and an 11 MB span, which is what confirmed the stride `0x472C8` survived
`1.4.1.3` unchanged. `garageIndex` is not a useful discriminator — it reads `0`
for every slot even in the garage.

The later `1.4.2.0` build does **not** preserve that stride: its mapped code
uses `0x47308` at 314 indexed-container sites. A reanchored array base with
the old stride is unsafe even when its RVA references resolve. The placement
gate now requires the live code's dominant stride to match the profile.

### What is still out of reach

`derivedFlags.0` is refused by the reference profile: it is written by
`mov word ptr [rip+x], 0x0101`, and the `0x66` operand-size prefix is not one of
the forms `DecodeRipRef` handles. The decoder is deliberately partial — an
unhandled form yields no reference rather than a wrong one — so this is a
refusal, not a miss. Handling `0x66` would resolve it, at the cost of touching a
primitive the older tiers also depend on.

Heap-resident data cannot be validated from a module dump at all. `pitPosTable`
and `garPosTable` are pointers; a dump confirms the pointer variable is where
the profile says, and can say nothing about what it points at.

### The probe must never vouch for itself

`reanchor` used to refresh the probe bytes by reading the NEW image at the probe
RVA — **even when that RVA had not been re-derived**. The gate then compared the
new image against bytes taken from the new image at the same address, so it
could never fail. A reanchor that failed on half the profile produced one whose
every gate passed, and `status` printed stale track-limits flags as a confident
`0`.

Three fixes, all tested:

- `ProbeSpec.Rva` is an `Rva`, not a bare `ulong`, so its confidence is parsed
  and enforced. `VerifyProbe` calls `Require` before reading anything.
- `reanchor` refreshes the probe bytes only when the probe was re-derived, and
  says loudly when it was not.
- `RulesController.Read` no longer reaches through `Rva.Value`. An unresolved
  rule reports as unread (`?`), never as a number.

The general lesson is the one this codebase keeps relearning: `.Rva.Value`
bypasses `Require()`, and every such bypass turns a refusal into plausible
nonsense.

---

## Current limitations — read before trusting a placement

**1. A new game build needs a verified offset profile.** The executable hash
prevents an old profile from being used after a patch. `reanchor` finds moved
addresses, but a maintainer still has to verify changed engine behaviour in the
running game before publishing the profile. See [`HEADING_BUG.md`](HEADING_BUG.md)
for the earlier heading diagnosis and its correction.

**2. The cut flags re-arm themselves.** Session init sets derived flags 0 and 1
together with a single word write, so returning to the garage silently re-enables
lap invalidation. The UI shows the raw flag values read back from memory every
refresh — trust those, not the toggle. Nothing re-applies them automatically.

**3. Derived placement constants have a measured limit.** The tool can derive
`D` and `L` for an uncalibrated vehicle, then learn from each placement. Its
borrowed vertical constant `H` has only been confirmed on tested combinations.

**4. Sector restoration is measured; the pit-flag clear is a secondary guard.**
The sector write is the fix and was confirmed against the running game. The
pit-flag clear removes a genuine but narrower race — the engine clears that flag
itself in about 33 s, and a final-sector placement can reach the line in ten. Do
not let anyone re-describe the pit flag as the cause; that mistake has already
been made once here.

---

## Corrections carried in — do not repeat these

- **"Nothing reads this" was often a tooling limit, not a fact.** Early scans
  recognised only `lea` / `mov r64`, missing every byte-sized access and every
  instruction with an immediate after the displacement. The decoder in
  `Reanchor.DecodeRipRef` handles those. `Steward Penalties` was declared to
  have zero readers; it has **seven**.
- **RIP-relative displacements are not always the last four bytes.**
  `cmp byte ptr [rip+X], 0` carries an immediate *after* the disp32, so the
  target is not `site + 7 + disp`. Getting this wrong points you at an adjacent
  byte, which reads fine and does nothing.
- **Signature searches must mask displacements.** Bytes 12–15 of the probe are a
  `disp32` and change on every rebuild even when the function is identical. The
  first 12 bytes are the stable part.
- **A code-only patch cannot invalidate a calibration.** `D` and `H` are
  physical geometry. Check whether the *car or track content* changed, and
  whether `kSlotReset`/`kSlotRestart` differ beyond relocated displacements.
- **`LibraryImport` does not append the A/W suffix** that `DllImport` with
  `CharSet` does. `OpenFileMapping` resolves to nothing; the entry point must be
  explicit.
- **A stale note about which path is exercised is worse than no note.**
  `TARGETS.md` recorded for months that the UI place flow was "unproven" and that
  the CLI was the well-exercised path. The reverse was true: the windowed app is
  the front end in use. Because the page was treated as the lightly-used path, it
  shipped without ever reporting the sector write — the single field that decides
  whether a placed lap counts — and the person relying on it daily could not see
  the fix working. Check which path is actually used before deciding which one
  deserves scrutiny.

---

## Build and run

    dotnet build
    dotnet test
    dotnet run --project src/InactiveReset.Cli -- status
    dotnet publish src/InactiveReset.App -c Release -o dist

    inactive-reset status list watch capture plan place rules lap serve

    inactive-reset-reanchor dump reanchor      # maintainer only, never shipped

A note for anyone editing on Windows: **Windows PowerShell 5.1 `Get-Content -Raw`
decodes UTF-8 as ANSI.** Round-tripping a source file through `Get-Content` /
`Set-Content -Encoding utf8` silently corrupts every non-ASCII character.
`Page.cs` is deliberately pure ASCII as a result.

---

## What comes next

[`TARGETS.md`](TARGETS.md) holds the candidates with what is already known about
each. Nothing there is committed. The next likely candidate:

1. **Steward Penalties**, reopened now that the reference scan is correct.
