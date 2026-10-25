# Placement and cleanup

Current implementation, reviewed on 2026-10-07.

## Sequence

1. Require active local Practice and live track/car identity, then resolve the
   checkpoint and build a plan from live engine values. Check the track and use
   the loaded car's calibration or derived offsets. Missing telemetry refuses
   placement rather than substituting checkpoint metadata.
2. Revalidate mapped code, table selection and AI ownership. Verify Flag Rules
   are off before arming. Failed suppression blocks placement; a failed rule
   write attempts to restore its original value.
3. Temporarily write and verify the 24-byte spot payload, preserving the
   eight-byte tail. Report **PRESS DRIVE**. The Drive wait has a 120-second
   default deadline; cancellation or timeout restores the spot and saved rules.
4. On player ownership, sample arrival, allow 400 ms to settle, and restore the
   spot bytes. Measure the resting position. The driver can move during this
   delay; the measurement is not a locked stationary snapshot.
5. Attempt sector and out-lap repairs immediately, before the pit-state wait.
6. For normal teleport, attempt the guarded rolling
   start described below. Failures are reported while rule cleanup still runs.
7. Allow normal driving with Flag Rules verified off. When restoration is
   pending, monitor pit state without a clearing timeout. Keep suppression
   active until pit state clears or the car returns to AI ownership in the
   garage. Then restore the exact original rule value.

If Flag Rules were already off, there is no saved rule change to restore and
no pit-clear wait. The tool does not operate the car's pit limiter or write pit
state to zero. Flag Rules also affects track-limit enforcement while disabled.

## Cancel and close

**Cancel placement** stops an armed placement before Drive and restores its
temporary writes. After Drive, cancellation waits for pit state to clear or a
garage return. It does not re-enable penalties while the player is still
driving with inherited pit state. Activity explains when a garage return is
needed. The CLI uses the same policy for Ctrl+C.

Exceptions after Drive follow the same safe restoration wait. If the session
changes while waiting, old rules are not written into the replacement session.
If cleanup cannot read the state needed to prove restoration safe, it fails
without re-enabling Flag Rules; check the rule setting before continuing.

Closing the app requests this cleanup and keeps the window and process alive
until the placement task finishes. Its title explains why closing may be
waiting for a garage return. The console UI server also waits for its placement
task when stopping. A new placement cannot start during shutdown.

## Session ownership and failures

Restoration checks the attached process, selected table pointer and player slot
before using saved values. When that context is gone, cleanup does not write
saved spot bytes or rules into the replacement context. A session-end report
does not claim the original values were restored. These checks identify the
observed memory context; they are not an independent game session ID.

Spot restoration is verified and attempted on normal or exceptional scope
exit. Rule restoration preserves the original value and refuses to overwrite
an unexpected competing value. Failed restoration remains an error; forced
process termination cannot run cleanup. Do not treat an error as a successful
restoration.

Garage return, cancellation and session end are reported separately from pit
state clearing. They are not accepted as learning samples. `Completed` does
not guarantee that every sector or pit-flag repair succeeded; read those
results separately.

## Fields and controls

| Field or control | Purpose | Placement action |
| --- | --- | --- |
| Flag Rules | Gates pit-speeding and affects track-limit enforcement | Verified off before arming; original value restored after clearing or garage return |
| Track-limit derived flags | Runtime off-track and invalidation state | Separate UI/CLI toggle; may re-arm on session transitions |
| Pit state | Engine pit-procedure state | Monitored, never written by production placement |
| Pit flag | Feeds out-lap demotion | Clear attempted after settling |
| Sector | Controls acceptance of a start/finish crossing | Final sector set after settling by default |
| Pit limiter | Driver-operated vehicle speed limiter | Not controlled |

Track-limit flags can re-arm on session initialisation or garage return. Check
their displayed values again. The sector/pit-flag diagnosis is retained in the
[lap-validity investigation](research/PLACEMENT_EVIDENCE.md); its addresses and
measurements describe the recorded builds. Forcing the final sector is not a
guarantee of accurate intermediate splits.

## Experimental rolling start

The app restores captured speed and gear automatically for normal teleport.
**Standing teleport** explicitly skips motion restoration.
Motion restoration requires a reset layout verified against the running engine,
the capture car/track, and a straight, level checkpoint captured at 0.5 km/h or faster. There is no upper speed ceiling.
The sideways component must stay within 0.05 m/s. Vertical motion must stay
within the larger of 0.05 m/s or 0.5% of forward speed, allowing small road/pitch
components in faster captures.
Unresolved engine layouts, older checkpoints without velocity and cross-car
speed restoration are refused. Ordinary pose placement remains available.

