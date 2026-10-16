# Zone authoring and native semantics

The zone panel edits the active map or loaded section. Rectangle files remain compatible. New optional metadata is stored with the section and participates in editor history.

## Draw, review, apply

1. Open **Zones, density and room rules**. Select a layer and enter its name.
2. Draw a rectangle or a polygon using the map tool. Polygon vertices lie on integer tile edges. Close the ring through the tool; do not repeat the first vertex in the saved array.
3. Choose the zone type, registered vehicle distribution, zombie density, or supported game room name.
4. Select **Preview changes**. Inspect the resulting outlines and every affected native density cell or whole room. Density results are paged; all entries remain available.
5. For room changes, explicitly check the review checkbox. **Apply reviewed preview** changes the map in one history operation.
6. **Cancel draft** aborts in-flight requests and clears temporary geometry. Editing or switching sections invalidates a preview; preview again before applying.

Select an existing region to change its geometry or properties. **Preview removal** uses the same review/apply flow. A selected region retains its original source tags.

## Polygon zones

`GameZone.Polygon` is an optional list of `{x,y}` integer points. Null preserves the legacy rectangle. A polygon has one simple outer ring, 3–128 distinct vertices, no crossings or backtracking edges, and a bounding area of at most 4,194,304 tiles. Its existing `x`, `y`, `width`, `height` must exactly enclose the ring.

A polygon covering no tile centres is rejected with an actionable error. A tile is covered when its centre passes the polygon's even-odd inside test. Edge/diagonal sampling is deterministic. Same-layer overlap and vehicle water/building checks use these actual covered tiles. Terrain zones and vehicle zones remain separate overlapping layers.

`objects.lua` contains deterministic, non-overlapping rectangles made from horizontal covered-tile spans. Equal spans merge vertically. Authored polygon coverage is also subtracted from automatic terrain rectangles. Concave empty areas remain available to automatic terrain zones and other non-overlapping zones. Native polygon behavior is not assumed. Vehicle spawning still uses the installed distribution and sandbox probabilities.

## Reversible density regions

`MapProject.zombieDensityRegions` contains `{id,name,x,y,width,height,polygon?,density,order,sourceTags}`. `density` is 0–255. Regions apply in ascending `order`, then ordinal stable ID; the last covering region wins at each tile centre.

Native density has one value per 8×8 tile block. A block's value is the rounded mean of its 64 tile values: selected tiles use their region's density; uncovered tiles retain the baseline raster value. A narrow region therefore affects a fraction of a native block. The preview reports coverage out of 64 and the exact before/after density.

`zombieDensityState` stores a baseline, previously applied values and manual override indices. On the next region operation, any raster value changed since region application becomes a manual override. Repeated edits and moved/removed regions preserve it. Removing every density region restores the baseline plus later independent edits and clears region ownership. A manual write equal to the previous region value cannot be distinguished numerically from no change.

The layer supports at most 256 regions and a cumulative bounding area of 16,777,216 tiles. The native output remains the existing zombie raster; these authoring records do not create a native zone type.

## Whole-room mappings and loot

`MapProject.roomRegionRules` contains `{id,name,x,y,width,height,polygon?,level,roomName,sourceId,requiredMods,selection,sourceTags}`. Supported room names come from the existing `BuildingDesigner.RoomNames` catalog, with `sourceId="game"`. The catalog endpoint exposes their existing native aliases: `hotelroom → motelroom`, `garage → garagestorage`, and `balcony → emptyoutside`.

A rule selects an existing floor room code either when **any** of its tiles intersects the region (`intersects`) or when **all** its tiles are contained (`contained`). The preview lists the building ID, level, room code, previous/new name and total tile count. Applying requires the exact reviewed list. Ambiguous rules selecting the same room fail atomically. This renames the entire room code, including disconnected pieces represented by that same native room record. Buildings need explicit floor designs for room review.

Game room names determine the existing native room/distribution behavior. This feature does not invent a native loot-zone type or bind arbitrary procedural distributions to polygon zones. Existing registered procedural container loot remains available through the model authoring tools. Removing a room rule retains the already reviewed room name; undo restores the full prior map.

## API and source evidence

- `GET /api/zones/catalog`: supported room names/native aliases, selection rules, raster size and polygon coverage convention.
- `POST /api/zones/preview`: `{project,zones?,densityRegions?,roomRules?}`. Each supplied list replaces that complete active-section layer in the proposed map. Returns `{project,rooms,density,notes}` without changing persistent state.
- `POST /api/zones/apply`: the same request plus `reviewedRooms`, copied exactly from the preview. Revalidates the full candidate, registered vehicle ownership and the reviewed room changes. The editor adopts its result as one history operation.

`GameZone`, density regions and room rules retain bounded `sourceTags`. Reserved scene keys are `pz:zone:type`, `pz:zone:name`, `pz:vehicle:distribution`, `pz:zombie:density`, and `pz:room`. Unknown source tags remain evidence. They are not silently translated into unsupported native types. Imported source geometry and provider identity remain the importer's responsibility.
