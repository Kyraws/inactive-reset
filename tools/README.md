# Developer tools

These tools are for maintainers. They are not included in release packages and
are not required for normal checkpoint use. See the [architecture](../docs/ARCHITECTURE.md)
for the application's automatic discovery.

| Tool | Purpose |
| --- | --- |
| `dump-sdk-offsets.cpp` | Generate the shared-memory byte layout from LMU's installed SDK headers |
| `src/InactiveReset.Reanchor` | Capture and inspect mapped modules, investigate addresses, and run experimental probes |
| [Discovery-resource scripts](reanchor-research/README.md) | Regenerate embedded instruction anchors |
| [Native observers](observer/README.md) | Observe natural engine calls in a direct-launch Practice session |
| `ghidra/` | Export reader evidence from a locally analysed image |

## Shared-memory layout

`offsets/shared-memory.json` contains the telemetry layout consumed by
`SharedMemoryOffsets`. Generate its offsets using the compiler's `offsetof`
against the SDK headers supplied with LMU under
`<LMU install>\Support\SharedMemoryInterface\`.

The headers are not redistributed. Keep them outside version control; see
[NOTICE](../NOTICE). A normal .NET build uses the committed layout and does not
need or compare installed SDK headers. The JSON's `sdkHeaderSha256` is provenance,
not an automatically enforced SDK compatibility check.

From an x64 MSVC Developer command prompt at the repository root, replace
`<LMU install>` with the actual path:

```text
cl /std:c++17 /EHsc /O2 /I"<LMU install>\Support\SharedMemoryInterface" tools\dump-sdk-offsets.cpp /Fe:tools\dump-sdk-offsets.exe
tools\dump-sdk-offsets.exe > artifacts\shared-memory.generated.json
```

Create `artifacts/` first if it does not exist. The generator emits the layout
but does not add `sdkHeaderSha256`. After reviewing the generated layout, add
that field from the header used to compile it before replacing the committed
file. For example, in PowerShell:

```powershell
$layout = Get-Content artifacts/shared-memory.generated.json -Raw | ConvertFrom-Json
$headerHash = (Get-FileHash '<LMU install>\Support\SharedMemoryInterface\SharedMemoryInterface.hpp' -Algorithm SHA256).Hash
$layout | Add-Member -NotePropertyName sdkHeaderSha256 -NotePropertyValue $headerHash
$layout | ConvertTo-Json -Depth 10 | Set-Content offsets/shared-memory.json -Encoding utf8
```

Revisit the layout when SDK headers change. Telemetry vectors use eight-byte
components; the loader rejects a layout declaring another component size.
Executable-address discovery and SDK layout generation are different processes.
A code update does not necessarily change the shared-memory layout.

## Maintainer analysis

Build the solution before running the Reanchor executable from its output in
`artifacts/bin/`. Its `--help` lists read-only analysis and explicitly armed
experimental commands. Experimental writes are not production validation.

Mapped images, native binaries, local logs, and private SDK headers remain
untracked. Do not publish captured game images. Historical experiments and
build-specific addresses are in the [archive](../docs/archive/README.md).

## Experimental speed restoration

New checkpoints store optional `motion.local_velocity_mps` and the existing
capture gear. Speed is the velocity magnitude converted to km/h. These are
recorded values only; normal placement still starts stationary.

From the repository root after building Release:

```powershell
dotnet artifacts/bin/InactiveReset.Reanchor/release/inactive-reset-reanchor.dll velocity-probe <full-build-sha256> <checkpoint.json>
```

The default command is read-only with respect to game memory. It requires a
moving checkpoint captured at 0.5 km/h or faster (no upper speed ceiling), the same track and capture vehicle, active
offline Practice without EAC, and a stopped player-controlled car in neutral
with pit state clear. It uses locally discovered offsets and the exact executable
hash supplied. Paths default to repository `offsets/` and `data/`;
`INACTIVE_RESET_OFFSETS` and `INACTIVE_RESET_DATA` override them.

Append `--arm` to attempt ONE 24-byte write to the candidate world/local
velocity fields and record two seconds of SDK speed, position, elapsed time and
motion-block readings under `artifacts/velocity-probe/`. Observe natural driving
first to cross-check the motion fields against SDK velocity. Stop the car in a
clear area before an armed test. The probe does not restore gear, engine speed,
wheel rotation or angular velocity. It does not loop writes or restore stale
velocity after the car starts moving. A readback failure may mean the write was
partially applied or physics immediately changed it; the observation log keeps
that failure and subsequent samples.

A completed observation file or zero exit status is not proof of a successful
rolling reset. Review elapsed-time progression and actual position change as well
as reported speed. The first live test on build `66942337` (Monza Curva Grande,
Richard Mille AF Corse 2026 #50:ELMS, rear against a wall, clear space ahead,
neutral, pedals released) verified the write but found near-zero velocity again
by the 24 ms sample. SDK speed stayed below 0.0024 km/h and position moved less
than 0.00004 m over two seconds. A velocity write alone did not restore motion
in that test. Physics authority and drivetrain restoration remain unresolved. The probe never feeds placement learning or enables a production
option. The existing telemetry reader is not a locked SDK snapshot, so capture
coherence while moving also needs validation.

The maintainer probe also accepts `--bodies`. This selects a separate experiment
on the researched full `66942337` executable hash and the 12-body/two-chassis
layout observed on the Ferrari 296 LMGT3. It checks the exact code paths that
copy body velocity into telemetry-facing fields and integrate body velocity into
position. A read-only natural-driving sample correlated these values with SDK
speed. A subsequent explicitly armed test verified all 12 body writes and
produced 3.97 m of clean, owner-confirmed forward movement over two seconds. The
8.94 km/h request peaked at 7.65 km/h and decayed to 6.86 km/h. This is evidence
for a low-speed motion prototype on that car/build, with exact speed and complete
rolling restoration still unresolved.

Without `--arm`, this mode only prepares and reports the proposed translation.
With `--bodies --arm`, it adds one shared world-velocity change to each body's
existing linear velocity, preserving their relative velocities. On the researched
car this is 12 separate 24-byte writes, 288 bytes total. The batch is not atomic
and may partly complete if a write fails. It leaves gear, wheel rotation and
angular momentum unchanged. It retains the low-speed and stationary gates and
records body velocities with subsequent telemetry. It is never called by normal
placement and does not establish a complete rolling reset.

The alternative `--wheels` mode prepares the same body translation plus four
wheel angular velocities, angular momenta and scalar spin rates (512 bytes
total). It adds released-pedal, stopped-wheel, wheel-membership, orientation,
radius and inertia gates. It derives approximate rolling spin from captured
longitudinal velocity and current wheel radii; it does not restore tyre-contact
state or the drivetrain. Use it instead of `--bodies`. Without `--arm` it
remains read-only.

One explicitly approved live test stopped at the second body's readback
mismatch, after one verified body write and before any wheel writes. The car
moved smoothly according to the owner, peaking at 6.01 km/h and moving 3.05 m.
The mismatching write may also have applied. This partial batch does not validate
wheel restoration. No retry was performed. Applying disjoint state while physics
advances remains unresolved; neither mode is suitable for production placement.

The `--paused-wheels` alternative requires the driver to stop in neutral,
release both pedals and open the normal pause menu first. It checks the game's
explicit pause state, frozen SDK time and stable body/wheel values, checks the
pause boundary before each write, and verifies the complete applied state while
paused. With `--arm` it attempts the same 512-byte batch and leaves the game
paused. Resume is a separate manual action: the written motion may take effect
then. Normal pause was observed to freeze all sampled state on the researched
car/build; this is not an atomic lock if the driver resumes during the batch.
No process suspension, native calls or automatic Resume are used.

The explicitly approved paused test verified all 12 body writes and 12 wheel
fields, then repeatedly verified the complete state for two seconds before
manual Resume. On Resume, SDK speed first read 8.56 km/h for an 8.94 km/h request,
peaked at 8.56 km/h and coasted to 7.70 km/h over two seconds, travelling 4.59 m
with released pedals. The owner confirmed smooth movement. This establishes one
paused low-speed prototype on the researched car/build; the initial 4.2% speed
deficit, tyre-contact/drivetrain state and placement integration remain open.

The `--auto-wheels` alternative owns the researched physics-pause byte, freezes
physics without opening the pause menu, verifies the 512-byte motion batch and
resumes automatically (514 bytes including pause set/restore). It does not pause
audio. A possible partial motion update stays paused for recovery. Without
`--arm` it only prepares the plan. `--placement-wheels` uses the same sequence
but also permits raw pit state 5 / API EXITING; it never changes pit state.
All probe alternatives are mutually exclusive.

Body, wheel and pause primitives are shared with the opt-in application rolling
start. The app additionally waits for the post-Drive control handoff and
temporarily releases/restores Hold Brakes; the standalone probe does not manage
that assist and still requires released physical and filtered pedal values.
See [Placement](../docs/PLACEMENT.md) and [Status](../docs/STATUS.md) for current
integration evidence and limits. Earlier prototype paragraphs above describe
their individual test stages.

The `--gear-wheels` mode extends automatic body/wheel restoration with captured
gear, calculated engine angular velocity and clutch state using the loaded car’s
ratios and rev limit. It accepts the inherited placement EXITING state and
requires stopped neutral before starting. The additional 64 bytes bring the
automatic batch to 578 bytes including pause/resume. First-gear placement at
39.86 km/h and third-gear placement at 148.52 km/h passed live on the researched
Ferrari/Monza setup; other gears remain unverified. All motion remains paused after a possible partial update. It does not initialize tyre-contact state.
