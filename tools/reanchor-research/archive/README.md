# Archived reanchor prototypes

Archived on 2026-10-07. These scripts preserve earlier string, call-graph, and
RTTI-anchor experiments. They use private, hardcoded dump paths and require
Python and Capstone. Results and numeric addresses apply to those captures.

| Script | Historical purpose |
| --- | --- |
| `stranchor.py` | Locate functions through referenced log strings |
| `callgraph.py` | Compare callees through surviving caller signatures |
| `anchors3.py` | Survey RTTI and string anchors |

No application project imports these scripts. Use the active
[discovery-resource generator](../README.md) for embedded anchor regeneration.
The [former architecture](../../../docs/archive/ARCHITECTURE.md) records the
techniques and their original limitations.
