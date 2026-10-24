# Circuit maps and checkpoint library

The Redline workspace reads layout geometry from LMU's own read-only track-map
API. It does not ship third-party circuit drawings or use illustrative marker
positions. The installed menu's `getTrackMap` helper supplies the route; missing
map support leaves the checkpoint lists available.

## Choosing a layout

The library groups named checkpoints by their recorded track name. Quick capture
slots remain in Practice tools, where **Save as named** makes a permanent copy
of the saved capture, including its original pose, speed, gear and other metadata.

Choose **Map layout** when the recorded track cannot be matched uniquely to the
installed or cached catalog. Different event configurations can share a circuit
name, so the app does not guess between them. This choice affects only the map;
it does not load a track or change the checkpoint's placement eligibility.

Map markers and library rows highlight a checkpoint for inspection. **Select
checkpoint** changes the active destination and opens Practice tools. Selection
does not teleport the car. A destination must match the loaded track; speed and
gear restoration additionally require the capture car and an eligible capture.

## Geometry and offline browsing

LMU supplies world-coordinate waypoints. Type 0 is the circuit outline and type
1 is the pit lane. The renderer fits these points into one view using world Z
as horizontal and world X as vertical. Checkpoint positions use the same
transform, without snapping them to the circuit or inventing a lap-distance
position. Points without coordinates remain available in the list.

Successful responses are cached by exact catalog layout ID under
`data/circuit-maps/`. Cached layout metadata is available after LMU closes.
**Refresh map** requests new geometry; a failed refresh retains a valid saved map
and labels it accordingly. Refresh after installing changed track content.
No fixed compass direction or game-menu rotation is implied by the display.

The route and coordinate fields were checked against the installed menu package.
Read-only live requests on 2026-10-24 fetched, validated and cached Portimao
(1,424 waypoints), Barcelona (1,502) and Monza (1,560). LMU also returns additional
waypoint types, including values through 149 in these responses. They are checked
for valid coordinates and retained; the renderer uses only types 0 and 1.
The previous validator incorrectly refused types above 2. Automated HTTP and
WebView2 tests cover validation, caching, coordinate projection and preview/
selection behavior. Marker alignment with the driven car remains unverified.

## File management

Rename and permanent-copy operations preserve the complete capture file and
refuse overwriting another checkpoint. Renaming Quick capture is refused; use
**Save as named** to retain the replaceable quick slot.
Learned placement corrections are keyed by checkpoint name. Renaming or copying
does not migrate that separate history; existing learned files are retained.

Delete moves a capture into `data/checkpoints/.deleted/` with a non-JSON extension,
so it is excluded from the library. **Undo delete** restores the latest deletion
in this app instance, provided its original filename is still free and the
deleted content is unchanged. Earlier deleted files are retained for manual
recovery. Undo is not available after restarting the app. File actions are
blocked during a placement or tyre operation.
