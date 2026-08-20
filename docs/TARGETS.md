# Targets

Things worth doing. **Not commitments** — what is open, why it matters, and what
is already known, so that picking one up does not start from scratch.

Grouped by kind, not priority. Entries are named rather than numbered so that
adding one does not renumber the rest. Where a question is already answered in
depth elsewhere, this file holds the pointer and the one fact you need to avoid
the wrong move — not a second copy of the analysis.

---

# The game

Things not yet known or not yet fixed about how Le Mans Ultimate behaves.

## The 11.633 degree heading error

Placement lands ~0.57 m off. Fully characterised in
[HEADING_BUG.md](HEADING_BUG.md) — read that, not a summary.

**Do not fix it by re-calibrating.** That bakes a rotation into two translation
constants and is correct at exactly one range. `H` is already correct to 1.8 mm,
which is the evidence that the vertical constant is not the problem.

**Superseded in part, 2026-08-20.** `GetPitDestination` has been located in this
build (`0x00D4C310`, was `0x00D4C700`) and the yaw arithmetic read directly. Two
engine constants changed in the 1AC2F605 patch and the profile was still carrying
the old ones: the yaw offset is now **45 deg** held in `.data` as degrees and
converted at runtime (was a 35 deg `.rdata` radian literal), and the pit-spot
search start is now **0.55 × width** (was 0.2). Both are corrected in
`offsets/1AC2F605.json` at confidence `I`.

**This is not yet a fix for the 11.633 deg error.** The measurements in
HEADING_BUG.md are dated 2026-08-07, before the patch, so they describe the build
whose constants the model got *right*. What the finding does explain is that any
placement on the *current* build has been running with a 10 deg wrong offset and a
0.35 × width wrong search start.

**Next, and it needs the game running:** place at two checkpoints whose range from
the pit spot differs materially, with the corrected constants. That separates the
two candidate faults — a wrong `D` versus a destination point that is not where
the model puts it. One sample cannot, which is why the old single-range data
supports both readings.

The old `kSlotRestart` branch-polarity lead is **not** resolved, but it is now
lower value: the offset it was meant to explain turned out to be a data change,
not a mode change.


## Track-limits flags re-arm themselves

Session init executes `mov word ptr [0x03B36274], 0x0101`, setting derived flags
0 **and** 1 together. Returning to the garage therefore silently re-enables lap
invalidation, and flag 1 is the one that matters.

**Observed:** after a placement and a garage return, flag 1 read `1` while flags
0 and 2 stayed `0`.

**Consequence:** turning track limits off is not durable across exactly the
transition this tool is used around, so they get re-toggled every session.

**Deliberately left manual.** Re-applying after every placement was considered
and declined; watching the flags and re-writing on change is a background writer,
which is a bigger commitment than it looks. The UI shows the true state read back
from memory, so the drift is at least visible.

## Sector splits after a forced sector write

`place` writes `sector = 2`, which is what makes the first start/finish crossing
count. Measured; see [LAP_VALIDITY.md](LAP_VALIDITY.md).

**The loose end:** the engine did not reset the sector to 1 when the car passed
the sector-1 line with the field already forced to 2, so a lap driven that way may
carry no meaningful sector splits. Untested, and it does not affect whether the
lap is timed.

If splits ever matter, the fix becomes conditional on where the checkpoint is,
which needs per-track sector boundaries — cheap to learn by logging lap distance
at each sector transition over one driven lap.

## Capture at speed

`capture` has only been exercised from the garage. Capturing while driving is the
actual use case, and shared memory updates at a different cadence than the
physics thread. Worth confirming the pose is coherent at 200 km/h before trusting
a checkpoint taken there.

## Calibration for more track/vehicle pairs

Only Circuit de Barcelona with the Richard Mille AF Corse 296 GT3 is calibrated.
`D` and `H` are per track **and** per vehicle, so every combination needs its own.

**Worth doing only after the heading error is resolved** — otherwise each new
calibration inherits the same rotation.

---

# The codebase

Structural friction. None of these change what the tool does; all of them change
how easily it can be changed or verified.

## Rules are identified by their display label

`RulesController.ReadAll()` returns human labels like
`"Pit-speeding gate (Flag Rules)"`, and the UI recovers identity by
substring-matching them — `r.Name.Contains("Pit-speeding")` and
`r.Name.Contains("lap invalidation")` in `PlacementRunner`. Rename a label for
clarity and the toggle silently reports `known: false`. No compile error, no test
failure.

