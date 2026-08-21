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

One known defect: placement is subject to an **11.633 degree heading error** in
the orientation-to-heading conversion, which lands the car about 0.57 m from the
target. It is
characterised but not fixed — see `docs/HEADING_BUG.md`. Run-to-run
repeatability is ~1.5 mm, so the mechanism is sound; only the aim is off.

**Do not attempt to fix it by re-calibrating** the forward/vertical constants.
That would absorb a rotation into two translation terms and be correct at
exactly one distance.

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