Select the checkpoint in the garage, place it and press Drive when
prompted. Keep pedals released and gear neutral. The app reads Hold Brakes and,
when enabled, temporarily switches it off before Drive through LMU’s options
API. It verifies the change and restores the original On preference immediately
after the rolling handoff, or during failure/cancellation cleanup; an already-Off
preference is left alone. No manual assist change is required.

After settling and any tyre conditioning, the app waits at least three seconds
and requires 500 ms of stable driving control, neutral and released physical
and applied throttle/brake, with a ten-second readiness deadline. Small slope
creep is allowed. It then uses the verified direct-reset path to restore the
checkpoint pose, linked-body/wheel motion and captured gear, avoiding a
stationary-wheel wait that cannot succeed on some slopes. Captured speed and
gear still require the capture car. Loaded drivetrain limits and complete
paused-write verification remain in force, followed by resumed observation.
The handoff clears inherited pit and partial-lap state before the original
Flag Rules value is restored.

A failed precondition makes no motion write and reports that speed was not
restored. A possible partial motion update leaves physics paused for recovery;
the app never rolls back stale velocities or repeatedly writes to hold speed.
Restart LMU before another reset if physics stays paused; Esc/Resume did not clear the app-set pause in the recorded failure. Spot/rule cleanup still
runs under the existing policy. Rolling outcomes cannot feed stationary learning.

The separate automatic prototype was measured live on the Ferrari 296 LMGT3 at
Monza: all 514 bytes (including pause set/restore) verified; first speed was
8.54 km/h for an 8.94 km/h request, peaking at 8.56 km/h, with smooth movement
confirmed by the owner. A separate inherited-EXITING test also verified all
514 bytes, reached 8.56 km/h and moved smoothly while pit state stayed EXITING.
The owner then confirmed smooth integrated checkpoint restoration after the
control delay with Hold Brakes manually Off. A subsequent integrated test with
Hold Brakes initially On logged temporary release before Drive, automatic speed
restoration and original Hold Brakes restoration; the settings API read On
afterward and the owner confirmed smooth rolling. Subsequent staged integrated
tests on the same car/track restored 18.79 km/h from 19.55, 38.42 from 39.86,
and 56.32 from 58.19. The owner confirmed smooth movement in each case. The
last run cleared pit state normally and verified original Flag Rules restored.
A subsequent third-gear test requested 148.52 km/h and observed 144.83 km/h.
Filtered clutch stayed engaged and physical/applied pedals stayed released
for five seconds. Engine RPM rose from about 4,145 in the first passive sample
to about 5,822 after half a second; the observed five-second peak was 5,968 RPM
against the loaded 8,000 RPM limit. The owner confirmed smooth rolling, pit
state cleared, and Hold Brakes and Flag Rules were restored. Exact speed,
other cars/builds and speeds above 148.52 km/h are not established.

If LMU exits or its context changes during the temporary assist override, the
app reports that it could not restore Hold Brakes. Forced app termination or a
power outage cannot run cleanup; check that preference after an interrupted run.

## Experimental reset on track

When the current engine reset layout resolves completely, the app shows **Reset on track** while driving. **Ctrl+Alt+R** restores the selected checkpoint with its captured speed and gear while LMU has focus. **Ctrl+Alt+Shift+R** performs a standing teleport. Both shortcuts also use the existing Drive handoff in the garage. The standing button remains available for checkpoints without eligible motion data; normal teleport refuses unsupported motion restoration rather than silently starting stationary. The app ignores repeated/busy requests. No garage return or Drive handoff is involved; the existing garage placement remains available.

The backend selects the flow from live control ownership: garage placement keeps
the Drive handoff; player-controlled cars use direct reset even with inherited
pit state. The hotkey also works in the garage. A garage placement waiting only
for pit state to clear allows a direct reset; its original rule cleanup remains
owned until that reset clears pit state or the driver returns to the garage.

Direct reset repairs lap state inside its verified physics pause: pit state and
pit flag become zero, sector becomes the final sector, and the next-lap latch is
armed. The teleported partial lap is marked untimed. Cross start/finish to begin
a clean timed lap; this does not award a time for the teleported partial lap or
restore intermediate splits. A failed repair follows the same partial-write
pause policy as a failed pose update.

