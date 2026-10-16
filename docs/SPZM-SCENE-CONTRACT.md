# SPZM scene and editor integration contract, draft 1

This describes the shared boundary between the core/import/export developer and the editor/streaming developer. Existing map/world JSON remains readable. Fields described as reserved are integration proposals, not claims that native export already supports them. Unknown required feature versions must fail explicitly instead of silently discarding objects.

## Ownership and compatibility

- Core/import/export developer: geographic rotation; fence-line domain and native gates/corners; small/attached structures; appearance tags; tile-pack compilation and resource metadata; mod packaging/installation; headless scene CLI; installed-game validation.
- Editor/streaming developer: section workspace storage, bounded loading, revision-safe editing and streamed export; layer visibility/locking and section/seam presentation; interactive adapters to the agreed core tools; zone geometry, density authoring, room-rule previews and UI; documentation/localization.
- Each changes feature files on a branch, adds focused regressions and integrates via small changes against current `develop`. Additions to MapProject/WorldProject, validators and native writers must be coordinated. Existing RequiredMods/AssetSources, geographic evidence and original source geometry must survive every path.

## Coordinates and identities

Map-local X/Y are tiles, X east and Y south before any optional geographic rotation. A native cell is 256 tiles. World X/Y for an object equal local X/Y plus section WorldX/WorldY multiplied by 256. Z uses the existing signed range -32 through 31. SourceGeometry always contains actual WGS84 longitude/latitude. Never encode rotated pseudo-longitude/latitude as original source data.

Reserved rotation extension: WorldGeoReference.RotationDegrees defaults to zero, finite in [-180,180]; positive is clockwise in game X-east/Y-south coordinates. Rotation is applied once to the common metric frame about an explicit geographic pivot; default pivot is the unrotated bounds centre. RequiredTiles, SectionBounds and inverse Geographic must use all four transformed corners and the same shared origin. The core implementation must pin pivot/origin semantics with round-trip and adjacent-section fixtures before UI consumes it.

Stable IDs identify one authored/source object independent of section clipping. A crossing object is stored once in world metadata; section previews/exports receive clipped projections. SourceId/provider IDs and authored object IDs are different concepts. Copies get new authored IDs; they do not claim a duplicate real-world identity.

## Fence and small-building adapters (reserved until core capability is available)

Fence data is an edge layer, not ordinary single-cell WorldDecoration. Proposed fence command: `{id, points:[{x,y}], level:0, tiles:{north,west,northWest,post}, postEvery, gates:[{id,at,direction,kind,opensInto}], requiredMods, assetSources}`. Gate `at` is distance in tiles along the canonical sampled path; a gate has a stable ID, N/W edge and explicitly resolved inward side. Shared crossing fences retain world coordinates and one ID. Existing bush/prop occupancy must not forbid an independent wall edge. Core chooses and publishes the final DTO and capability name before editor can commit these commands.

Small-building drawing records the exact requested footprint. It must not silently enlarge a 2×2 shed into a larger building. Apply is enabled only when the active core advertises the supported footprint range and shared-wall behavior. Existing larger-building workflows remain available.

## Source appearance tags

Reserved `pz:wall`, `pz:trim`, `pz:style`, `pz:roof` and `pz:roof:color` retain source strings separately from resolved game resources. Standard roof:shape remains independent. Resolved tiles require provider bindings and mod dependencies; missing resources are actionable errors. Trim uses the existing WallOverlay edge field. Source tags must not overwrite manual material choices during preserve-edits reimport. MultiPolygon/OSM relation support already exists and must retain holes and part identities.

## Zones (editor developer owns implementation)

Rectangular GameZone remains the compatibility default. Optional Polygon uses section-local integer tile vertices, one simple outer ring, no self-intersection; bounding X/Y/Width/Height must exactly enclose the polygon. Vehicle polygons are validated against their actual covered tiles and resource distribution. The native exporter decomposes polygons to deterministic non-overlapping tile rectangles so it does not rely on unverified native polygon behavior.

Zombie density regions are separate authored records with stable ID/name, polygon/rectangle, Density 0..255 and deterministic ordered precedence. The existing 8×8 zombie raster remains the native export representation. Preserve a baseline or reversible ownership before applying regions so editing/removing a region does not destroy independent raster edits. Coverage/quantization is visible in preview.

Room/loot tag authoring targets existing room names/distributions through registered catalogs. A region cannot invent a native loot-zone type. Room name changes are reviewed as whole-room changes with an explicit selection rule; unsupported tags remain source evidence. Scene tags proposed for CLI ingestion: `pz:zone:type`, `pz:zone:name`, `pz:vehicle:distribution`, `pz:zombie:density`, `pz:room`. The import developer maps them into the published zone DTOs after these validators ship.

## Section workspaces

The existing SimplePZWorldSections index stays a small manifest referencing immutable content-hashed section blobs and shared metadata. Revision/hash checks guard every write. World aggregate raster size must not force all sections into RAM; section/blob and shared-metadata budgets remain explicit. Opening a section loads only it and the minimal shared projections/context required to validate/edit it. Unloaded neighbours stay manifest entries, never empty invented maps. A stale save or cancellation cannot replace the authoritative manifest.

Export pins a manifest revision and its immutable blobs, validates and emits one section at a time, retains dependency/provider/attribution information, and includes a reconstructible authoring index with per-section source files. A shared world path/decor/fence uses authoritative geometry before clipping; unsupported geometry must fail visibly. Saved-workspace export jobs must not call ReadWorld or serialize a monolithic WorldProject first. Progress/cancellation and resource checks remain available. Compatibility full-world JSON operations may keep their existing explicit size budget.
