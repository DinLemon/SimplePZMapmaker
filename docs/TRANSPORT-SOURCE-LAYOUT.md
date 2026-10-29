# Source road widths, parking and fuel stations

These are transport additions to the existing geographic importer. They do not establish full KnoxMap parity or a client driving/refuelling pass. Original source geometry and tags remain separate from generated layouts.

## Carriageway widths

`RoadWidthPolicy` is shared by geographic paths and roadside sign placement. Priority is a valid physical `width`, then `est_width`, total `lanes`, a complete directional lane count, eligible one-way fallback, then highway class. Directional counts never get added to a supplied total. Explicit `oneway=no` overrides an implied roundabout direction.

Widths accept metres, a decimal comma, feet (`20 ft`) and feet/inches (`12'6"`). Invalid, non-finite, negative, ambiguous semicolon and excessive values fall through to the next source; original tags remain retained. Lane-derived widths use 3.2 m/lane plus a 1 m allowance, capped at eight lanes. Walking paths do not become vehicle carriageways from incidental lane tags.

The physical decision is scaled once by metres-per-tile, with the existing 1–48-tile geographic raster limit. `pzmap:width-source`, `pzmap:width-metres` and `pzmap:width-tiles` disclose that decision; import notes disclose inference and scale clipping. `width` is a supplied value, not a field measurement verified by the application. Sidewalk/verge widths and markings remain separate work.

Editable paths and the raster-only overflow now use the same circular stroke and work budget. The 65th road no longer switches to a wider square stamp. Preserved edited paths supply their actual width and geometry to roadside placement. Imported rails retain their existing native track geometry.

## Polygon parking

Ordinary `amenity=parking` polygons are packed with whole native 3×5 or 5×3 stalls, deterministic row identities, and three-tile rear access strips. Every retained tile interior must be inside the outer ring and outside all holes. Centre sampling alone is insufficient: a tiny hole or diagonal crossing only a tile corner rejects that tile too.

The planner uses the shared parcel occupancy rules, imported fences, buildings, water, explicit surfaces/edits, models, protected areas, existing vehicle zones and street props. One street-prop proposal is reused when applying the parking result, avoiding a seeded re-roll after access aisles are reserved. Derived rear aisles are reserved by `ParcelPlacement` after serialization too.

Rows are tagged with a source-owner hash. Refreshing that source retires its old rows even when the feature becomes farmland or changes to an explicit rectangle. Older generated rectangles are migrated from the previous source evidence and exact former geometry; a manually moved/resized rectangle is retained. Unrelated/authored zones remain. Legacy rectangles without original evidence cannot be safely distinguished from resized authored zones; they are retained with an explicit review/remove-old-layout diagnostic instead of guessed migration. Explicit rectangular `pz:zone=Vehicle` keeps its historical contract.

Bounds: 4,000 native zones, 262,144 mask tiles, 4,096 vertices and an aggregate 8 million work budget. Rejected stalls and limits are reported; no substitute bounding rectangle silently fills a concavity or courtyard. The local aisle is an inferred clearance reservation. Connection to a public road, vehicle turning radii and actual sandbox car spawns remain acceptance gates.

## Native fuel stations

The source pipeline retains `amenity=fuel` and standalone `man_made=fuel_pump` points through saved GeoJSON, Overpass, the small-area OSM map backup and normal/indexed PBF readers. Fuel types and lifecycle tags survive evidence capture. Pump/station roles, closure/access restrictions and level/layer evidence take priority over optional fields at the 64-tag cap. Inactive/disused/abandoned features are reported and do not automatically become operational pumps. Closed station polygons also suppress contained mapped pump nodes, using the filled polygon rather than its bounding box. Courtyard holes remain outside that inherited closure. Closure context is retained by the online and offline source readers.

The first profile uses the installed game's single-part `location_shop_fossoil_01_12` (E) and `_14` (S) resources, with native solid collision and positive `fuelAmount`. Active-resource validation rejects replacements that lose capacity or facing. No game images or textures are bundled.

Placement is conservative: one pump per source, a wholly contained 4×5 or 5×4 forecourt, an adjacent three-tile driving strip, and a straight clear connection of at most 24 tiles to a real three-tile ground-road patch. Polygon holes and existing reservations remain protected. An observed pump point is quantized to a tile; its location is not moved to an invented empty cell. A point station without an observed boundary uses a disclosed inferred 12×12 search area. Unsupported or obstructed cases are skipped with reasons.

Stable source IDs preserve existing pump decorations during refresh, including manual edits; preserved access is explicitly not revalidated. A digest in newly generated IDs records the original object baseline. If a source closes, only an unchanged generated pump is retired; authored edits and legacy IDs remain, with accurate removed/preserved counts. Generated clear forecourt/driveway ground is paved, while original roads, manual surfaces and terrain edit masks remain protected. Layout and access assumptions are written in the import notes. Generic indoor/raised stations, other pump facings/fuel types and complete real forecourt reconstruction remain unsupported by this first profile.

The exported resource uses the game's normal pump semantics. Native power, gas-infinite/min/max/empty-chance sandbox options and refuelling actions remain authoritative; the importer does not prefill fuel or force electricity with Lua. Native archive/resource readback and controlled getter checks are distinct from initial fuel generation, hose reach, turning and refuelling in a fresh client save.

## Evidence

- Width red/green and review regressions: `work/knoxmap-research/road-width-*.txt`.
- Parking red/green and review regressions: `work/knoxmap-research/parking-*.txt`.
- Fuel source/resource/layout checks: `work/knoxmap-research/fuel-*.txt`.
- Independent zone/install check: `scripts/verify-transport-archive.py`.
- Live import/compile fixture: `scripts/verify-transport-source-import.mjs`, isolated port 48813; proof/artifacts under `work/knoxmap-research/transport-import/`.
- Ramp geometry/native proof is tracked separately in [NATIVE-ROAD-RAMPS.md](NATIVE-ROAD-RAMPS.md).
