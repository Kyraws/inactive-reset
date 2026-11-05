# QA automation case study

Tracking: [career-development #9](https://github.com/Kyraws/career-development/issues/9).
This is personal-project testing, not professional QA employment.

## Run the checks

Windows x64, .NET 8 SDK and Microsoft Edge WebView2 Runtime are required.
NuGet restore supplies the pinned Microsoft.Playwright .NET library and its
bundled driver. No `playwright install` or separate Chromium download is needed:
the driver attaches to the WebView2 control already created by the test runner.

```powershell
# Portable xUnit/HTTP tests plus all browser checks, including Playwright:
.\build.cmd test -Configuration Release
# Browser checks only:
.\build.cmd browser -Configuration Release
# Focused checkpoint regression and length boundaries:
dotnet test InactiveReset.sln -c Release --filter "FullyQualifiedName~CheckpointNameBoundaryTests"
```

`tests/InactiveReset.BrowserTests/Program.cs` creates an invisible WinForms host,
a unique temporary browser profile and a loopback CDP port. The debugging option
is supplied only to that test environment; normal application startup is unchanged.
`PlaywrightChecks.cs` attaches using `ConnectOverCDPAsync`, selects that control's
page, and uses real clicks, text input and retrying locator assertions. It does
not start a second browser or call production JavaScript functions to simulate
those user actions. Each workflow has a new `UiTestHost` and temporary data.

The backend is the real local HTTP server, but menu responses are synthetic.
The fixture's attachment delegate always throws before any game access; normal
polling can reach this refusal, which is reported in the result. Browser cases
also assert zero menu writes. No running game, private captures or credentials
are required. Passing these checks does not establish live game compatibility.

## Requirement-to-test plan

Existing tests are reused; this table is a selected risk map, not a coverage
percentage or a promise about every possible failure. Method names below are in
`tests/InactiveReset.Tests/` unless prefixed with `playwright-`.

| Requirement / risk | Level and automated case | Assertion |
| --- | --- | --- |
| Single-player REST edits work with protected launch | HTTP: `UiServerTests.EacAllowsSinglePlayerRestEditingWithoutReadingGameMemory` | Successful payload, updated duration, one fake write, no attachment |
| Protected launch refuses memory actions | HTTP: `EacRejectsMemoryActionsBeforeAnyAttachmentOrMenuWrite` (parameterized) | Unsuccessful response, readable error, zero writes/attachment |
| Multiplayer refuses edits in either launch mode | HTTP: `MultiplayerStillRejectsRestChangesInEitherLaunchMode` | Error payload and no writes |
| Invalid methods, content types and JSON fail clearly | HTTP: `InvalidRequestsReturnAReadableErrorWithoutMenuWrites` (parameterized) | Unsuccessful status and nonempty JSON error |
| Unknown endpoints with valid POST/JSON have a stable error contract | HTTP: `UnknownEndpointReturns404JsonWithoutSideEffects` | 404, JSON content type, `error` string, no side effects |
| Damaged checkpoint does not hide valid data | HTTP: `MalformedCheckpointDoesNotBreakTheStateResponse` | Valid checkpoint still returned and damaged filename reported |
| Preferences persist without partial concurrent output | HTTP/storage: `PreferencesPersistAndConcurrentSavesLeaveOneCompleteDocument` | Complete winning document matches disk and readback |
| Concurrent operations are refused and cancellation works | HTTP: `BusyOperationRejectsAnotherChangeAndCancellationFinishesTheFirst` | Second request refused, first finishes after cancellation, busy clears |
| Checkpoint name stays within its track folder | Unit/filesystem: `CheckpointNameBoundaryTests.CheckpointNameCannotEscapeTrackFolder` | Traversal rejected before creating any directory |
| Filename length limits are enforced exactly | Unit/filesystem: `CheckpointNameLengthBoundary` | 100 characters save/load; 101 rejected before creation |
| Ambiguous names do not silently select the wrong checkpoint | Unit: `CheckpointSelectionTests.RemainingAmbiguityIsRefusedWithIds` | Exception identifies an explicit selection ID |
| Transaction failure restores original bytes | Unit/synthetic memory: `SpotWriteTransactionTests.A_partial_write_failure_rolls_back_before_returning_the_error` | Original byte array restored after simulated failure |
| Search handles no matches and recovers | WebView2/Playwright: `playwright-library-search` | Empty result, then case-insensitive match and correct preview |
| Failed refresh remains understandable and retryable | WebView2/Playwright: `playwright-refresh-error` | Backend error visible, button enabled, no fabricated events |
| Protected actions are disabled and menu can recover | WebView2/Playwright: `playwright-protected-recovery` | Controls disabled under EAC; direct-launch menu recovers |

The existing five JavaScript suites, real HTTP page smoke checks and viewport
checks remain in place. Optional private-capture and live checks stay separate;
see [Testing](TESTING.md) and [live checklist](../tests/LIVE_CHECKLIST.md).

## Historical bug report: checkpoint name escapes its folder

**Type:** historical regression, already fixed in `4901711`; not a newly
introduced production fix in issue #9.

**Affected revision:** `abc9f207f543885a5427e5248c6a759cc973918d` (parent of the fix).

**Precondition:** synthetic checkpoint and a unique temporary root, with no game.

**Steps:** call `CaptureService.Save` with name `../escaped`, track `Test track`
and vehicle `Test car`.

**Expected:** throw `CheckpointException` before creating files/directories.

**Actual at the affected revision:** save returns successfully and places
`escaped.json` in the test root, outside its intended track/car subdirectory.
One parent segment deliberately confines this reproduction to its own test root.

**Cause:** the name was combined with a directory without filename validation or
a final path-containment check. The original fix added name validation and
containment checks, alongside safer save handling. The new focused regression
protects that existing fix; the boundary cases also exercise the length rule.

**Executed evidence (2026-11-05):** the focused regression failed on an isolated
archive of the affected revision: `Assert.Throws() Failure: No exception was
thrown` (1 failed, 0 passed). The same test passes on the current implementation.
No source in the working application was reverted to produce the failure.

To reproduce safely, archive the affected commit into a fresh temporary directory,
copy only `CheckpointNameBoundaryTests.cs` into its test project, then run only
`FullyQualifiedName~CheckpointNameCannotEscapeTrackFolder`. Do not run the old
application or broaden the reproduction to absolute paths or multiple parent
segments. The test owns and cleans its temporary root.

## Results and CI

Local verification is recorded for the issue #9 changes on top of local baseline
`3fb506c`, which includes unpublished development. Results are not release claims.
Local final run (2026-11-05): 666 portable xUnit cases passed; all 15 browser
result groups passed, including the three Playwright workflows. These groups
include the existing JavaScript suites and are not 15 individual assertions.

The existing Windows `Verify` workflow runs `build.cmd package`, which invokes
`build.cmd test` and therefore the Playwright cases. No additional browser install
step is required. It uploads `artifacts/TestResults/` even on failure: TRX,
`browser/results.json`, and named failure screenshots. A failed Playwright
assertion contributes a failed runner result and nonzero process exit code.

The same isolated test-only patch was also verified on public baseline `b5e1fbf`:
510 xUnit cases passed, six private-fixture cases skipped, and all 14 browser
groups passed, including the three Playwright workflows. This offers a publication
path that excludes the unfinished local 0.5 snapshot.

The new revision has not been pushed or run in GitHub Actions. Public CI evidence
remains pending explicit publication approval; an earlier successful run cannot
validate these changes. The broader unpublished snapshot is not authorized for
publication by this task.

## Walkthrough before CV integration

1. Run the browser command and find the three `playwright-` results in JSON.
2. Explain what the real server handles and what `UiTestHost` fakes/refuses.
3. Follow `library-search`: locator, click, fill, then retrying assertions.
4. Explain why empty results and recovery are checked in one workflow, and how
   fresh host data prevents cross-case dependence.
5. Run the focused checkpoint regression and explain its historical failure and
   current rejection before file creation.

User understanding and approval remain pending. C#, xUnit, HTTP integration
testing, WebView2 automation and Playwright can be described as project skills
only to the extent actually used, verified and understood.

References: [Playwright WebView2 guide](https://playwright.dev/dotnet/docs/webview2)
and [CDP connection limitations](https://playwright.dev/dotnet/docs/api/class-browsertype#browser-type-connect-over-cdp).
