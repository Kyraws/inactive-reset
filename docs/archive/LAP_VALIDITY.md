> Archived on 2026-10-07. Historical evidence and superseded guidance; not current operating instructions. Build-specific addresses, measurements, and conclusions apply only to the recorded experiments. See the [current architecture](../ARCHITECTURE.md) and [status](../STATUS.md) before using this material.

# Lap validity

Why the first lap after a placement used to be thrown away, and what the tool
does about it.

**Everything below was measured against the running game on 2026-08-12**, build
`1AC2F605`, Circuit de Barcelona, Richard Mille AF Corse 296 GT3. Where a claim
is inferred rather than observed, it says so.

Read [§ What this document got wrong first](#what-this-document-got-wrong-first)
before trusting any older description of this behaviour.

---

## The symptom

Teleport to a point on the racing line, drive to the start/finish line, and the
crossing does nothing at all. The lap counter does not move, no lap time is
recorded, and you must drive a further complete lap before timing resumes.

Total cost: the distance from the placement to the line, **plus one whole lap**.

---

## The cause: the sector sequencer

The engine only accepts a start/finish crossing as a lap completion when it
believes you are in the **final sector**. It tracks that in a single field:

| Container offset | Name | Meaning |
|---|---|---|
| `+0x1CEA8` (int32) | `sector` | current sector, zero-based |

Measured across one clean lap:

```
05:14:33  lap=3  sector=0  lapStartEt=863.494    just after start/finish
05:15:35  lap=3  sector=1  lapStartEt=863.494    sector 1 line
05:16:19  lap=3  sector=2  lapStartEt=863.494    sector 2 line
05:16:53  lap=4  sector=0  lapStartEt=1111.035   start/finish: lap++, ET updated
```

`0 → 1 → 2 → 0`, and the lap completes only on the `2 → 0` edge.

**`Slot_Reset` sets `sector = 0`.** That is correct for its normal job — you are
leaving the pits at the start of a lap. The placement then breaks the
assumption by putting the car mid-lap. If the car lands *past the last sector
line*, it crosses no sector lines on the way to start/finish, arrives still
reading sector 0, and the engine discards the crossing.

Measured, teleporting to 4311 m (final sector) and driving to the line:

```
05:19:58  lap=5  sector=0  lapStartEt=1170.652  countLapFlag=1
          ... crossed start/finish ...
          lap=5  sector=0  lapStartEt=1170.652  countLapFlag=1     NOTHING MOVED
```

Then, with the sector written to 2 and **nothing else changed** — same
checkpoint, same placement, pit flag already 0:

```
05:20:28  lap=5  sector=2  lapStartEt=1170.652  countLapFlag=1     the write
05:20:53  lap=6  sector=0  lapStartEt=1281.541  countLapFlag=2     ACCEPTED
```

Lap counter incremented, lap-start time updated, lap counting promoted. One
field, one value, decisive.

### Only final-sector checkpoints are affected

- Placed **before the sector-1 line** — crosses both sector lines on the way
  round, reaches 2 naturally. Already worked.
- Placed **between the lines** — crosses the sector-2 line, reaches 2. Already
  worked.
- Placed **after the sector-2 line** — crosses nothing, stuck at 0. **Broken.**

So the value to write is always `2`, with no per-track sector table required.

Writing it when the car is placed *earlier* in the lap is harmless, and this was
measured rather than assumed. Placed at 477 m (sector 0) with the sector forced
to 2, the crossing was still accepted and the next lap timed:

```
05:24:13  lap=7  sector=2  lapStartEt=1316.048  countLapFlag=1   placed, sector written
05:26:08  lap=8  sector=0  lapStartEt=1458.984  countLapFlag=2   crossing ACCEPTED
```

Note what did **not** happen: `sector` never read 1 on the way round. The engine
did not reset it when the car passed the sector-1 line, so it does not appear to
move the sector backwards. The prediction that "the real sector lines overwrite
it" was wrong; the outcome is right for a different reason. The cost is that a
lap driven this way probably has no meaningful sector splits — untested, and it
does not affect whether the lap is timed.

---

## The other two fields

These are real, and they are what the engine uses to decide whether an *accepted*
lap is timed. They were not the cause of the lost lap, but they are worth
knowing about because they look like it.

| Container offset | Name | Meaning |
|---|---|---|
| `+0x1CFF0` (int32) | `countLapFlag` | the lap you are **on** |
| `+0x1CFF4` (byte) | `lapCountsNext` | latch for the lap about to **start** |
| `+0x1CFF8` (double) | `lapStartEt` | when the current lap started |

`countLapFlag` is LMU's inheritance of rF2's `mCountLapFlag`; the binary still
carries `COUNT_NEITHER`, `COUNT_LAP_ONLY` and `COUNT_LAP_AND_TIME`. 0 counts for
neither, 1 is counted but **not timed** — what an out-lap gets — and 2 is counted
and timed.

At an accepted crossing (`0x00D0428A`):

```c
if (gamePhase != 3) {                      // not a formation lap
    countLapFlag  = lapCountsNext ? 2 : 1;
    byte[+0x1D000] = 0;                    // clear off-track-this-lap
    word[+0x1CFF4] = 0x0001;               // re-arm the latch
    double[+0x1CFF8] = ET;                 // lap start time
}
```

and immediately after (`0x00D04396`):

```c
if (session is NOT a race && pitFlag && countLapFlag == 2)
    countLapFlag = 1;                      // the out-lap rule
```

Races are exempt — the check is a `JBE` on `session - 10`, and 10..13 are the
races. Practice and qualifying are not exempt.

`lapStartEt` is worth its own mention: **the promote block writes it and nothing
else does.** That makes it the only field that can distinguish "the handler ran
and demoted" from "the handler never ran" — which look identical in
`countLapFlag`. It is the reason the real cause was found.

---

## The pit flag: a secondary guard, not the cause

`pitFlag` (`+0x8FD0`, published to shared memory as `mInPits`) feeds the out-lap
demotion above. `Slot_Reset` sets it. The tool clears it after a placement.

But it is **not** why laps were being lost. Measured after a teleport, with no
intervention at all:

```
05:01:57  pitFlag=1  pitState=5     the teleport
05:02:30  pitFlag=0  pitState=0     cleared on its own, 33 s later
```

The engine clears it unaided. Clearing it still earns its place, narrowly: 33
seconds is slow, and a car placed in the final sector can reach the line in
about ten. So the clear removes a genuine race between the flag clearing and you
arriving — it just was never the thing costing a whole lap.

Clearing it is a complete operation, not half of one: the pit-entry and pit-exit
handlers reach the same byte as `controller[0x2050]` where
`controller = container + 0x6F80`, and `0x6F80 + 0x2050 == 0x8FD0`. There is no
second copy to leave inconsistent, and a later genuine pit entry — which refuses
to run unless the byte is 0 — still works.

`pitState` (`+0x8FC0`, the same object at `controller[0x2040]`) is deliberately
left alone. It clears with distance on its own and is what the placement already
waits on for the speeding penalty.

---

## What the tool does

After a placement, once the pit-state wait is done, it writes two values:

1. `sector = 2` — makes the next start/finish crossing count as a lap. **This is
   the fix.**
2. `pitFlag = 0` — stops that lap being demoted to an out-lap if you get there
   fast.

Both are read back and verified, and both report their before-value.

    inactive-reset lap                                     # read only
    inactive-reset lap --set-sector 2 --accept-write       # sector only
    inactive-reset lap --clear-pit-flag --accept-write     # pit flag only
    inactive-reset place <cp> --accept-write               # both, by default
    inactive-reset place <cp> --accept-write --keep-sector --keep-pit-flag

---

## What this document got wrong first

The first version of this file claimed the lost lap was caused by the out-lap
rule: that a placed car keeps `pitFlag` set, so its first crossing gets demoted.
That was wrong, and the tool shipped a fix for it before anyone drove the car.

Three things went wrong, all the same shape:

1. **A verified mechanism was mistaken for the operative one.** The out-lap rule
   is real, and every instruction quoted for it was correct. It just was not what
   was happening.
2. **"No path clears this" was asserted from an incomplete search.** The
   geometry-driven `pitFlag` recompute is gated on remote cars, so it was
   concluded nothing else could clear it. Something else does. This is the same
   error the project already records under *"nothing reads this" was often a
   tooling limit, not a fact* — made again, one directory over.
3. **An ambiguous non-change was read as confirmation.** `countLapFlag` staying
   at 1 is what you see whether the handler demoted it or never ran at all.
   Only adding `lapStartEt` — a field with exactly one writer — separated them.

The general lesson is cheap to state and was expensive to relearn: **a field
that changes for exactly one reason is worth more than three fields that change
for many.** When a hypothesis predicts "no change", find a witness that has to
move if you are right.
