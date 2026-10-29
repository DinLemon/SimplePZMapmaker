# KnoxMap adaptations

Upstream: [spytheeuclidean-a11y/knoxmap](https://github.com/spytheeuclidean-a11y/knoxmap), inspected commit `5d8195f94f751264d3b4fd6b0098e6e3dcf3df64`.

KnoxMap is not uniformly licensed. [LICENSES.md at the inspected commit](https://github.com/spytheeuclidean-a11y/knoxmap/blob/5d8195f94f751264d3b4fd6b0098e6e3dcf3df64/LICENSES.md) assigns MIT to added fork work, excludes listed original Knoxify files that lack a published licence, and separately assigns GPL-2.0-or-later to the WorldEd subtree. Our adaptation does not copy the excluded original files. Game assets remain loaded from the user's installed game.

## Adapted modules

- `generator/rendering/gridangle.py` (upstream blob `8a357267b3c1fe365eb43f5288c4d2c03e6128cd`) → `src/SimplePZMapmaker.Core/GeographicImporter.StreetGrid.cs`: fourfold directional mean, 400 m district votes and 12-degree clustering. C# adaptation clips source segments to the requested bounds, splits them at district boundaries, supplies our projection's rotation convention, and refuses insufficient/weak evidence. No rotation is automatically applied to a saved world.
- `generator/overture.py`: source for the optional gap-fill design; the adapter retains original Overture source identifiers and geometry and integrates with our existing source composition rather than adopting negative synthetic OSM identifiers or requiring DuckDB at application startup. Exact implementation status is recorded in `docs/KNOXMAP-PIPELINE.md`.
- `generator/structures.py` (upstream blob `fdc048546fc34af60fb51cd37f4913f718357e6d`): native ramp direction/index convention and the 20-cell-per-storey concept inform `RoadRampGeometry.cs`. The C# planner uses explicit integer segments 1–20, shared local/world geometry and verified native floor ordering; it does not copy upstream half-step quantisation or treat segment 20 as a flat monument block. Native physics properties and images are read from the installed game.
- `generator/rendering/widths.py` (upstream blob `4aaecdb206ab0d3ad27a3ecf33d512fef5631296`) → `RoadWidthPolicy.cs`: physical width/estimate/lane/class hierarchy and lane-width constants. The adapter validates units and finite values, keeps metre and tile decisions separate, supports directional lane totals, and shares the decision between imported paths and roadside placement. Sidewalk/verge and marking generation remain separate tracked work.

Copyright (c) 2026 spytheeuclidean-a11y. Full MIT copyright, permission and warranty notice: [KNOXMAP-MIT.txt](KNOXMAP-MIT.txt). Keep it with distributed copies of these adaptations. SimplePZMapmaker's existing GPL licence remains applicable to the combined application.

OSM and Overture data attribution is independent of the code licence. Generated maps must retain the source credits provided by the selected data; a code licence does not license photographs or game artwork.
