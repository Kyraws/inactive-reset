# Reader sites in LMU build D9B92CA9

Static, read-only research on the mapped runtime dump at
`artifacts/LMU_runtime_D9B92CA9.bin` using Ghidra 12.1.2 headless. File offset
equals RVA. The candidate profile remains under `artifacts/`; this research does
not authorize placement with it. The table below is static evidence except
where noted.

| Field | Candidate RVA | Ghidra evidence |
|---|---:|---|
| `Flag Rules` | `0x01E10A38` | `0x00D1D3A6`: `cmp dword ptr [0x01E10A38], 2`, matching the old-build sequence at `0x00D02FD9`; whether this is the pit-speeding consumer is unverified |
| Track-limits stored setting | `0x01E10AB8` | `0x00D15AE8`: reads the byte before a branch |
| Track-limits effective flag 1 | `0x03B493B5` | `0x00CE649C` and `0x00CF0D8D`: compare the byte with zero; `0x00D9E130` also reads it |
| Track-limits flag 2 | `0x03B493B6` | `0x00CEDD18`: compares the byte with zero |
| Steward penalties | `0x01E14CD8` | `0x00B420F8`: compares the value with 2; its effect is still unknown |
| Pit spot table pointer | `0x01DFA578` | `0x00A90C27`: reads a pointer, indexes 32-byte entries, copies position fields |
| Garage spot table pointer | `0x01DFA5D8` | `GetSpotTransform` at `0x00A908C0` reads this pointer and 32-byte entries |
| Lap flag and latch | container `+0x1CFF0` / `+0x1CFF4` | `0x00CE85BA`: start/finish path writes both via base `RDI+0x160` / `+0x164` |
| Next-lap invalidation | container `+0x1CFF4` | `0x00CE698B`: writes zero via base `RSI+0x164` |
| Pit flag set | container `+0x8FD0` | `0x00EA208E`: writes one via base `RCX+0x2050`, with `RCX=container+0x6F80` in the prior build |

The old profile's `readBy` RVAs cannot simply be remapped by masked bytes: some
are stale even in the old dump, and some matches in this build land inside other
instructions. The useful procedure is to
find references to a candidate *value* in the new image and have Ghidra decode
each site. `inactive-reset-reanchor refs` prints candidate sites for that review.

PE unwind metadata places the Flag Rules reader inside function
`0x00D1C270–0x00D1DD49`. A direct call at `0x00D1602A` reaches the entry and
immediately overwrites `EAX` afterward, so that caller does not use an integer
return value. The function is large; an entry hook will show that it runs but
will not prove execution of the internal comparison. `tools/observer/` now
contains a separate, pass-through flag-function observer and a resolver test.

Live read-only check in the garage on build D9B92CA9: Flag Rules `1`, Track
Limits stored setting `3` (Relaxed), verified effective bytes `1` and `0`.
The flag-function observer fired repeatedly with value `1` before and after
each call; LMU remained responsive. This does not prove its internal reader
ran. A broader static reference pass found 34 direct references to Flag Rules.
The `0x00CF4DB0` function, reached from one path using the literal `Speeding In
Pitlane` at `0x00CEB299`, reads Flag Rules at `0x00CF4E36` and tests it against
zero and three. This is related static evidence, but not the observed stop/go
path or proof of the penalty decision gate.
`tools/observer/` initially hooked `0x00CF4DB0`, the branch reached by a
tail-jump at `0x00CEB2B2`. With Flag Rules `2`, the driver triggered a stop/go
penalty displaying `Speeding In Pitlane`, but this hook recorded no call; LMU
remained responsive. The same source function also has a second reference to
that literal at `0x00CEB2EA`, followed by a call at `0x00CEB2F7` to
`0x00CF4860`. The revised offline resolver identifies this second function
and passes the mapped-dump test. On a direct-launch Practice session with Flag
Rules `2`, the revised hook logged one `PIT_SPEEDING_PENALTY_CALL` (`mode=0`,
`code=10`, flag `2` before and after) when the driver received the stop/go on
leaving the pit lane. LMU remained responsive.

Tracing backward, `0x00CE59BA` loads a per-object accumulated value at
`RBX+0x27218`; `0x00CE59C2` compares it with a threshold; `0x00CE59C7`
checks Flag Rules against zero; `0x00CE59F5` calls the penalty-building
function at `0x00CEAEA0` with `DL` set according to a computed excess. That
function contains both `Speeding In Pitlane` message branches. The new resolver
locates this connected speed-gate call and both message paths on the saved
image, failing closed if any part is absent or ambiguous. On a fresh direct
launch, the dual-hook observer logged `PIT_SPEED_GATE_CALL` with
`positive_excess=1` and Flag Rules `2`, followed by
`PIT_SPEEDING_PENALTY_CALL` with `mode=0`, `code=10`, and Flag Rules `2` before
and after. Both calls used the same object pointer; LMU remained responsive.
The driver reported that the stop/go appeared about 1–2 metres before their
current position. This validates the live gate-to-penalty path for this build,
but not the exact spatial threshold or future-build stability.

Live rule-write check in Practice: setting Flag Rules from Full (`2`) to None
(`0`) took effect without another Drive press. With Track Limits still Default
(`1`) and the identified lap-invalidation flag still `1`, a deliberate cut on
the second lap did not invalidate it. Restoring Flag Rules to Full (`2`) while
the car was on track brought cut-related lap invalidation back. Thus Flag Rules
is a broader live gate, not a pit-speeding-only switch. Placement now restores
the prior Flag Rules value after its pit-state wait, but that restore timing
still requires a live placement test on this build.

An offline compatibility check against every saved prior mapped dump passed:
`266D1AF6`, `44C3EE9C`, `E669E121`, `C40C815F`, and two session states of
`0F6DCAC1`. The two oldest builds needed a larger bounded gap between the
message branches and a second validated penalty-function prologue. For each
dump the resolver found one path and failed closed after either the speed-gate
call or message literal was corrupted. This is structural compatibility, not
live hook or penalty-suppression validation on those builds.

## Repeatable offline evidence pass

