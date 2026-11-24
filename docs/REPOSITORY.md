# Repository layout and cleanup

| Path | What lives there | Cleanup decision |
| --- | --- | --- |
| `src/` | Desktop host, CLI, shared Core and web UI | Keep. Start with the [architecture guide](ARCHITECTURE.md). |
| `build.cmd`, `build.ps1` | Build, test and package entry points | Keep at the root. CI and contributors use `build.cmd`. |
| `docs/` | User guides, engineering references and historical evidence | Start with the [documentation index](README.md). |
| `tests/` | C# regressions, browser checks, fixtures, package smoke and live checklist | Keep test source. Generated reports live under `artifacts/TestResults/`. |
| `tools/` | SDK layout generator and maintainer diagnostics | Keep the tracked tools and fixtures. They support compatibility work and do not ship as executables. See [developer tools](../tools/README.md). |
| `offsets/shared-memory.json` | Tracked SDK telemetry layout used by the app and release package | Keep. |
| `offsets/*.auto.json` | Local caches for discovered build and tyre layouts | Rebuildable when a supported game build is available. Historical regression fixtures live in `tests/Fixtures/offsets/`. |
| `data/` | User checkpoints, recordings, presets, bindings, calibrations and caches | Use the [storage map](ARCHITECTURE.md#workspace-storage-and-build) before clearing individual files. |
| `artifacts/` | Build and test output | `build.cmd clean` removes the build directories listed below. |
| `experiments/` | Throwaway prototypes, previews and scratch code | Disposable. Move anything worth keeping to a tracked branch or retained storage first. |
| `local-evidence/` | Runtime captures, analysis projects, diagnostic recordings and older workspace material | Retain or review individually. |
| `dist/` | Staged executables, release ZIPs and checksums | Keep releases you want to retain. The `ship` task replaces this directory. |
| `.scratch/` | Local issues, specs, saved work and validation evidence | Review per project folder. Some directories contain unique local files alongside disposable builds. |

## What can be cleared

- **`artifacts/bin`, `artifacts/obj`, `artifacts/package`** hold binaries,
  intermediates and package staging. `build.cmd clean` clears them.
- **`artifacts/TestResults`** holds reports and screenshots. Keep old reports
  when you need a record of what passed on an earlier version.
- **`experiments/`** is for work you are happy to throw away. Put each prototype
  in its own subfolder, such as `experiments/ui-redesign/`. For a full checkout,
  use `git worktree add --detach experiments/ui-redesign HEAD` and remove it with
  `git worktree remove experiments/ui-redesign` when finished.

## Where older artifacts went

- Runtime captures moved to `local-evidence/runtime/`, where the
  [local compatibility checks](TESTING.md#local-game-captures) look for them.
- Probe recordings moved to `local-evidence/velocity-probe/`. New recordings
  also go there.
- Ghidra projects, prototypes of uncertain status, historical logs and loose
  research files moved to `local-evidence/from-artifacts/`, with their names
  intact. Use this path when an older note points to one of those files under
  `artifacts/`.

In `.scratch/`, `pr-2-validation/` contains a paused integration, local changes
and live validation. `regressions/` mixes disposable browser output with saved
live measurements. Preserve the handoffs, source changes, captures and reports
before clearing generated descendants. See the [local issue tracker](agents/issue-tracker.md)
for the current scratch layout. The October 2026 cleanup inventory is retained
locally at `.scratch/repo-cleanup/inventory.csv`.
