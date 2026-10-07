# Documentation

Current implementation reviewed on 2026-10-07. These guides describe the code in
this repository. Recorded game experiments are identified separately; passing
an offline test does not establish live compatibility.

| Guide | Purpose |
| --- | --- |
| [Project README](../README.md) | Installation and the practice workflow |
| [Architecture](ARCHITECTURE.md) | Components, discovery, placement, and data flow |
| [Placement and rules](PLACEMENT.md) | Current sequence, restoration, and lap timing |
| [Tyres](TYRES.md) | Live tyre view, physics rules and build-specific engine evidence |
| [Status](STATUS.md) | Known implementation gaps and validation limits |
| [Testing](TESTING.md) | Automated coverage, local capture requirements, and live-test boundaries |
| [Contributing](../CONTRIBUTING.md) | Build, tests, bug reports, and documentation expectations |
| [Glossary](../CONTEXT.md) | Domain terms used by the project |
| [Developer tools](../tools/README.md) | SDK layout generation and research tooling |
| [Archive](archive/README.md) | Historical experiments and superseded guidance |

Keep current instructions here. Put dated, build-specific investigation records
in the archive, with their evidence and limitations intact. Add a result to a
current guide only after checking it against the implementation or recording a
live test with its game build, track, vehicle, and outcome.
