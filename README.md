# Inactive Reset

A Windows practice companion for **Le Mans Ultimate**: repeat corners, build local
sessions, and practise official-event conditions. Built with **C# / .NET 8**, a
Windows Forms/WebView2 host, and an embedded HTML/CSS/JavaScript workspace.

**[Download for Windows x64](https://github.com/Kyraws/inactive-reset/releases/latest)** ·
[Report a problem](https://github.com/Kyraws/inactive-reset/issues)

## Get started

1. Extract the release ZIP and keep its files together. Run `inactive-reset-ui.exe`
   with Steam running. Requires Windows x64 and
   [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/);
   no separate .NET installation is needed.
2. Select **Launch local play** and dismiss LMU's **Press any button** screen.
3. Build a session or review a preset, apply it at LMU's single-player main menu,
   then select **Start session** and press **Drive** in LMU once loaded.
4. Capture a starting point in **Practice tools**, select it, and teleport to repeat.
   Garage placement prompts you to press Drive; keep pedals released for moving restoration.

### Choose the right launch mode

**With EAC on: teleporting, tyre preparation and in-session practice rule toggles are DISABLED.**
Use **Launch local play** for these tools. They also require an active **offline,
single-player Practice** session; EAC off alone does not enable them in other modes.

| Capability | Launch local play — EAC off | Launch with EAC — EAC on |
| --- | --- | --- |
| Build/apply/start a local session | Yes, at the single-player main menu | Yes, at the single-player main menu |
| Capture checkpoints | Yes, with valid active-session telemetry | Yes, when LMU exposes valid active-session telemetry |
| Garage teleport and on-track reset | Local single-player Practice only | **Disabled** |
| Prepare tyres and toggle in-session practice rules | Local single-player Practice only | **Disabled** |
| Online racing in LMU | Unavailable | Available through LMU |

**Close LMU completely before switching launch modes.** The app cannot change a
running game's EAC mode. Session-builder rules remain editable at the single-player
main menu in either launch mode. The app never edits multiplayer sessions or registers for or
joins online races. Official-event presets reproduce supported conditions in solo Practice.

## Features

- **Checkpoint capture:** save position, orientation, speed and gear. Quick capture
  replaces a reusable slot; named checkpoints keep individual starts.
- **Teleport with speed and gear:** return through the garage and Drive handoff,
  or use the experimental on-track reset. Moving restoration requires the capture
  car and an eligible straight, approximately level capture.
- **Standing teleport:** start stationary, including from older captures or another
  car's checkpoint on the same track.
- **Circuit maps:** preview checkpoints on LMU's circuit and pit-lane geometry.
  Cached maps and saved checkpoints remain available for offline browsing.
- **Checkpoint library:** filter by circuit/car, mark favourites, rename, copy Quick
  capture into a named checkpoint, delete and undo the latest deletion.
- **Local session builder:** choose owned cars, liveries and circuit layouts;
  configure weekend sessions, start times, race length, AI classes/grid and rules.
- **Weather and track conditions:** edit five forecast points for supported
  sessions, surface conditions and simulation settings, with confirmed game readback.
- **Personal session presets:** save, search, review, update, duplicate and share
  setups through import/export. Review is available offline.
- **Official-event practice:** browse cached daily, weekly and special events;
  review separate Qualifying/Race conditions and apply them to solo Practice.
  See the [supported conditions](docs/LAB.md#official-scheduled-sessions).
- **Garage tyre preparation:** set condition and optional starting heat per wheel,
  directly or during garage teleport, while keeping the fitted compounds.
- **Practice rules and lap repair:** control track-limit enforcement, temporarily
  suppress Flag Rules during garage placement, and repair lap state after teleport.
- **Keyboard and wheel controls:** assign shortcuts and controller buttons for
  capture, teleport, standing teleport and previous/next speed-ready checkpoint.
  Actions require LMU focus.
- **Activity and recovery:** follow progress, cancel operations, filter issues and
  export diagnostics from the fixed dark workspace.
- **Command-line tools:** capture, inspect, plan and place checkpoints, inspect rules,
  or serve the workspace in a browser. Run `inactive-reset.exe --help`.

## App tour

![Inactive Reset walkthrough of every app page and all five session-builder tabs](docs/media/interface.gif)

*Overview, all five builder tabs, Official races, Session presets, Practice tools,
Checkpoint library, Activity and Controls. Actual app UI shown with sample data;
the car emblem is loaded from the installed game.*

Reset and moving restoration are experimental and depend on verified engine
layouts. Unsupported game changes disable the affected capability. Fuel, damage
and captured tyre state are not restored. See [limits and recovery](docs/PLACEMENT.md)
and [known issues](docs/STATUS.md).

Back up `data/` before upgrading; keep `data/` and `offsets/` beside the executables.
The unsigned executables may trigger Windows SmartScreen.

## Engineering overview

The desktop host and command-line tool share a C# core. A loopback HTTP server
connects the embedded workspace to session management, checkpoint storage and
background operations. SDK telemetry is separate from native memory attachment;
local session setup uses the game's menu API.

| Engineering challenge | Approach and source |
| --- | --- |
| Read game state | [Shared-memory reader](src/InactiveReset.Core/SharedMemoryReader.cs) uses a generated SDK layout for telemetry; capture guards reject menu/loading leftovers. |
| Adapt to executable changes | [Automatic discovery](src/InactiveReset.Core/AutomaticOffsets.cs) derives addresses from mapped code using structural resolvers and instruction anchors, verifies probes, and checks caches against the current executable. Missing or ambiguous evidence refuses use; matching code does not prove live compatibility. |
| Limit temporary memory changes | [Spot transaction](src/InactiveReset.Core/SpotWriteTransaction.cs) checks build identity, writable pages and planned bytes, verifies writes by readback, and attempts restoration on normal or exceptional exit. Its scope is the spot-entry bytes; rule/lap cleanup is separate, and process termination cannot run disposal. |
| Enforce operation boundaries | [Game attachment](src/InactiveReset.Core/GameSession.cs) refuses EAC/protected launch. Memory actions additionally require active offline single-player Practice; the session API has its own menu/multiplayer gates. |
| Validate without the game | [xUnit and HTTP tests](tests/InactiveReset.Tests) and [WebView2 suites](tests/InactiveReset.BrowserTests) exercise domain logic, failure handling and real browser interaction using isolated fixtures. Live game checks remain separate. |

See [Architecture](docs/ARCHITECTURE.md) for component responsibilities and data
flow, [Testing](docs/TESTING.md) for coverage and execution limits, and
[research evidence](docs/research/README.md) for build-specific reverse-engineering
measurements. Private executable captures and SDK headers are not distributed.

### Build and test

Use Windows x64, the .NET 8 SDK and WebView2 Runtime. From the repository root
in PowerShell:

```powershell
.\build.cmd
.\build.cmd test -Configuration Release
```

The test wrapper runs portable C#/HTTP checks and actual WebView2 suites without
attaching to LMU. The [Verify workflow](.github/workflows/verify.yml) runs the
package path, including Release tests and an extracted-package smoke check.
See [Contributing](docs/CONTRIBUTING.md) for local packaging and optional
read-only compatibility checks. Automated tests do not establish live reset
reliability across every game build, car or circuit.

### Release and development status

The published download is [v0.4.0](https://github.com/Kyraws/inactive-reset/releases/tag/v0.4.0).
Its [successful Verify run](https://github.com/Kyraws/inactive-reset/actions/runs/40208372185)
applies to that release commit, not the newer local development work.

Work toward **0.5 is unreleased**. The local [telemetry recorder and timestamp
selection](docs/TELEMETRY_TIMELINE.md) integrate recorded pose/speed/gear with the
existing guarded operation runner. That integration has offline checks; recorded
resets have not been live-tested. Complete suspension, tyre-model and other native
state restoration remain unsupported. See [Status](docs/STATUS.md) for the current
implementation and research limits. No new release or live-validation result is
claimed by this README update.

### Project summary for CVs and applications

> Developed a Windows practice companion in C#/.NET with a WebView2 interface,
> shared-memory telemetry, local session API integration and runtime address
> discovery. Implemented guarded checkpoint placement, verified temporary writes
> with spot-byte restoration, and automated xUnit/HTTP/WebView2 checks. Memory
> operations are restricted to offline single-player Practice; compatibility and
> live outcomes remain build- and scenario-dependent.

[User and technical guides](docs/README.md) · [Build and contribute](docs/CONTRIBUTING.md) ·
[MIT licence](LICENSE) · [Third-party notices](NOTICE)

Unofficial; not affiliated with Studio 397, Motorsport Games or the ACO.
