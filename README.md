# Inactive Reset

An external practice tool for **Le Mans Ultimate**. Puts your car back on a
recorded point of the racing line, and turns off the penalties that make
practising from there annoying.

The name is a joke about iRacing's *Active Reset*. This one is less active.

> Offline single-player Practice only. The tool proves that over LMU's own
> local API before it touches anything, and refuses otherwise.

---

## What it does

**Teleport.** LMU keeps a *spot table* — the pit and garage positions it places
cars at. Press Drive from the garage and the engine reads your pit-spot entry
and puts the car there. So: overwrite that entry with a computed position, let
the engine do the placing, then put the original bytes back. **The engine
places the car; the tool only changes where the engine thinks your pit spot
is.** The write is 24 bytes, verified, and always restored.

**Rules.** Turn off the penalties that get in the way of practice:

- *Pit-speeding stop/go* — the engine reads this setting on every evaluation,
  so changing it applies immediately.
- *Track limits / lap invalidation* — this one is read **once at session start**
  and expanded into derived flags, so the setting itself does nothing
  mid-session; the derived flags have to be written instead.

That distinction is the single most important thing in this codebase. It is why
earlier attempts to change rules through the settings menu or the REST API had
no effect on a running session.

**Lap validity.** Not a rule, and not read from settings at all. The engine
accepts a start/finish crossing as a lap only when it thinks you are in the
final sector, and `Slot_Reset` zeroes the sector index — correct when you leave
the pits, wrong when the tool has just put you mid-lap. A car placed past the
last sector line therefore crosses the line and the engine ignores it
completely: no lap, no time. Placement restores the sector index, which costs a
whole lap otherwise. See `docs/LAP_VALIDITY.md`, which also records the
diagnosis this replaced and why it was wrong.

**Launching.** The game has two entry points and they are not interchangeable.
`Le Mans Ultimate.exe` starts it with no anticheat in the process tree, which is
the only kind of session this tool can attach to; `start_protected_game.exe` is
what Steam launches, and starts EasyAntiCheat first. The tool can start either
one — `launch direct` and `launch eac`, or the two buttons on the page — so
switching between practising and racing does not mean going back to Steam. The
install is found through Steam's own library configuration, so a second library
or a renamed folder is handled; `--game-dir` overrides it for one run and
`--set-game-dir` saves it.

Note that *direct* does not mean *offline*. Launching directly does not stop the
game reaching the network; what the word distinguishes is the anticheat, and
nothing else.

---

## Why offsets are data

Every LMU patch moves every address. When those addresses are compiled-in
constants, patch day means editing and rebuilding code.

Here, **offsets are data**: a JSON profile keyed by the executable's SHA-256.
A maintainer tool regenerates it by comparing two memory dumps, and publishing
is a `git push`. **You never run it.** When LMU updates, the app tells you the
build is new and offers to fetch the profile for it -- one file, over HTTPS, and
only if you say yes.

Constants the engine can retune without moving anything -- the pit-spot yaw
offset and clearance search factors -- are read live out of the running game
instead of being stored, so most game updates need no new profile at all.

---

## Layout

    src/InactiveReset.Core   offsets, memory access, gates, placement math, capture
    src/InactiveReset.Ui     local HTTP server and the single HTML page
    src/InactiveReset.Cli    command-line front end, and `serve` for the UI
    src/InactiveReset.App    windowed app: WebView2 around that same page
    tests/                   the test suite
    offsets/                 build profiles, one JSON per LMU build
    data/profiles-default/   calibrations shipped with a release
    docs/                    how the machinery actually works

The windowed app is the front end in normal use; the CLI is for diagnosis and
patch day. Both call the same command layer, so they cannot drift apart.

Start with [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): how both mechanisms
work, the known defects, and the mistakes already made here. Then
[`CONTEXT.md`](CONTEXT.md) for the vocabulary.

---

## Build

Needs the .NET 8 SDK. Everything goes through one script:

    .\build                  build (Debug)
    .\build test             build and run the tests
    .\build ship             Release build, tests, then dist\
    .\build clean            delete artifacts\ and dist\

Run `.\build`, not `.\build.ps1`. Windows blocks unsigned `.ps1` files by
default; `build.cmd` is a wrapper that runs the script with that restriction
bypassed for its own process only, so it works on a fresh machine with no
setup and no administrator rights.

