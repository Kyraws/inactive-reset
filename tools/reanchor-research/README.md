# Discovery-resource scripts

`build-discovery-seeds.py` generates the instruction anchors embedded in Core.
It is a maintainer tool requiring Python and Capstone; users need neither.

Pass a known mapped image, its offset profile, the output JSON path, and any
additional images against which anchors must resolve uniquely:

```text
python tools/reanchor-research/build-discovery-seeds.py known.bin known-profile.json src/InactiveReset.Core/discovery-seeds.json check1.bin check2.bin
```

Review the generated resource and run the discovery regression tests with the
relevant local captures. Update `AutomaticOffsets.ResolverVersion` when changing
discovery resources so existing caches are rebuilt. Unique offline matches are
not a substitute for a production-path live test.

## Archived prototypes

[archive/](archive/README.md) preserves `stranchor.py`, `callgraph.py`, and
`anchors3.py`. They are standalone historical experiments with hardcoded local
capture paths and no project callers. They remain available as research
references, but are not automatic discovery dependencies or supported workflows.
