# Pilna verification — 0.3, 2026-10-12

This dated record describes the saved 0.3 example, not current overall test counts.

Real Overpass data contained 2,378 features, 1,789 building outlines and 439 road features. A later repeated download returned 504; the committed source JSON permits offline regeneration.

The 2048×2048 map has 1,577 buildings. Of 212 skipped outlines, 59 conflicted with water/roads, 103 crossed the then-unsupported cell boundary, 49 overlapped and one was oversized. Bounds cover roughly 3.7×4.7 km at 2.28995 m/cell.1,470 outlines became free-form cell footprints;107 used adapted rectangles. Geography/interiors are not an exact reconstruction.

Types:1,487 detached homes, 46 residential blocks, 22 shops, 11 garages, three schools, three warehouses, two hospitals, two industrial buildings and one hotel.1,517 buildings have one level, 52 have two and eight have three.42 level counts came from OSM, one from a height estimate and 1,534 from the generator; the UI distinguishes these origins. Unusual source roofs were adapted with notes.

The independent reader checked 64 native cells, 65,536 chunks, 7,345 rooms and 1,577 buildings. All 68 tile names existed in the installed game. Complete bed pairs and furniture orientations matched Facing/SpriteGridPos. The ZIP downloaded through the published UI was rechecked.

The installed game's loaders accepted 64 native cells, 65,536 chunks, 7,345 rooms, 776 upstairs rooms and 204 stair sprites recognized by `HasStairs()`. This checks files and selected objects, not character traversal. The game installation and saves were not changed.

At that stage, 41/41 core and 19/19 editor tests passed. Chromium checked opening Pilna, migration of old autosaves, 2048-map recovery after reload, session isolation, full search bounds, saving, reopening and export. Separate checks covered multi-level editing and preservation of manual changes/templates. No uncaught browser errors occurred.

Character traversal, roof/furniture appearance, loot and multiplayer remain unverified. Isometry is schematic and a railway layer is absent. ZIP semantic content is repeatable for the same source/recipe, while entry timestamps can differ.
