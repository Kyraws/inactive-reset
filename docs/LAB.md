# Practice workspace

The desktop app uses Redline with a fixed dark appearance. Open
`inactive-reset-ui.exe`; the browser UI serves the same workspace.

## Launch and access

- **Launch local play:** EAC off; memory actions require active single-player
  Practice. Online racing is unavailable.
- **Launch with EAC:** online racing in LMU, local-session setup through REST,
  and valid SDK checkpoint capture. Memory actions remain disabled.
- Setup requires the single-player main menu in either mode. Loading,
  multiplayer and active sessions lock editing. Dismiss LMU's startup prompt
  manually. Close LMU before switching launch modes.
- Capture needs an active session and valid current pose. Menu/loading guards
  reject stale telemetry left behind after a session ends.

## Session builder and layout

The five tabs are Car & circuit, Weekend, Conditions, Competition and Rules.
Content, ranges and accepted labels come from LMU. Model cards group seasons
while keeping distinct Evo/regulation variants; livery and layout remain separate.
Weekend uses horizontal rows. Conditions holds per-session surface controls and
five-point weather for enabled Practice, Qualifying and Race. There is no separate
Warmup forecast. Driving assists stay in LMU and are excluded from all presets,
including old imports.

Builder edits save immediately with readback. Numeric/time edits coalesce while
typing; failed saves retain drafts with retry/discard. Start waits for confirmed
settings. See [Local sessions](SESSIONS.md) for the launch sequence.

Main views fit at 1040×680 and 1280×800. Long lists scroll within panels;
dialogs/additional controls can scroll. Builder tabs support arrow keys. Setting
notes remain accessible and appear on hover. The summary resizes by dragging or
Left/Right keys and becomes a drawer at narrow widths. Overview fits ten event
cards with both practice buttons visible. Controls opens bindings directly;
appearance/density selectors and compact utility mode are removed. Old appearance
preferences remain readable but do not affect presentation.

## Personal presets

Save up to 40 presets in the validated, atomically replaced 512 KB
`data/ui-lab.json`. Presets retain car, layout, rules, session counts, class/grid
selection and Scripted weather, rather than specific generated AI entries.

The manager supports search, details, rename, duplicate, update, delete and
import/export, including individual export. The builder shows the applied source
and whether settings differ from its baseline. Restore opens a review; Update
saves the confirmed setup. Save a personal copy to retain edits to official sources.

Review works offline; applying requires the local main menu. Writes are sequential
with readback. Failure stops remaining requests; cancellation finishes the pending
request. Confirmed changes remain in LMU. Applying never starts a session. Undo
covers the latest simple setting/session-count edit, not a whole plan. Unknown
content/settings are refused; controls requiring over 64 individual steps need
manual adjustment. Saving/importing succeeds only after persistence.

## Official scheduled sessions

Overview and Official races list RaceControl daily, weekly and special events.
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

Capture saves a pose; selecting a destination never moves the car. Quick capture
replaces its track/car slot and survives restart. Named captures stay separate;
Save as named copies the saved quick slot without recapturing the current car.
The library separates preview from selection and provides circuit/car filters,
favourites, rename, delete and one-operation undo. See
[Circuit maps and library](CIRCUIT_MAPS.md) for geometry, caching and recovery.

Normal teleport restores speed/gear for an eligible straight, approximately level
moving capture in the capture car. Standing teleport can reuse another car's pose
on the matching track. Actual ownership/state chooses garage placement with a
Drive handoff or experimental on-track reset. See [Placement](PLACEMENT.md).

Tyre preparation sets independent FL/FR/RL/RR condition and optional initial heat
through the garage. Blank heat delegates to LMU; the all-wheel slider's zero
clears overrides. Editing choices does not apply them. Prepare tyres or Apply on
garage teleport uses the guarded handoff. On-track reset preserves fitted tyres
and refuses conditioning. See [Tyres](TYRES.md) for verified scope.

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
| Previous / Next speed-ready checkpoint | Ctrl+Alt+Up / Down |

Previous/Next wraps through eligible moving captures for the loaded track/car,
including Quick capture, ordered by lap distance then name/ID. It selects without
teleporting. Background input refreshes eligibility while LMU has focus.

Bindings are atomically saved in `data/input-bindings.json`. Dispatch requires
LMU focus, although Windows reserves shortcuts while the app runs. Controller
bindings retain device path/button across disconnection. Reconnect/held-button
baselines prevent accidental triggers; devices without a neutral report may need
a second press. Axes/hats are unsupported. Read-only Raw Input does not acquire
the wheel exclusively or send force feedback. Native rebinding/focus checks passed;
physical wheel testing was deferred.

Activity retains progress, cancellation, diagnostics, issue filtering and export.
Manufacturer logos come from installed `Bin/UI.zip`, with a text fallback; game
artwork is not distributed. See [Status](STATUS.md) and [Testing](TESTING.md) for
validation and remaining limits.
