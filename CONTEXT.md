# Project glossary

Terms used by the current implementation. See [Architecture](docs/ARCHITECTURE.md)
and [Placement and rules](docs/PLACEMENT.md) for behaviour and limitations.

| Term | Meaning |
| --- | --- |
| Placement | Plan a checkpoint target, temporarily write the selected spot entry, let LMU place the car on Drive, and restore the entry. Rule and lap-state changes are separate parts of the sequence. |
| Checkpoint | A recorded pose on one track, with the capture vehicle recorded as provenance. Other vehicles can use the pose with their own offsets. It does not restore captured speed, fuel, tyres, or damage. |
| Calibration | Measured resting-offset constants for a track/vehicle combination, with build provenance when available. |
| Learned rest | Corrections derived from accepted placement outcomes, keyed by build, track, vehicle, and checkpoint. |
| Spot table | An engine table of placement positions and orientations. The selected ordinary or indexed destination entry is the temporary write target. |
| Slot | The engine's car index, resolved from live container fields. |
| Arrival | The sampled vehicle state when control changes from AI to player, before the settle delay. |
| Rest position | The vehicle position measured after settling; currently sampled 400 ms after player control begins. |
| Pit-speeding penalty | The game penalty for exceeding the pit-lane speed limit. Flag Rules gates its evaluation. |
| Pit limiter | The car's driver-operated speed limiter. Inactive Reset does not control it. |
| Flag Rules | A broader rule gate temporarily disabled during placement. It affects pit-speeding and track-limit enforcement. |
| Track limits | Off-track enforcement and lap invalidation, including runtime derived flags that may re-arm during session transitions. |
| Pit state | Engine pit-procedure state. Production placement waits for it to clear; it does not write it to zero. |
| Pit flag | A separate field involved in out-lap demotion. Placement can clear it. It is not the pit-state field. |
| Out-lap demotion | Accepting a lap crossing without awarding a timed lap because of pit-related state. |
| Sector | The engine's current lap sector. The default placement repair forces the final sector so a start/finish crossing can be accepted. |
| Read-live rule | A setting evaluated during runtime, such as Flag Rules. |
| Expanded-once rule | A setting turned into derived runtime flags at session initialisation; changing the setting alone does not change those flags. |
| Game build | One executable identified by its full SHA-256. A version string alone is insufficient. |
| Offset profile | Executable addresses, container offsets, model metadata, and probe bytes for one build. |
| Automatic discovery | Local resolution from the mapped game image using structural patterns and embedded instruction anchors. |
| Direct launch | Starting `Le Mans Ultimate.exe` without EAC. The game allows local sessions only; use single-player Practice with this tool. |
| Protected launch | Starting `start_protected_game.exe` through EAC. Inactive Reset refuses attachment when EAC or the protected launcher is detected. |
| Gate | An implemented check that must pass for the operation it protects. A declared field or documented intention is not itself an enforced check. |

Use “pit state”, “pit flag”, “pit-speeding penalty”, and “pit limiter” precisely:
they refer to different things. Build-specific numeric addresses belong in
offset resources or dated research evidence, not in this glossary.
