# Continuous telemetry recorder

The app and `inactive-reset serve` own one background recorder. The standalone
`inactive-reset record` command uses the same implementation; Ctrl+C flushes it.
It reads the official SDK mapping without native memory attachment, including
when EAC is enabled. Only the SDK's lock-coordination mapping is writable.
Practice tools and `GET /api/recordings/status` expose status and errors.

Open **Practice Teleport** and pick a recorded session by date, layout and
complete lap count. The picker shows sessions from the loaded layout, a
compatible chassis and the current build, with at least one speed-and-gear
point on a complete lap. The app numbers the retained complete laps and marks
the quickest. LMU's lap number appears when the recorded frame includes it.
Click the route, move the slider or type elapsed lap time to select a retained
frame. Once the lap loads, frame selection uses cached preview data.

Recording headers carry a session identity across continuous play and verified
pause/resume. A clock reset, game identity change or loss of continuity starts
a new session. Older files without this identity remain grouped by segment.

Recorded speed/gear reset and standing pose reset use the existing production
operation runner and its Practice, EAC, identity, motion, cancellation and cleanup
gates. Both recorded modes additionally require the recorded executable build
and car. Reset resolves the exact segment/sequence again; an expired sample is
refused. The recorder stays read-only and independent of native attachment.
Legacy checkpoints remain supported. Optional version-1
`restoration` checkpoint metadata retains source segment/sequence/time and SDK
details; it does not enable additional native writes.

Pose uses existing reset/placement behavior; speed and gear require eligible
motion and a supported live layout. Angular motion, suspension, arbitrary wheel
state, tyres, fuel, energy, damage and the session clock are not restored. The
selector states these limits and offers standing teleport for unsuitable motion.
Garage teleports can apply an explicit tyre condition and temperature override.
On-track teleports use the currently fitted tyres. See [Tyres](TYRES.md).

The speed reset starts with zero chassis angular velocity/momentum. Wheel spin
is recomputed from selected longitudinal speed and the loaded wheels' radius,
orientation and inertia; recorded per-wheel rotation is not applied. The current
linked-body relative positions/orientations are retained and transformed to the
selected pose, so the suspension configuration comes from reset time, not the
recording. Recorded deflections, ride heights, loads and third-spring values
remain observations. Restoring those scalars alone has no verified native
reconstruction path. Standing garage placement uses normal engine placement.

