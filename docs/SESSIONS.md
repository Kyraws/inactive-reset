# Local session setup

The windowed app opens **Session setup**. Its five sections cover car and track,
sessions, weather, opponent grid, and rules and driving assists. **Practice tools**
opens the existing checkpoint and tyre workspace.

Both workspaces share the same header, navigation, sidebar, cards and fixed
summary panel. Practice instructions and cancellation stay in the summary while
the tool area scrolls. Tyre choices and the selected checkpoint survive workspace
switches. Startup confirmation is remembered for the current LMU process and
cleared for a new launch.

1. Keep Steam running and select **Launch local play**.
2. In LMU, dismiss the startup video's **Press any button** prompt.
3. Choose your car, livery and circuit in the app. Only owned cars can be selected;
   unowned tracks are disabled.
4. Configure Practice, Qualifying, Warmup and Race. Counts, durations, start times,
   race length modes and track conditions follow LMU's available settings.
5. Adjust weather at Start, 25%, 50%, 75% and Finish for each supported session,
   or choose a forecast preset. Configure AI opponents and class selection, then
   rules and assists. Additional session controls are under Rules & assists.
6. Confirm that LMU's main menu is visible and select **Start session**. Progress
   appears in the app. Once loaded, switch to LMU and press **Drive**.

Edits save immediately to LMU and read back the game's accepted values. LMU
controls the available ranges. The summary stays visible while you edit. Refresh
from LMU after changing settings in the game. Changing tracks keeps the setup;
**Use track defaults** explicitly replaces session settings, grid and weather.

Setup is locked during loading, an active session or a practice-tool operation.
**Return to menu** asks before ending the loaded session. Setup works only in
local single-player mode without EAC. Checkpoint placement, tyre preparation
and practice rule toggles additionally require an active Practice session.

## Startup prompt and cancellation

The current API cannot distinguish LMU's intro screen from its visible main
menu. A backend session can load while the intro remains on screen. Dismiss the
prompt manually once per launch and confirm in the app before starting. This
confirmation is cleared when the game exits or the app returns it to the menu.

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
