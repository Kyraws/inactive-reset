# Native observers

Maintainer-only instruments for observing natural LMU engine calls. These DLLs
install detours inside the game and pass calls through without changing the
returned spot data. “Observation-only” describes their purpose; they still
modify executable code to install hooks.

Use a fresh direct-launch, single-player Practice session. The loader rejects
EAC and another loaded observer. Each DLL also checks its structural signature
before hooking. Exit LMU to unload an observer; there is no supported hot unload.

These instruments are not part of the production placement path or release ZIP.
Their signatures and saved-dump tests have build-specific limits. Earlier call
traces, candidate addresses, and conclusions are preserved in the
[observer notes](../../docs/archive/OBSERVER_NOTES.md) and
[reader investigation](../../docs/archive/D9B92CA9_READER_RESEARCH.md).

## Build

Use an x64 MSVC Developer command prompt with CMake and Ninja available, from
the repository root. CMake fetches MinHook at the commit pinned in
`CMakeLists.txt`.

```text
cmake -S tools/observer -B tools/observer/build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build tools/observer/build
```

The output directory is ignored by Git. All observer DLLs and resolver test
executables are built together.

| Observer DLL | Resolver test | Observed path |
| --- | --- | --- |
| `lmu_spot_observer.dll` | `spot_resolver_test.exe` | Spot-function arguments and outputs |
| `lmu_flag_observer.dll` | `flag_resolver_test.exe` | Containing function and Flag Rules value |
| `lmu_pit_observer.dll` | `pit_resolver_test.exe` | Pit-speeding gate and penalty-call path |
| `lmu_drive_observer.dll` | `drive_resolver_test.exe` | Drive destination function and modes |
| `lmu_pit_lookup_observer.dll` | `pit_lookup_resolver_test.exe` | Normal-slot spot helper |
| `lmu_final_spot_observer.dll` | `final_transform_resolver_test.exe` | Downstream spot lookup reached by Drive |

## Validate and observe

Run the corresponding resolver test against a saved mapped image before loading
an observer. For the spot test's recorded D9B92CA9 fixture:

```text
tools/observer/build/spot_resolver_test.exe artifacts/LMU_runtime_D9B92CA9.bin
```

The captures are local and not distributed. Several tests assert exact addresses
for specific recorded builds. A test failure on another build may require new
analysis; do not assume these are universal compatibility checks. The pit test
has a `--probe` mode for investigating another mapped image.

In PowerShell, after starting a fresh direct-launch Practice session:

```powershell
$gamePid = (Get-Process 'Le Mans Ultimate').Id
powershell -NoProfile -ExecutionPolicy Bypass -File tools/observer/load.ps1 `
  -ProcessId $gamePid -Dll tools/observer/build/lmu_spot_observer.dll
Get-Content tools/observer/build/lmu_spot_observer.log -Wait
```

Supply the selected DLL explicitly. `load.ps1` warns on an executable hash other
than its recorded D9B92CA9 reference; the DLL's structural checks decide whether
to hook. A function-entry hit does not prove every internal branch ran. No hit
is inconclusive. Compare logged arguments and outputs with the selected table
and telemetry before drawing a placement conclusion.

`probe-final-spot.ps1` is a separate maintainer experiment; inspect its parameters
and implementation before use. Experimental writes and observer traces do not
establish production-path live validation.