The leading `.\` is required in PowerShell, which does not run commands from
the current directory. From `cmd.exe`, plain `build` works.

To run without publishing:

    dotnet run --project src/InactiveReset.Cli -- status
    dotnet run --project src/InactiveReset.App

Two things worth knowing:

- **Build the solution, not a project.** Every project is `x64` only, and the
  `.sln` is what maps `Any CPU` to `x64`. `dotnet build src\InactiveReset.Cli`
  fails where `dotnet build InactiveReset.sln` succeeds.
- **Ship is Release, always.** Debug disables inlining and changes
  floating-point codegen, and the placement math is measured against Release.

All build output goes to `artifacts/`, not a `bin/` and `obj/` beside every
project. `dist/` holds only the two shipping executables.

---

## Status

**Placement is accurate to sub-millimetre** on a calibrated combination. The
long-standing "11.633 degree heading error" is fixed: it was never a heading
error that calibration could not reach. The engine's displacement from a written
destination is a constant vector in the vehicle frame, and the model had terms
for only two of its three components, so half a metre of lateral miss had
nowhere to go. `docs/HEADING_BUG.md` has the measurement and why rotation and
translation are indistinguishable from outside the engine.

Verified by driving on LMU 1.4.1.3 (build `0F6DCAC1`): 0.000500 m horizontal,
0.000883 m vertical, down from 0.710 m.

### The limit you will actually hit

**Placement needs a calibration for your exact track and vehicle, and only one
ships.** Circuit de Barcelona in the Richard Mille AF Corse 296 GT3. In anything
else, `place` refuses with a clear message; everything else in the tool works.

The constants are per track, per vehicle **and per build** -- `D` moved 0.5 m
across a single LMU patch. There is no `calibrate` command yet, so you cannot
currently measure your own. Three cars were measured to see whether the
constants could be derived from the vehicle dimensions the tool already reads
live; no simple relationship holds. The intended fix is to find the value inside
the running engine, the way the yaw offset and search factors already are.

A calibration used on a build it was not measured on still places the car, and
warns when the miss is materially worse than that calibration has ever recorded.
It is a warning, not a refusal: being wrong about offsets means writing bytes to
wrong addresses, being wrong about a calibration means stopping half a metre
away.

---

## Install

Download the zip from [Releases](https://github.com/Kyraws/inactive-reset/releases)
and unzip it anywhere. Nothing to install: both executables are self-contained
and need no .NET runtime.

    inactive-reset-ui.exe    double-click; the windowed app
    inactive-reset.exe       console; run --help
    offsets\                 build profiles - keep this folder beside the exes
    data\profiles-default\   calibrations shipped with the release

Keep the folder together. Both executables find `offsets\` and `data\` by
searching upwards from their own location, and `offsets\shared-memory.json` is
required and is never downloaded.

Three things to expect on a first run:

- **Windows SmartScreen** will say "Windows protected your PC", because the
  executables are not code-signed. More info -> Run anyway. For a tool that
  writes into another process's memory that warning is not unreasonable, and
  you should be more suspicious of one that does not appear. The source is here
  and `.\build package` reproduces the zip.
- **The windowed app needs the WebView2 runtime.** Present on Windows 11 and
  most Windows 10 installs; if the window comes up blank, install the Evergreen
  runtime from Microsoft. The CLI does not need it.
- **A new LMU build** means the tool asks permission to fetch one JSON profile
  over HTTPS. Say no and it refuses to touch the game rather than guessing.

### Your calibrations versus the shipped ones

`data\profiles\` is yours and a release never writes to it.
`data\profiles-default\` is overwritten wholesale on upgrade. When both describe
the same track and vehicle, yours wins.

---

## Licence

MIT — see [`LICENSE`](LICENSE). Scope and third-party terms are in
[`NOTICE`](NOTICE).

The Studio 397 Plugin SDK headers that `tools/dump-sdk-offsets` compiles
against are **not** included: their own terms forbid redistribution. They ship
with the game under `Support\SharedMemoryInterface\`, so every user already has
them. See `tools/README.md`.

Unofficial tool. Not affiliated with or endorsed by Studio 397, Motorsport
Games, or the ACO.
