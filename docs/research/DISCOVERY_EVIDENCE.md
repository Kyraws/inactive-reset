# Discovery and indexed-destination evidence

Consolidated from the former architecture, D9B92CA9 reader log, observer notes
and 66942337 update check. See [Architecture](../ARCHITECTURE.md) for production
discovery and [tools](../../tools/README.md) for supported maintainer commands.

## Reanchor lessons

- Independently derive a probe RVA before refreshing its bytes. Reading a stale
  RVA and saving those bytes as expected lets the executable vouch for itself.
- Resolve data, fingerprint functions by agreeing global references, and iterate
  to a fixpoint. Scores and regional deltas are not independent corroboration.
  A partial fingerprint on `0F6DCAC1` selected length 0x1BA instead of old 0x279;
  refusing it allowed later witnesses to identify the correct 0x278 function.
- Deduplicate nearby RIP-reference sites: REX-prefixed instructions can decode
  twice. Bound searches and prioritize distinctive contexts; `DataMatch.Sites`
  counts examined references. Unsupported decoder forms mean unknown.
- Record capture state: menu dumps zero tables/container fields. Multi-car grids
  expose stride mistakes hidden at slot 0. A module dump does not include heap
  contents behind pointer globals. Never infer a whole-structure shift.
- SDK vectors use eight-byte components; `LibraryImport` needs explicit A/W
  entry points. Edit UTF-8 explicitly with Windows PowerShell 5.1.

Removed hardcoded prototypes had no application callers. Their retained ideas:
unique NUL-terminated log string → decoded RIP reference → plausible function
boundary, first validated on a known image; masked surviving caller → checked
relative call → agreeing callee; RTTI/string/vtable surveys as leads. Padding,
pooled constants and guessed ASLR bases are hypotheses, not profile evidence.
Active regeneration uses `tools/reanchor-research/build-discovery-seeds.py`.

## D9B92CA9 reader/observer evidence, September 2026

SHA-256 `D9B92CA9FF84302D6EEF31FDE2074A0CA027D51C5F33AAF290EE0DD1A1484CCB`.
Curated sites remain in `tools/ghidra/D9B92CA9-readers.tsv`.
`export-reader-evidence.ps1` emits bounded decoded-reference packets; it neither
attaches nor proves runtime consumption. Garage seed 0x00A908C9 read count,
not the pointer; the corrected pointer reader was 0x00A90900.

| Family | 0F6DCAC1 | D9B92CA9 |
| --- | --- | --- |
| Container stride | 0x472C8 (313 sites) | 0x47308 (314 sites) |
| Container array | 0x01DD9560 | 0x01E16170 |
| Slot / pit / garage indices | 0x47194 / 0x471A8 / 0x471AC | 0x471D4 / 0x471E8 / 0x471EC |
| Owner / mode | 0x6F78 / 0x9098 | 0x6F90 / 0x90B0 |
| Length / width / sign | 0x444B4 / 0x444B8 / 0x46898 | 0x444EC / 0x444F0 / 0x468D0 |
| Spot transform | 0x00A7E2D0 | 0x00A908C0 |
| PitPos | 0x01DBD990, ordinary loop | 0x01DFA578, special slots only |
| Normal-slot table | 0x01DBD980 | 0x01DFA590 |
| Indexed destination | Absent | 0x01DFA5C0 |
| Garage table | 0x01DBD998 | 0x01DFA5D8 |
| Track-limit flag block | 0x03B0AD24 | 0x03B493B4 |

Old stored track-limit address 0x03B36274 was stale even for 0F6DCAC1.
Initialization word 0x0101 and separated readers established all three flags;
live D9B92CA9 readback was 1/1/0. Steward candidate 0x01E14CD8 had no established
behavioral meaning.

Flag Rules 0x01E10A38 had reader 0x00D1D3A6; a containing-function hook did not
prove that comparison ran. Initial message-branch hook 0x00CF4DB0 had no hit
during a stop/go; the other branch called 0x00CF4860. A dual hook then observed
speed gate and penalty calls on the same object, Flag Rules 2, positive_excess=1.
The gate read accumulated state at object+0x27218. This established a connected
path, not exact spatial thresholds. Live 2→0 prevented cut invalidation even
with Track Limits Default/effective flag 1; restoring 2 restored invalidation.
Flag Rules is broader than pit speeding. Mutation checks on older saved images
`266D1AF6`, `44C3EE9C`, `E669E121`, `C40C815F` and two `0F6DCAC1` states
were offline structural evidence only.

