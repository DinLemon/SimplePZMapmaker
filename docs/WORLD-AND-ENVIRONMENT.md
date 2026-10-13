# World assembly, OpenStreetMap and environment editing

Open **World** in the top toolbar to display **World sections**. Add the current map or several `.pzmap.json` files. Sections use native cell origins (each native cell contains 256×256 tiles); overlap is rejected. Change coordinates or drag a section, then edit the selected section. Changes return to the world when switching sections. World-layout undo/redo is separate from the active map's history.

A saved `.pzworld.json` embeds the source projects of all sections. Building one combined mod creates common spawn points, zones and atlas. Each native cell gets its own lotheader/lotpack/chunkdata. Game sprites/models stay in the game or required mods. Export validates resource ownership, dependencies and overlaps. A water section may omit a spawn if another section contains a valid land spawn.

The last world replacement remains undoable even if it exceeds the normal 128 MB history budget. If a replacement needs over 512 MB of history, it is rejected before mutation. Save the current world, create an empty world and open the finished result again. This is an editor memory limit; the imported result remains available.

## Size and geography

**2048×2048 tiles is the limit for one editor section, not the entire Project Zomboid world.** Up to 128 sections and 8,192 native cells are supported without creating one huge raster. World project files are limited to 128 MB and ZIPs to 256 MB; zone/atlas descriptions also have budgets.

Seam diagnostics compare roads, water and scale. Buildings may cross native cell borders (256×256 tiles per cell). In 0.7, buildings crossing independent section boundaries can be stored in the shared layer described below; they no longer need to be forced into one section.

In the world workspace, choose the OpenStreetMap world import, define bounds, metres per tile and section size, then estimate the size before downloading. Requests are split into small areas and temporary failures use backup servers. Every section shares one projection, maintaining road scale/coordinates across seams. Progress reports stages, source, data date, cache and notes. Only a completed result can replace the world; errors/cancellation retain the current project. One import is limited to 768 cells and 15 minutes.

Preserving manual edits on reimport requires the same bounds, scale and layout. Manual buildings, props, models, surfaces, appearance regions, spawns, zombie density and painted terrain are retained. Older projects without a manual-terrain mask cannot reconstruct that mask; the report explains it. OSM completeness limits the result; unsupported geometry and boundary cases are reported.

## Environment editor

The local environment workshop displays an area with actual sprites. Choose a sprite, surface or static model; the brush previews the complete object. It supports ground/path painting, rectangular appearance regions, single/group selection, moving, removal and undo. Sprite rotation uses actual N/W variants; missing directions are reported. Models have arbitrary rotation/scale. Apply transfers one undoable change; closing without applying retains the original map.

In 0.7, model behavior is chosen separately: pickup item, non-pickup scenery, obstacle, destructible or container. A blocking profile occupies one game tile regardless of visible scale. See [Static models](STATIC-MODELS.md). Saved projects require the original resources/mods; a changed resource owner is diagnosed.

## Shared buildings and elevation (0.7)

**Shared buildings and geography** promotes section buildings, imports templates, edits coordinates and opens the normal visual building workshop. A building is stored once with absolute X/Y. Export clips rooms, walls, roofs and sprites by section/native cell and emits models once. Validation checks coverage by adjacent sections, water, overlaps and access. Moving/deleting a section cannot strand a shared building outside coverage. The section editor displays shared buildings and protects them from local objects/water.

The seam-connection operation joins compatible roads/rivers up to three tiles on each side. Objects, explicit surfaces and manually marked terrain are protected. Water opposite a road requires a manual bridge; scale differences require correction. World history can undo the operation.

DEM comes from [Terrain Tiles](https://registry.opendata.aws/terrain-tiles/) or ESRI ASCII `.asc` in EPSG:4326/3857. It preserves actual metres, NODATA, coordinate system, source, license and import time. Import time is not measurement time. Limits: one million samples, two-minute network budget and seven-day cache. The colored overlay helps planning; `elevation.asc` is included in the ZIP. **Native PZ ground stays flat**; DEM is not converted to levels or invented slopes.

`examples/Worlds/World-DEM.pzworld.json` contains two sections, a shared house/cellar, authored roads/river and measured DEM near Smolensk. The layout is authored, not an OSM reconstruction. World 2 protects the new layers from older editors.

Recovery-version controls show current and three previous map/world snapshots. Bounded streaming writes preserve a previous version before atomic replacement. A damaged current snapshot falls back to the first valid backup. Large world overviews remain sparse; brush edits refresh affected raster blocks, and off-screen buildings are skipped.
