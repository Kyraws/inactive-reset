# Placement and cleanup

Current implementation, reviewed on 2026-10-07.

## Sequence

1. Resolve the checkpoint and build a plan from live engine values. Check the
   track and use the loaded car's calibration or derived offsets.
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
6. Allow normal driving with Flag Rules verified off. When restoration is
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
[lap-validity investigation](archive/LAP_VALIDITY.md); its addresses and
measurements describe the recorded builds. Forcing the final sector is not a
guarantee of accurate intermediate splits.
