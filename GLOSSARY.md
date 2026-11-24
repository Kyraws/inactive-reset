# Inactive Reset glossary

The language of local practice setup, recording, and repeatable driving in Le Mans Ultimate (LMU).

## Sessions and presets

**Practice workspace**
The Inactive Reset interface for preparing local sessions and returning to points on a circuit. Some files and tests use the older names Lab and Redline. [Practice workspace](docs/LAB.md)

**Session builder**
The workspace area for choosing a circuit layout, car, session schedule, weather, and rules before starting a local session. [Local sessions](docs/SESSIONS.md)

**Session preset**
A reusable set of builder choices. A personal preset is saved by the user. An official practice preset derives local conditions from an official event's qualifying or race session.

**Car model**
The race car design shared by its liveries, such as the Corvette GT3. Distinct variants, such as Evo versions, can have different names.

**Livery**
A team and race-number presentation of a car model, such as TF Sport #2.

**Official event**
A scheduled RaceControl event that can serve as a source for local practice conditions. Qualifying practice and race practice are the local practice configurations derived from its corresponding sessions.

**RealRoad**
LMU's evolving track surface conditions. The initial surface describes the starting grip and state. Progression governs how the surface evolves. Natural grip carries a surface forward from an earlier session.

**Direct launch**
Launching LMU directly for local play. The game starts without Easy Anti-Cheat (EAC).

**Protected launch**
Launching LMU through EAC for protected play. Local session setup and protected play have different access requirements from checkpoint placement.

## Circuits and destinations

**Circuit layout**
A particular driveable configuration of a circuit. Two layouts at one venue can have different routes and separate checkpoints.

**Circuit map**
The geometry used to display a circuit layout and pit lane. A recorded route shows where the car actually drove during a recording. [Circuit maps and library](docs/CIRCUIT_MAPS.md)

**Pose**
The car's position and orientation. Speed and gear describe its motion and drivetrain state.

**Checkpoint**
A saved destination containing a pose, circuit identity, and capture-car provenance. It may also contain eligible speed and gear. Reusing the pose and restoring the captured motion have different eligibility requirements.

**Quick capture**
The replaceable checkpoint for a track and car. A named checkpoint is a separately saved destination in the checkpoint library.

**Checkpoint library**
The collection of saved checkpoints. Preview inspects a destination. Selection makes it the target of a later action.

## Recording and laps

**SDK telemetry**
Read-only measurements exposed by LMU, including simulation time, pose, speed, gear, and tyre readings. [Telemetry recorder](docs/TELEMETRY_TIMELINE.md)

**Recording**
A time-ordered collection of telemetry retained during driving. A segment is a continuous portion of that recording with a consistent session and vehicle identity.

**Recorded frame**
One retained telemetry sample. Selecting a frame on a recorded route can supply a destination for a teleport.

**Recorded lap**
A continuous portion of a recording between two forward start/finish crossings. Its lap number is relative to the recording.

**Opening run**
The continuous portion before the first forward start/finish crossing in a recording.

## Placement and timing

**Teleport**
A request to move the car to a selected checkpoint or recorded frame. Standing teleport requests a stationary arrival. Speed-and-gear teleport requests restoration of eligible recorded motion. [Placement and rules](docs/PLACEMENT.md)

**Garage placement**
Placing the car at the selected destination using LMU's Drive handoff. The Drive handoff is the transition from the garage to a driveable car.

**Direct reset**
An on-track teleport while the player controls the car. It can serve either a standing or an eligible speed-and-gear request.

**Rolling start**
A placement outcome in which the driver takes over at the destination with target speed and gear.

**Calibration**
Measured offsets for achieving the intended resting pose in a car and track combination. A calibration can carry game-build provenance.

**Learned rest**
Historical corrections derived from accepted stationary placement outcomes for a build, track, car, and checkpoint. Automatic learning has been retired. [Storage map](docs/ARCHITECTURE.md#workspace-storage-and-build)

**Spot table**
An LMU table of placement positions and orientations. A spot entry is one destination in that table. A car slot is the game's index for a car.

**Arrival**
The car's state when control passes to the player. The rest position is its position after the subsequent settling interval.

**Pit state**
The car's current pit procedure. The pit flag is a separate lap-counting field associated with pit exit and out-lap demotion.

**Pit-speeding penalty**
A penalty for exceeding the pit lane speed limit. The pit limiter is the driver's speed-limiting control.

**Flag Rules**
An LMU rule setting that affects checks such as pit speeding and track limits. Track limits govern whether the car remains within the permitted circuit boundaries and can affect lap validity.

**Read-live rule**
A rule whose setting the game evaluates during the session. An expanded-once rule is translated into separate runtime state when the session initializes.

**Sector**
One timing section of a lap. The current sector is the section LMU considers active for lap counting.

**Out-lap demotion**
An accepted lap crossing that starts the next lap without awarding a time for the completed lap because of pit-related state.

**Lap repair**
Re-establishing the timing state needed for a clean next lap after repositioning the car. The incomplete lap created by a teleport is an untimed lap.

## Tyres

**Tyre preparation**
Applying a requested condition and optional initial temperature to each fitted tyre during the garage flow. [Tyres](docs/TYRES.md)

**Tyre condition**
The remaining condition requested in the preparation controls. Raw wear is a separate SDK measurement expressed as a fraction.

**Compound**
The fitted tyre specification. Temperature channels describe physical regions or layers within each tyre.

## Build compatibility

**Game build**
A particular LMU executable identified by its full SHA-256. Different executable builds can have the same displayed version. [Architecture](docs/ARCHITECTURE.md)

**Automatic discovery**
Deriving build-specific information from the locally installed game. An offset profile is the resulting build-specific description of the locations and layouts Inactive Reset uses.

**Reset layout**
The verified description of the game state required for direct reset and motion restoration on a build. Live-validation evidence records which scenarios were actually exercised on that build.

**Gate**
An enforced prerequisite for a capability, such as a supported game build or an active local Practice session.
