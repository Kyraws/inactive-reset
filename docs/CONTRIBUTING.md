# Contributing

Bug reports, documentation corrections, and focused code changes are welcome.
Keep discussion respectful and give enough information for someone else to
reproduce the result.

## Before changing code

Read the [architecture](ARCHITECTURE.md), [placement sequence](PLACEMENT.md),
[known limitations](STATUS.md), and [glossary](../GLOSSARY.md). The
[research summary](research/README.md) contains historical evidence, not current
implementation requirements. Agent implementation issues live under `.scratch/`
as described in [the issue guide](agents/issue-tracker.md).

## Build and test

Use Windows x64 and the .NET 8 SDK. From the repository root in PowerShell:

```powershell
.\build.cmd
.\build.cmd test -Configuration Release
.\build.cmd package
```

Build the solution through the wrapper so its x64 mapping is applied. `test`
runs portable C# and actual WebView2 checks; Windows needs the WebView2 Runtime.
`package` tests Release, publishes the App and CLI, creates the ZIP and smoke
tests the extracted package outside the checkout. The research
project and native observers are not shipped. See [developer tools](../tools/README.md)
for their separate requirements.

Use `build.cmd test-local -Configuration Release` for read-only compatibility
checks needing an LMU install or private mapped-game dumps. Check skips and
record which captures were used; a green suite without those inputs does not
validate a new game build. SDK headers and runtime dumps must remain outside
version control. Existing historical offset JSONs are regression inputs.
See [Testing](TESTING.md) for coverage, capture paths, and focused commands.

## Reporting a problem

Include the tool version, LMU version and executable hash when available, track,
vehicle, launch method, reproduction steps, expected result, and actual result.
Attach the relevant Activity text or a short placement-log excerpt. Review logs
for machine-specific paths before sharing them. Do not upload game executable
images or proprietary SDK headers.

## Practice workspace UI

From the repository root, run `.\build.cmd dev` to open the desktop app with
live reload. It builds once at launch and reloads the current workspace page
when HTML, CSS, or JavaScript in `src/InactiveReset.Ui` is saved. A refresh can
discard unsaved form edits. Restart the command after changing C#.

- Start at 14 px with about 1.4 line height for instructions users need to act.
  Place instructions near the relevant label or control and allow them to wrap.
- Explain why an action is unavailable beside that action. Lead with the next step.
- Keep save, refresh, and assignment feedback near the controls that caused it.
  Use consistent wording and distinguish feedback from persistent instructions.
- Start at 12 px for metadata such as sources, counts, and timestamps. Keep at
  least 4.5:1 contrast for ordinary text, including secondary text.
- Use consistent heading levels, casing, field styles, and keyboard focus.
  Style the main action in each region as primary, supporting actions as
  secondary, and deletion as destructive. Use the same label for the same action.
- Make the entire label or tile clickable for on/off settings. Use an accent
  fill for on and a neutral fill for off.

Review layouts at the desktop minimum of 1040×680 and at a narrower browser
viewport. See [Testing](TESTING.md).

For visual reviews, save a before-and-after PNG for each workspace page in a
`comparisons/` folder. Keep raw screenshots, JSON, and supporting files outside it.

## Verifying a behaviour change

Use the smallest relevant automated checks. For process writes, additionally
record a direct-launch single-player Practice test with the full game hash,
track, vehicle, checkpoint, before/after values, and verified restoration.
Distinguish read-only planning, experimental probes, and the production path.
Do not infer live success from instruction matches or mathematical residuals.

## Updating documentation

Put setup and workflows in the relevant user guide, implementation in
[Architecture](ARCHITECTURE.md), and verification commands and expectations in
[Testing](TESTING.md). Use [Status](STATUS.md) to summarize limitations and link
to dated outcomes. Keep detailed historical results in [research](research/README.md)
with their test conditions and scope.

Link to the owning guide when a fact is needed elsewhere. The release copies
these guides into the ZIP, so use shipped documentation for relative links.
Refer to source-only paths in code formatting when readers need to find them in
the checkout. Label planned behavior explicitly.
