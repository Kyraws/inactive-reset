# Inactive Reset

Inactive Reset is an unofficial practice tool for **Le Mans Ultimate**. Save a
checkpoint on the racing line and return to it from the garage to practise a
corner or section of track.

**For offline, single-player Practice only.** The tool verifies the game build
and refuses attachment when EasyAntiCheat is detected. It does not independently
check the session type; choose Practice before using it.

## Getting started

1. Download the release ZIP from [Releases](https://github.com/Kyraws/inactive-reset/releases).
2. Extract it to a folder and keep its contents together.
3. Open `inactive-reset-ui.exe`. Steam must be running before you launch LMU.
4. Select **Launch game**, then start an offline, single-player Practice session.

The release runs on Windows x64 and includes its own .NET runtime. The windowed
app also needs Microsoft WebView2. If the window is blank, check that the
WebView2 runtime is installed. A console version, `inactive-reset.exe`, is
included; run it with `--help` for commands.

Release executables are unsigned, so Windows may show a SmartScreen warning.
Download from this repository's Releases page and check the source before
choosing to run them.

## Using checkpoints

1. Drive to the position you want to practise from.
2. Enter a checkpoint name, such as `turn-1-entry`, and select **Capture**.
3. Return to the garage, select the checkpoint, and select **Place**.
4. Wait for **PRESS DRIVE** in Activity, then press **Drive** in LMU.
5. Follow the Activity messages as placement completes. Repeat from the garage
   whenever you want another attempt.

The capture list shows only the loaded track. Captures can be reused with
another vehicle on that track; placement uses the loaded car's calibration or
live engine offsets. It can derive starting values without manual calibration.
CLI placements save learned corrections; the windowed app currently uses
existing corrections but does not save new ones.

Placement restores a position, not captured speed, fuel, tyres, or damage.
New captures also store four-wheel tyre temperatures in Celsius, raw SDK wear,
vehicle class and fitted compounds. This records tyre state; placement does not
apply it.
The compact Tyres panel prepares all four fitted tyres in the garage, with
separate condition and optional initial temperature settings for FL, FR, RL and RR. Placement can include the
same reset. It keeps the loaded car's compounds. Live readings remain in the game;
discovery, protection and calibration messages appear in Activity. See [Tyres](docs/TYRES.md) for
the workflow, live evidence and build support.

Placement verifies Flag Rules are off before arming, so you can drive normally
after teleport. It restores the original rules when pit state clears or you
return to the garage. There is no pit-clear timeout. The tool does not control
the car's pit limiter.

Cancel or close restores an armed placement immediately. After teleport,
cleanup waits for pit state to clear or a garage return; the app stays open
until that cleanup finishes.

Track limits have a separate toggle. Check its displayed state after returning
to the garage or starting a session, since LMU can re-enable them. Placement
also attempts sector and out-lap repairs immediately after settling. See
[Placement and rules](docs/PLACEMENT.md) for the sequence and
[known limitations](docs/STATUS.md) before relying on lap timing.

## Launching and game updates

**Local practice** starts LMU without EasyAntiCheat and only allows local
sessions. Use a single-player Practice session with Inactive Reset. For online
racing, close the game and use **With EAC**. Inactive Reset refuses to
attach to that session.

The tool finds LMU through Steam's library configuration. If detection fails,
the CLI accepts `--game-dir` for a one-time override or `--set-game-dir` to save
the location.

When the game executable changes, Inactive Reset automatically discovers the
required memory offsets locally and caches them. If discovery cannot resolve a
required field unambiguously, placement stops and reports the problem. A game
update can still require changes to the tool.

## Saved data

Keep the `offsets/` and `data/` folders with the executables.
`offsets/shared-memory.json` is required and is included in the release.

| Folder | Contents |
| --- | --- |
| `data/checkpoints/` | Your saved checkpoints |
| `data/learned-rest/` | Placement corrections learned for each game build |
| `data/observations/` | Placement logs, including rejected observations |
| `data/profiles/` | Your manual calibrations, if any |
| `data/profiles-default/` | Calibrations included with the release |

Back up your data before upgrading. Releases replace the shipped calibrations
in `data/profiles-default/`; your manual calibrations in `data/profiles/` take
priority over those defaults.

## Reporting problems and contributing

For a useful bug report, include the Inactive Reset version, LMU version, track,
vehicle, steps to reproduce, and the relevant Activity message or placement log.
Describe what you expected and what happened. Please keep discussion respectful
and focused on information others can use to reproduce the problem.

For code contributions, start with [Contributing](CONTRIBUTING.md) and the
[documentation index](docs/README.md). Dated investigations and superseded
guidance are kept in the [archive](docs/archive/README.md).

### Building from source

Install the .NET 8 SDK, then run these commands from the repository root in
PowerShell:

```powershell
.\build.cmd          # Debug build
.\build.cmd test     # Build and run tests
.\build.cmd ship     # Test and publish Release executables to dist/
.\build.cmd package  # Create a self-contained release ZIP
```

Use `.\build.cmd` explicitly; PowerShell can select `build.ps1` for `.\build`.
The wrapper runs the script without a machine-wide execution-policy change.
Build the solution rather
than individual projects so its x64 configuration is applied. Build output goes
to `artifacts/`; published releases go to `dist/`.

## Licence and credits

The source code is licensed under [MIT](LICENSE). See [NOTICE](NOTICE) for
third-party terms. Studio 397 Plugin SDK headers are not redistributed; they
are supplied with LMU. See [tools/README.md](tools/README.md) for SDK tooling.

Inactive Reset is not affiliated with or endorsed by Studio 397, Motorsport
Games, or the Automobile Club de l'Ouest.
