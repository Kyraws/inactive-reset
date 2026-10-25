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

## Retired prototypes

The hardcoded string/caller/RTTI experiments were superseded. Their useful
techniques and limitations are retained in
[discovery evidence](../../docs/research/DISCOVERY_EVIDENCE.md).
