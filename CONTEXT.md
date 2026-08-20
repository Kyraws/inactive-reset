# Inactive Reset

An external practice tool for Le Mans Ultimate. It puts the car back on a
recorded point of the racing line, and switches off the penalties that make
practising from there tedious.

This glossary exists because several concepts in this project share spoken
names with things they are not. Where two terms have been confused in practice,
both are defined here and the wrong one is marked.

## Placing the car

**Placement**:
One complete run of plan, arm, write, Drive, restore. The engine does the
placing; the tool only changes where the engine thinks the pit spot is.
_Avoid_: teleport, reset, respawn

**Checkpoint**:
A recorded point on the racing line that a placement can target, captured for
one track and one vehicle.
_Avoid_: waypoint, marker, save point

**Calibration**:
The measured constants describing where a vehicle comes to rest relative to its
spot entry. Specific to a track and a vehicle together, never to one alone.
_Avoid_: profile, config, tuning

**Spot table**:
The engine's table of pit and garage positions. A placement overwrites one
entry, lets the engine read it, and puts the original bytes back.
_Avoid_: pit table, position table

**Slot**:
The engine's index for one car in a session. The player's slot is whichever the
engine reports about itself, not necessarily the first.
_Avoid_: car index, vehicle id, seat

**Arrival**:
The state of the vehicle at the instant control passes to the player. Sampled
before the car can move, so it describes what the engine produced rather than
what the driver did.
_Avoid_: landing, result, final position

## Penalties and lap timing

**Pit-speeding penalty**:
The stop/go the game issues for exceeding the pit-lane speed limit. This is what
the tool switches off. It is a game rule, not a car control.
_Avoid_: pit limiter, pit limiter penalty, speeding flag

**Pit limiter**:
The car's own speed limiter, which the driver engages from the cockpit. **This
tool never touches it.** Listed here only because it has been confused with the
pit-speeding penalty, which the tool does change.

**Track limits**:
The rule that penalises leaving the track and invalidates the lap. Re-arms
itself whenever a session initialises, including on a return to the garage.
_Avoid_: off-track penalty, cut rule

**Pit state**:
The condition in which the engine treats the car as engaged in pit-lane
procedure. Clears with distance travelled, not with time.
_Avoid_: pit flag, in pits

**Out-lap demotion**:
The rule that lets a lap be counted but not timed. A placement clears the field
that feeds it, so the first lap after a placement is timed.
_Avoid_: pit flag, lap invalidation

**Sector**:
Which part of the lap the engine believes the car is in. A start/finish crossing
is only accepted as a lap completion from the final sector, so a placement
restores it.
_Avoid_: split, segment

**Rule consumption**:
How the engine reads a rule, and therefore whether writing it does anything.
The single most important distinction in this project.

- **Read-live** — read on every evaluation, so a write applies at once.
- **Expanded-once** — read at session start and exploded into derived flags. A
  write to the setting does nothing mid-session; the derived flags must be
  written instead.

_Avoid_: rule type, setting kind

## Connecting to the game

**Build**:
One released version of the game executable, identified by the hash of that
executable. Never by its version number, which does not always change when the
build does.
_Avoid_: version, patch, release

**Offset profile**:
The addresses for one build, held as data rather than code. An address the
profile cannot vouch for refuses to be used rather than reading plausible
nonsense.
_Avoid_: address map, offsets file, symbols

**Gate**:
A check that must pass before the tool touches the game. A failed gate stops the
operation; it never downgrades it to a guess.
_Avoid_: guard, precondition, validation
