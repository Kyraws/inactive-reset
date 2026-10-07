# Developer tools

These tools are for maintainers. They are not included in release packages and
are not required for normal checkpoint use. See the [architecture](../docs/ARCHITECTURE.md)
for the application's automatic discovery.

| Tool | Purpose |
| --- | --- |
| `dump-sdk-offsets.cpp` | Generate the shared-memory byte layout from LMU's installed SDK headers |
| `src/InactiveReset.Reanchor` | Capture and inspect mapped modules, investigate addresses, and run experimental probes |
| [Discovery-resource scripts](reanchor-research/README.md) | Regenerate embedded instruction anchors |
| [Native observers](observer/README.md) | Observe natural engine calls in a direct-launch Practice session |
| `ghidra/` | Export reader evidence from a locally analysed image |

## Shared-memory layout

`offsets/shared-memory.json` contains the telemetry layout consumed by
`SharedMemoryOffsets`. Generate its offsets using the compiler's `offsetof`
against the SDK headers supplied with LMU under
`<LMU install>\Support\SharedMemoryInterface\`.

The headers are not redistributed. Keep them outside version control; see
[NOTICE](../NOTICE). A normal .NET build uses the committed layout and does not
need or compare installed SDK headers. The JSON's `sdkHeaderSha256` is provenance,
not an automatically enforced SDK compatibility check.

From an x64 MSVC Developer command prompt at the repository root, replace
`<LMU install>` with the actual path:

```text
cl /std:c++17 /EHsc /O2 /I"<LMU install>\Support\SharedMemoryInterface" tools\dump-sdk-offsets.cpp /Fe:tools\dump-sdk-offsets.exe
tools\dump-sdk-offsets.exe > artifacts\shared-memory.generated.json
```

Create `artifacts/` first if it does not exist. The generator emits the layout
but does not add `sdkHeaderSha256`. After reviewing the generated layout, add
that field from the header used to compile it before replacing the committed
file. For example, in PowerShell:

```powershell
$layout = Get-Content artifacts/shared-memory.generated.json -Raw | ConvertFrom-Json
$headerHash = (Get-FileHash '<LMU install>\Support\SharedMemoryInterface\SharedMemoryInterface.hpp' -Algorithm SHA256).Hash
$layout | Add-Member -NotePropertyName sdkHeaderSha256 -NotePropertyValue $headerHash
$layout | ConvertTo-Json -Depth 10 | Set-Content offsets/shared-memory.json -Encoding utf8
```

Revisit the layout when SDK headers change. Telemetry vectors use eight-byte
components; the loader rejects a layout declaring another component size.
Executable-address discovery and SDK layout generation are different processes.
A code update does not necessarily change the shared-memory layout.

## Maintainer analysis

Build the solution before running the Reanchor executable from its output in
`artifacts/bin/`. Its `--help` lists read-only analysis and explicitly armed
experimental commands. Experimental writes are not production validation.

Mapped images, native binaries, local logs, and private SDK headers remain
untracked. Do not publish captured game images. Historical experiments and
build-specific addresses are in the [archive](../docs/archive/README.md).
