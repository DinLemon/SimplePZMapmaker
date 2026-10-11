# Verification status — 2026-10-12

- Core regression suite: 14/14 passed (generation, invalid projects, geography, holes, clipping, full native binary readback, room/building references, navigation, nonzero world offsets, worldmap cell size).
- Editor regression suite: 5/5 passed (brush, roads, collisions, undo/redo and validation).
- Actual headless Chromium against the running server: blank map generation; mouse-driven road, building, spawn and zombie edits; undo/redo; project download and reopen; mod ZIP download; GeoJSON file import; isometric overview; narrow desktop viewport; no uncaught browser errors. Invalid project and cross-origin writes rejected.
- Nominatim address search verified against the real public service.
- Live Overpass import is being checked: first public node returned 504; alternative private.coffee returned 500. These are not import successes. GeoJSON and captured Overpass fixtures are independent of service availability.
- Build 42 installed at `./game-installation`; native exporter reference matches 256-cell / 8-chunk format.
- Actual in-game playthrough, roof visibility, loot behavior and multiplayer compatibility: **not yet verified**. Do not claim them as passing.
- Current catalog is deliberately one storey. Roads and buildings follow a square grid; heightfields are not implemented.

All research tools, downloaded QA browser, temporary files, caches, logs and checks live beneath the repository's `work/`. No game files are installed or altered by development.
