# Practice workspace

Open `inactive-reset-ui.exe` or the browser UI. It opens **Session & Conditions**.
Use the workspace navigation to switch pages. Open the review panel from
**Session summary** in the header.

## Launch and access

- **Launch local play:** EAC off; memory actions require active single-player
  Practice. Online racing is unavailable.
- **Launch with EAC:** online racing in LMU, local-session setup through REST,
  and valid SDK checkpoint capture. Memory actions remain disabled.
- Apply and Start require the single-player main menu in either mode. While
  a session is loaded, you can edit the next session or load a preset into
  the draft. Dismiss LMU's startup prompt manually. Close LMU before switching
  launch modes.
- Capture needs an active session and valid current pose. Menu/loading guards
  reject stale telemetry left behind after a session ends.

## Session builder and layout

The builder cards open detail dialogs for car and livery, circuit, session and
time, AI grid, and weather. **Session rules** opens a separate dialog.
Content and ranges come from LMU. Model cards group seasons while keeping
distinct Evo and regulation variants. Select a circuit card to browse the
layout row. It shows each layout's name and outline when geometry is
available. Select a layout to update the draft and its main preview. Weather groups
session-wide behaviour and track surface separately from the five forecast
points for Practice, Qualifying and Race. Driving assists stay in LMU and
are excluded from presets, including old imports.

Session & time starts a newly enabled Race at 20 minutes and offers named race
starts. Legacy preset durations stay in their original units until you edit Race
duration in minutes.
Unspecified starts can follow the previous enabled session. The draft adds
five minutes to the previous scheduled finish and wraps after midnight.
Choose a fixed time or an LMU start mode to override the sequence. Loading a
preset keeps its supplied start times.

For AI strength, drag the slider, type a percentage, or use the step buttons.
The controls respect LMU's range. AI aggression and forecast sky use named
choices. The same sky names appear in the official preset review.

Builder edits stay in a draft until you select **Apply**. Apply writes to LMU
with readback. Start waits for confirmed settings. See
[Local sessions](SESSIONS.md) for the launch sequence.

Use the Controls page to configure keyboard and wheel assignments. See
[Testing](TESTING.md) for desktop window and keyboard coverage.

## Personal presets

Save up to 40 presets in the validated, atomically replaced 512 KB
`data/ui-lab.json`. Presets retain car, layout, rules, session counts, class/grid
selection and Scripted weather, rather than specific generated AI entries.

The manager supports search, details, rename, duplicate, update, delete and
import/export, including individual export. The builder shows the applied source
and whether settings differ from its baseline. Restore opens a review; Update
saves the confirmed setup. Save a personal copy to retain edits to official sources.

Review works offline. When a session is loaded, **Load into draft** prepares a
preset for later. Applying requires the local main menu. Writes are sequential
with readback. Failure stops remaining requests. Cancellation finishes the pending
request. Confirmed changes remain in LMU. Applying never starts a session. Undo
covers the latest simple setting/session-count edit, not a whole plan. Unknown
content/settings are refused; controls requiring over 64 individual steps need
manual adjustment. Saving/importing succeeds only after persistence.

## Official scheduled sessions

**Official Events** lists RaceControl daily, weekly and special events. Select a
card to feature it. Changing the filter keeps the featured event if it still
matches, otherwise the first visible event is featured. An empty filter hides
the featured area.
While LMU runs, its Steam ticket authenticates automatic refresh at most every
15 minutes after success; Refresh events updates immediately. Tokens stay in
memory. The app does not register for or join online events.

Qualifying/Race conditions are cached separately in `data/official-sessions.json`,
outside the personal-preset limit. Failed categories retain saved entries and
report the failure. Hosted/community events are excluded; alternating configurations
stay separate. Saved presets remain available offline.

Qualifying practice and Race practice review **solo Practice with zero AI**.
Matching uses the exact installed scene and an owned eligible car, retaining the
current eligible livery. Time, duration, surface/water/temperature source, five
weather points and exposed simulation multipliers are copied. Online BoP, fixed
setups, tyre allocation and traffic-created rubber/water are not reproduced.
Applying opens the builder; Start session remains separate.

