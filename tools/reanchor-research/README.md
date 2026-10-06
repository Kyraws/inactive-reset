# Reanchor research scripts

`build-discovery-seeds.py` generates the embedded instruction anchors used by
the application's automatic discovery. It requires Python and Capstone only
when regenerating those development resources; neither is needed by users.
Pass a known mapped image, its offset profile, the output JSON path, and any
additional mapped images against which the anchors must resolve uniquely:

```text
python build-discovery-seeds.py known.bin known-profile.json discovery-seeds.json check1.bin check2.bin
```

The generated JSON belongs in `src/InactiveReset.Core/discovery-seeds.json`.
Bump `AutomaticOffsets.ResolverVersion` when changing discovery resources so
existing local caches are rebuilt. The scripts described below remain research
tools rather than application dependencies.

Maintainer-only, like everything else that re-derives addresses. Never shipped,
never run by a user, not referenced by any project.

These are techniques that **work and are not yet in `Reanchor.cs`**. They are kept
because each one has already resolved an address that the implemented tiers could
not, and re-deriving them from scratch next patch would be wasted work. Anything
already ported (value anchoring, reference voting, reference profiling, data
fingerprinting) has been deleted from here rather than left to rot out of sync.

They hardcode dump paths at the top. Point them at
`G:\LMU_Plugin\analysis\dumps\` and mind which game state each capture was taken
in — see "The capture state is part of the dump" in `docs/ARCHITECTURE.md`.

Requires Python with `capstone` 5.0.7.

| script | technique | status |
|---|---|---|
| `stranchor.py` | string anchoring — find a function by the log string it references | **works, not ported.** Resolved `slotRestart` and `assignSpotIndices` on `0F6DCAC1` |
| `callgraph.py` | call-graph anchoring, with the working masked-regex matcher | works, weak on its own — produced one vote per function on `0F6DCAC1` |
| `anchors3.py` | RTTI type-descriptor and string survey | survey only; the intended seed for a vtable tier that does not exist yet |

## Why string anchoring is worth porting

LMU is a **logging build**. Functions reference their own names and the build
server's source paths, e.g. `'Entered Slot::Restart(%d)'` alongside
`'D:\bamboo-agent-home\...\rFactorSource\Source\slot.cpp'`. Strings survive
recompilation completely, so this works exactly where masked signature search
cannot.

The method, validated against the old image first where the answer was already
known: find the unique string, find the `lea r64,[rip+disp]` that references it,
then walk back to the nearest `INT3`-padded prologue.

It is the only technique here with an independent confirmation behind it.
`assignSpotIndices` was resolved to `0x00CF3880` by string anchoring, and later
and separately to `0x00CF3880` by the data-fingerprint tier now in
`Reanchor.cs`. Two unrelated methods, same answer.

`slotRestart = 0x00CFA230` on `0F6DCAC1` is a string-anchoring result that
nothing else has reproduced — the fixpoint run still reports it unresolved. It is
NOT in any profile, and it should not be hand-written into one: put the technique
in the tool and let the tool derive it.

## The capstone trap

`disasm` **halts at the first undecodable byte** and returns what it had. A
chunked linear sweep therefore returns almost nothing, silently, and looks like
"no references found" rather than like an error. Scan for the encoding directly
instead:

- `48/4C 8D` with modrm `mod=00 rm=101` for `lea r64,[rip+disp32]`
- `E8` for `call rel32`

This cost a cycle once. `Reanchor.DecodeRipRef` avoids it by decoding only the
forms this project actually needs, and returning nothing for anything else.
