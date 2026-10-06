# Direct-launch spot observer

This is an observation-only, in-process spot-function test. It scans LMU's
executable sections for the established function prefix, then validates its
count, multiplier, and garage-table references. It requires exactly one match;
otherwise it refuses without patching the game. It calls the original function
and logs the first 20 argument and output-value sets. It does not alter the
returned data or spot table. It refuses protected launches. **Do not use it
in a protected session.** The resolver has been checked against the saved
`D9B92CA9` runtime image; future updates may still require new analysis.

The Drive and final-spot resolver tests also pass against the saved
`29CE422A` successor dump. The Drive observer logged one natural
garage-to-Drive call with mode argument 0 on the exact player object; its
separate third-argument flag was 1. The first matching-car final-spot trace
returned a spot position roughly
8 m from the stationary car's telemetry pose. The next observer build also
logs the indexed helper's output at the same Drive call, so both intermediate
outputs can be compared in one session. An indexed-table write is not yet
justified. Do not stack observers in one process.

Build from a Visual Studio x64 Developer command prompt (CMake fetches pinned
MinHook 1.3.4 from its official repository):

```powershell
cmake -S tools/observer -B tools/observer/build-next -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build tools/observer/build-next
tools/observer/build-next/spot_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
```

Open the game by **direct launch** and enter single-player Practice. Then run:

```powershell
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-next/lmu_spot_observer.dll
Get-Content tools/observer/build-next/lmu_spot_observer.log -Wait
```

The script reports an unfamiliar executable SHA-256, checks for EAC, and
prevents a second observer from stacking in one game session. The DLL
independently checks EAC and the unique structural match before installing
the detour. Exit the game to unload it. There is no hot-unload command.

## Flag Rules candidate

On a **fresh** direct-launch Practice session (exit the game first if a spot
observer is already loaded), run:

```powershell
tools/observer/build-next/flag_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-next/lmu_flag_observer.dll
Get-Content tools/observer/build-next/lmu_flag_observer.log -Wait
```

This pass-through hook observes the containing function and reads the Flag
Rules value before and after it runs. A function hit is **not** proof that its
internal `cmp` at the reader site executed or that it is the pit-speeding
consumer. It logs the first ten calls and subsequent value changes.

## Pit-speeding message path

On a **fresh** direct-launch, single-player Practice session, with no other
observer loaded:

```powershell
tools/observer/build-pit4/pit_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-pit4/lmu_pit_observer.dll
Get-Content tools/observer/build-pit4/lmu_pit_observer.log -Wait
```

The resolver validates the literal `Speeding In Pitlane`, both outgoing message
branches, the stop/go penalty-call function, and an upstream speed gate tied to
Flag Rules. Both hooks pass through to the original functions. The penalty-call
hook and speed-gate hook both fired during a live stop/go penalty with Flag
Rules `2`, on the same object. A gate log line records the positive-excess
argument, not the measured speed or pit-limit units. Exact instruction checks
fail closed after code changes; the resolver still needs an offline image and
live validation for every new build.
Use `pit_resolver_test.exe <mapped-dump> --probe` for another build; this also
checks that corrupting the call or message makes resolution fail. The saved
mapped dumps for `266D1AF6`, `44C3EE9C`, `E669E121`, `C40C815F`, and both
`0F6DCAC1` states passed offline. These older builds were **not live-tested**
with this DLL.
Only test a pit-speeding event in direct-launched single-player Practice; do
not use a protected or multiplayer session.

## Drive-path pit spot reader

The small pit lookup at `0x00A90C10` did not fire on Drive, pit exit, or Return
to Garage in the observed D9B92CA9 session. The larger GetPitDestination path
reads the same candidate pointer directly at `0x00D14780` and `0x00D14857`.
Its entry observer logs natural calls and mode values, without calling the
function or changing spot data. It requires a **fresh** direct-launch Practice
session: the previous observer stays loaded until LMU exits.

```powershell
tools/observer/build-pit4/drive_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-pit4/lmu_drive_observer.dll
Get-Content tools/observer/build-pit4/lmu_drive_observer.log -Wait
```

The offline resolver also rejects a damaged function prefix and a forged
duplicate. A logged function call confirms execution of the containing path,
not necessarily the internal pit-table branch. A live Drive call logged
`mode=0`; static analysis showed the normal-slot branch uses helper
`0x00A90C70`, which selects other spot globals, not the candidate
`pitPosTable` at `0x01DFA578`. Do not promote that candidate profile or make
a placement write until the actual helper output is validated.

## Normal-slot Drive spot helper (read-only)

For a fresh direct-launched Practice session, without another observer loaded:

```powershell
tools/observer/build-pit4/pit_lookup_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-pit4/lmu_pit_lookup_observer.dll
Get-Content tools/observer/build-pit4/lmu_pit_lookup_observer.log -Wait
```

This DLL now targets the helper at `0x00A90C70` called by the observed
`mode=0` normal-slot Drive path, not the unused small lookup at `0x00A90C10`.
It passes through normal game calls and logs their requested index and 12-byte
outputs; it does not call the helper or alter any spot data. The resolver
rejects missing, ambiguous, or structurally changed functions. Compare the
logged output with a separate read of the selected table entry and the car's
arrival before revising any offset profile. No call is inconclusive.

## Last unconditional Drive spot lookup (read-only)

The helper output is overwritten downstream. A first probe at the conditional
vehicle transform logged no call during a normal Drive, so the next probe
targets the immediately preceding unconditional spot lookup. It resolves
through the validated Drive caller, logs only calls returning to that caller,
and records the selected index, selector, position, and orientation. It changes
none of them. Use it only in a fresh direct-launch Practice session with no
other observer loaded:

```powershell
tools/observer/build-pit4/final_transform_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build-pit4/lmu_final_spot_observer.dll
Get-Content tools/observer/build-pit4/lmu_final_spot_observer.log -Wait
```

The saved-dump test checks the expected Drive call and callee prologue and
rejects a damaged prologue. A live natural Drive call is still needed; no spot
table is yet approved as a write target.
