# Architecture

Reviewed against the repository on 2026-10-07. See [Status](STATUS.md) for gaps
between intended behaviour and the current implementation.

## Components

| Component | Responsibility |
| --- | --- |
| `src/InactiveReset.Core` | Process access, offset discovery, placement maths, transactions, rules, telemetry, learning and LMU menu API |
| `src/InactiveReset.Ui` | Loopback HTTP server, embedded session/practice workspaces and background operation runner |
| `src/InactiveReset.App` | Windows Forms window hosting the page through WebView2 |
| `src/InactiveReset.Cli` | Console commands and an optional browser UI server |
| `src/InactiveReset.Reanchor` | Maintainer-only dump analysis and experimental probes |
| `tests/InactiveReset.Tests` | Maths, format, discovery, and report regression tests |
| `tools/` | SDK layout generator, native observers, and discovery-resource generation |

The CLI and UI use `PlacementService` and `PlacementReport`. Their orchestration
is separate: the CLI records learning after placement; the UI currently only
loads previously learned values. Sharing Core does not guarantee identical
behaviour in both front ends.

## Launch and attachment

`GameLauncher` finds the install through an explicit override, a saved override,
or Steam's library and application manifests. It starts either
`Le Mans Ultimate.exe` or `start_protected_game.exe`, with Steam already running.
Direct launch allows local sessions only, as confirmed by the project owner.
Protected launch is the route for online racing.

`GameSession.Attach` checks for a running LMU process and rejects attachment if
an EasyAntiCheat or protected-launcher process is detected. This is a
machine-wide process check, not a process-tree inspection. It hashes the
installed executable and compares its PE identity with the mapped image before
using discovered offsets and checking the live probe bytes.

Single-player Practice is the supported scope for checkpoint and tyre writes.
The windowed runner additionally checks LMU's navigation API for an active local
Practice session before placement, tyres or rule toggles. The CLI still lacks
this independent check. Do not describe the absence of EAC as proof that the
game is in Practice.

The window opens `/lab`; `--classic` opens `/session`, and `/` retains the original practice workspace. Session actions
share the runner's operation lock and cancellation with practice transactions.
Both pages use the embedded `App.css` and the same header, navigation and
three-column shell. Practice progress remains in the fixed summary panel.
`LmuSessionClient` reads and edits the current local menu setup, then starts it
through preset/apply/generate-save/load-save calls. It requires a local main
menu and explicit startup-prompt confirmation; it does not attach to game
memory. See [Local sessions](SESSIONS.md) for controls and launch limitations.

## Automatic offset discovery

`AutomaticOffsets.LoadOrDiscover` uses
`offsets/<full-executable-sha256>.auto.json`. Every attachment captures the mapped
module and fingerprints its PE header and executable sections. Discovery derives
the complete expected profile; an identical build/version/code fingerprint can
reuse that immutable derivation in memory. Disk caches are reused only when the
entire JSON profile equals the verified derivation and the live probe matches.
Altered fields or model metadata are repaired. Capture/discovery/probe failures
preserve the previous cache and refuse attachment.

Structural resolvers identify tables, indices, stride, ownership, dimensions,
pit state, and rule fields. Embedded instruction anchors resolve remaining
vehicle and lap fields. Missing, ambiguous, or disagreeing matches abort
discovery. The cache is written through a temporary file and replaced locally.
The application does not download profiles from GitHub.

Discovery is constrained by the implemented instruction shapes and field
relationships. Tuning discovery follows the search and yaw consumers without
matching stored float values. The pit-speed rule resolver decodes both branch
destinations and requires the speed read and reset to use the same field;
field and branch displacements can change. Missing or ambiguous relationships
still refuse discovery. Resolver version 4 invalidates earlier placement caches.
This is not a guarantee of compatibility with future patches.
Automatic profiles record `placementValidated=false`; the current production
path does not use that field as a live-validation gate.

The tracked `offsets/0F6DCAC1.json` and `offsets/1AC2F605.json` remain historical
reference inputs for tests and maintainer analysis. `GameSession.Attach` does
not select them through the legacy `OffsetProfile.ForBuild` lookup.

## Placement planning

A checkpoint contains track and vehicle names, a recorded position and
orientation, lap distance, and gear. Capture reads LMU's shared-memory interface
without writing to the game. Its pose is validated before saving.

The UI selects checkpoints by a relative file ID and shows only the loaded
track. CLI name lookup uses the loaded track and prefers a same-car match;
remaining ambiguity is refused with the available IDs. Explicit IDs select the
exact capture, including a capture from another car.

`PlacementService.Plan` requires matching track geometry and the loaded car's
calibration and learned corrections. The capture car is provenance, not a
placement restriction. Both front ends allow identity to be absent if shared
memory is unavailable, falling back to the recorded car. The plan reads live container fields
and engine tunables, validates the target, computes the entry, and checks the
predicted position against it. Read-only planning may create an offset cache;
it does not write game memory.

There are two destination layouts:

- **Ordinary PitPos:** the inverse models the engine's yaw adjustment,
  clearance-search displacement, and resting offsets.
- **Indexed destination:** the entry at `pitIndex * 3 + 2` uses a direct inverse
  with the engine's resting distance, without the ordinary clearance search.

The spot entry is 32 bytes. Placement writes its first 24 bytes, containing
position and orientation, and preserves the eight-byte tail. Rule and lap-state
writes are separate from that transaction.

For the execution sequence and failure paths, see [Placement and rules](PLACEMENT.md).

## Placement constants and learning

The chosen forward/lateral constants come from learned checkpoint values, an
eligible manual calibration, or the running engine, in that order. For indexed
placement, a manual calibration must match the executable hash; the ordinary
path currently accepts a valid unlocked calibration from another build.

The ordinary model includes fitted settle coefficients and a lateral sign.
The indexed model derives a forward distance and starts with zero lateral
offset. The placeholder vertical offset is a borrowed constant, not a general
physical model. Live engine tunables are range-checked; a checked snapshot from
the offset profile is used if the live read is rejected.

`RestLearning` evaluates a completed outcome, rejects unsuitable samples, and
can update forward, lateral, and near-target vertical corrections. Values are
keyed by game build, track, vehicle, and checkpoint. Currently only CLI
placement calls `RestLearning.Apply` and records observations; UI placement
does not save new learning.

## Data and build output

| Location | Purpose |
| --- | --- |
| `offsets/shared-memory.json` | Generated SDK layout required for telemetry |
| `offsets/*.auto.json` | Ignored local discovery caches |
| `data/checkpoints/` | User checkpoints |
| `data/profiles/` | User manual calibrations |
| `data/profiles-default/` | Tracked shipped calibrations |
| `data/learned-rest/` | Build-specific checkpoint corrections |
| `data/observations/` | CLI placement observations |
| `artifacts/` | Build output, local analysis, and package staging |
| `dist/` | Published executables and release ZIP |

The window and CLI find data folders by searching upwards from the executable,
unless overridden. The window serves the UI on a selected loopback port;
`Server` binds to `127.0.0.1` and requires JSON POST requests for actions.

`build.cmd` wraps `build.ps1`; invoke `.\build.cmd` explicitly in PowerShell.
An extensionless `.\build` can select the script instead. The solution maps configurations to x64 and
targets .NET 8 for Windows. `ship` tests Release and publishes the App and CLI;
`package` adds the SDK layout, shipped calibrations, licence, notice, and linked
Markdown guides, including the historical archive.
It does not include the research executable or native observers. CI runs the
package command on Windows. See [Contributing](../CONTRIBUTING.md) for commands.
