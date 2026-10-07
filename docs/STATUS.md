# Status and known limitations

Reviewed on 2026-10-07. This is the current technical status, not a compatibility
promise. [Architecture](ARCHITECTURE.md) describes the implementation;
[Placement and rules](PLACEMENT.md) records the execution sequence.

## Validation evidence

- Historical ordinary-path placement measurements, including correction of the
  lateral-offset model, are retained in the [heading investigation](archive/HEADING_BUG.md).
  Those results do not establish accuracy for every car, setup, track, or build.
- The [66942337 investigation](archive/66942337_UPDATE_CHECK.md) records successful
  attachment and read-only planning after integration. Its live placement used a
  maintainer experimental command and manually supplied constants. The record
  does not establish that the integrated production placement was live-tested.
- Automated tests cover arithmetic, profile handling, discovery, cache rejection,
  indexed selection gates, spot-transaction rollback and restoration, and reporting.
  Some dump-based tests need local runtime captures and can be skipped without
  them. Neither a matching instruction pattern nor a passing test suite proves
  live placement behaviour.
  Each recorded build now has an independent skip. See [Testing](TESTING.md).

## Priority fixes

| Priority | Finding | Evidence and next step |
| --- | --- | --- |
| High | Practice-only use is stated but session type is not independently checked | `RestGates` is parsed but unused; define and enforce the session check before writes |
| High | UI placements do not save learning or observations | `PlacementRunner.RunPlacement` loads learned values but never calls `RestLearning.Apply`; align orchestration with the CLI |
| Medium | Live track/vehicle identity is optional | Both front ends continue after a shared-memory failure; decide whether placement must require identity |
| Medium | Checkpoint names are used directly as file paths | `CaptureService.Save` appends the name to a path; validate names and containment at the shared save boundary |
| Medium | Capture and measured rest can include motion | Shared-memory reads are not a single locked snapshot; rest is sampled after a 400 ms delay with player control; validate capture coherence and learning while moving |
| Medium | Discovery depends on known tuning values | `FindTuningBlock` matches a specific float sequence; a retune can block discovery despite otherwise matching structure |
| Medium | A manual ordinary-path calibration can be reused across builds | `PlacementService.Plan` only requires matching build for indexed placement; review staleness policy |
| Low | Invalid CLI sector input silently falls back to the final sector | `Program.Place` uses the default on failed parsing; reject malformed or missing values |
| Low | Rule identity depends on display text | `PlacementRunner.State` selects rules with label substrings; separate identity from presentation if changing this flow |
| Low | Each UI state poll reattaches and hashes the executable | `PlacementRunner.State`; measure before optimising, retaining write-time checks |
| Low | App build reports a WindowsBase reference conflict | Release solution build emits MSB3277 through WebView2's WPF assembly, although the app uses WinForms; review unused package references |

## Behaviour that still needs live checks

The new cancellation/close workflow through live garage and session transitions;
lap timing near start/finish; sector splits after a forced sector
write; capture at speed; and placement/learning across varied cars, tracks,
setups, and surfaces. Record full executable hash and test conditions for each
result rather than inferring support from a version string.

Captured tyre state, fuel, general damage, and checkpoint speed restoration are not implemented.
The compact UI provides per-wheel tyre settings; live readings remain in the
game, and protection/discovery diagnostics appear in Activity. Tyre reset addresses are discovered from the current executable,
code-probed, cached by full hash, and refused when the engine structure changes.
See [Tyres](TYRES.md) for verified fields, engine setters and remaining
thermal/damage investigation.
New captures store surface, carcass and inner-layer tyre temperatures in Celsius,
raw SDK wear (0..1), fitted compound names/indices and vehicle class. SDK surface
temperature channels are physical left/centre/right, not inside/outside. The wear
value is not presented as a remaining-tread or grip percentage. Older captures
without tyre data remain supported. A separate garage reset prepares the four
fitted tyres for Drive with independent FL/FR/RL/RR condition and optional initial
temperature. Per-wheel settings have automated validation but await a live test
using different values on each wheel. It is opt-in during placement and keeps fitted compounds. Fresh
condition and 70°C internal layers were measured live on build 66942337; surfaces
cool immediately afterward. The shared-memory output itself remains read-only.
The requested compound policy is Medium/Wet for GT3, LMP2 and LMP3, and
Soft/Medium/Hard/Wet for GTE and Hypercar. Actual compound availability and engine
indices must be resolved for the loaded vehicle before implementing selection.
Checkpoints store pose and metadata; placement does not resume the captured
vehicle condition or motion.
