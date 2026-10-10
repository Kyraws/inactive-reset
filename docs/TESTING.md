# Testing

The suite uses xUnit and runs on Windows x64 with .NET 8. From the repository
root in PowerShell:

```powershell
.\build.cmd test -Configuration Release
```

To run only tests that need no installed game or private captures:

```powershell
dotnet test InactiveReset.sln -c Release --filter "Category!=LocalGame"
```

Use `--filter "Category=LocalGame"` for mapped-image and installed-menu checks.
The default run includes both categories and reports unavailable local fixtures
as skips. Browser checks below run separately.

## Coverage

| Area | What the checks establish |
| --- | --- |
| Placement maths | Inverse/forward consistency, indexed layout, and regression values from recorded measurements |
| Capture files | Checkpoint serialization, tyre state round-trip, wheel order, temperature units and damage indicators |
| Capture selection | Exact IDs, case-insensitive names on the loaded track, same-name captures from different cars, and ambiguity rejection |
| Vehicle identity | Same-track pose reuse, loaded-car calibration checks, and separate cross-car learned corrections |
| Learning | Correction calculations, build separation, and rejection of unsuitable samples |
| Discovery | Structural and instruction-anchor matching, retuned/relocated tuning storage, moved pit-speed fields and branch destinations, disagreement, section bounds, and ambiguity rejection |
| Indexed placement gate | Accepts either normal-slot override state; rejects changed readers, profile, destination mode, pit index, table pointer, or planned entry |
| Offset cache | Full-profile comparison with current-code derivation, repair of altered fields/model data, invalidation on code changes beyond the transform probe, and preservation after failed capture/discovery/probe checks |
| Spot transaction | Payload size, preservation of padding, rollback after partial writes, exceptional cleanup, idempotent restoration, and retry after restore failure |
| Cleanup | Cancellation waits for a safe rule-restore point; no pit-clear timeout; garage/session-end handling; stale spot writes skipped; failed suppression rolls back the exact original value |
| Rules and reports | Profile fields, lap-state predictions, and outcome messages |
| Launch detection | Install/manifests and launch-selection logic |
| Local session API | Menu and Practice gates, owned-content selection, relative steps, assist ranges, generated save loading, preset preservation, recovered routes/ports after package changes, refusal of ambiguous/unsupported request helpers, and refusal without startup confirmation |

`OffsetProfileTests` owns profile parsing, confidence and placement-validation
gates, plus the container-relative lap-field contract across both shipped
profiles. Its tracked JSON fixtures are copied into the test output explicitly;
generated `.auto.json` caches are excluded. `LapValidityTests` covers lap behavior
without depending on a particular build's numeric addresses.

Cache failure and repair cases use small portable fixtures. The local cache
integration check covers executable-code changes outside the transform probe.
Recorded placement values remain independent regression evidence; demonstrations
of superseded incorrect models and assertions about fixture constants have been
removed. Preserve measured inputs/outputs and failure paths when consolidating
tests, rather than replacing them with only inverse/forward round trips.

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

`LmuApiRoutesTests` includes a read-only installed-menu check, independently
skipped when LMU cannot be located. It parses the current installed bundle and
settings without launching LMU or sending API requests. Portable synthetic
fixtures cover route/port changes, dynamic parameters, comments/strings/regex
decoys, payload and method changes, and package replacement. These checks do not
establish live compatibility of a new API contract or launch sequence.

`tests/session-ui.html` runs 23 checks of the production editor in an iframe
with a fake API. Serve the checkout root over HTTP and open the page. It covers
startup and busy/online gates, owned content, step semantics, race units,
dependent controls, search focus, section switching, failed-save readback and
startup confirmation tied to the current game process.
It performs no game writes. Like the checkpoint browser checks below, it runs
separately from xUnit. See [Local sessions](SESSIONS.md) for dated live evidence.

`tests/lab-ui.html` checks the Lab UI with a fake API, including plan review,
relative setting changes, cancellation and stopping after partial failure. It
also runs separately in a browser and performs no game writes.

From a source checkout, `tests/checkpoint-ui.html` provides dependency-free
selector checks of the production page script with a small DOM stub. Serve the repository root over
HTTP and open `tests/checkpoint-ui.html` in a browser to run them. They cover
track filtering, name-only rows with distinct IDs, cross-car selection, polling,
track changes, menus, busy state, tyre requests, Activity diagnostics and result
details, preparation choices across workspace navigation, and preservation of
destination/log scroll positions. They do not check visual layout
or native browser rendering and are separate from the xUnit suite.

The complete `PlacementService.Place` sequence still depends on a running game.
The suite does not establish successful live placement, penalty suppression,
pit-state clearing, lap timing, or the native window's closing behaviour. Tests of
outcome messages do not establish that the game produced those outcomes.

Use the live verification procedure in [Contributing](../CONTRIBUTING.md) for
process and engine behaviour. Record which dump cases ran and which were
skipped when reporting results.