On 2026-10-16, build 14200 / SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`,
the Team Qatar by Iron Lynx Mercedes at Barcelona completed an on-track rolling
reset and an inherited-EXITING reset. The latter verified pit state 0, pit flag
clear, sector 2 and the armed next-lap latch. The first run crossed start/finish
and reported `LapAndTime` afterward. Garage placement also completed with its
original Flag Rules restored. A later United Autosports #23 / Barcelona test completed a standing direct reset
while garage placement was waiting for pit state to clear. Pit state cleared and
the original Flag Rules value of 2 was restored. These checks do not establish
every car/build or precise intermediate splits.

Direct reset owns a brief physics pause, transforms linked and tyre-contact bodies, synchronizes the separate vehicle collision body and cached pose/motion, resets wheel road caches, and invalidates the road lookup for the next engine update. It uses two 25 ms settling/stability waits, bulk reads of tyre records and membership metadata, and overlaps read-only menu requests with preparation/verification. Per-write context checks and complete verification remain in place. It then resumes and observes two seconds of simulation before reporting completion. Grossly inconsistent travel or an automatic pit recovery reports failure. This observation is not a guarantee of contact or damage correctness. Enabled Hold Brakes is temporarily released/restored for a rolling reset. Fuel and damage are not restored. Direct reset does not change rules itself; a pending garage placement still restores its saved rules after pit state clears.

Native collision-pair removal frees engine-owned allocations. The app does not externally erase that vector or call native functions; it refuses a direct reset while the car has cached collision pairs and asks the driver to move clear. Possible partial writes remain paused under the existing recovery policy.

Validation is build/car/track-specific: a two-metre translation and a stopped, approximately 54-metre checkpoint return were smooth. The first moving reset over roughly 880 metres reached the target, then fell below the road, became unstable and was recovered to the pits. The revised vehicle-level cache/collision refresh passed a moving reset over roughly 819 metres after a one-byte pause recovery: first speed 39.81 km/h, 38.13 after two seconds, with road contact and first gear retained. The owner confirmed smooth rolling. The initial verification wrongly rejected a refreshed road pointer and kept physics paused; the subsequent fix recognizes consumed invalidation signals and native float-matrix rounding, while verifying the refreshed lookup position. The final automatic hotkey test reset from 110.36 km/h in second gear over roughly 525 metres to the saved 39.86 km/h in first gear. Physics resumed automatically after 763 ms; first passive speed was 39.81 km/h, 38.16 after two seconds and 35.52 after five seconds, with road contact and player control retained. The owner confirmed smooth rolling, and the app reported completion only after observation. A subsequent timing-optimized build was tested with **End-of-Lap-Speed** at Circuit de Barcelona in Team Qatar by Iron Lynx 2026 #62:ELMS. The app observed 125.12 km/h for the captured 126.68 km/h, restored second gear, resumed after a 470 ms pause and completed its two-second observation. The owner confirmed smooth rolling and a shorter pause. This establishes these recorded scenarios; other combinations, repeated resets and faster target checkpoints remain unverified.

### Game updates and compatibility

On 2026-10-16, the Barcelona Mercedes #62 garage handoff reproduced full applied
brake with zero physical brake before native input control settled. Waiting for
released applied pedals fixed the measured case: the final solo-session rolling
test restored 126.45 km/h in second gear, verified individual tyre conditioning,
and recorded zero physical/applied brake in all 114 moving telemetry samples.
Speed fell from 126.63 to 107.06 km/h over 4.61 seconds with no throttle in second
gear; engine and clutch RPM remained synchronized. Original Hold Brakes and Flag
Rules settings were restored. Evidence is under `.scratch/regressions/`.

The same solo session also passed moving direct resets from pit-lane `EXITING`
and ordinary on-track `NONE` states. Pit-lane reset changed `EXITING` to `NONE`,
restored second gear at 124.42 km/h and recorded zero applied brake in 208 moving
samples. The on-track repeat began at 62.73 km/h, restored second gear at
125.05 km/h and recorded zero applied brake throughout 317 samples. Direct resets
preserved fitted tyre condition; they do not apply new tyre conditioning.

Reset addresses are derived from the current mapped engine, cached by full executable hash, and checked against native code. Structure operands and moved globals/functions can be rediscovered automatically when their supported instruction shapes and layout relationships remain intact. A missing, duplicate, disagreeing or changed reset signature disables reset/speed restoration instead of falling back to previous offsets. Other independently supported features remain available. The app preserves checkpoints and does not download offset profiles.

The offset refactor passed offline relocation, body-operand change, cache-tampering and failure checks. All 79 discovered values match the addresses used in the recorded live tests above. No live reset has been performed after this refactor because LMU was closed. These tests do not establish compatibility with every future patch; changed physics semantics, unsupported engine setters or an SDK ABI change require reviewed compatibility work and live validation.
