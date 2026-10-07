# Tyre telemetry and engine investigation

Captures store four-wheel telemetry in FL/FR/RL/RR order: surface, carcass and
inner-layer temperatures in Celsius, raw wear, fitted compounds, flat tyres and
detached wheels. Surface and inner channels are physical left/centre/right.
The compact UI leaves live tyre viewing to the game. Its Tyres panel prepares all four fitted tyres
in the garage. FL, FR, RL and RR each have condition (0–100%) and optional initial
heat (0–150°C). Set 100% for a fresh tyre; lower values request that wheel's
remaining condition. Unchecked heat uses the game's initial temperature for that
wheel. Press Drive when Activity says tyres prepared.
Select **Apply on placement** to include the same reset during placement.

This edits the four selected inventory records and lets the game's Drive loader
rebuild the tyre models. It keeps the loaded car's fitted compounds and does not
allocate new inventory slots or copy another car's compound IDs. Cancel before
Drive to restore the pending records. Once Drive consumes them, cancellation
does not undo the fitted tyres. Unsupported builds refuse the reset. Temperature
is an initial condition; the tyres continue heating and cooling normally.

All four settings are validated and copied before preparation. Condition is
written to each wheel's selected inventory record; thermal writes apply only to
wheels with initial heat enabled. After Drive, each wheel is checked against its
own condition and optional temperature. Uniform Core/API callers remain supported.
Independent settings have automated parser, ordering and verification checks;
the earlier live test below used equal settings on all four wheels. A live test
with different values on each wheel is still needed.

Activity diagnostics separate effective per-car invulnerability
from the stored invulnerability, wear multiplier and damage multiplier settings.
Tyre physics addresses are discovered from engine consumers for the current full
executable hash and cached only after every code probe matches. A missing or
ambiguous anchor makes the tyre controls unavailable; the app never reuses a
different build's addresses. Captured telemetry uses the compiler-generated SDK
layout independently.

Cache reuse also derives the field values again from the verified code operands.
Altered values or incomplete probes trigger fresh discovery. The current
resolver was checked against the recorded 1.4.2.0 image and a modified copy with
moved globals and structure offsets. This demonstrates relocation handling;
it does not guarantee compatibility with an unseen game patch.

## Engine evidence, 2026-10-07

Read-only inspection of the running Team WRT 2026 #32:LM at Daytona located the
physics pointer at vehicle + `0x159A0`. These offsets are specific to the build
above. Wheel blocks have a `0x6B0` stride, with the following offsets from physics
base plus wheel index times that stride:

| Offset | Observed state |
| --- | --- |
| `0x988` | Carcass temperature, Kelvin; agrees with telemetry |
| `0x998`, `0x9A0`, `0x9A8` | Inner-layer temperatures, Kelvin; agree with telemetry |
| `0x9B0`, `0x9B8`, `0x9C0` | Surface temperatures, Kelvin; agree within 0.002 K in the sampled FL wheel |
| `0x9E0` | Cached wear double; all four wheels agree with telemetry within 3e-8 |
| `0xA00` | Pointer to the detailed tyre model |
| `0xA08` | Separate heat-related scalar; **not** the telemetry carcass temperature |

Static traces identify these engine paths (all addresses are module RVAs):

| RVA | Evidence |
| --- | --- |
| `0xED2550` | Tyre replacement/initialisation path: sets surface readings from the initial temperature, resets wear at `0xED2708` to 1.0, and creates the detailed tyre model when absent |
| `0xED2E60` | Logs uniform wear setting, writes cached wheel wear, then calls `0xF12B10` on the detailed tyre model |
| `0xED2DF0` | Routes saved wear/heat data through the wheel's detailed tyre pointer to `0xF128D0`; logs a missing-model failure |
| `0xF12B10` | Uniform setter updates distributed wear state and heat indices, with negative inputs skipping the corresponding update |
| `0xF128D0` | Restores distributed saved wear values; clamps each value to its model maximum and updates dependent factors |
| `0xC57A30`, `0xC57C90` | Save tyre state into the tyre inventory; log before/after wear and copy distributed state |
| `0xF5EEE0` | Updates four per-car wear values and calls the uniform setter for each wheel when the control check permits |
| `0xEE8650` | Copies stored aids into per-car physics and clamps them to session allowances; invulnerability lands at physics + `0x13D` |
| `0xF4D2EC` | Checks that effective invulnerability byte; nonzero branches to the early return at `0xF4DB20`. An auto-pit aid check can also take that return under a separate condition |
| `0xF5D218` | Packs effective invulnerability into bit 7 of the vehicle's aid/status byte |

The current live rule sample was effective invulnerability off, stored
invulnerability 0, stored wear multiplier 1 and stored damage multiplier 100.
The global settings are separate from copied per-car flags and rates. Do not
assume changing a stored setting immediately changes tyre physics, or that
invulnerability disables every form of wear and heat. The exact scope of the
early-return path and all other wear/damage gates still need tracing.

The detailed tyre model has distributed contact-node state. Its wear setters
also recompute dependent values. Writing the cached `0x9E0` or reported
temperatures alone is not established as a persistent physics modification.
The garage-to-Drive fresh-condition test on the build above returned wear 1.0
for all four wheels, retained Medium compounds, and reported neither flat nor
detached flags. The owner confirmed all four tyres behaved normally and the
punctures were gone. A false SDK flat flag alone did not explain the previously
exhausted rear-left tyre.
Inventory wear is stored as percent (100 is fresh), while physics/telemetry use
0–1 remaining condition. The prepared record sets the uniform-reset flags and
leaves the separate heat-index setter untouched.

Optional temperature application writes all five layers of every thermal node,
the detailed model's bulk temperature, and the seven observed temperature
channels after Drive. It uses bounded numeric readback because physics continues
updating these values. It also checks four-wheel telemetry against the request;
failure is reported rather than claimed successful. The live 70°C test returned
69.997–70.000°C carcass/inner layers on all four wheels, with 100% condition;
surfaces had cooled to 63.1–64.1°C by the sampled readback. Verification checks
internal layers within 2°C and reports current surfaces separately. The first
exact-byte verification attempt stopped partway through the first tyre; numeric
readback fixed that race. No engine function is injected or invoked directly. Per-frame
thermal writers and every damage suppression gate are not yet fully mapped.

Local mapped image, disassembly, probe code and live comparisons are retained
under `.scratch/tyre-state/`; proprietary SDK headers and memory dumps are not
distributed. The verified 1.4.2.0 profile remains in
`offsets/tyre-physics-66942337.json` as dated evidence. Runtime discovery writes
an ignored `tyre-physics-<full-hash>.auto.json` cache beside it.
