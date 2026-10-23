# Local session setup

The windowed app opens Overview. Its session builder covers car and track,
weekend sessions, conditions, competition and rules. Practice tools provides
checkpoints and tyre preparation. Driving assists stay configured in LMU.

The workspace uses one navigation rail and fixed summary panel. Practice
instructions and cancellation stay visible while the tool area scrolls. Tyre
choices and the selected checkpoint survive navigation.

1. Keep Steam running and select **Launch local play**.
2. In LMU, dismiss the startup video's **Press any button** prompt.
3. Choose your car, livery and circuit in the app. Only owned cars can be selected;
   unowned tracks are excluded.
4. Configure Practice, Qualifying, Warmup and Race. Counts, durations, start times,
   race length modes and track conditions follow LMU's available settings.
5. Adjust weather at Start, 25%, 50%, 75% and Finish for each supported session,
   or choose a forecast preset. Configure AI opponents and class selection, then
   rules. Additional session controls are under Rules.
6. Select **Start session** once LMU is ready at its main menu. Progress
   appears in the app. Once loaded, switch to LMU and press **Drive**.

Numeric and clock edits stay responsive while saving. Rapid +/− clicks combine
into one final value after a short pause; type a duration or start time directly
and press Enter or leave the field to save. Other edits save immediately. Every
write reads back the game's accepted value, and LMU controls the available ranges.
Start session waits for pending edits to be confirmed. Failed saves keep the
requested values visible with **Retry save** and **Discard pending edits**;
discard reads the game's current values rather than undoing confirmed writes.
The summary stays visible while you edit. Refresh
from LMU after changing settings in the game. Changing tracks keeps the setup;
**Use circuit defaults** explicitly replaces session settings, grid and weather.

Starting checks the displayed setup against LMU again, applies a grid limited
to the requested AI count (zero loads only the player), and verifies session
settings, amounts, weather, assists, car and track after applying the preset.
The generated save must contain that same preset before it can be loaded.
An insufficient opponent selection or changed setup stops the launch with an
explanation. The applied grid can reduce the available class selection; choose
classes again when increasing the grid for a later session.

Returning to the menu first performs LMU's event-exit handoff, then waits for
the actual setup state. Showing the home screen alone does not establish that
the previous event has unloaded.

Live regression checks on 2026-10-16, build 14200 / executable SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`,
loaded the Mercedes #62 at Barcelona with zero AI (one car total) and two AI
(three cars total). Practice duration, AI strength, Flag Rules, qualifying
enablement and forecast temperature edits read back and restored exactly.
The corrected return-to-menu sequence reached `GSTATE_SETUP`. These checks
cover the recorded settings and content, rather than every session combination.

Setup is locked during loading, an active session or a practice-tool operation.
**Return to menu** asks before ending the loaded session. REST session setup
works at the single-player main menu with EAC either on or off. Loading,
multiplayer and active sessions keep editing locked. Checkpoint capture uses
shared telemetry in either launch mode when LMU exposes a valid position.
Teleporting, tyre preparation and practice rule toggles require EAC off and an
active single-player Practice session.

Choose **Launch local play** for all practice tools, or **Launch with EAC** for
normal online access plus REST session setup and telemetry capture. Both choices
describe their capabilities before launching. To switch modes, close LMU first;
the app does not restart a running game. An EAC notice stays visible throughout
the app and disabled practice actions explain their requirements.

Live checks on 2026-10-23, executable `66942337`, launched through EAC and verified
REST setup/readback, official-event refresh, solo Practice loading, telemetry
checkpoint capture, teleport refusal and return to the menu. The original full
native preset restored exactly. API route discovery reads installed UI/settings
files under EAC without inspecting the protected process's modules. Online-session
changes remain blocked; no online race was joined for this validation.

## Official event practice

For official-event practice, use **Overview → Qualifying practice / Race practice**
or the official entries in **Session presets**. Both reproduce that session's
conditions in solo Practice, after review. The event library is cached separately
from personal presets. See [Official scheduled sessions](LAB.md#official-scheduled-sessions)
for refresh behavior, supported conditions and live-validation limits.

## Startup prompt and cancellation

The current API cannot distinguish LMU's intro screen from its visible main
menu. A backend session can load while the intro remains on screen. Dismiss the
prompt manually once per launch. The app has no confirmation checkbox; it enables
Start from the available menu/setup state and still refuses loading, online and
active-session states.

**Stop monitoring** cancels the app's wait. After LMU accepts the load request,
it does not undo that load. Check LMU before attempting another start. Failed
writes are not automatically repeated.

## Implementation and validation

`LmuSessionClient` recovers routes from the running install's `Bin/UI.zip` (or the
located install before launch) without executing JavaScript. It finds the unique
`start/assets/app-*.js` bundle, resolves supported named helpers and URL constants,
and checks their HTTP wrappers, parameter expressions and known request shapes.
Changed route prefixes and leaf names are supported when those helper shapes
remain identifiable. The configured `WebUI port` is read from `Settings.JSON`;
requests always use `127.0.0.1`, with redirects and proxies disabled. Port 6397 is
the recorded build's setting, rather than a discovery assumption.

Every request re-reads the selected bundle and port. Parsing is reused only for
the same full script SHA-256 and port. Missing, ambiguous or conflicting helpers,
unsupported expressions and detected request-shape changes refuse use before
that request is sent. There is no fallback to stale routes and no automatic retry
of a failed write. Helper renaming/minification, arbitrary payload changes,
response schema changes and new launch sequences still require explicit support;
route discovery does not prove engine behavior. The track catalog continues to
use GET on the track resource recovered from the selection helper, with response
shape checked by the client.

It reads live
setting metadata, all session settings (62 on the tested build), 21 driving
assists, the vehicle/track catalog and weather nodes. It uses LMU's own relative
step semantics for session/weather controls and absolute values for assists.
Direct numeric entry rereads the current setting and calculates one relative
write on the server. Clock entry changes only the selected start-time field in
the current native preset, preserving the rest, then confirms readback. The UI
serializes these saves, keeps newer drafts over older replies, and preserves the
focused editor node. Saved-plan numeric/time changes use the same direct setter.
Launching requests the current preset, applies it, generates a save and loads
that generated save. Unrelated player settings are retained. The obsolete
`/rest/race/startRace` route is not used.

On 2026-10-08, build 14200, executable SHA-256
`66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF`:
live checks passed for duration, session enablement, weather temperature,
automatic wipers, car/track selection and multiclass grid selection. Each edit
was read back and the original setup restored. The published app also completed a BMW M4 LMGT3 Practice load at Daytona,
with the API confirming the player vehicle loaded in the garage and the UI
showing completion. The same sequence previously had user confirmation of the
visible session. Automated API and browser checks do not establish every
combination of content, conditions or future game build.