Inspired by [ReAgent's bounded evidence packets](https://github.com/Dryxio/reagent),
`tools/ghidra/export-reader-evidence.ps1` runs Ghidra on a saved mapped module
dump and checks the curated seeds in `tools/ghidra/D9B92CA9-readers.tsv`. It
does not generate C/C++, call an LLM, attach to LMU, or write to the game.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ghidra/export-reader-evidence.ps1 `
  -Dump artifacts/LMU_runtime_D9B92CA9.bin `
  -Seeds tools/ghidra/D9B92CA9-readers.tsv `
  -Output artifacts/D9B92CA9-reader-evidence.json `
  -JavaHome 'G:\Tools\Java\jdk-21.0.12+8'
```

The current dump (SHA-256 `2E7EDBC3968B756C...`) yields nine direct
data-reference matches, three bounded instruction-context records, and one
explicit unresolved field (track-limits effective flag 0). This pass caught
an incorrect garage-table seed: `0x00A908C9` reads the spot count at
`0x01DFA618`, not the garage table pointer; the latter is read at
`0x00A90900`. The lap update window also shows writes at `0x00CE85C6`
(`RDI+0x160`) and `0x00CE85D3` (`RDI+0x164`).

On a later build, first derive candidate data RVAs and reader sites, then
curate a new seed manifest and run this check against that build's dump.
`direct-reference` means only that Ghidra decoded an instruction referencing
the claimed data address; it does not prove that the rule is consumed in the
intended situation or authorize the candidate offset profile for placement.

These instructions establish static reads and writes, not their complete runtime
conditions. A live observation of entering Practice, pressing Drive, and a lap
crossing is needed to establish that the same paths are exercised. Track-limits
flag 0 remains unresolved, and the candidate pit spot table pointer still needs
independent live validation before any placement write.

## Live debugger observation (2026-09-23)

A later direct-launch, in-process MinHook observer (`tools/observer/`) found
`GetSpotTransform` by a unique structural signature and recorded 18 of the
first 20 calls after invoking the original function. In the garage, its two
12-byte outputs decoded
to `(138.939, 0.493, 245.873)` and `(-0.007, -1.004, 0)` for the captured
calls. LMU remained responsive. The first logger lost two call lines and its
activation line to concurrent file writes; the source now serializes logging,
but that revision has not yet been injected. This validates this build's hook
and output observation, not the semantics of either output or a future build.

WinDbg attached to the offline Practice process and a one-shot hardware execute
breakpoint at module RVA `0x00A908C0` fired while the car was in the garage.
`RCX=0x00007ff735336178`, `RDX=0`, and the return address was module RVA
`0x00F6D0AB`. This confirms the garage transform function executes in this
build, but does not yet validate its output or the other candidate offsets.

**Do not detach from inside a hardware-breakpoint command.** The attempted
`qqd` at the breakpoint left exception `0x80000004` at RVA `0x00A908C0`;
Windows Error Reporting recorded an LMU APPCRASH at 01:39:18. The game was not
restarted during that attempt.

CDB 10.0.28000.2526 was then installed. A disposable process survived the
tested sequence: hardware breakpoint with `gc` in its command, a separate
`breakin.exe` request, and `qd` at the resulting ordinary break. The same
sequence captured a second garage-function hit in LMU. The game remained alive
after that detach. A later short trace at Flag Rules reader RVA `0x00D1D3A6`
confirmed the instruction bytes but recorded no hit in the garage. LMU was no
longer running after that trace. LMU's own trace records `Fatal Error 11` and
`Crash submitted to bugsplat` at 01:55. The local BugSplat minidump at
`%LOCALAPPDATA%\Temp\LeMansUltimateEA457JP3.dmp` shows `0xC0000005`, an
attempt to execute address `0x2`. The stack return at module RVA `0x005B5CCC`
follows a `call qword ptr [RAX+0x40]` at RVA `0x005B5CC9` in the mapped runtime
image (`RAX` comes from `[R14]`, and `R14` is passed as `RCX`), consistent with
an invalid virtual-call target. This is distinct from the earlier
`0x80000004` breakpoint/detach crash. The minidump lacks the relevant heap
page, so it cannot establish why that target became `0x2` or whether tracing
contributed. Do not infer that the rule reader is dead or that this debugger
workflow is safe for unattended use.

## Read-only spot and pit-state check (2026-09-23)

In a direct-launch Practice garage session on executable SHA-256
`D9B92CA9FF84302D6EEF31FDE2074A0CA027D51C5F33AAF290EE0DD1A1484CCB`,
the candidate spot globals read as pit pointer `0x0000021D82806990`, garage
pointer `0x0000021DAEABA280`, multiplier `104`, and count `1`. Slot 0 reported
pit and garage indices `0`. Garage entry 0 gave `(138.939, 0.493, 245.873)`,
matching the earlier live `GetSpotTransform` output exactly. Pit entry 0 gave
the distinct, plausible `(100.566, 0.525, 130.270)`, but has **not** been
independently matched to a game-function output.

A 60-second, read-only watch spanning the driver's normal garage-to-track
departure saw slot 0 control owner, pit state, and pit flag stay `0`. A later
read while the driver was stationary outside the pit lane also returned all
three as `0`. This does not invalidate pit state: the previous-build trace in
`docs/LAP_VALIDITY.md` observed it become `5` after a *placement* and clear
with distance. Ordinary departure is not that transition.

The pit lookup is a separate small function at RVA `0x00A90C10`: it bounds the
index with the count at `0x01DFA608`, reads the candidate pointer at
`0x00A90C27`, and copies two 12-byte outputs from a 32-byte entry. The
existing garage-function observer does not cover this function. A pass-through
observation of natural calls, compared with the independently read pit entry,
is the next live confirmation; do not invoke the function synthetically.
The passive `lmu_pit_lookup_observer` is now built for that check. Its offline
resolver test passes on this mapped dump and refuses a damaged signature or a
forged duplicate. A similar sibling function reads a different spot table, so
the resolver additionally requires the pit count-to-pointer displacement
`0x90`; this is fail-closed structural evidence, not future-build proof.

Next direct-launch session: confirm the direct pit-table path, then make one gated
placement while recording pit state,
Flag Rules, and the exact restore point. Keep the pit limiter engaged until
pit state is verified clear; do not promote this candidate offset profile on
the strength of the garage match or unchanged normal-departure fields alone.

Follow-up: the passive pit lookup at `0x00A90C10` was active in a fresh
direct-launch Practice session but logged no calls during Drive, pit exit, or
Return to Garage. Ghidra inspection of the saved image found that the larger
GetPitDestination function at `0x00D14440` reads the pit pointer directly at
`0x00D14780` and `0x00D14857`; the small helper is not required on this path.
An entry observer for the larger function builds and passes its offline
resolver checks. A function-entry hit alone cannot prove its internal
table-reading branch ran.

Live follow-up in a fresh direct-launch Barcelona Practice session: the
pass-through observer at `0x00D14440` logged one call on pressing Drive,
`mode=0`, `flag=1`, and LMU stayed responsive. The small pit helper remained
unused in the previous session. Static control flow for a normal slot
(`<0x68`) in this mode goes to helper `0x00A90C70`, not the direct
`0x01DFA578` reads used by the `>=0x68` branch. That helper checks
`0x01DFA61C` and reads either `0x01DFA5A8` or `0x01DFA590`; the live switch
was `0`, so the first path is excluded. Live pointers held entry-0 positions
`(100.566, 0.525, 130.270)` at `0x01DFA578` and
`(113.620, 0.618, 145.110)` at `0x01DFA590`. The player's official shared
memory position while stationary in the pit box was
`(148.089, 0.680, 230.024)`. These positions are not expected to be equal
because destination search and vehicle transform intervene, but the branch
analysis is decisive only for this observed normal-Drive subpath: it does not
read `0x01DFA578`. It does **not** identify the working baseline's PitPos
write path. Do not enable placement from this observation alone.

The helper's saved-image entry is `0x00A90C70`. The existing pass-through
`lmu_pit_lookup_observer` has been retargeted to this function; its resolver
passes the mapped-dump test, including damaged-signature and duplicate
rejection. In a fresh direct-launch Practice session, its first natural Drive
call requested index `0` and returned position `(113.620, 0.618, 145.110)`
and orientation `(-0.005, 0.584, 0.028)`. An independent read through the live
`0x01DFA590` pointer matched all six floats exactly; selector `0x01DFA61C`
was `0`. LMU remained responsive. This validates the helper's table source for
that call, **not** a placement write, its downstream arrival transformation,
or future-build stability. The candidate profile still names `0x01DFA578` as
`pitPosTable`; its identity and baseline consumption still require independent
checks before use.

Further saved-image tracing corrects the scope of that validation. The caller
immediately invokes `0x00D31570`, which for a normal slot copies position and
orientation from the table pointer at `0x01DFA5C0` (index derived from
`slot+0x471E8` and a caller-supplied `1`), replacing the helper's output.
In the live Barcelona session, this table's entries 0 and 1 both have position
`(143.785, 0.415, 240.429)`; entry 1's orientation is
`(-0.007, 0.578, -0.017)`. The caller then invokes `0x00A908C0` with
`slot+0x471E8` and `slot+0x471EC`, which can replace both outputs again from
the garage table at `0x01DFA5D8`, before the vehicle-transform call. Therefore
`0x01DFA590` is only a confirmed *intermediate* lookup, not the final Drive
destination. The current observer cannot identify the final selected entry;
no spot table is yet validated as a safe placement write target.

The normal-slot caller at `0x00D148B4` supplies live selectors
`slot+0x471E8=0` and `slot+0x471EC=0` to `0x00A908C0`. Those select entry 0
of `0x01DFA5D8`, whose live position/orientation are
`(138.939, 0.493, 245.873)` / `(-0.007, -1.004, 0)`; the same slot has
`+0x90B0=0` and `+0x471D4=0`. This is the last unconditional table lookup
visible before the conditional `0x00D26FF0` adjustment and
`0x00F70AA0` vehicle transform. The latter copies the supplied position into
the vehicle object and computes a transformed arrival; the physical car
position need not equal the table coordinates. Because the currently loaded
observer samples only `0x00A90C70`, it cannot prove which conditional
adjustment ran. Keep the profile unpromoted and do not try an arrival write
until a passive capture at the final transform or equivalent live comparison.

A passive probe at the conditional vehicle transform `0x00F70AA0` was active
for one natural Drive in a fresh direct-launch session, but logged no call.
The code at `0x00D148B9` skips that transform when its local `R14B` is zero;
the absence of a log is consistent with that branch but does not prove it
(the caller filter may also be at fault). The previous claim that this is
necessarily the final normal-Drive step was too strong. The unconditional
preceding call to `0x00A908C0` is now
the next capture point. A caller-anchored pass-through observer is built and
its saved-dump resolver test passes, but it has not been loaded live yet.

Live follow-up in a fresh direct-launch Barcelona Practice session: the
caller-anchored `0x00A908C0` observer logged one natural Drive lookup,
`index=0`, `selector=0`, position `(138.939, 0.493, 245.873)`, and
orientation `(-0.007, -1.004, 0)`. An independent read of entry 0 through
`0x01DFA5D8` matched all six floats. The live slot had `+0x90B0=0`,
`+0x471D4=0`, `+0x471E8=0`, and `+0x471EC=0`; LMU stayed responsive. This
validates the last unconditional spot lookup on this observed Drive path,
not the effect of a placement write or the physical car's arrival. Keep the
candidate profile unpromoted until a gated write/arrival test establishes
those semantics.

Controlled one-metre lookup test in the same direct-launch session: while the
user was in the garage, a temporary probe verified the executable SHA-256,
observer presence, slot selectors, entry pointer, and writable data page. It
saved entry 0's original 24 bytes, changed only X from `138.939` to `139.939`,
and verified the write. The next natural Drive call logged
`drive_spot=3 index=0 selector=0 position=(139.939,0.493,245.873)` with the
original orientation. The probe immediately restored and verified all 24
original bytes. Official shared memory then showed a stationary player pose
`(145.629, 0.758, 241.745)` in Barcelona, yaw `-0.20714`. Because there is
no same-session unmodified arrival baseline yet, **do not infer** that the
one-metre lookup change shifted the physical car. Obtain a no-write repeat
Drive arrival for comparison before promoting a write target.

The same-session no-write repeat Drive returned the original entry-0 values
again (`drive_spot=4` and `5`). Official shared memory reported the stationary
player pose `(145.629242, 0.758128, 241.745453)` in three samples. Compared
with the edited-lookup arrival `(145.628723, 0.758164, 241.745483)`, this is
only about `0.00052 m` different in X, not the `1 m` table edit. Thus, in this
observed garage-to-Drive flow, changing `0x01DFA5D8` entry 0 changed the
lookup output but **did not shift the car's physical arrival**. Do not use
this table as the placement write target based on the lookup evidence. The
normal Drive path appears to skip or supersede this candidate before applying
the car transform; locate the actual arrival consumer before another write.

Large-displacement follow-up (same process): the user returned to the garage.
There was no saved Barcelona checkpoint for the current Kessel Racing 2026
`#74:LM` car, so the probe used only the world position of the saved BMW
Barcelona `hyper2` checkpoint `(126.13267, 0.6993956, 194.36264)`, about
53 m from the normal garage entry; the garage entry's orientation was left
unchanged. The probe verified the write, captured the natural Drive lookup
returning that checkpoint position (`drive_spot=7`), then restored and
verified the exact original 24 bytes. The user reported an initial movement
to the same garage position, then a game-issued penalty/automatic return and
a second placement in front of the pit wall. Official shared memory after
that sequence showed a stationary car at `(123.981087, 0.677970, 195.731781)`
in three samples, roughly 2.55 m from the injected checkpoint and 50.8 m
from the normal stationary arrival. This is strong evidence that a **large**
change to `0x01DFA5D8` entry 0 can affect a later placement/recovery path,
despite the prior one-metre test showing no change in the immediate normal
Drive arrival. It is not evidence of a safe direct-arrival formula: the first
movement, penalty, and automatic return are distinct game transitions, and
the cross-car checkpoint orientation was intentionally not used. No further
write should be made until those transitions are observed separately.

Second large-displacement probe used the user-selected, previously verified
Barcelona Richard Mille AF Corse `cp-023328` checkpoint position
`(-87.676720, -1.530338, -146.856659)` while retaining the original
garage-entry orientation. The natural lookup returned that value
(`drive_spot=11`), and the probe restored and verified all original 24 bytes.
The user again observed Drive initially leave the car in the garage, followed
by a later Return to Garage placement near the requested point, but without
usable car control. After that sequence, read-only slot fields showed
`controlOwner=0`, `pitState=0`, `pitFlag=0`; this snapshot alone does not explain
the reported lack of control.

Crucial correction: these isolated probes wrote `GarPos[0]` through
`0x01DFA5D8`. The known-working 0F6DCAC1 `PlacementService` instead writes
`PitPos[pitIndex]` through `spotTable.pitPosTable`, disables/restores relevant
rules, waits for the Ai-to-player transition, samples arrival immediately,
then restores the 24 written spot bytes with read-back verification after
400 ms of settling. A timeout restores before returning; exceptional exits
also attempt restoration, and a failed restore is now surfaced to the UI
rather than silently swallowed. The 8-byte entry tail is never written.
Flag Rules restoration is separate and waits for pit state to clear when
the pit-speeding penalty is relevant. Therefore the garage-table
experiments do **not** disprove the candidate `0x01DFA578` PitPos pointer as
the current build's placement write target. The earlier statement that this
pointer is categorically the wrong write target was too strong: it is not read
on the observed normal Drive subpath, but the working placement has a different
state sequence. Keep it unpromoted pending evidence for that sequence, and
prioritize making its resolver and state gates update-resistant rather than
adding speculative flags based on this incomplete probe.

The live car in this probe session is `Kessel Racing 2026 #74:LM`, whereas
`cp-023328` and its calibrated/learned rest data belong to `Richard Mille AF
Corse 2025 #50:ELMS`. Both may use Ferrari GT3 content, but the baseline
planner intentionally keys rest constants on the exact recorded vehicle name
and would reject this cross-vehicle combination. The probe copied checkpoint
world coordinates only; it did **not** reproduce the baseline inverse
transform, Flag Rules change, control handoff, or restoration timing. Do not
infer a new control/flag requirement from its lack of usable control.

Focused static resolver note: at `0x00D14759`, a `slot+0x471D4` comparison
against `0x68` splits the normal-player and special-slot paths. The special
path reads count `0x01DFA608`, indexes 32-byte entries through `0x01DFA578`
at `0x00D14780`, and copies position/orientation fields at offsets
`0/8/C/14`; the normal-player edge calls the alternate-table helper at
`0x00D147EA`. A sibling consumer at `0x00D31680` has the same split. An
automatic, fail-closed resolver should require this control-flow/dataflow
agreement and the independently referenced count/pointer relationship,
not a single fixed byte signature. The saved dump alone cannot show whether
the baseline reaches a different state or consumer when a PitPos override is
armed.

Additional static pass over the current mapped dump: the reanchor reference
index reports many `0x01DFA578` references, but the inspected placement and
distance paths at `0x00D9CDDE`, `0x00F643D9`, and `0x00F66F1B` also gate the
direct PitPos read on `slot+0x471D4 >= 0x68`. The apparent table-initialization
link at `0x00A8FA14–0x00A8FA80` only copies each table's pointer into its own
adjacent shadow pointer (`578→580`, `590→598`, `5D8→5E0`); it does not copy
PitPos entries into the normal-player tables. These are negative constraints,
not proof that the baseline's PitPos override cannot work. A static-only pass
cannot establish which runtime state/call sequence the baseline exercised.

An initial single-build `SpotTableResolver` now scans executable sections of
a mapped image for the `slot+0x471D4` branch and agreeing 32-byte PitPos entry
readers, then requires the referenced pointer/count globals to be in writable
data and rejects disagreement. It needs no previous dump. Against this image,
`inactive-reset-reanchor spot-resolve` reports `PitPos=0x01DFA578` and
`count=0x01DFA608` from 11 agreeing reader sites. Its unit test covers one
reader (refuse), two agreeing independent readers (candidate), corrupted
reader (refuse), and conflicting reader (refuse). This is one address-family
resolver, **not** an automatically usable placement profile: the normal-slot
consumer, remaining offsets, and live placement gates are unresolved.

The resolver now also derives the garage table independently: an indexed
reader at `0x00A908C0` and an indexed writer near `0x00A96025` agree on
`GaragePos=0x01DFA5D8`, `MULT=0x01DFA614`, and `count=0x01DFA618` from this
single mapped image. The synthetic regression refuses a lone reader, a
conflicting second pair, or a damaged writer. The full suite has 139 passing
tests. This broadens static offset discovery but still does not establish the
baseline's normal-player PitPos consumption or authorize placement.

Cross-build check (2026-09-24): two mapped dumps of the formerly working
`0F6DCAC1` executable were located at
`G:\LMU_Plugin\analysis\dumps\LMU_runtime_0F6DCAC1_garage-multicar.bin`
and `LMU_runtime_0F6DCAC1_grid38-race-start.bin`. Both are 63,954,944 bytes.
The current structural resolver finds GaragePos in both, but returns
`PitPos=False` in both. This is a concrete regression against the known
working build: the new PitPos motif is too narrow to qualify as update-proof,
even before proving the normal-player consumer. Keep it candidate-only and
fail closed. The old profile identifies PitPos at `0x01DBD990`, GaragePos at
`0x01DBD998`, mult at `0x01DBD9B4`, and count at `0x01DBD9B8`; the reference
index finds many executable references to each. Next compare the old
`GetPitDestination` (`0x00D4C310`) and the normal-slot helper with their
current counterparts, then derive a cross-build dataflow invariant rather
than widening the byte window blindly.

Old-versus-current reader trace: in `0F6DCAC1`, `0x00CF39C0` clamps the
ordinary slot index using `MULT` at `0x01DBD9B4`, shifts by five, then
`0x00CF39DD` reads PitPos `0x01DBD990`. It copies offsets `0/8/C/14`,
calls `GetSpotTransform` at `0x00A7E2D0`, and repeats while the slot index
is below `0x68` (`0x00CF3A8C`). This establishes baseline PitPos consumption
for ordinary slots in that path. The loop is in spot-index assignment:
after its candidate checks, `0x00CF3AC5` writes `container+0x471A8`
(pit index), and `0x00CF3AE6` writes `container+0x471AC` (garage index).
It does not itself prove the vehicle pose was copied directly from PitPos.

The homologous current assignment function `0x00D0DA60` uses the same
`0..0x67` candidate loop and index writes, but at `0x00D0DBC0` reads
`0x01DFA5C0` with `pitIndex*3+1`: fields at `+0x20/+0x28/+0x2C/+0x34`
of each three-entry group. It calls current `GetSpotTransform` at
`0x00D0DBF8`, then writes `container+0x471E8` (pit index) at
`0x00D0DCA9` and `container+0x471EC` (garage index) at `0x00D0DCCA`.
This is a direct cross-build dataflow match for the ordinary assignment
consumer and identifies the indexed mode-1 table as its replacement.
It still does not establish an arrival write or validate the old inverse
placement model against the new layout.

Further working-baseline caller trace narrows that claim: in the old build,
the ordinary-slot branch of `0x00D179B0` copies indexed PitPos into the
destination outputs (`0x00D17A67` onward). Its Drive caller at `0x00CFA61E`
then calls `GetSpotTransform` with the garage index at `0x00CFA6AA`, which
can replace those outputs from GaragePos before the optional adjustment and
vehicle transform. The current analogous helper `0x00D31570` instead reads
an indexed `pitIndex * 3 + mode` table at `0x01DFA5C0` for an ordinary slot;
its Drive caller passes mode `1`, then makes the analogous GaragePos lookup.
Thus *reading* old PitPos and even copying it into intermediate outputs are
not, by themselves, proof that those exact bytes reached the physical car.
The known-working placement observation remains real, but the precise
winning branch/timing still needs a matching live trace. The current helper's
indexed table is a stronger candidate than the old PitPos pointer for this
stage, not an approved write target.

The cross-build substitution is also inside `GetSpotTransform` itself.
In the old routine `0x00A7E2D0`, the out-of-range/selection fallback at
`0x00A7E42B` reads ordinary PitPos `0x01DBD990`, copies a 24-byte entry
into its candidate list, and selects from that list before applying the
transform. In the current routine `0x00A908C0`, the corresponding fallback
at `0x00A90A1B` reads `0x01DFA5C0`; it computes `pitIndex * 3`, then
copies floats at entry offsets `+0x20/+0x28/+0x2C/+0x34` (mode 1 of a
three-entry group) into the candidate list before the analogous selection
and transform. This is independent code-path evidence for the table
substitution, stronger than matching coordinates or adjacent globals.
The direct normal garage-entry path still reads GaragePos at
`0x01DFA5D8`, and a prior +1 m GaragePos probe barely moved arrival;
which path wins a real placement remains a live question.

An offline `reanchor` run using the saved old/current mapped dumps and the
installed current executable now works without LMU running. It checks
that the executable and dump PE headers agree before assigning the SHA-256
build identity; a mismatched old dump was refused before output. The
generator removes historical `functions` and `stale` research sections,
which `OffsetProfile` never consumes. Its current-build candidate has the
structurally derived stride `0x47308`, owner `0x6F90`, pit state/flag
`0x8FD8/0x8FE8`, and track-limits flags starting `0x03B493B4`. Crucially,
the generated `D9B92CA9.offline-candidate.json` now marks PitPos
`0x01DFA578` confidence `U`: the pointer reanchored, but the new build
only proves a special-slot consumer, unlike the old ordinary-slot consumer.
The command exits 1, and `placementValidated` remains false. Do not
promote this profile until the replacement write path and matching-car
placement/restore are validated.
Profile generation and live placement now use the same ordinary-slot
PitPos predicate. Its focused synthetic regressions accept the old-style
ordinary reader and reject the current-style special-slot-only reader;
the saved old/current dumps independently exhibit those respective paths.

`FindIndexedDestination` now requires the pit-index field, the independently
resolved MULT global, `index * 3 + mode`, and two matching table-pointer
reads. It additionally requires a separated `GetSpotTransform` fallback
reader of the same table with the `index * 3`, 32-byte stride, and mode-1
entry copy, plus the homologous spot-index assignment loop reading mode-1
fields, calling the resolved transform, and writing the pit index. Synthetic
regressions reject a missing/damaged fallback or assignment writer, a
damaged second helper read, and an ambiguous candidate. On the saved dumps,
it resolves `0x01DFA5C0` only in the current build, not the old working
dump. Read-only live inspection of PID 36636 found
`pitIndex=0`, so mode-1 entry 1 at that pointer held position
`(143.785, 0.415, 240.429)`, matching the previous table inspection.
This is not a Drive-arrival or exact-restore test; the profile stays disabled.

The post-GaragePos conditional step clarifies a possible winning branch:
working `0x00D0D420` checks container `+0x9098==2`, then calls the
PitPos-based `0x00D179B0` with mode 2, replacing the output. Current
`0x00D26FF0` checks `+0x90B0==2`, but its analogous call reaches
`0x00D31570`, which takes the `pitIndex*3+mode` table instead. The static
resolver finds these mode fields from that branch and separated masked
writers on both dumps, yielding `0x9098` and `0x90B0`. In the current live
garage snapshot owner/mode were `1/0`; the mode-2 indexed entry's yaw was
`-0.207` radians, close to a previously observed stationary car yaw, but
that numerical agreement is only a clue. A read-only `spot-live --watch`
trace on PID 34216 did observe mode `0 -> 2` at 10.199 s after arming,
then control owner `1 -> 0` 15 ms later while mode stayed 2. Thus mode 2
is a state reached during this garage-to-Drive transition, but the trace did not
capture the helper call or establish which output ultimately determines
arrival. Fresh Ghidra disassembly of the current saved dump confirms the
caller at `0x00D148B9` only calls `0x00D26FF0` and the vehicle transform
`0x00F70AA0` if local `R14B` is nonzero; the mode-2 test in `0x00D26FF0`
then calls `0x00D31570`. At function entry `0x00D1446E`, `R14B` is copied
from `DL`, the low byte of the caller's second argument. The earlier live
entry observer logged `mode=0, flag=1` for a natural Drive, so this branch
was **not** taken on that observed call. Earlier in the same caller, `R14B` is passed to
`0x00F32E30` and `0x00F6B1A0`, and gates another call at `0x00D14740`;
it is a transition-control input, not itself the destination-mode field.
A prior passive transform observer logged no call on one natural Drive,
consistent with this result. The observed field change must **not** be
taken as proof that the conditional placement branch ran. The command
neither cached a profile nor wrote game memory; this is not arrival
validation or permission to promote the build.

This changes the next trace target. Repeating a normal Drive watch of
`+0x90B0` cannot identify the winning placement write path: the observed
Drive call entered `0x00D14440` with argument `0`, which skips the
`0x00D26FF0`/`0x00F70AA0` block. Follow the old ordinary-slot PitPos
consumer at `0x00CF39C0` and its current counterpart into the normal
mode-0 handoff, or capture a matching-car placement with entry/exit
outputs. Do not infer a writable destination from the mode-2 table alone.

In `D9B92CA9`, the corresponding inspected
path at `0x00CF57A7` first branches on `slot+0x471D4 >= 0x68`;
`0x00CF57C5` reads PitPos only on that branch, while a separate normal-slot
path begins at `0x00CF57F8`. The current candidate therefore has a genuine
semantic caveat, not just a missing byte signature. We have not yet proved
that the current normal-slot path consumes the override or identified an
equivalent writable input. Do not promote the current build on the basis of
the old PitPos behavior. Also, the old profile's prose naming
`0x00D4C310` as `GetPitDestination` appears inconsistent with disassembly
of this saved mapped dump; treat that label as unverified until reconciled.

Following the current normal-slot edge farther: `0x00CF5837` calls
`0x00A90C70` with a slot index clamped below `0x68`. That helper checks an
override condition and `0x01DFA5A8` first; its default path reads indexed
32-byte entries from `0x01DFA590` at `0x00A90CE3`, with nonzero/fallback
handling later in the function. This is stronger evidence that the baseline
PitPos write target is no longer the ordinary-slot input on this build.
The adjacent PitPos reader ending at `0x00A90C5E` is a separate code path;
mere proximity is not a dataflow connection. The current build needs a
separately validated normal-slot target and restore sequence.

The normal-slot table is also a real engine input, not just a shadow pointer:
the configuration-loading routine bounds its index to `0..0x67`, shifts it
by five, and writes position floats at `+0/+4/+8` through `0x01DFA590`
at `0x00A951C1`; its sibling at `0x00A95259` writes orientation floats at
`+0xC/+0x10/+0x14`. The normal-slot reader at `0x00A90C70` clamps to the
same range and checks all six floats for zero before reading or deriving a
fallback. Thus the table has independent producer/consumer evidence, but a
live write still needs checks that the selected entry is the one Drive will
consume, that the engine is not concurrently rebuilding it, and that the
current inverse transform model remains valid. No current-build write is
authorized by this static finding alone.

2026-09-24 follow-up: `FindNormalSlotSelection` now requires the two conditional
branches, the indexed 32-byte override read, and the fall-through pointer to
the independently resolved normal-slot table to agree. It resolves the current
image's switch `0x01DFA61C`, override-vector start/end globals
`0x01DFA5A8/0x01DFA5B0`, and default table `0x01DFA590`; a damaged or
duplicate branch fails the synthetic test. The old mapped image has no matching
override branch and still resolves its independent ordinary PitPos reader.
Read-only `spot-live` on PID 36636 reported switch `0`, 104 override entries,
and therefore default-table selection *at inspection time*. The active observer
still modifies the transform probe, so candidate inspection/cache was refused;
there was no game write. A later Drive could change the switch or rebuild the
table, and this observation does not validate arrival or restore.
The diagnostic now derives the player slot from the independently resolved
container base/stride and index fields even when the old candidate probe is
hooked. It read slot/pit/garage indices `0/0/0`, then GaragePos entry 0 position
`(138.939, 0.493, 245.873)`, matching the earlier passive Drive observer's
lookup input. This corroborates the last unconditional lookup's selected
entry without borrowing the stale candidate profile; it remains read-only and
does not show the post-lookup conditional adjustment or physical arrival.

The maintainer's `spot-resolve` command now reports a normal-slot candidate
only when a clamped indexed reader and both position/orientation writers
agree on one writable-data pointer. It reports `0x01DFA590` for the current
dump and `0x01DBD980` for both saved old-build dumps. The old build also has
this table; its existence alone does not establish which table the Drive
sequence uses. In particular, the old direct PitPos path at `0x00CF39DD`
remains distinct. The resolver refuses an ambiguous second candidate in its
synthetic regression, and all 140 tests pass. It does not enable a profile.

Working application sequence, as implemented in `PlacementService.Place` and
`SpotWriteTransaction` (not a claim about the game's internal call order):
`Plan` checks matching track/car identity, reads the container and current
PitPos entry, inverts the target through the calibrated model, and rechecks
the forward prediction. `Place` requires Ai ownership in the garage, saves
and temporarily disables Flag Rules where possible, confirms the exact
original 24 spot bytes and a writable page, then writes and reads back the
24-byte override. After the user presses Drive, it waits for a fresh
Ai-to-player transition, samples Arrival immediately, waits 400 ms, restores
and verifies the original 24 bytes, and measures the achieved position.
It keeps Flag Rules disabled until pit state clears or the wait times out,
then restores the original rule value. Sector/pit-flag repair follows.
On an exception or timeout, `Dispose` attempts restoration, but it cannot
guarantee restoration after process termination or if the game reallocates
the table. A newly resolved build must verify the actual consumer, control
transition, Arrival, rule state, and exact-byte restoration live; static
pointer agreement is insufficient.

Cross-build resolver update: a second, legacy PitPos path now uses the
GaragePos reader/writer agreement to establish MULT and COUNT, then requires
two separated 32-byte indexed PitPos readers tied to that same MULT while
excluding the GaragePos pointer. Both saved `0F6DCAC1` dumps now resolve
`PitPos=0x01DBD990`, `COUNT=0x01DBD9B8` from six agreeing reader sites;
the current dump still resolves `0x01DFA578/0x01DFA608` through its distinct
special-slot branch. This removes the previous old-dump false negative but
does **not** reconcile their different normal-slot consumption, so neither
result alone licenses a placement write. The legacy matcher is covered by a
synthetic missing-second-reader/damaged-reader refusal test.

The resolver now labels its PitPos evidence explicitly: `OrdinarySlotLoop`
for `0F6DCAC1`, `SpecialSlotBranch` for `D9B92CA9`. Both dumps also expose
a normal-slot table, but the labels prevent a caller from mistaking a
special-slot PitPos reference for proof of ordinary-slot consumption. In the
current build, `0x00D147EA` calls the normal-slot helper `0x00A90C70`, then
the transform path at `0x00D14828`; the later ordinary-slot garage path calls
`0x00A908C0` at `0x00D148B4`. These paths remain to be matched to the actual
Drive transition and inverse-model assumptions before any write is attempted.

Read-only live check on the running `D9B92CA9` process (PID 36636,
2026-09-24): `spot-live` captured a complete 0x3D3E000-byte mapped image,
resolved the same globals without using a saved dump, and found non-null,
aligned pointers to readable, already-writable data pages. PitPos count was
8; GaragePos MULT was 104 and COUNT was 1. This proves the static candidates
exist in that live image and point to allocated tables, **not** that the
ordinary player consumes PitPos, that the normal-slot entry selected by
Drive is known, or that a write/restore would work. No write was performed.
The maintainer command now caches those candidate RVAs by the executable's
full SHA-256 under `artifacts/spot-candidates/`, separate from loadable
`offsets/` profiles. It refuses a conflicting result for the same hash.
This is a partial evidence cache, not the requested full automatic profile:
the remaining required addresses, placement semantics, and matching-car
live validation are still missing.

The older `artifacts/D9B92CA9.candidate.json` was generated before structural
stride resolution and still says `0x472C8`; the current image resolves
`0x47308`. `spot-live` refuses that candidate. Reanchor now derives the stride
from the new image before writing any new candidate, and refuses output if it
cannot resolve it. Do not manually promote or copy the older candidate.
An end-to-end 0F6DCAC1→D9B92CA9 reanchor smoke run also emitted the resolved
slot/pit/garage, control-owner, lateral-sign, and vehicle-dimension offsets
from the new image (not copied from the old profile), with `placementValidated`
still false. Nine other addresses remained unresolved, including one track-
limits flag. The temporary smoke profile was removed after verification.

Track-limits flag 0 was unresolved in that earlier run because the *old*
profile still held `0x03B36274` from a prior build. In the actual 0F6DCAC1
mapped image, session initialization writes `0x0101` to `0x03B0AD24`; in
D9B92CA9 it writes to `0x03B493B4`. Each image has a separate zero-compare
reader for byte 0, two for byte 1, and one for byte 2. The new structural
resolver therefore identifies all three bytes without trusting the old
address; missing or ambiguous reader evidence fails its synthetic test.
Reanchor now replaces all three derived-flag RVAs from this result. A read-only
live check on PID 36636 found current bytes `1/1/0`; this is an observation,
not proof of a changed rule or a write/restore sequence. The original nine-
unresolved count predates this correction; an end-to-end rerun is still needed
to confirm the new profile's final unresolved count.

The cache-key check now compares the live and on-disk PE COFF headers,
section tables, and image size before associating a mapped capture with the
disk executable SHA. A changed header is refused; a focused regression test
mutates the timestamp and a section RVA. This does not prove encrypted code
identity by itself, but closes the obvious "game still running after disk
update" mismatch for distinct PE builds. The read-only shared-memory sample
in the running session identified `Circuit de Barcelona / Kessel Racing 2026
#74:LM`; the previously verified checkpoint is for `Richard Mille AF Corse
2025 #50:ELMS`, so that session is not eligible for the required matching-car
placement validation.

Placement now has a runtime consumer gate before **any** temporary Flag Rules
or spot-entry write: it captures the running mapped module read-only and
requires the configured PitPos RVA to match an `OrdinarySlotLoop` resolver
result. The current `SpecialSlotBranch` candidate is rejected even if someone
copies its JSON into `offsets/`; the saved working-build code shape passes
the focused synthetic regression. The transaction constructor is internal
so callers cannot bypass this through the public API. This protects the
proven PitPos workflow while the new normal-slot workflow remains unresolved.

Follow-up live audit: the running process has
`lmu_final_spot_observer.dll` loaded from `tools/observer/build-pit4`.
It patches the first five bytes of the candidate profile's
`GetSpotTransform` probe at `0x00A908C0` from `40 53 56 41 56` to a JMP
(`E9 0E 07 56 FF`). The app's exact probe gate correctly refuses this
instrumented process. `spot-live` now reports the mismatch, skips candidate
container reads, and refuses to cache when that profile probe is modified.
The partial candidate cache created before this was noticed was removed;
the live table-pointer observation remains read-only evidence, not approval.

Independent container-layout check: the saved working build uses
`0x472C8` as its indexed-container stride (313 agreeing IMUL sites); the
current `D9B92CA9` dump uses `0x47308` (314 sites). Disassembly at
`0x00D1BC06`, `0x00D2354E`, and `0x00CBB100` ties the current stride to
container/vehicle-slot paths; old `0x00D0A26B` uses `0x472C8`. The
unpromoted reanchor candidate still carries `0x472C8`, so its container
reads would be wrong for nonzero slots even if its probe were unhooked.
Placement now checks both the ordinary PitPos consumer and the independently
resolved live container stride **before** temporary rule or spot writes.
The stride matcher refuses weak or competing vote totals; its synthetic
regression covers agreement and conflict. Field offsets may also have moved
by 0x40 and remain unverified; correcting the stride alone is insufficient.

Focused old/current Drive-path comparison supports a specific three-field
shift, rather than a blanket shift of every container field. In the working
dump, `0x00CF3F9A` writes `container+0x471AC` (garage index) and
`0x00CF3FB0` reads `container+0x471A8` (pit index). In the current dump,
the corresponding ordinary-slot path at `0x00D148A7` passes
`container+0x471EC` as the garage index and `0x00D148AE` passes
`container+0x471E8` as the pit index to `GetSpotTransform` at
`0x00A908C0`. Its branch at `0x00D1482D` checks
`container+0x471D4` against `0x68`, consistent with slot index also
moving from profile `0x47194` to `0x471D4`. Multiple current callers
repeat the `0x471E8/0x471EC` argument pair. Thus the reanchored profile's
old slot/pit/garage offsets are not safe to use. Other fields, including
control owner, pose, and vehicle dimensions, need independent checking;
the insertion point and whole-structure layout are not yet established.

The static resolver now derives this index triplet from separated
`slot < 0x68` guards and nearby pit/garage arguments to a transform call,
requiring unanimous agreement. It reports `0x47194/0x471A8/0x471AC`
from seven sites in each of the two working-build dumps and
`0x471D4/0x471E8/0x471EC` from eight sites in the current dump. A focused
regression rejects a lone site, a conflicting site, and a damaged argument.
This evidence does not identify the other container fields or authorize a
new-build placement.

Those same agreeing call sites now resolve the direct transform-call target
as well: both saved working dumps yield `GetSpotTransform=0x00A7E2D0`
from seven sites, and the current dump yields `0x00A908C0` from eight.
The matcher refuses a conflicting call target even when the three index
offsets still agree. This adds a function RVA candidate without depending
on an exact prologue, but the live function is currently observer-hooked and
the candidate is not a substitute for the independent live/probe checks.

`spot-live` now includes the agreed index triplet and transform RVA in its
read-only report and candidate-only cache, and refuses a profile whose probe,
stride, or three fields disagree before reading its container fields. The
running current-build process still reports the observer JMP at the transform
probe, so candidate container inspection and cache creation remain refused;
no game write was attempted.

The placement path now independently checks that the running mapped code's
slot/pit/garage fields and transform-call target match the loaded profile,
in addition to the ordinary PitPos consumer and container stride. It does
this before trusting profile container fields in `Place` and before any
temporary rule or spot write. Synthetic regressions cover a matching
triplet, stale fields, and a stale transform RVA. This guard preserves the
old proven path; it does not enable the current special-slot-only PitPos
candidate.

The array base is now independently derived from stride-indexed code paths:
`0x01DD9560` in both working dumps and `0x01E16170` in the current dump,
with 14 separated agreeing sites in each. The matcher ties a writable-data
LEA to the register added to the indexed container address and refuses weak
or competing results. Placement checks the resolved base against its profile
before rules or spot writes; `spot-live` includes it in candidate-only output
and checks it before profile-based container reads. The focused regression
covers agreement, a stale profile base, and a conflicting code site.

Control ownership moved by `0x18`: the working reader at `0x00A785B9`
compares `container+0x6F78` to AI owner `1`, calls the handoff function
`0x00CFA230`, then checks slot `0x47194` and calls the spot transform. The
current counterpart at `0x00A89E29` repeats that sequence with owner
`0x6F90`, handoff `0x00D14440`, slot `0x471D4`, and transform
`0x00A908C0`; a separate current reader at `0x00CF5A1F` also compares
`container+0x6F90` to `1`. The resolver requires this handoff/slot sequence
and a separated agreeing comparison. It returns `0x6F78` from both working
dumps and `0x6F90` from the current dump, with three witnesses each.
Placement now checks this field against mapped code before trusting the
profile's owner value; `spot-live` reports it and includes it in a
candidate-only cache when the probe gate passes.
The old-to-new candidate profile's inherited `0x6F78` is rejected.

Profiles now carry `build.placementValidated`. Existing live-proven profiles
are marked true; a new reanchor always writes false, and a missing marker is
also false. The app refuses to attach to an unvalidated profile before
opening a game-memory handle. This prevents a candidate with plausible
address reanchors but unverified field semantics from becoming active simply
by copying it into `offsets/`. It is not a substitute for implementing the
full automatic validation workflow.

Geometry-field comparison: the working build's `vehicleLength/vehicleWidth`
scalar loads use `0x444B4/0x444B8`; the current dump instead repeatedly uses
`0x444EC/0x444F0`. The resolver now identifies these as the dominant
adjacent scalar-load pair in the geometry region, with a paired use and
separated reads: 33/58 loads and one paired site in each working dump,
29/58 loads and two paired sites in current. This is a `0x38` shift and
is now a static candidate, though still a frequency-assisted heuristic
requiring live dimension/placement validation. The lateral-sign source is
stronger: the working reader
at `0x00ADEF79` loads `container+0x46898` in symmetric subtract/add
branches; current `0x00AF210D` repeats the same flow at `0x468D0`, with
separated supporting scalar reads. The static resolver returns `0x46898`
for both working dumps and `0x468D0` for current. Placement checks this
field and vehicle dimensions against mapped code before writing; the
candidate-only live report includes them. The current profile's inherited
geometry fields are rejected.

### 2026-09-24 successor build, matching-car Drive trace

LMU updated again while testing. The running Barcelona / Richard Mille AF
Corse 2025 #50:ELMS session is build `29CE422A1EB9F1049BB8502DA383998BF8ED8200ED6CEC452B6742A561A18EAA`
(PID 34312 at capture), not `D9B92CA9`. A read-only mapped-module capture is
`artifacts/LMU_runtime_29CE422A.bin`. `spot-live` resolved stride `0x47308`,
array base `0x01E17270`, owner `0x6F90`, mode `0x90B0`, pit/garage indices
`0x471E8/0x471EC`, and indexed destination global `0x01DFB6C0`. With the
exact checkpoint car in the garage, slot 0 had owner/mode `1/0`, pit index 8,
and garage index 0. The 45-second read-only Drive watch saw mode `0→2` at
13.956 s, owner `1→0` at 13.972 s, unchanged indices, then settled mode 2.
This proves the transition timing, not which destination entry the handoff
consumed or the final arrival position.

Targeted Ghidra inspection of the successor dump identifies the homologous
Drive function `0x00D14880`, conditional transform caller `0x00D14CF9`,
mode-2 branch `0x00D27430`, and indexed helper `0x00D319B0`. At
`0x00D27437`, mode 2 calls the indexed helper with argument 2. But the
caller at `0x00D14CF9` only invokes that branch and the vehicle transform
when its separate `R14B` transition flag is nonzero; it skips both when the
flag is zero. The earlier natural-Drive entry observation on the previous
build recorded flag 0. Consequently the new mode `0→2` watch alone cannot
justify writing mode-2 entry bytes. A call/output trace or another direct
consumer witness is needed before the perturbation test.

The indexed destination resolver now also requires the mode-2 reader to
call the same indexed helper containing its `pitIndex*3+mode` table reads.
Synthetic tests reject a changed call; `spot-resolve` still identifies the
indexed table in both `D9B92CA9` and `29CE422A`, but not in the working
`0F6DCAC1` dump. This is static reachability evidence only, not proof that
natural Drive takes the gated transform branch.

On PID 34312, a pass-through observer resolved and hooked the new Drive
function `0x00D14880` after an offline resolver test accepted both recent
dumps and rejected damaged/duplicated matches. One garage-to-Drive call on
the exact player object (`module+0x01E17270`) logged `mode argument=0`,
`third-argument flag=1`. The caller copies the low byte of its second
argument into `R14B`, so the `0x00D14CF9` conditional transform branch was
skipped on this observed natural call. A later read-only snapshot had player
owner/mode `0/0`. No spot or rules bytes were written. This supersedes an
inference from the earlier mode-2 state transition: a mode value sampled
during Drive is insufficient to identify the path that moved the car.

A fresh PID 25888 had the same build, exact car/track, owner/mode `1/0`,
pit index 8, garage index 0, and no observer loaded. The pass-through
final-spot observer validated `GetSpotTransform=0x00A90A40` with the exact
Drive return site `0x00D14CF9`. On one natural Drive it logged
`index=8, selector=0`, output position `(112.919, 0.490, 205.397)`,
orientation `(-0.007, -1.004, 0)`. The user confirmed stationary player
control afterward. Independent SDK shared-memory telemetry was stable at
`(119.386, 0.758, 200.777)`, about 8 m away horizontally. The selected
indexed entries were mode 0/1 `(117.554, 0.413, 199.454)` and mode 2
`(119.907, 0.376, 198.284)`; garage[0] was `(138.939, 0.493, 245.873)`.
These comparisons do **not** establish a causal winning entry, but they
disprove equating this spot-function output with final vehicle pose. No
spot/rules values were changed.

On a second clean matching-car session (PID 36780), a combined pass-through
observer validated both exact Drive call sites and logged their outputs in
order: indexed helper mode 1 returned `(117.554, 0.413, 199.454)` with
orientation `(-0.008, 0.578, -0.015)`; `GetSpotTransform` then returned
`(112.919, 0.490, 205.397)` with orientation `(-0.007, -1.004, 0)`.
The user confirmed player control; SDK telemetry twice showed a stationary
pose `(119.386, 0.758, 200.777)`, gear 0. None of these intermediate
outputs equals final pose. The indexed entry *is* consumed on natural Drive,
but this observation alone does not show that changing it would move the
physical car. With the helper's prologue detoured, `spot-live` correctly
refuses static indexed resolution; `spot-telemetry` reads only the independent
SDK channel. No spot/rules bytes were changed.

The maintainer-only `spot-probe` command is prepared for a causal test; it
has **not** yet been armed live. It dry-runs by default and refuses a process
with an observer, anti-cheat, wrong executable hash, wrong SDK car/track,
non-garage owner/mode, ambiguous indexed consumer, non-default normal-slot
selector, or non-writable entry. With explicit `--arm`, it changes only the
first float of one 32-byte indexed entry, waits up to 45 seconds for Drive,
then restores the original 24-byte payload and checks all 32 bytes. A
partial write can be restored only when each live byte is still either its
original or intended value; an unrelated writer or table reallocation is
reported as uncertain, never silently overwritten. This tool is not a
production offset profile and cannot enable placement.

First live `spot-probe` attempt on clean PID 34080: dry-run resolved mode-2
entry for pit 8 at `0x1D749F45BE0` and captured original 32 bytes
`2BD0EF42F0A8C03E9C4846436F1203BC306054BE8FC275BC0000000000000000`.
The armed test verified a temporary X shift `119.907→121.907`, but no
player-control transition occurred within 45 seconds. Its `finally` path
restored and verified all 32 original bytes; a subsequent dry run read the
same original bytes. This is a successful timeout/restore test, **not**
evidence about whether mode 2 controls physical placement.

Second live attempt on the same clean PID 34080, with the user ready at the
garage menu, produced a causal result. The tool verified the temporary
mode-2 entry X `119.907→121.907` (+2.000 m), observed player control and
SDK stationary pose `(121.386, 0.734, 200.777)`, then restored and verified
the exact original 32 bytes. The matching-car unperturbed stationary pose in
two preceding sessions was `(119.386, 0.758, 200.777)`; X shifted exactly
+2.000 m while Z remained unchanged. A separate post-restore read still
found the original mode-2 entry X `119.907` and the car at X `121.386`
(physical arrival persists after table restoration). This is strong live
evidence that mode-2 entry position X reaches physical placement, despite
the natural Drive caller's visible conditional transform branch being
skipped. It does not yet prove the Z/Y/orientation mapping, control across
target checkpoints, or the flag/pit-rule sequence. The candidate profile
remains disabled.

Third live attempt, same clean PID and car, after Return to Garage: a
temporary mode-2 Z change `198.284→200.284` (+2.000 m) yielded stationary
player pose `(119.386, 0.775, 202.777)` versus unperturbed
`(119.386, 0.758, 200.777)`. Z moved exactly +2.000 m and X stayed at
baseline. All original 32 entry bytes were verified after restore and a
separate live read again found mode-2 Z `198.284`. Small Y variation is
consistent with settling but is not yet modeled. Together the X and Z
experiments establish unit translation gain in both horizontal axes for
this car/build. SDK yaw after the Z test was `-0.207167` rad, close to the
mode-2 entry yaw `-0.207` rad; yaw causality still requires its own probe.

Fourth live attempt, same clean PID/car, temporarily changed mode-2 entry
yaw `-0.207398176→-0.107398176` (+0.1 rad). Stationary telemetry was
`(119.638145, 0.755254, 200.816971)`, yaw `-0.107263`; the tool restored
and verified all original 32 bytes. The entry's exact unmodified X/Z were
`119.906578/198.283630`. After another unmodified Return-to-Garage/Drive,
high-precision stationary telemetry was `(119.386253, 0.758449,
200.777328)`, yaw `-0.207205`. Thus yaw gain is approximately 1 and the
rest-offset vector rotates with entry yaw. Resolving that vector into
`heading=(sin yaw,cos yaw)` and `lateral=(cos yaw,-sin yaw)` gives
`D=2.547400 m, L=0.004314 m` from the unmodified run and
`D=2.547519 m, L=0.004667 m` from the +0.1-rad run. Agreement is
~0.12 mm forward / 0.35 mm lateral. The small yaw bias and vertical
settling remain to be validated at a distant checkpoint. Old-build
`D=3.049423 m, L=0.503253 m` are plainly wrong for this new path.

Offline reanchor against the old working dump wrote
Checkpoint placement test on the same clean build/car used the measured new
mode-2 rest offset (`D=2.54746 m`, `L=0.00449 m`, height `0.38216 m`, yaw
bias `0.000164 rad`) to invert checkpoint `cp-023328`. The dry run computed
entry position `(-88.938744,-1.912498,-149.069550)` and yaw `0.516537`.
After an explicitly armed Drive, initial player-control telemetry was
`(-87.678917,-1.523320,-146.855591)`, yaw `0.515213`, versus the recorded
checkpoint `(-87.676720,-1.530338,-146.856659)`, yaw `0.516701`:
horizontal error 2 mm, vertical error +7 mm at the first sample. The probe
restored and verified the exact original 32 indexed-entry bytes. Later
telemetry moved a few metres; the driver confirmed this was their input and
that they retained control. The live pit state/flag remained `5/1`, and the
driver reported the pit limiter active. This test made no rule, sector, or
pit-flag writes. It validates physical placement and control for this one
matching checkpoint, not pit-state clearance or a complete safe restore
sequence. The candidate profile remains disabled.

After the driver continued with the pit limiter on, LMU displayed "Turn off the
pit limiter: B" at the same time its automatic pit limiter switched off.
The driver reported no penalty or automatic return to garage. A fresh,
read-only `spot-live` on the same process
(PID 34080) then found player control/mode `0/0` and pit state/flag `0/0`.
Thus the pit state did clear with driving after this placement, with no rule
write by the probe. This does not yet establish the time/distance of the
transition or that Flag Rules can be safely
disabled and restored on this build.

The existing pit-speeding path resolver independently finds Flag Rules at
`0x01E11B38` in the `29CE422A` mapped dump, matching the reanchor candidate.
It follows the unique `Speeding In Pitlane` literal through both message
branches, the penalty function, and a speed gate that reads the same global.
The resolved penalty/message/gate function RVAs are
`0x00CF4C80/0x017C8E10/0x00CEB2C0`; the speed-gate call returns at
`0x00CE5E1A`. The `pit_resolver_test` now accepts the independently known
`D9B92CA9` and `29CE422A` tuples and rejects a damaged gate call or
message literal for either dump. This is structural offline evidence, not
proof of the live value or the effect and safe restoration of a write.

The guarded `spot-place-rules-test` then exercised the matching checkpoint
on clean PID 34080. It resolved Flag Rules from the pit-speed gate at
`0x00CE5DDA`, read the original live value `2`, and dry-ran without writing.
The armed run temporarily wrote `0`, verified the mode-2 checkpoint payload,
and observed player-control arrival at `(-87.679008,-1.523377,-146.855850)`
with 2 mm horizontal / +7 mm vertical target error. It verified the exact
original 32 spot-entry bytes after Drive. Pit state cleared after 38.0 s of
driving; only then did the tool restore and verify Flag Rules `2`. A separate
read-only `spot-live` confirmed owner/mode `0/0`, pit state/flag `0/0`,
Flag Rules `2`, and the original mode-2 entry. The driver confirmed control
from arrival, no penalty, and no automatic return to garage. This one-run test does not enable
the candidate profile; automated full-profile resolution and calibration
remain incomplete.

Reanchor now replaces both `rules.flagRules.rva` and its `readBy` site with
the uniquely resolved pit-speed gate evidence from the new mapped image.
The old `readBy=0x00D01537` was stale even when the reanchored global RVA was
correct. A fresh 0F6DCAC1→29CE422A offline smoke run emitted
`flagRules.rva=0x01E11B38`, `readBy=0x00CE5DDA`, confidence `E`, and still
kept `placementValidated=false` and the incompatible ordinary-slot PitPos
profile field unresolved. It exited nonzero as intended; this is not a
promotion of the special-slot candidate into the old placement path.

Offline reanchor against the old working dump wrote
`artifacts/29CE422A.offline-candidate.json` and exited 1. It found the
special-slot `PitPos` global at `0x01DFB678`, but correctly marked it `U`
because the old ordinary-slot consumer no longer applies. The candidate has
`placementValidated=false`; no live game-memory write or profile promotion
was performed. Next gate: establish the indexed mode-1/mode-2 consumer and a
bounded temporary-write/restore test with measured arrival and control.
