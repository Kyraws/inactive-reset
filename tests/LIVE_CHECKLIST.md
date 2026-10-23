# Live release checks

Automated checks use simulated game state. They never establish that a changed
game build physically handles placement correctly. Record live checks before
claiming compatibility with a new build. Do not run writes in protected/online
sessions.

Use the smallest set relevant to the change. One stopped-car Practice workflow
is the baseline for a release; repeat moving, tyre or native-input cases when
those capabilities or the game build changed. Do not require every car/track
combination for an unrelated UI or documentation change.

## Record once per run

- App commit/version, Windows/WebView2 versions, full LMU executable SHA-256.
- Direct/protected launch, session type, track/layout, vehicle and setup.
- Capture/target, before/after telemetry, observed behavior, and Activity output.
- Which cases passed, failed or were not exercised, with the reason.

## Baseline

1. Verify writes are refused outside active local Practice and on a protected
   launch. A refused request must not change game state.
2. Capture a stationary position, save/reload it and select it. Confirm track
   identity; verify a wrong-track checkpoint is refused.
3. In the garage, arm placement and cancel before Drive. Compare the original
   spot entry, padding and saved rules byte-for-byte after cleanup.
4. Complete a standing placement through Drive. Observe position, road contact,
   gear and continued simulation. Record error rather than assuming one fixed
   tolerance suits every track/car.
5. Cancel after Drive while pit state is active. Confirm rules stay suppressed.
   Return to the garage or clear pit state; confirm original rules/spot bytes
   are restored and the operation finishes.
6. Close the app during an armed operation. It must complete safe cleanup before
   exiting. Check the original values afterward.

## When affected

- Discovery/placement: ordinary and indexed destinations; changed sessions or
  player ownership while armed; supported builds and refusal of unsupported ones.
- Motion/direct reset: captured speed and gear, resume, repeated resets, road
  contact and control for several seconds. Standing/cross-car/legacy captures
  must continue to follow their declared limits.
- Tyres: original inventory on cancellation, requested condition/temperature
  after Drive, no changes to unintended wheels or options.
- Inputs: keyboard and wheel assignment, conflicts, release/press behavior,
  device removal/reconnection, focus gate and default restoration.
- Window/browser changes: compact/comfortable layouts, both styles, supported
  window sizes and a non-default Windows display scale; navigation and visible
  errors remain usable.

Unexpected native memory failures should be tested with simulated state first.
Do not create unsafe live faults just to satisfy this checklist. Record any
remaining fault-injection limits explicitly.
