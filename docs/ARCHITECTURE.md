# Architecture

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
    tests/                   44 tests, including byte-exact fidelity vs the C++

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

## Known defects — read before trusting a placement

**1. Placement is ~0.57 m off.** An **11.633 degree heading error** in the
orientation-to-heading conversion. It is a *rotation*, not a translation: `H` is
correct to 1.8 mm, and run-to-run repeatability is ~1.5 mm, so the mechanism is
sound and only the aim is wrong. Full analysis in
[`HEADING_BUG.md`](HEADING_BUG.md).

**Do not re-calibrate to fix it.** That bakes a rotation into two translation
constants and is correct at exactly one distance.

Separately, and found later: the 1AC2F605 patch changed two engine constants the
profile was still carrying from the previous build — the yaw offset (35 deg to
45 deg, and it moved from an `.rdata` radian literal to a `.data` value in
degrees) and the pit-spot search start (0.2 to 0.55 of vehicle width). Both are
corrected in the profile. That is a *different* defect from the 11.633 deg error
above, which was measured before the patch and is still open.

**2. The cut flags re-arm themselves.** Session init sets derived flags 0 and 1
together with a single word write, so returning to the garage silently re-enables
lap invalidation. The UI shows the raw flag values read back from memory every
refresh — trust those, not the toggle. Nothing re-applies them automatically.

**3. Only one calibration exists** (Circuit de Barcelona, Richard Mille AF Corse
296 GT3). `D` and `H` are per track **and** per vehicle. `place` refuses without
a matching one.

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
each. Nothing there is committed. The two most likely to matter:

1. **The heading error** — the open lead is `kSlotRestart`'s branch polarity
   flip, and whether it changes which *mode* `GetPitDestination` is asked for.
2. **Steward Penalties**, reopened now that the reference scan is correct.
