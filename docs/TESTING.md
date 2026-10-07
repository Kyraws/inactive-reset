# Testing

The suite uses xUnit and runs on Windows x64 with .NET 8. From the repository
root in PowerShell:

```powershell
.\build.cmd test -Configuration Release
```

## Coverage

| Area | What the checks establish |
| --- | --- |
| Placement maths | Inverse/forward consistency, indexed layout, and regression values from recorded measurements |
| Capture files | Checkpoint serialization, tyre state round-trip, wheel order, temperature units and damage indicators |
| Capture selection | Exact IDs, case-insensitive names on the loaded track, same-name captures from different cars, and ambiguity rejection |
| Vehicle identity | Same-track pose reuse, loaded-car calibration checks, and separate cross-car learned corrections |
| Learning | Correction calculations, build separation, and rejection of unsuitable samples |
| Discovery | Structural and instruction-anchor matching, disagreement, and ambiguity rejection |
| Indexed placement gate | Accepts either normal-slot override state; rejects changed readers, profile, destination mode, pit index, table pointer, or planned entry |
| Offset cache | Valid cache reuse, invalidation on hash/version/probe problems, and preservation after failed capture or discovery |
| Spot transaction | Payload size, preservation of padding, rollback after partial writes, exceptional cleanup, idempotent restoration, and retry after restore failure |
| Cleanup | Cancellation waits for a safe rule-restore point; no pit-clear timeout; garage/session-end handling; stale spot writes skipped; failed suppression rolls back the exact original value |
| Rules and reports | Profile fields, lap-state predictions, and outcome messages |
| Launch detection | Install/manifests and launch-selection logic |

Transaction tests run the production `SpotWriteTransaction` logic with an
injected verified-write operation backed by a byte buffer. Production `Begin`
still performs the process, hash, writable-page, and original-byte checks before
using `ProcessMemory.WriteVerified`. The buffer tests do not exercise Windows
memory APIs or those attachment checks.

`TyreStateTests` checks SDK wheel order, Kelvin-to-Celsius conversion, unmodified
wear/compound metadata, checkpoint round-tripping and the requested class compound
policy. These tests do not establish that tyre physics can be changed in game.

`TyreResetTests` checks input limits on every wheel, FL/FR/RL/RR request ordering,
optional temperature per wheel, legacy uniform requests, snapshots before arming,
per-wheel condition/temperature verification, inventory percentage encoding, rollback
after partial writes, cancellation/session/Drive boundaries, and numeric thermal
verification. Live measurements are recorded separately in [Tyres](TYRES.md).

## Local game captures

The automatic-discovery tests check four recorded builds independently:
`D9B92CA9`, `29CE422A`, `66942337`, and `0F6DCAC1`. Each build has its own test
and skip reason. A missing image skips only that build's check. The separate
discovery/cache integration test needs the `66942337` image.

Expected paths are `artifacts/LMU_runtime_<build>.bin`. The baseline image can
instead be supplied through `INACTIVE_RESET_BASELINE_DUMP` for `0F6DCAC1`.
These captures are private mapped game images and are not distributed or stored
in Git. CI can run the portable maths, cache, transaction, and reporting tests
without them; missing capture checks remain visibly skipped.

For a focused run after building:

```powershell
dotnet test InactiveReset.sln -c Release --no-build --filter "FullyQualifiedName~SpotWriteTransactionTests|FullyQualifiedName~AutomaticOffsetCacheTests"
```

## What remains untested automatically

From a source checkout, `tests/checkpoint-ui.html` provides dependency-free
selector checks of the production page script with a small DOM stub. Serve the repository root over
HTTP and open `tests/checkpoint-ui.html` in a browser to run them. They cover
track filtering, name-only rows with distinct IDs, cross-car selection, polling,
track changes, menus, busy state, tyre requests, Activity diagnostics and result
details, and preservation of destination/log scroll positions. They do not check visual layout
or native browser rendering and are separate from the xUnit suite.

The complete `PlacementService.Place` sequence still depends on a running game.
The suite does not establish successful live placement, penalty suppression,
pit-state clearing, lap timing, or the native window's closing behaviour. Tests of
outcome messages do not establish that the game produced those outcomes.

Use the live verification procedure in [Contributing](../CONTRIBUTING.md) for
process and engine behaviour. Record which dump cases ran and which were
skipped when reporting results.
