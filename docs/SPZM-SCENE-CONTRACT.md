# SPZM editor and streaming integration contract

This document extends the canonical [real-place transfer contract, version 1](TRANSFER-CONTRACT.md) with editor zones and section streaming. That contract defines source projection, stable identities, transfer ownership, fence/gate DTOs and appearance tags. This document does not redefine them. Existing map/world JSON remains readable; required feature versions must fail explicitly instead of silently discarding objects.

## Ownership and compatibility

- Core/import/export developer: geographic rotation; fence-line domain and native gates/corners; small/attached structures; appearance tags; tile-pack compilation and resource metadata; mod packaging/installation; headless scene CLI; installed-game validation.
- Editor/streaming developer: section workspace storage, bounded loading, revision-safe editing and streamed export; layer visibility/locking and section/seam presentation; interactive adapters to the agreed core tools; zone geometry, density authoring, room-rule previews and UI; documentation/localization.
- Each changes feature files on a branch, adds focused regressions and integrates via small changes against current `develop`. Additions to MapProject/WorldProject, validators and native writers must be coordinated. Existing RequiredMods/AssetSources, geographic evidence and original source geometry must survive every path.

## Coordinates and identities

Map-local X/Y are tiles, X east and Y south before any optional geographic rotation. A native cell is 256 tiles. World X/Y for an object equal local X/Y plus section WorldX/WorldY multiplied by 256. Z uses the existing signed range -32 through 31. SourceGeometry always contains actual WGS84 longitude/latitude. Never encode rotated pseudo-longitude/latitude as original source data.

`WorldGeoReference.RotationDegrees` is implemented: finite **−45…45**, default zero. Positive angles turn content clockwise in the east/south metric plane. The pivot is fixed at the centre of `Bounds`; the common origin is anchored to the minimum rotated bounds corner. Required extents and inverse section queries use all four corners. Follow [the canonical formulas and examples](TRANSFER-CONTRACT.md#1-coordinates), including the −11.22° Pilna reference, rather than introducing another pivot or a broader angle range.

Stable IDs identify one authored/source object independent of section clipping. Generated object/section identities follow [the canonical hashing rules](TRANSFER-CONTRACT.md#2-stable-identities); editor-authored objects may use random GUIDs. Crossing objects are stored once at world level; projections clip them for preview/export. Copies receive new authored IDs without claiming duplicate real-world identity.

## Fence and small-building adapters

The implemented fence edge layer uses `Fences`, `FenceStyles` and `GateStyles` on map/world projects, with the exact [canonical FenceLine/FenceStyle/GateStyle definitions](TRANSFER-CONTRACT.md#4-fences-and-gates). A line references a style; it does not carry the earlier proposed inline `tiles` object. Gates use `At`, `Style`, `OpensTo` and `Open`; posts use `PostSpacing`. World lines use global tile coordinates. Native edge occupancy is independent of ordinary decorations, allowing a fence and a plant to share a cell.

Fence authoring requires map format **12** and world format **7**; nonzero geographic rotation also requires world format **7**. A browser adapter must commit the actual fence layer and retain its resource bindings, source paths and gate styles. It must never approximate fences as ordinary decorations. Unsupported geometry or unavailable adapter capabilities remain explicit drafts.

Small-building drawing records the exact requested footprint and must not silently enlarge a 2×2 shed. Apply is enabled only for the active adapter's supported size and shared-wall behavior. The current general building adapter supports 6–64 tiles per side; the [canonical small/attached building extension](TRANSFER-CONTRACT.md#6-small-and-attached-buildings) must not be advertised as implemented merely because its schema is documented.

## Source appearance tags

Use [the canonical appearance tag schema](TRANSFER-CONTRACT.md#5-building-appearance-tags): `pz:wall`, `pz:trim`, `pz:style`, `pz:kind`, standard `roof:shape` and `roof:colour`. The earlier draft `pz:roof`/`pz:roof:color` names are not substitute import keys. Source strings remain separate from resolved game resources. Resolved tiles require provider bindings and mod dependencies; missing resources are actionable errors. Trim uses the existing `WallOverlay` edge field. Preserve-edits reimport retains manual material choices, MultiPolygon holes and part identities.

## Zones (editor developer owns implementation)

Rectangular GameZone remains the compatibility default. Optional Polygon uses section-local integer tile vertices, one simple outer ring, no self-intersection; bounding X/Y/Width/Height must exactly enclose the polygon. Vehicle polygons are validated against their actual covered tiles and resource distribution. The native exporter decomposes polygons to deterministic non-overlapping tile rectangles so it does not rely on unverified native polygon behavior.

Zombie density regions are separate authored records with stable ID/name, polygon/rectangle, Density 0..255 and deterministic ordered precedence. The existing 8×8 zombie raster remains the native export representation. Preserve a baseline or reversible ownership before applying regions so editing/removing a region does not destroy independent raster edits. Coverage/quantization is visible in preview.

Room/loot tag authoring targets existing room names/distributions through registered catalogs. A region cannot invent a native loot-zone type. Room name changes are reviewed as whole-room changes with an explicit selection rule; unsupported tags remain source evidence. Scene tags proposed for CLI ingestion: `pz:zone:type`, `pz:zone:name`, `pz:vehicle:distribution`, `pz:zombie:density`, `pz:room`. The import developer maps them into the published zone DTOs after these validators ship.

The canonical transfer contract's section 7 describes survey/import tag mapping (`pz:zone`, `pz:vehicles`, `pz:direction`) and its proposed bounded import decomposition. Those keys are not aliases for every authoring API field. The editor's optional polygon records retain the full accepted outline and native export preserves its complete covered-tile set. Its exporter does **not** truncate an authored polygon after 64 rectangles. Import pipelines must report any unplaced remainder as source evidence rather than imply that the editor/exporter accepted and then discarded it. Actual DTOs, coverage limits and API operations are detailed in [zone authoring](ZONE-AUTHORING.md).

## Section workspaces

The existing SimplePZWorldSections index stays a small manifest referencing immutable content-hashed section blobs and shared metadata. Revision/hash checks guard every write. World aggregate raster size must not force all sections into RAM; section/blob and shared-metadata budgets remain explicit. Opening a section loads only it and the minimal shared projections/context required to validate/edit it. Unloaded neighbours stay manifest entries, never empty invented maps. A stale save or cancellation cannot replace the authoritative manifest.

Export pins a manifest revision and its immutable blobs, validates and emits one section at a time, retains dependency/provider/attribution information, and includes a reconstructible authoring index with per-section source files. A shared world path/decor/fence uses authoritative geometry before clipping; unsupported geometry must fail visibly. Saved-workspace export jobs must not call ReadWorld or serialize a monolithic WorldProject first. Progress/cancellation and resource checks remain available. Compatibility full-world JSON operations may keep their existing explicit size budget.
