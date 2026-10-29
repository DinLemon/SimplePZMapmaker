# Street catalog and automapping

Open **Tools → Street automapping** for a pinned, editable street catalog and an additive preview/apply engine. The editor registers its routes, embeds the bundled catalog and commits through the normal validated history path. Existing import/ensemble dressing uses the shared parcel occupancy rules.

## Integration

For other hosts, the integrated APIs are:

1. Register the web routes once, after constructing the application:

   ```csharp
   using var streetJson = File.OpenRead(Path.Combine(app.Environment.WebRootPath, "street-catalog.json"));
   var streets = StreetCatalog.LoadDefault(streetJson);
   app.MapAutomappingEndpoints(streets);
   ```

   `StreetCatalog.Load(Stream)` and `LoadDefault(Stream? fallback)` leave the supplied stream open. The optional embedded resource is `SimplePZMapmaker.Core.StreetCatalog.json`; Core can use this without a web-root file once its project file includes:

   ```xml
   <EmbeddedResource Include="../SimplePZMapmaker.Web/wwwroot/street-catalog.json"
                     LogicalName="SimplePZMapmaker.Core.StreetCatalog.json" />
   ```

2. Mount the UI through explicit editor dependencies:

   ```js
   import {mountAutomappingControls} from './automapping.js';
   const streets = mountAutomappingControls(host, {
       getProject: () => activeProject,
       getSelection: () => ({x: 0, y: 0, width: 256, height: 256}),
       getReservedCells: () => entrancePaths.flat(), // [{x,y}], optional
       applyProject: async candidate => commitThroughEditorHistory(candidate),
       onPreview: planOrNull => renderStreetGhosts(planOrNull), // optional
   });
   await streets.ready;
   // Call after map, section or selection changes:
   streets.refresh();
   // When unmounting:
   streets.dispose();
   ```

   The host must validate and commit `candidate` using its normal history/persistence path. Preview ghosts are optional. No function in this module mutates the current project. The UI refuses stale results if the map changed while a request was in flight. Select a rectangle explicitly on maps larger than one cell; the default is the northwest 256×256 rectangle.

3. Register `StreetCatalogTests.Cases()` in the existing console test runner. The editor test runner registers these cases. The test loader accepts an explicit `PZMAP_STREET_CATALOG` path, an embedded resource, or the source checkout JSON.

## Core API

```csharp
var plan = Automapping.Preview(project, catalog, options, tile =>
    new AutomappingResource(actualFlags, actualProvider, actualRequiredMods));
var candidate = Automapping.Apply(project, catalog, plan, sameResourceResolver);
```

`AutomappingOptions` has an X/Y/Width/Height rectangle, `Curbs`, `Drains`, `Markings`, `Parking`, `ParkingEntry`, `ParkingLayer`, `MaximumDecorations` and optional `ReservedCells`. `ParkingLayer` is `lines` or `bumpers`; `ParkingEntry` defaults to `parking-stall`. All four feature toggles default to true. `Markings` enables user-supplied neighborhood rules with kind `marking`; the bundled catalog paints parking boundary lines through `Parking`, without inventing road centerline artwork.

The resolver is required. The web endpoint gets flags, mod ownership, dependencies and complete sprite-grid validation from the active `GameAssetCatalog`. Core never guesses physical properties from a tile name or accepts physical flags from catalog JSON. A missing tile or incompatible sprite grid fails validation before apply. Core callers must provide equivalent resource validation if used outside the web host.

`AutomappingPlan` includes a map fingerprint, catalog fingerprint, deterministic ID, proposed decorations, per-kind placement counts, skip reasons, resource provenance and warnings. Apply recomputes the complete proposal against the current map/catalog/resolver, compares it with the posted preview, then validates a copied map. Changed map, catalog, flags, provider, dependencies, tiles or options reject an old preview. Neither preview nor a failed apply mutates the original map. Apply only adds decorations and resource metadata. It never removes or replaces existing decorations, repaints terrain, changes vehicle zones, erases blends, or changes the terrain edit mask.

The shared `ParcelPlacement` is authoritative for occupied/authored/protected cells, sites, model footprints, fences, linear features and infrastructure. `ReserveEntrances()` runs before optional street furniture; `ReservedCells` adds caller-known access paths such as ensemble paths and driveways. Current authored linear features are protected in full. A road authored as a linear feature can therefore produce fewer additions than terrain-only roads. Automapping does not bypass that protection. Existing generated decorations are protected just like manually placed ones; a repeated preview skips them and never replaces by ID.

Limits: catalog 1 MiB and JSON depth 24; 64 entries; 64 variants per entry; 32 parts per variant; 4096 parts total; 128 rules; eight neighbor predicates per rule; signed part offsets −7…7 with a maximum 8×8 footprint; supported layers `0_Furniture` and `0_FloorOverlay5`. Each operation selects at most 256×256 cells, creates at most 4000 decorations (default 2000) and performs at most eight million matching steps. The project's existing decoration limit also applies. At most 8192 explicit passage cells are accepted. Capacity skips are reported and never split a multi-cell stamp.

## HTTP API

