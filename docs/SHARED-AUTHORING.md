# Shared authoring workflow

## Settlement generation

**Tools → Generation studio** is the settlement workflow: select/draw an area, choose constraints and a placement strategy, preview up to two variants, then apply one as a single undoable operation. Strategies are contextual road frontage and regional building templates with parcels. Template constraints include layout, building budget, spacing and floor range. Source buildings, pinned buildings, protected areas, existing resources, infrastructure and authored ground remain protected. Apply uses a server-held proposal and checks the original map/world fingerprint. Complete validated road runs are retained between parcels; reservations stay separate from authored terrain masks, including underground stair shafts. The planner stops at remaining section building/floor budgets with a warning. Proposals accept the same 128 MB project limit, within a bounded 256 MiB server cache.

Creating a new town now creates its terrain and road network and opens the studio. It does not silently populate buildings. `MapGenerator`'s legacy preset remains available to existing API/CLI callers and old scenes; it is absent from the main creation form. The neighborhood MCP methods remain compatibility entry points into the same template planner.

## Parcels and fences

`ParcelPlacement` supplies common occupancy and pedestrian reservations to the neighborhood planner, street ensembles, contextual generation, OSM street dressing and street automapping. It includes complete building/decoration/model footprints, fence pieces, sites, infrastructure, manually assigned surfaces and protected ground. Optional props preserve bounded entrance routes. These reservations are placement constraints, not a claim that every route in a large world has been exhaustively audited.

New parcel and ensemble fences are cardinal `FenceLine` runs in the native fence layer. Their directional styles and resource ownership travel with their proposals; conflicting style IDs reject the entire apply. Open entrance gaps remain free. Existing fence decorations are left unchanged; no tile-name-based migration runs when opening old projects. Current format promotion remains automatic (map 12 for fences; higher versions retained for other authoring features).

## Export and building editing

`NativeExportMetadata` owns zone, spawn, atlas, thumbnail, mod/map metadata and attribution serialization for standalone, loaded-world and streamed-world export. Readers retain ownership of section leases, native-cell caching, cancellation and output streams. Streamed worlds still read sections sequentially; selected-section export does not read unrelated sections.

`building-edit-operations.js` owns detached edit transactions and common floor/room/edge/object/model operations. Both the standalone workshop and direct map editor use it. Workshop drafts retain their separate history; world/project revisions, resource validation and history commits remain host responsibilities. Failed structural edits leave the source and history unchanged.

## External tools

- **Tools → Street automapping**: select up to 256×256 tiles, preview curbs/drains/markings/parking and apply through map history. Catalog JSON updates retain catalog identity, increase revision and are validated before use. Rules never execute Lua. See [street catalog](STREET-CATALOG.md).
- **Import → BuildingEd / TileZed exchange**: import supported TBX/TMX content into a separate workshop draft or export the selected building. The workshop also exposes BuildingEd / TileZed formats beside JSON template export. Unsupported content is reported and blocks conversion until omission is explicitly allowed. No external files or URLs are resolved. See [supported subset](BUILDING-EXCHANGE.md).
- **Tools → Project Doctor**: one report for structure, resources, access, zones, seams and cell conflicts, with scope/coverage shown. Compare installed or uploaded tile-definition snapshots in the same panel: original numeric slots, properties and unresolved snow/burnt replacements. See [coverage and limits](PROJECT-DOCTOR.md).

The street mappings come from the pinned Unjammer sources [curbs](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/TileZed/lua/tool-curb-data.lua), [parking](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/TileZed/lua/parking-stall.lua) and [street decoration](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/TileZed/lua/tool-street-decoration-data.lua). The diagnostic approach follows [Project Doctor](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/docs/PZ-Project-Doctor-Tiles-and-Paths.md) and [TileDef comparison](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/docs/PZ-TileDef-Comparator-and-Snow-Editor.md). Source commit and GPL attribution are retained with the catalog. Existing room, wall, fence, furniture and template catalogs are reused.

## Verification

`scripts/verify-shared-authoring.mjs` uses an isolated browser context and workspace. It verifies template preview/apply/undo/redo, native fence persistence, street preview/apply/undo, live Doctor checks and TBX import into a separate draft without modifying the map. Its screenshots and JSON proof are written under `work/shared-authoring-verification/`. It never uses the user's active browser map or real Zomboid save directory.

Final checks: **504/504 C#**, **344/344 Node**, Release solution **0 warnings / 0 errors**. Both the new authoring browser scenario and the existing frontend consolidation regression suite pass in real Chrome with installed Build 42 resources. CLI exchange, tile comparison and Doctor were also exercised. The editor runs the resulting Release build at its existing local address. Changes remain in the working tree for review; no new commit, push or GitHub release was created for this work.