Entry hits do not prove branches; no hits are inconclusive. Hooks can alter the
production probe and correctly cause refusal. Hardware-breakpoint detach caused
one 0x80000004 crash; a later trace ended with a separate 0xC0000005 executing
address 0x2 after an indirect call. Missing heap data prevented attributing its
cause to tracing. One successful detach does not establish a safe workflow.
Use fresh direct-launch Practice; never stack observers; exit LMU to unload.

## Intermediate lookup traps

Natural Drive entered with second argument 0 and third argument 1. Its transition
flag skipped a conditional transform branch despite sampled destination mode
0→2. Helper 0x00A90C70 returned a normal-slot lookup, then downstream lookups
replaced it. The final unconditional garage output matched all six table floats.

GaragePos X+1 m changed the lookup but shifted physical arrival only 0.00052 m.
Larger edits affected later penalty/recovery/garage transitions, sometimes without
usable control. These probes used Kessel Racing #74 with other cars' coordinates
and omitted the production inverse, rule suppression and handoff. They neither
validated GaragePos nor disproved the former PitPos placement workflow.

The indexed resolver needed agreeing `pitIndex*3+mode` helper reads, separated
transform fallback and assignment consumers, plus a mode-2 call to that helper.
Ordinary/special-slot evidence must stay distinct. Validate code/layout/probes
before trusting fields or writes. Historical candidate `placementValidated`
attachment gates were superseded; the current marker is not a live-test gate.

## Causal tests on 29CE422A

SHA-256 `29CE422A1EB9F1049BB8502DA383998BF8ED8200ED6CEC452B6742A561A18EAA`,
Barcelona / Richard Mille AF Corse 2025 #50:ELMS, clean direct-launch Practice.
Indexed global 0x01DFB6C0, pit index 8, garage index 0. Mode-1 output
`(117.554,0.413,199.454)` and transform output `(112.919,0.490,205.397)`
both differed from stationary SDK pose `(119.386,0.758,200.777)`.

| Guarded mode-2 experiment | Outcome |
| --- | --- |
| X+2 m, no Drive within 45 s | Timeout restored/verified original 32 bytes |
| X+2 m, Drive | Arrival X 121.386 vs 119.386; Z unchanged |
| Z+2 m, Drive | Arrival Z 202.777 vs 200.777; X unchanged |
| Yaw -0.207398176→-0.107398176 | SDK yaw -0.107263 vs baseline -0.207205 |

Every completed perturbation restored/verified all 32 bytes. Yaw-test D/L were
2.547519/0.004667 m vs baseline 2.547400/0.004314 m: agreement within
0.12/0.35 mm. Horizontal translation/yaw gain was about one with a rotating
rest vector, unlike old ordinary D=3.049423/L=0.503253.

Using D=2.54746, L=0.00449, H=0.38216 m, yaw bias 0.000164 rad at
`cp-023328` produced arrival `(-87.678917,-1.523320,-146.855591)` vs target
`(-87.676720,-1.530338,-146.856659)`: 2 mm horizontal / +7 mm vertical.
Driver confirmed control; original bytes verified. Initial pit state/flag 5/1
cleared with driving. A guarded rules test repeated placement with Flag Rules
2→0, restored the entry, waited 38.0 s for pit clearance, then restored 2.
Independent readback and driver confirmed no penalty/automatic garage return.
This was maintainer-only, not integrated-app validation.

## 66942337 follow-up, 2026-10-06

SHA-256 `66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`.
Slot index moved to 0x471E4; special PitPos 0x01DFD968 had 11 readers, indexed
destination 0x01DFD9B0, pit state/flag 0x8FE0/0x8FF0, Flag Rules 0x01E13E68.
Using the independently resolved index fixed the preceding-build assumption.

Same car/track, pit index 8: unmodified pose `(119.386047,0.758482,200.777328)`,
yaw -0.207134, was within about 0.2 mm horizontal / 0.03 mm vertical of the prior
baseline. Guarded checkpoint arrival `(-87.679024,-1.523374,-146.855820)`, yaw
0.515187, again missed 2 mm / +7 mm. All 32 original bytes verified; pit state
cleared after 39.6 s, then Flag Rules 2 restored. Driver confirmed normal placement.
Attachment/planning integration passed separately; this experiment did not prove
the shipped path. Later production evidence is in [Status](../STATUS.md).
