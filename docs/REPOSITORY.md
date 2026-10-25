# Repository inventory

Cleanup review: 2026-10-25. Classifications cover all tracked files by role;
the per-file working inventory is `.scratch/repo-cleanup/inventory.csv`.

| Classification | Files / former location | Decision |
| --- | --- | --- |
| Needed | `src/InactiveReset.App`, `.Cli`, `.Core`, `.Ui` | Keep application code, embedded web assets and discovery resources; preserve runtime checks and public/file formats |
| Needed | `tests/InactiveReset.Tests`, `.BrowserTests`, `Support`, browser HTML/JS, package smoke and live checklist | Keep independent regression, HTTP, rendering, package and live coverage |
| Needed | Four historical JSONs formerly in `offsets/` | Move unchanged to `tests/Fixtures/offsets`; still copied into the test output's `offsets/` directory |
| Needed | `offsets/shared-memory.json`, `data/profiles-default/` | Keep runtime SDK layout and shipped calibration; update calibration's evidence pointer |
| Needed | `tools/observer`, `tools/ghidra`, SDK dumper, active discovery generator | Keep maintained analysis tools, resolver tests and curated evidence |
| Needed | Former `src/InactiveReset.Reanchor` | Move to `tools/InactiveReset.Reanchor`; update solution, test reference and Core reference; never publish it |
| Needed | Root `build.ps1` | Move implementation to `scripts/build.ps1`; keep `build.cmd` as the stable root/CI entry point |
| Needed | Root `CONTEXT.md`, `CONTRIBUTING.md` | Move to `docs/`; update links, agent guidance and package staging |
| Needed | Current `docs/*.md`, `docs/agents/`, tools guides | Keep distinct user/developer contracts; condense index/architecture and correct obsolete claims |
| Needed | Root README | Replace stale walkthrough/appearance claims with implemented feature list and short setup/access notes |
| Needed | `.gitattributes`, `.gitignore`, `.github/workflows/verify.yml`, `AGENTS.md` | Keep repository/tooling discovery conventions; shorten agent entry point |
| Needed | Solution, `Directory.Build.props`, `LICENSE`, `NOTICE` | Keep at root for build discovery, licensing and package consumers |
| Deprecated | `docs/archive/` architecture, targets, heading/lap investigations, reader log, observer notes, update check and index | Consolidate useful measurements, caveats and investigation techniques into `docs/research/`; remove superseded records |
| Not needed | `tools/reanchor-research/archive/{stranchor,callgraph,anchors3}.py` and index | No project callers, hardcoded private captures; retain ideas in discovery evidence, remove |
| Needed | `docs/media/interface.gif` | Replace the outdated walkthrough with all eight current pages and five builder tabs; clearly label sample data |
| Deprecated, local | Ignored root `HANDOFF.md` | Preserve local paths/UTF-8 gotcha in `.scratch/repo-cleanup/local-context.md`; current build status is superseded; remove |

The exact deletion set was explicitly approved by the owner on 2026-10-25.
Tracked originals remain recoverable in Git history.

## Directory layout

```text
src/                 shipped application projects and embedded resources
tests/               automated checks, historical fixtures and live checklist
tools/               maintainer-only Reanchor, observers, Ghidra and generators
scripts/             build/test/package implementation
docs/                user/developer guides, glossary and condensed research
offsets/             runtime SDK layout and ignored per-build discovery caches
data/                shipped default calibration and ignored user work
artifacts/, dist/    ignored generated outputs
.scratch/            ignored issues, specs and local working records
```

Ignored checkpoints, user calibrations, caches, private dumps, tool builds and
release outputs are local state, not obsolete source. Preserve them; use the
documented build clean task only when intentionally discarding generated outputs.
Keep source compact through shared implementation and focused comments, rather
than minification or dropping error handling, evidence or independent tests.
