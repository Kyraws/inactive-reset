# Lab 02 — experimental UI

The Lab workspace is the default in v0.3.0. Open `inactive-reset-ui.exe` from the release ZIP. The previous session workspace remains available with `--classic`, and the original practice page remains in the comparison dialog.

## What to try

| Area | Experiment |
| --- | --- |
| Navigation | One rail for the dashboard, five builder sections, Practice, library, Activity and settings |
| Overview | Current setup, session metrics, quick plans and pinned controls |
| Car browser | Installed manufacturer-logo cards grouped across seasons, class/season filters, model favourites and full season/team/livery selection; distinct Evo names and LMP2 regulations stay separate |
| Circuit selection | Circuit names from catalog metadata; separate layout/event-configuration list and favourite layouts |
| Quick plans | Solo focus, sprint rehearsal and wet-weather repetitions, each reviewed before applying |
| Session presets | Dedicated manager: save, review/reuse, rename, update from current session, delete, search and import/export; includes car, layout, session rules, class selection and Scripted weather |
| Conditions | Five-point forecast chart with rain percentages and temperature; reviewed copying between points or sessions |
| Controls | Pin frequently used settings, switch-style boolean controls, duration shortcuts and reviewed undo for the latest simple edit |
| Practice | Explicit selectable destination buttons; consolidated all-wheel and individual condition/temperature controls; fresh/warm/worn/game-temperature choices and generated checkpoint names |
| Library | All captured tracks, search, name/track/distance sorting, favourites and selection on the loaded track |
| Activity | Operation ribbon, fixed next-step panel, issue filtering and report export |
| Appearance | Midnight/Daylight themes; Compact reduces row heights, typography, navigation, cards and page spacing |
| Comparison | Open the unchanged classic UI in a comparison dialog |

Driving-assist editing and keyboard-shortcut navigation are removed from Lab. Presets never capture or apply driving assists, including old imported presets that contain them. The preserved classic interface retains its original controls.

Blank tyre-temperature fields delegate to LMU’s starting temperature, including its warmer setting and selected compound. Enter a number to override that wheel, or use the all-wheel field to fill four temperatures. No fixed warm/cold temperature is presented as a universal default.

Plans can be reviewed offline. Applying requires LMU's local main menu and uses
the existing guarded API actions. Changes are sequential, with readback after
each write. A failure stops the remaining requests; cancellation finishes the
pending request before stopping. Already confirmed changes remain in LMU.
Review the current setup before retrying. Plans do not launch a session by
themselves. Startup prompt dismissal remains manual.

Saved plans retain the opponent class selection and grid source, rather than
specific generated AI entries. Unknown or unavailable settings/content are
refused by the existing game API gates. Controls that need more than 64 individual
steps must be adjusted manually. Undo covers a simple setting, or session
count edit; it is not a rollback of a complete plan.

Lab choices live in the fixed `data/ui-lab.json` file, independent of the classic
UI. The store validates sizes and types and replaces the file atomically. It
accepts at most 40 plans within a 512 KB preference file. Saving/importing a plan
reports success after the file write is confirmed. Changing tyre choices only
prepares the requested values; **Prepare tyres** or **Place checkpoint** applies
them through the existing practice tools.

## Validation

282 .NET tests passed with one local-capture check skipped. The browser suites
passed 52 Lab checks, 23 session-editor checks and 38 checkpoint checks. Lab
checks cover review without writes, relative/absolute settings, no-op changes,
failure/partial progress, cancellation, inactive-menu gates, offline review,
owned cars, active-model livery preservation, bulk tyre choices, library filters
and theme/density switching. Preference tests cover round-trip persistence,
invalid input, plan limits and preservation after rejected writes.

All Lab pages were rendered in WebView2 at 1280×800 and the 1040×680 minimum;
both themes were inspected. Loaded-layout checks used captured metadata as a
visual fixture. No new live session combinations were run for this experiment.

## RaceControl investigation (2026-10-10)

The public [community event directory](https://www.racecontrol.gg/events) lists events, tracks and classes. A complete, supported feed covering scheduled official races and all local-session weather/rule values has not been verified. Automatic copying and online joining are not implemented; missing settings must not be guessed. Existing local presets can be reused for rehearsal, while online registration stays in RaceControl/LMU.

## Logo and layout polish (2026-10-10)

Manufacturer artwork is read directly from the user’s installed `Bin/UI.zip`, under `start/images/manufacturer/Brand=*.svg`. No game artwork is bundled or extracted into this repository. Light mode uses Dark logo variants where supplied; other logos retain a dark backing for visibility. Missing artwork falls back to the manufacturer name. The cache refreshes when the archive changes. Both ZIP path separators are supported.

Active controls, hover/focus states, native widgets and selections now follow the theme. Compact uses five car columns (four at the minimum window), puts livery selection above the model list, places session/rule groups side by side, and keeps the main Practice controls visible. The main tyre controls remain visible at the minimum window without the repetitive tyre summary.

Validation: full .NET suite passed 280 tests with 1 skipped before the ZIP separator correction; both logo path/theme regression cases passed afterward. Browser suites passed 46 Lab, 23 session and 38 checkpoint checks, including light-theme contrast. All 35 current model cards loaded genuine installed logos in the light-theme visual check. No game settings or tyre writes were exercised.

Final minimum-window measurement (1040×680): Compact Practice has no main-panel scrolling; Prepare tyres is visible. Sessions, rules and settings also fit. The full car list still scrolls when all models are shown; class/season/search filters reduce it. Both light and dark logo loading were visually verified.

## Garage styles (2026-10-10)

Settings now offers Original and Redline. Original preserves the existing interface and its saved Midnight/Daylight choice. Redline is dark only, with local SVG tyre artwork, chequered accents, red paint and race-plate typography. Switching back restores the Original theme. The style is saved with existing Lab preferences; older preference files default to Original.

The Practice rules block is removed. Both styles retain the compact controls. Redline Overview, Practice and Settings fit without main-panel scrolling at 1040×680 in Compact.

Validation: 282 .NET tests passed, 1 skipped; browser suites passed 52 Lab, 23 session and 38 checkpoint checks. Redline and restored Original Daylight were visually inspected in WebView2. No game session or tyre writes were performed.

All-wheel initial heat uses a slider with numbered guides. Zero clears all four temperature overrides and selects Game default; explicit slider values use 1–150°C. Individual fields remain independent. The redundant Link condition values checkbox is removed.