- `GET /api/automapping/catalog`: bundled `{catalog, fingerprint, maximumBytes, maximumArea, maximumDecorations}`.
- `POST /api/automapping/catalog/validate`: raw catalog JSON, bounded while reading even without Content-Length. Returns canonical `{catalog, fingerprint}`. Updates retain the bundled ID and increase its revision. It does not change server state or write files.
- `POST /api/automapping/preview`: `{project, options, catalog?: <custom document>}` → `AutomappingPlan`.
- `POST /api/automapping/apply`: `{project, plan, catalog?: <same custom document>}` → `{project}`.

Omit `catalog` or pass null for the bundled document. A custom document must keep the bundled catalog ID and have a newer revision. Send that document on both preview and apply. Concurrent browsers have independent catalogs. Structural validation returns HTTP 400; an oversized raw validation upload returns 413. The existing project-body limit remains the host's responsibility.

## Editing and updating the catalog

Download the current catalog from the UI, edit JSON, increase `revision`, and upload it. The file picker enforces increasing revisions within the current session as well. The Core `catalog.Update(stream)` enforces the same ID and a strictly greater revision than that particular catalog instance. `schemaVersion` is a parser contract, currently exactly 1; `revision` is a positive catalog-data revision. Unknown JSON members, duplicate property names (including case variants), duplicate entry/rule IDs, unsupported schema versions, unknown rule references, malformed tile IDs and out-of-budget data are rejected. Source metadata is a required pinned HTTPS reference and never triggers a fetch. No Lua, JavaScript, expressions, filesystem paths or remote resource loading are evaluated.

A rule selects one exact entry/variant and matches a center terrain plus neighbor terrain predicates on the original map. Terrain names are case-sensitive enum names (`Road`, `Pavement`, `Grass`, `Dirt`, `Sand`, `Forest`); water is never a paint target. Predicates have dx/dy in −1…1, excluding (0,0). Higher priority runs first; equal priority uses ordinal rule IDs. `every` (1…256), `phase` (0…every−1), and `axis` (`x`/`y`) control repetition in absolute world-tile coordinates. Missing map neighbors do not match; selection boundaries can read real neighbors outside the selection but can never place outside it.

For example, clone `curb-near-s` with a new rule ID to change its repetition, or clone a drain rule and adjust `every`. A new mod marking entry supplies an explicit tile and layer; reference that entry and variant from a new `marking` rule. All placement cells must still be road/pavement and pass shared protection checks. An uploaded source field documents provenance; it does not authenticate third-party claims. Review new mappings against their cited source before distributing a bundled update.

## Direction conventions and scope

The catalog transcribes these exact pinned sources:

- [tool-curb-data.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-curb-data.lua): all 24 straight/corner/join/sunken mappings.
- [parking-stall.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/parking-stall.lua): all four eight-part layouts with signed offsets and original layers.
- [tool-street-decoration-data.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-street-decoration-data.lua): drainage w/n/e/s = 13/14/30/31.
- [tool-curb.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-curb.lua): inspected to interpret `near`/`far` and east/south edge anchoring. Its executable algorithm is not copied.

World coordinates use x east, y south. A curb `.e` denotes the east edge of its anchor tile, so it is a vertical run; `.s` denotes the south edge, a horizontal run. `near` occupies the pavement tile west/north of the road; `far` occupies the road tile west/north of the pavement. These are geometric edge conventions, not vehicle facing. Built-in rules require the two parallel runs to continue on both neighboring cells; they leave corners, uncertain joins and missing map-edge neighbors untouched. All corner and sunken IDs remain available as catalog data for explicitly reviewed future rules; no arbitrary sprite-index arithmetic or rotational offset is used.

Drain w/n/e/s refers to the road-facing side of its pavement anchor. Default spacing is 16 world tiles. Sparse drains take priority over straight curbs in a shared decoration cell.

Parking is generated only for rectangular existing ground vehicle zones with exact 3×5 (N/S) or 5×3 (W/E) multiples. Polygon zones and incomplete stalls are skipped explicitly. N/W source anchors are at the stall's northwest corner; S uses y+4 and E uses x+4, retaining the original negative line offsets. The whole car footprint must be free, including unpainted cells. No zones or traffic flow are inferred from an arbitrary road rectangle.

The current map validator permits one decoration per cell. The original parking source has both a wheel stop on `0_Furniture` and a line on `0_FloorOverlay5` at its anchor. Both are preserved in the JSON, but apply selects one layer explicitly. Both line sprites and the three wheel-stop pieces are independent single-tile decorations, applied atomically per source stamp. The installed Build 42 definitions have no SpriteGridPos for these pieces; inventing a three-part sprite grid would fail actual resource validation. Applying both coincident layers requires a separately integrated map-format/validation/export change. An incompatible mod sprite grid is reported instead of silently substituting a sprite.

See [upstream attribution and license](licenses/UNJAMMER-STREET-CATALOG.md). The game sprites are looked up in the user's installation and are not redistributed.

## Verification

Register the new C# cases in the parent runner, then run the repository's tests serially. The cases cover exact mappings, signed offsets/layers, bounded nonseekable input, invalid catalogs, revisions, all drain directions, parking directions/layers, deterministic atomic operations, stale/tampered previews, complete footprints, authored cells, access reservations, budgets, custom rules and mod ownership. `node --test tests/automapping.test.mjs` verifies the browser-side request options, preview summaries and pinned source data. A game-resource preview through the registered endpoints remains necessary to validate an installed Build 42 sprite grid.
