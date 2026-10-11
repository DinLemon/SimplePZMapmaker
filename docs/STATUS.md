# Verification status — 2026-10-12

- Core regression suite: 22/22 passed (generation, invalid/null projects, geography, stitched OSM relations and holes, clipping, bounded complex polygon rasterization, full native binary readback, exact water/tree/door navigation flags, room/building references, nonzero world offsets, worldmap cell size and pre-buffer upstream response limits).
- Editor regression suite: 8/8 passed (brush, roads, collisions, undo/redo including pending metadata, seed/bounds validation and perimeter parity).
- Actual headless Chromium against the running server: blank map generation; mouse-driven road, building, spawn and zombie edits; undo/redo; project download and reopen; mod ZIP download; GeoJSON file import; isometric overview; narrow desktop viewport; no uncaught browser errors. Invalid project and cross-origin writes rejected.
- Nominatim address search verified against the real public service.
- Live Overpass import: first public node returned 504; alternative private.coffee returned 500; a third node timed out. These are not import successes. GeoJSON and Overpass fixtures are independent of service availability. Application default remains the fixed original Overpass endpoint; no automatic request storm or provider bypass.
- Build 42 installed at `./game-installation`; native exporter reference matches 256-cell / 8-chunk format.
- Actual in-game playthrough, roof visibility, loot behavior and multiplayer compatibility: **not yet verified**. Do not claim them as passing.
- Current catalog is deliberately one storey. Roads and buildings follow a square grid; heightfields are not implemented.

All research tools, downloaded QA browser, temporary files, caches, logs and checks live beneath the repository's `work/`. No game files are installed or altered by development.

Published app rechecked after the review fix pass: full browser cycle and delayed-import cancellation/frozen edits pass. Explicit null nested fields now return HTTP400. Launcher checked against the current workspace with `-NoBrowser`.

Independent review: seven Important findings fixed in one consolidated pass; contract, perimeter, launcher and progress notes also resolved. No Critical findings. Regression tests were observed failing before corresponding fixes.

Performance: a full-extent 12,001-point polygon originally took 53.98s; the scanline implementation passes the 4s budget. Total raster complexity is capped at 40M estimated operations with cancellation between rows/features. A 1024-square town with 150 buildings exported in 0.87s before the final fix pass.

Game tile audit: all16 emitted names from a browser-created house map exist among29,058 names in the installed game's tile-definition text. This audit verifies naming, not playability.