Meanwhile `PlacementService` reaches around `RulesController` entirely to re-read
`Offsets.Rules.FlagRules` itself, so there are two routes to one fact.

**Proposal:** a stable `RuleId`, with the display label as a separate field on
`RuleState`. `RulesController` gains `IsEnabled(RuleId)`, which `PlacementService`
uses instead of its private re-read — keeping its deliberate "assume the penalty
is live if unreadable" fallback, which is good judgement currently living where
nothing else can reach it.

## The placement sequence has no test surface

`PlacementService.Place` takes a `GameSession`, which wraps a real process and a
real memory handle, and is sealed. So the sequence — gate, write, wait for Drive,
sample arrival, restore, write the sector — can only be exercised by starting the
game and pressing Drive. Everything the test suite covers is pure: the placement
math, the capture round-trip, the offset profile.

**Proposal:** put a seam at the *slot* rather than at memory. An interface of
about five members — control owner, pit state, arrival, position, spot-entry
write — with a live adapter and a scripted one, plus an injected clock so the
timeout paths do not take two minutes to test.

**Honest about the value.** The original argument was safety: a wrongly reported
CLEAR tells a driver to accelerate while carrying pit state. That argument is now
weak, because a placement disables the pit-speeding penalty and the wait is
skipped. What remains is narrower but real — the 24-byte write and its restore,
the arrival check, and the sector write have no test surface, and the sector
write is the one the tool is depended on for.

**Open design questions**, deliberately unanswered:

- What sits behind the seam — reads only, or also the spot write, the
  sector/pit-flag writes, and the pit-penalty query? If only the reads, the
  untestable part just moves.
- Does the clock go behind the same interface, or its own?
- Does `Plan` move behind it too? Hand-built `PlacementPlan` fixtures rot
  silently: a new required property gets a plausible value and the test keeps
  passing while meaning something different.
- What do the tests assert — hand-written branch scenarios, or replaying the
  measured traces in `LAP_VALIDITY.md`?

## Every UI poll re-attaches and re-hashes the executable

`PlacementRunner.State()` opens a fresh `GameSession` on every `/api/state` poll,
and `GameSession.Attach()` SHA-256s the whole game executable each time. The page
polls every 250 ms while a placement is running and every 700 ms otherwise. The
same method also opens shared memory and re-reads every checkpoint and
calibration from disk.

**Proposal:** split `State()` by rate of change — a held session snapshot, a disk
catalogue, and the genuinely live sample.

**Measure before acting.** On a warm file cache the hash may be cheap enough not
to care. And do **not** weaken the re-hash inside `SpotWriteTransaction.Begin`:
that one is deliberate and closes a real window where the game was replaced
underneath a long-lived session.

## `--sector banana` is accepted silently

The `--sector` parse in the CLI's `place` falls back to the default when the
value will not parse, so a typo places with sector 2 and says nothing. Three-line
fix.

The wider point — that every verb re-parses `args` by hand — is **not** worth
acting on. An argument-parsing library would add a dependency to a project that
deliberately runs at zero.

---

# Settled

Kept as a record of what was decided, so it is not reopened from scratch.

- **Sector restoration.** `place` writes `sector = 2` after placing, which is
  what makes the first start/finish crossing count as a lap. The value is a
  constant because only final-sector placements are affected, and writing it for
  an early placement was measured to be harmless. See
  [LAP_VALIDITY.md](LAP_VALIDITY.md), whose post-mortem is more useful than this
  line. One loose end remains, above.

- **The UI place flow.** Long recorded here as "unproven". It was not — the
  windowed app is the front end in use. See `docs/ARCHITECTURE.md`, § Corrections carried
  in, for what believing otherwise cost.

- **The pit-speeding penalty is not a choice.** Every placement disables it.
  There is no case where a stop/go is wanted for a position the tool put the car
  in, so the UI toggle was removed in favour of a status line.

- **The placement report.** Both front ends render `PlacementReport` without
  interpreting it, so an outcome cannot mean two different things in two places.
  This class of drift had already happened once: the page never reported the
  sector write.

---

# Deliberately out of scope, for now

- **Condition restore** (tyres, fuel, damage). Left in the predecessor. The REST
  channel exists there; `RepairAndRefuel` never worked, and whether the pit-menu
  path changes condition mid-session is unproven.

- **The boundary conversation.** The predecessor enforced capability by binary:
  separate executables, import auditing, one write-capable component. This
  rewrite has one executable that can write. That was a deliberate trade, not an
  oversight, and it has not been revisited.
