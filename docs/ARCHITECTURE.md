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

Patch day is two commands:

    inactive-reset dump before.bin        # once, while the current build works
    inactive-reset reanchor --old-dump before.bin --base offsets/<old>.json

`reanchor` captures the running game, re-derives every address (masked signature
search for code, majority vote across referencing instructions for data),
refreshes the probe bytes and build identity, and writes a new profile.

Shared-memory offsets come from `tools/dump-sdk-offsets.cpp`, which asks the
compiler for `offsetof` against LMU's own SDK header. Re-run it if LMU ships a
new `SharedMemoryInterface.hpp` — the header is not redistributable and is not in
this repo; see [`tools/README.md`](../tools/README.md). Note
`vect3ComponentSize` is **8** — positions and orientations are `double`; reading
them as `float` gives plausible nonsense, so the loader asserts it.

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
verification · CLI · web UI · windowed app · `reanchor`.

---

## Known defects — read before trusting a placement

**1. Placement is ~0.57 m off.** An **11.633 degree heading error** in the
orientation-to-heading conversion. It is a *rotation*, not a translation: `H` is
correct to 1.8 mm, and run-to-run repeatability is ~1.5 mm, so the mechanism is
sound and only the aim is wrong. Full analysis in
[`HEADING_BUG.md`](HEADING_BUG.md).

**Do not re-calibrate to fix it.** That bakes a rotation into two translation
constants and is correct at exactly one distance.

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

    inactive-reset status list watch capture plan place rules lap serve dump reanchor

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
