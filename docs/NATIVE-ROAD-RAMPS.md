# Native Build 42 road ramps

Implementation contract for the KnoxMap transport adaptation. This document does not establish a client driving pass.

- Existing `LinearFeature.Access` values `none` and `stairs` retain their behavior. New `ramps` is explicit and optional.
- First profile: straight cardinal road/bridge, exactly two integer points, width 1–16 whole tiles, peak floor +1 or +2. The points describe the complete ground approach, raised deck and descent. Each end needs 20 cells per storey; retain at least two flat deck cells between them.
- One planner emits integer segments 1–20 for each climb/storey, using native `ramps_01` starts N=0, S=24, W=48, E=72. Segment 20 remains a 95–100% slope at its lower integer Z; the next square is a flat floor at Z+1.
- Both full-width approaches are planned before world-section clipping. Local and shared paths use the same profile and tile identities. Sparse section gaps and all budgets remain checked.
- A ramp is the structural floor on its square. The export must not leave an earlier ordinary `solidfloor` object beneath it in the same native square: the game's `IsoGridSquare.getFloor()` chooses the first one.
- Approaches reject obstructed/edited ground and water landings atomically. The raised deck keeps the road below; support piers avoid its traffic corridor. Guard rails protect the flat deck's sides and leave its approach joins open. Flat fences are not floated over inclined surfaces.
- Diagonal/curved or insufficient approaches, negative levels, non-road transport profiles and slope artwork are reported as unsupported. No unsupported case silently becomes a ground crossing.
- Ramp properties/textures come from the local game installation, never from redistributed game artwork. Adapted index/direction conventions retain the KnoxMap MIT notice.

Acceptance: geometry continuity in all cardinal directions and both supported heights; full-width lanes; local/shared equality and a 255/256 seam; obstruction/unsupported-case regression tests; independent LOT readback; native game `getFloor`, slope min/max/direction and edge-height checks. Pedestrian and vehicle traversal in an isolated client save remains a distinct final check.
