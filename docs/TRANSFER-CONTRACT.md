# Real-place transfer contract, version 1

This contract fixes the data that crosses the boundary between a real-place survey pipeline (GeoJSON produced outside the editor) and SimplePZMapmaker (import, world assembly, editing, export). Both sides implement against it; a change to any section below raises the contract version. The first consumer is the Pilna village transfer (`neovolgan/pilnamappz`), which must assemble all of its sections into one `PilnaVillage` world without losses at section seams.

| Area | Implements | Section |
|---|---|---|
| Rotated geographic reference | Claude | [1](#1-coordinates) |
| Stable identities | both (rules here) | [2](#2-stable-identities) |
| Section bounds and seam ownership | editor storage/streaming: ChatGPT; import rules: Claude | [3](#3-sections-and-seams) |
| Fence and gate layer | core: Claude; editor UI: ChatGPT | [4](#4-fences-and-gates) |
| Building appearance tags | Claude | [5](#5-building-appearance-tags) |
| Small and attached buildings | core: Claude; editor UI: ChatGPT | [6](#6-small-and-attached-buildings) |
| Game zones | data, checks, export, UI: ChatGPT; source tag mapping: agreed here | [7](#7-game-zones) |
| Versions | both | [8](#8-versions) |

Already implemented and extended rather than replaced: MultiPolygon import, retained source evidence (`GeographicFeature`), vehicle/parking zones (`GameZone`), mod dependencies (`RequiredMods`, `AssetSources`) and the installed-class game probe.

## 1. Coordinates

Source coordinates are **real WGS 84 longitude/latitude** and are stored unchanged (`SourceGeometry`, `SourcePath`, `SourceOutline`). No pipeline feeds pre-rotated or otherwise synthetic "pseudo lon/lat" into the importer; every adaptation to the game grid happens in the projection and is reproducible from the stored reference.

`WorldGeoReference` gains one field:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Bounds` | West/East/South/North | — | source clip area, unchanged |
| `MetresPerTile` | double | 1 | unchanged |
| `OriginCellX`, `OriginCellY` | int | 100 | unchanged |
| `RotationDegrees` | double, −45…45 | 0 | rotation of the local metric plane before it is laid on the tile grid |

Projection of a source point (λ, φ), with M = 111 320 m/°, φ̄ = (North + South) / 2, s = `MetresPerTile`, θ = `RotationDegrees`, and the rotation centre (λc, φc) = centre of `Bounds`:

```
e = (λ − λc) · cos φ̄ · M / s          east, tiles
n = (φc − φ) · M / s                  south, tiles (grid Y grows southwards)
x' = e · cos θ − n · sin θ
y' = e · sin θ + n · cos θ
(x0, y0) = component-wise minimum of (x', y') over the four corners of Bounds
global tile X = OriginCellX · 256 + x' − x0
global tile Y = OriginCellY · 256 + y' − y0
```

- With θ = 0 this is exactly the current projection, so existing worlds load and project unchanged.
- θ is applied in the (east, south) plane: a positive θ turns map content clockwise as drawn on screen. A street running at bearing β (degrees clockwise from north) becomes grid-aligned with θ = −(β mod 90) or 90 − (β mod 90), whichever is within ±45. Pilna: θ = −11.22.
- Because placement is anchored to the rotated bounds' minimum corner, the rotation centre affects nothing but readability; it is defined as the `Bounds` centre so all implementations agree.
- `RequiredTiles` is the extent of the rotated `Bounds` corners. `SectionBounds` (the lon/lat area a section must read) is the bounding box of its four rotated-back corners, clipped to `Bounds`; importers must still clip by tile footprint, because that box is larger than the section when θ ≠ 0.
- Section-local tile = global tile − section `WorldX/WorldY` · 256.

Reference example (Pilna: the town's residential land use plus a 100 m margin):

```json
{"Bounds": {"West": 45.8817, "East": 45.9437, "South": 55.5354, "North": 55.5790},
 "MetresPerTile": 1, "OriginCellX": 200, "OriginCellY": 200, "RotationDegrees": -11.22}
```

## 2. Stable identities

A rebuild from the same source must reproduce the same identities, so that **Preserve edits**, survey corrections and seam checks can match objects.

- **Source identity** (`SourceId`) is assigned by the pipeline as the GeoJSON feature `id` and becomes `geo-<id>` on import (existing behaviour). Formats:
  - `osm-<type>-<number>`: an OpenStreetMap object, for example `osm-way-123456`;
  - `cad-<cadastral number>`: a cadastral parcel;
  - `survey-<block>-<slug>`: an object that exists only in a field or panorama survey, for example `survey-block2-urickogo-37-gate`;
  - `gen-<parent source id>-<role>-<n>`: generated content derived from a source object, for example a shed inferred inside a parcel.

  Anonymous features keep the existing geometry/tag fingerprint (`geo-sha-…`).
- **Object identity** (`Id` of buildings, lines, zones, decorations) for generated content is the first 32 hex digits of SHA-256(`SourceId` + `"|"` + role), lowercase. Randomly generated GUIDs are allowed only for objects authored in the editor.
- **World section identity** (`WorldMapMember.Id`) is the first 32 hex digits of SHA-256(world name + `"|"` + `WorldX` + `","` + `WorldY`), so sections keep their identity across rebuilds.

## 3. Sections and seams

- A section is a world member with `WorldX`, `WorldY` in cells and `Width`, `Height` in tiles, each a multiple of 256 from 256 to 2048. Sections of different sizes may coexist; they must be cell-aligned and must not overlap (existing validation). A dense centre may use 512-tile sections next to 1024-tile ones.
- A section covers global tiles [`WorldX`·256, `WorldX`·256 + `Width`) × [`WorldY`·256, `WorldY`·256 + `Height`).
- **Ownership:** an object whose whole footprint lies inside one section belongs to that section. An object crossing a section edge belongs to the world: buildings go to `WorldProject.Buildings`, and lines (roads, fences) go to `WorldProject.LinearFeatures`, and fences go to `WorldProject.Fences`. A section never contains a clipped part of an object.
- **No silent loss.** Every imported source feature ends in exactly one of three places:
  - placed in one section;
  - placed at world level;
  - kept as evidence with a reason (`skipped: <reason>`).

  The world import report lists the counts per source kind (`placed-section`, `placed-world`, `evidence`), and the seam check fails when a feature is in none or in more than one. *Implemented:* `GeographicImporter.TransferCheck` re-reads the source and reports buildings and fences; `simplepzmap build`/`validate --source` print it and `--strict` fails on lost or duplicated features ([CLI](CLI.md#seam-check)).
- A section may be built separately; the world build reads the per-section scene plus a seam list (features that crossed its edge), deduplicates by `SourceId` and imports them once at world level.

## 4. Fences and gates

Fences are their own layer, not decorations and not `LinearFeature`s (which are limited to 64 per map). A fence edge therefore never competes with a plant, bench or lamp for a cell's single decoration slot, and a world fence is continuous across sections. `MapProject` and `WorldProject` both carry `Fences`, `FenceStyles` and `GateStyles`; world fences use global tile coordinates.

`FenceLine`:

| Field | Meaning |
|---|---|
| `Id`, `SourceId`, `SourceTags`, `SourcePath` | identity and retained source (`barrier=fence|wall|gate`, `fence_type`, `colour`, `material`; real lon/lat path) |
| `Points` | grid vertices; vertex (X, Y) is the north-west corner of cell (X, Y). Runs are horizontal or vertical along cell edges; a diagonal source line is turned into a staircase by the importer. |
| `Style` | id of a `FenceStyle` |
| `PostSpacing` | 0 = no posts; otherwise a post at every corner and every N edges |
| `Gates` | `{ "At": edges from the first point, "Style": gate style id, "OpensTo": "left" or "right" of the line direction, "Open": false }`; a gate stays on one straight run |

`FenceStyle`:
- `W`, `N`: the edge tiles;
- `NW`: optional corner tile;
- `Post`: optional post tile;
- `WAlt`, `NAlt` with `AltEvery`: optional alternative edges, used for a style that repeats every N edges, such as a brick pillar between forged sections;
- `GateSpaceW`, `GateSpaceN`: optional gate frame drawn under a gate on that edge.

The built-in styles are TileZed's reference fences (`Fences.txt`): `vanilla-planks`, `vanilla-short-planks`, `vanilla-tall-planks-2`, `vanilla-picket`, `vanilla-short-picket`, `vanilla-wire`, `vanilla-tall-wire`, `vanilla-cattle`, `vanilla-black-metal`, `vanilla-black-bars`, `vanilla-sheet` (tall concrete), `vanilla-military`, `vanilla-construction` and `vanilla-construction-barbed`. Two-piece fences alternate `west1`/`west2` with `AltEvery = 2`. A gate added without a style gets the wicket that TileZed pairs with the fence: `vanilla-wicket`, `-short`, `-picket`, `-wire` or `-metal`, each a full closed/open set.

`GateStyle`: `Width` 1–8 edges, `ClosedW`, `ClosedN` and optional `OpenW`, `OpenN`, each with one piece per edge in line direction. `OpensTo = "left"` mirrors the order. Vanilla example: `fixtures_doors_fences_01_12`…`15`, the solid wooden wicket (closed W, closed N, open W, open N). Tiles from a resource mod add that mod to `RequiredMods`. A style ID used by both the world and a section must mean the same tiles.

Rasterisation:
- a horizontal run between (x, y) and (x + 1, y) puts the `N` edge on cell (x, y);
- a vertical run between (x, y) and (x, y + 1) puts the `W` edge on cell (x, y);
- when both edges of one style are plain, the cell gets `NW`.

Edge pieces carry the native navigation edge bits (2 for N, 4 for W); gates carry none. Edges on building footprints or water are skipped and counted per reason; overlaps are counted too. A fence layer raises the map format to 12 and the world format to 7.

GeoJSON input (pipeline → importer):

```json
{"type": "Feature", "id": "cad-52:43:0300005:123-street",
 "geometry": {"type": "LineString", "coordinates": [[45.9187, 55.5532], [45.9192, 55.5533]]},
 "properties": {"barrier": "fence", "fence_type": "metal_sheet", "colour": "maroon",
                "pz:fence": "pilna_sheet_maroon", "pz:posts": "0",
                "pz:gates": "[{\"at\": 6, \"width\": 3, \"style\": \"pilna_gate_maroon\", \"opens\": \"right\"}, {\"at\": 10, \"width\": 1, \"style\": \"vanilla_wicket_wood\", \"opens\": \"right\"}]"}}
```

`pz:fence` selects the style. Without it, the importer maps `fence_type` and `colour` through the palette and falls back to a neutral vanilla fence. `pz:gates[].at` is measured in metres along the source line and converted to tiles. A standalone `barrier=gate` point lying on a fence line becomes a gate in that line.

## 5. Building appearance tags

Building feature properties read by the importer (OSM keys keep their OSM meaning):

| Key | Values | Effect |
|---|---|---|
*Implemented* for section and shared world imports. Tags are applied after the footprint is fitted, so regenerated edges never drop them; unusable values are reported once per value in the import notes.

| `building:levels` | 1–8 | storeys (existing) |
| `roof:shape` | `gabled`, `hipped`, `flat`, … | roof form (existing) |
| `roof:colour` | `grey`, `red`, `blue`, `green`, `brown`, `white`, `black`, `silver`, or `#rrggbb` | roof tile set: grey `roofs_30_04`, red `roofs_30_09`, blue `roofs_30_01`, green `roofs_30_05`, brown `roofs_30_07`, white `roofs_30_03`; a hex colour takes the nearest set by the average colour measured from the installed Build 42 sprites. Flat roofs keep their own surface. |
| `pz:wall` | `set:<first tile>`, a bare first tile, or the ID of a project palette whose first wall resource starts the set | exterior wall set, on every exterior edge except balconies. The pieces come from TileZed's wall catalog (`wall-catalog.json`, 175 sets); a set outside it uses wall `+0/+1`, window frame `+8/+9`, door frame `+10/+11` (W/N). Export draws the set's NW corner piece where plain N and W walls meet, and its SE pillar on outer corners |
| `pz:trim` | overlay tile pair `<north tile>:<west tile>` | window surround overlay on exterior window edges (wall edge overlays, PR #1) |
| `pz:style` | building style id, or `tilezed:<template>` | interior and furniture style; a TileZed template (`Bank`, `Barn`, `Diner`, `Police`, `Spiffo`, `Gigamart`… 25 in `building-templates.json`) sets the exterior wall set, door, window, and per room the floor and interior wall |
| `pz:kind` | `house`, `shed`, `garage`, `bathhouse`, `toilet`, `greenhouse` | building kind when OSM `building=*` is ambiguous; `bathhouse` and `toilet` make a one-room building a bathroom |

Unknown `pz:*` keys are kept as source tags and reported once per key as a warning; they never fail an import.

## 6. Small and attached buildings

- **Small buildings (implemented).** Any outline may be as small as 2×2 tiles; it is no longer inflated, and its footprint may hold as few as 4 cells. Under 6 tiles on a side the building is one room with a door. It keeps one floor unless its long side has at least 6 tiles for a straight stair run; otherwise source floor tags are retained but only one floor is built, and the import report says so.
- **Attached buildings (implemented).** `pz:attached = <source id>` lets a building touch the referenced building with no 1-tile gap. A typical case is a covered yard or barn joined to the house. The shared edge is a wall of the larger building (by floor area; the target on a tie); the smaller building draws nothing there, and the larger building's windows on that wall become plain wall. A door on it is added when `pz:door-to` names the other building. The smaller building keeps an exterior entrance off the shared wall. The relation is stored as `Building.AttachedTo` (map format 13) inside one section; shared world buildings cannot be attached. Wall rings of the pair may share tiles; rooms never overlap.

## 7. Game zones

Game zones remain rectangles; polygons are decomposed into at most 64 rectangles per source polygon, and the remainder is reported.

| Source | Zone |
|---|---|
| `pz:zone=<Type>` | that type (explicit override) |
| `landuse=residential` | `TownZone` |
| `landuse=forest`, `natural=wood` | `Forest`; `DeepForest` when the polygon's inner part lies more than 60 m from any road |
| `landuse=farmland`, `landuse=meadow` | `Farm` |
| `landuse=allotments`, garden plots | `Vegitation` |
| `amenity=parking` | `ParkingStall` with `pz:vehicles` or the default distribution |
| `pz:zone=Vehicle` line or polygon | `Vehicle`, `pz:vehicles=<distribution>`, `pz:direction=N|S|E|W` |

Pipeline-generated verge wrecks are emitted as `pz:zone=Vehicle` polygons with `pz:vehicles=junkyard` or another registered distribution.

## 8. Versions

- Input GeoJSON declares `"pzTransfer": 1` at the FeatureCollection level. Without it, `pz:*` keys are still read, and the import report notes the missing declaration.
- Writing `RotationDegrees ≠ 0` or a fence layer raises the world format to 7, and a fence layer raises the map format to 12. A shared decoration layer raises the world format to 8. Small buildings need no new format; attached buildings raise the map format to 13. All of this goes through `AuthoringFormats`, as for other new authoring features. An older editor refuses such files instead of silently dropping data, and files without these features keep their current version.
- This document is versioned with the code. Incompatible changes create `TRANSFER-CONTRACT.md` version 2 and keep version 1 readable.

## Acceptance

Implementation note: authoritative `WorldProject.Fences` survive isolated section reads, editing validation and streamed export with gate/post phase preserved across seams. The world importers (editor OSM import and `simplepzmap build`) promote every imported fence line whose edges reach two or more sections into the world layer once, merge the gates found near each section, and keep only the owning section's copy of a line that merely touches a neighbour. The seam check then verifies zero lost or duplicated buildings and fences.

`PilnaVillage` is built from the section scenes of real Pilna data: 21 sections of 1024 tiles under the rotated reference (the earlier, pre-rotated frame had 13). It is accepted when all of the following hold:

*Status 2026-10-17:* built from the OpenStreetMap extract with `examples/Pilna/village-scene.json` (26 sections, as empty sections with roads are kept). Every check below passes except fence continuity, which the extract cannot exercise because it has no barrier lines; see [examples/Pilna](../examples/Pilna/README.md#pilnavillage-world). The field-survey scenes are not in this repository.
- `WorldAssembler.Validate` passes;
- the seam check reports zero lost or duplicated source features;
- every fence that crosses a section edge is continuous in the exported cells;
- the native exporter output passes the structural verifier and the installed-class game probe;
- a dedicated Build 42 server starts with the mod.