Online Natural grip inherits earlier sessions. Solo presets use the last explicit
earlier surface and retain RealRoad progression. Bahrain Outer LMGT3 Fixed was
Heavy then Natural in the 2026-10-23 source: both solo presets therefore start
Heavy with Normal progression. Heavy is an initial condition, not frozen evolution.
Unresolved inherited baselines are refused rather than guessed as Green.

## Practice and checkpoint library

Capture saves the pose and simulation time of a fresh player-controlled frame.
**Quick capture** adds a persistent destination for the loaded layout and car.
The **+** beside a capture copies that exact frame to Saved Checkpoints. The
**+** below Saved Checkpoints names the current position, including standing
destinations. A saved row's **−** offers Undo after deletion.

The map shows both checkpoint sources and the previewed recorded lap. Select
a recording and a complete lap, then click its route, use the slider, or enter
elapsed time such as `1:30.2` or `130.2`. Clicking overlapping targets opens a
small list beside the map point. The list labels and color-codes saved
checkpoints, quick captures, and recorded laps. It offers one sample for each
pass through the clicked area. Use the slider or elapsed time to select an
exact sample.
Selection updates the active target. **Teleport standing** and **Teleport with
speed & gear** act on that target when eligible.
The library separates preview from selection and provides circuit/car filters,
favourites, rename, delete and one-operation undo. See
[Circuit maps and library](CIRCUIT_MAPS.md) for geometry, caching and recovery.

Normal teleport restores speed/gear for an eligible moving capture in a
compatible chassis. Standing teleport can reuse another car's checkpoint pose
on the matching track. Recorded frames also require the captured build and a
compatible chassis. Older captures resolve against the offline vehicle catalog
where possible and otherwise require the original vehicle name.
Actual ownership/state chooses garage placement with a
Drive handoff or experimental on-track reset. See [Placement](PLACEMENT.md).

**Tyre preparation** sets independent FL/FR/RL/RR condition and optional initial
heat. **Game** delegates heat to LMU. **Prepare tyres** and the **Apply tyres**
switch on a garage teleport use the same draft. Switch to LMU and press **Drive**
when prompted, or cancel before Drive. Turn off **Apply tyres** to teleport
on track with the fitted tyres. See [Tyres](TYRES.md).

## Keyboard and wheel assignments

Open **Controls** in the desktop app. Each action accepts one keyboard shortcut
and one controller button. Assign key learns a shortcut; Assign button learns a
released-then-pressed wheel/button-box input. Escape/Cancel or 15 seconds stops
learning. Clear independently or restore defaults. Duplicates and reserved
shortcuts are refused. Use a modifier or F1–F24.

| Action | Default |
| --- | --- |
| Quick capture | Ctrl+Alt+C |
| Teleport with speed and gear | Ctrl+Alt+R |
| Standing teleport | Ctrl+Alt+Shift+R |
| Previous / Cycle Saved Checkpoints | Ctrl+Alt+Up / Down |
| Cycle Quick Captures | Unassigned |
| Clear Quick Captures | Unassigned |

Each cycling binding wraps within its own eligible list for the loaded layout
and car. **Clear Quick Captures** asks for a second press in the same scope
and keeps other layouts' and cars' captures. Enable optional spoken feedback
in **Controls**. Background input refreshes eligibility while LMU has focus.

Bindings are atomically saved in `data/input-bindings.json`. Dispatch requires
LMU focus, although Windows reserves shortcuts while the app runs. Controller
bindings retain device path/button across disconnection. Reconnect/held-button
baselines prevent accidental triggers; devices without a neutral report may need
a second press. Axes/hats are unsupported. Read-only Raw Input does not acquire
the wheel exclusively or send force feedback. Native rebinding/focus checks passed;
physical wheel testing was deferred.

Activity retains progress, cancellation, diagnostics, issue filtering and export.
Manufacturer logos, liveries and circuit photos come from installed `Bin/UI.zip`
with text fallbacks. See [Status](STATUS.md) and [Testing](TESTING.md) for
validation and remaining limits.