`GET /api/recordings/list` lists retained segments and ranges. `POST
/api/recordings/select` accepts `recording` and either `sequence` or `timestamp`.
`POST /api/place` accepts `recording`, exact `sequence`, `restoreSpeed`, and optional
garage `tyres` overrides. These share Core selection
and the existing operation path; client-supplied pose/state is never trusted.
Refresh scans retained JSONL on demand; there is no persistent index. A corrupt
complete record can fail that request; incomplete final lines remain recoverable.
An owner-driven recording validated moving capture and stale-time handling in the
conditions described below. See [Status](STATUS.md#recorded-live-scope) for the
separate recorded-frame teleport trials and their limits.

## SDK evidence and field matrix

Offsets were regenerated with MSVC x64 against the installed SDK on 2026-11-03:

- `SharedMemoryInterface.hpp` SHA-256:
  `A82833D5E9E277A7C3AF8802518E35EA62F4EC7C7C7C9083650282E4EDF1F8BF`.
- `InternalsPlugin.hpp` SHA-256:
  `9B6EE8CF610FA5049B18DF580A9A9BC9EBB91346FC466584D576A6442ABCF68F`.
- Layout: 324,824 bytes; telemetry entry: 1,888 bytes; wheel: 260 bytes.
  The compiler emits offsets; SDK headers are not redistributed. Header provenance
  does not prove that a future game's published layout remains compatible.

The SDK declares a left-handed world frame with Y up. Vehicle-local axes are
X left, Y up and Z rearward. Positive rotations pitch up, yaw right and roll
right. Orientation rows convert local vectors to world components by dot product.
Wheel order is FL, FR, RL, RR. Fields below are observations, not writable engine
addresses. Telemetry values retain SDK units unless a conversion is stated.

| Recorded SDK fields | Units / frame | Existing restoration / remaining work |
| --- | --- | --- |
| `mElapsedTime`, `mDeltaTime` | Session seconds; seconds since telemetry update | Recording time, not a restored clock |
| `mPos`, `mOri` | World metres; orientation matrix rows | Existing gated pose reset; not a complete chassis rewind |
| `mLocalVel`, `mGear` | Local m/s; -1 reverse, 0 neutral | Existing speed/gear reset for eligible motion, current verified layout and capture car |
| `mLocalRot`, `mLocalRotAccel`, `mLocalAccel` | Local rad/s, rad/s², m/s² | Recorded in `Details.Values` as `.x/.y/.z`; arbitrary recorded angular/chassis restoration requires verified native discovery |
| `mSuspensionDeflection`, `mRideHeight`, `mWheelYLocation`, `mVerticalTireDeflection` | Metres per wheel; wheel Y is relative to vehicle Y | Observed; authoritative state, caches and write order remain unverified |
| `mSuspForce`, `mTireLoad` | Pushrod load and tyre load, N | Observed forces; may be derived rather than appropriate write targets |
| `mRotation` | Wheel rad/s | Existing rolling reset computes wheel motion; does not restore arbitrary recorded per-wheel rates |
| `mFront3rdDeflection`, `mRear3rdDeflection` | Third-spring deflection; header does not explicitly specify units | Observed; units/authoritative state require further evidence |
| `mFrontRideHeight`, `mRearRideHeight` | Header names ride height without explicit units/reference point | Raw observations; native semantics remain unverified |
| `mUnfiltered*`, `mFiltered*` throttle/brake/steering/clutch | 0..1 pedals; -1..1 steering left to right | Recorded independently; no input replay |
| `mFuel`, `mFuelCapacity` | Litres | Read-only; fuel writes require verified discovery, bounds and readback |
| `mBatteryChargeFraction`, `mElectricBoostMotorState` | Charge fraction 0..1; motor state 0 unavailable, 1 inactive, 2 propulsion, 3 regeneration | Charge is null when motor state is absent/0; native battery restoration unsupported |
| `mSoC`, `mVirtualEnergy`, `mRegen` | Separate raw floats; only `mRegen` is labelled kW | Not merged with battery charge. SoC/virtual-energy scale, availability and native producers remain unresolved |
| Tyre `mTemperature`, `mTireCarcassTemperature`, `mTireInnerLayerTemperature` | Kelvin converted to °C; physical left/centre/right channels | Existing optional uniform initial heat is garage-only; not exact thermal-model restoration |
| Tyre `mWear`, compounds, `mFlat`, `mDetached` | Raw 0..1 wear; fitted metadata and flags | Existing garage condition preparation retains compounds. Exact tyre model/compound/on-track restoration unsupported |
| `mDentSeverity`, body `mDetached`, `mLastImpactET`, `mLastImpactMagnitude` | Eight severity bytes, flag, session seconds, unspecified impact magnitude units | Recorded; damage restoration unsupported |
| Speed | Derived from local velocity, km/h | Display/diagnostic value, not an independent write target |
| Suspension velocity, distributed contact/thermal state, other energy stores | Not exposed by this capture contract | Unavailable; no invented zero values or inferred write addresses |

Available generated fields decode to nullable values; missing/nonfinite extended
channels stay null. SoC/virtual energy remain raw even when zero: zero alone is
not proof that a car supports either quantity. Tyre flags retain the existing
SDK reader's optional-field behaviour; this generated layout exposes both flags.

REA session inspection found no active analysis to reuse. Native energy producers,
car-specific semantics and restoration are still investigation work, not verified
capabilities. No native code addresses or write support were inferred from names.

## Synchronization and freshness

The reader opens the SDK's existing `LMU_SharedMemoryLockData` and lock event.
It uses the SDK's `busy` interlocked compare/exchange protocol, copies the full
mapping while holding the lock, releases it and signals existing waiters, then
decodes the private copy. Contention times out after approximately 5 ms rather
than resetting a lock owned by another participant. Missing gates fail closed.
Older generated layouts need regeneration before live reading.

The recorder checks Hold then Data update events in SDK order, without waiting
on the reset thread. Events are hints: simulation timestamps determine fresh
telemetry. The sampler targets one read per 20 ms, not a guaranteed 50 fresh
frames/s. Each sample includes read duration, simulation time, monotonic host
ticks and sequence; headers include host frequency and UTC for correlation.

Telemetry and scoring publish at different times. Locking makes the copied
published bytes coherent; it does **not** establish one physics instant for all
fields. Recordings retain scoring time and require matching telemetry/scoring
slot IDs, track and car names. Capture also requires realtime, player control and
an active game phase. The application options location is diagnostic only: it
remained zero in a live realtime/player-controlled SDK snapshot and incorrectly
blocked capture when used as a gate. Menus/loading/monitor/replay,
pause heartbeat and invalid/nonfinite pose/motion suspend recording. Optional
extended nonfinite channels are null; nonfinite tyre state refuses the sample.

Equal simulation timestamps are duplicates. A second without a new timestamp
ends the segment as stale/paused; unchanged frames cannot reopen it. Resuming
with fresh time starts a new segment. Build/process, track, car or session-number
changes, backwards simulation time, a jump over two seconds, and reconnects also
separate segments. Smaller forward gaps over 100 ms are recorded explicitly.
The SDK supplies no unique session instance ID or frame sequence: a restart that
never exposes an inactive state/time discontinuity cannot be distinguished, and
missed SDK frames cannot be counted exactly. Session-number changes are not a
substitute for a unique session ID.

The executable's full path/hash is read once per SDK connection using a limited
process identity query and file hashing. There is no per-frame native attachment
or hash. Reconnect attempts are limited to once per second. The user-driven
recording below measures freshness/read time for one car/build; other combinations
and connection/session transitions still require validation.

## Persistence, budget and recovery

Files live in ignored `data/recordings/` (or `<--data>/recordings/`). A store lock
allows only one recorder to apply retention there; a second recorder reports a
failure without interfering with practice operations. A single sampler feeds a
128-entry bounded queue; a separate consumer serializes JSONL. Buffer pressure
drops new entries without blocking capture and reports the number on the next
successful entry. Segment IDs/identity repeat on samples so a dropped header/end
cannot mix sessions. Shutdown drains the queue and writes cumulative counters.

Schema 1 uses newline-terminated `header`, `sample`, `end` and `summary` records.
Sequence counts queued attempts (gaps expose dropped entries); header sequence
identifies its first entry. Normal entries carry counts since the last successful
enqueue; `shutdown-totals` summaries carry cumulative counts. Each header retains
full executable and SDK-header identity. Files rotate at about 16 MiB; retention
deletes oldest recording files and their derived lap indexes before opening
another driving recording, reserving room within a 512 MiB recording budget for
the next file and bounded serialization overrun. Standalone shutdown diagnostics
do not trigger retention. A full or unwritable store stops recording
and exposes the error.

The lap browser saves a versioned index beside each recording part. The first
scan of an unindexed part reads its telemetry. Later app launches reuse the
index while the source file matches, and live refreshes read newly appended
records. A stale or unreadable index is rebuilt from the recording. Exact frame
selection still reads and validates the original telemetry.

The 64 KiB maximum line size bounds schema payloads; normal samples are much
smaller. At 50 Hz the worst permitted payload is about 3.1 MiB/s (roughly 2.7
minutes within 512 MiB), while 6 KiB samples would retain roughly 29 minutes.
These are storage estimates, not measured live rates. Queue capacity bounds
sample count; recording payload objects contain fixed-size SDK fields.

Complete records flush at least on the next write after one second and at segment
end/shutdown. A crash may lose queued/buffered samples, and OS/power loss is not
guaranteed durable. `TelemetryRecorder.ReadComplete` recovers only complete
newline-terminated schema-1 records and ignores an interrupted final fragment;
malformed complete records/unknown schemas fail explicitly. It does not repair or
modify the source recording. Retention can remove older evidence: copy important
recordings outside the managed directory.

## Offline validation

On 2026-11-03, `.\build.cmd test -Configuration Release` passed 517 backend tests,
259 checks across five WebView2 suites, and production HTTP/access/layout checks.
The loopback/WebView2 suite required execution outside the filesystem sandbox;
its first sandboxed attempt failed at HTTP listener startup. The Release solution
build passed with the existing WindowsBase/MSB3277 warning. Practice layout at
1040×680 was inspected after adding recorder status.

Focused recorder tests exercise duplicate/stale frames, simulation gaps/restarts,
identity changes, pause/replay gates, unavailable values, bounded enqueue loss,
segment identity after lost boundaries, incremental JSONL round-trip, truncated
tail recovery and retention. The real SDK reader fixture exercises lock contention,
release/waiter signalling and invalid player indices. These are synthetic checks;
they establish neither current-game update rates nor native restoration support.

### Loaded-session recorder diagnosis (2026-11-03)

A read-only probe at Bahrain Paddock Circuit, TF Sport 2026 #2:LM, Practice 1
used executable SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`. It
found matching identities, realtime true, player control 0, game phase 5, but
application options location 0. Requiring location 3 incorrectly suspended
recording. Eligibility now uses realtime/player control/phase and retains
identity, validity and freshness checks. A regression exercises that exact
location-0 condition and still refuses monitor, AI control, replay, pause and
identity mismatch. All 12 focused recorder/capture tests passed; the CLI rebuilt.

The corrected production recorder saved one sample in a bounded three-second
read-only probe (140 duplicates, no dropped entries). Simulation time remained
122.9625 s, so it then suspended as stale/paused. This validates eligibility,
persistence and stale handling for that loaded snapshot, not moving sampling
rate or restoration. Status now includes the suspension reason. Private raw
snapshot and JSONL evidence are in `.scratch/telemetry-timeline/`.

### Owner-driven recording (2026-11-03)

The owner recorded Bahrain Paddock Circuit / TF Sport 2026 #2:LM in Practice 1,
executable SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`.
Production `ReadComplete` parsed all six files from `20261103T181439922` through
`20261103T181750647` without errors. The initial frozen-time sample is a separate
segment; the driving segment spans three rotated files.

| Observation | Recorded result |
| --- | --- |
| Driving samples | 9,040, all realtime/player-controlled with matching identities |
| Simulation time | 122.9725–304.5625 s (181.59 s) |
| Monotonic host duration / fresh rate | 181.583 s / 49.779 Hz |
| Simulation intervals | Strictly increasing; maximum 50 ms; none over 100 ms |
| Buffered loss | Zero reported in samples, boundaries and shutdown summary |
| Speed / gears | Approximately stopped–256.164 km/h; reverse, neutral and gears 1–6 |
| Read/copy/decode duration | Median 0.1215 ms; 95th percentile 0.1783 ms; maximum 2.1227 ms |
| Tyres | Present in every driving sample; wheel/chassis/input fields recorded |
| Fuel | 22.1821–17.6882 litres |
| Battery | Unavailable, electric motor state 0 |
| Virtual energy | Separate raw field changed 0.260919–0.206826; units still unverified |
| Boundaries | Frozen simulation time ended the initial and driving segments; clean shutdown summary |

The source reports `mDeltaTime` near 10 ms, so the 50 Hz recorder intentionally
does not retain every SDK update. Zero buffer drops does not mean every physics
frame was captured. Read duration excludes classification, disk serialization and
overall game performance impact. Front/rear chassis ride-height fields remained
zero; raw zeros alone do not establish useful data or availability. No unique
session identity/reconnect transition, native restoration or reset was tested.

The complete analysis, file names and per-field ranges are retained in
`.scratch/telemetry-timeline/user-recording-summary.json`; source JSONL remains in
`data/recordings/` under normal retention. This is one recording's observed scope,
not general compatibility or a guarantee of a complete physical rewind.

### Finding a point on a lap

The lap list uses forward start/finish crossings in the retained SDK lap
distance, so existing recordings can supply laps too. A timed lap spans two
crossings without a sample gap over half a second. A continuous run that starts
before zero and reaches the first crossing appears separately as an opening run.
Lap numbers are relative to the recording. The route shows the driven line.
Clicking it selects the closest recorded frame and shows lap distance, speed and
gear. The reset still requires its explicit action and live gates.
