# Bounded BuildingEd / TileZed building exchange

This is an explicit, local building conversion, not a world-map importer or a
lossless replacement for the SimplePZMapmaker project format. Keep the original
project. No code in this feature resolves files, images, TSX, URLs or XML entities.

Implementation files:

- `src/SimplePZMapmaker.Core/Exchange/BuildingExchange.cs`
- `src/SimplePZMapmaker.Core/Exchange/BuildingExchange.Tbx.cs`
- `src/SimplePZMapmaker.Core/Exchange/BuildingExchange.Tmx.cs`
- `src/SimplePZMapmaker.Web/Services/BuildingExchangeEndpoints.cs`
- `src/SimplePZMapmaker.Web/wwwroot/building-exchange.js`
- `tests/SimplePZMapmaker.Tests/BuildingExchangeTests.cs`

Open **Import → BuildingEd / TileZed exchange**. Successful import opens a separate workshop draft. Saving/placing remains an explicit editor action; the active map is unchanged by import alone. CLI entry points are `building-import` and `building-export`; see [CLI](CLI.md).

## Source of the XML contracts

Inspected the maintained Unjammer mapping-tools repository at commit
`70e8b77676183e579cf9f1f7100dff6c383e28cb`:

- [TBX writer](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingwriter.cpp): ordered floors, 1-based room/tile-entry/user-tile references, explicit wall/opening objects, padded user layers.
- [TBX reader](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingreader.cpp): versions and entry categories, native furniture definitions.
- [Tile categories](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingtiles.cpp) and [category names](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingtiles.h).
- [Building-to-map conversion](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingmap.cpp): `RoomDefs`, `InternalName#id`, pixel coordinates, the extra wall border.
- [TMX writer](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/libtiled/mapwriter.cpp) and [reader](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/libtiled/mapreader.cpp): native map version **2.0**, layer `level`, atlas GIDs and object coordinate conversion.
- [Native layer list](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/TMXConfig.txt).

The exported geometry is represented by these actual structures. It does not
depend on a custom extension containing serialized SimplePZMapmaker JSON.

## Exact supported subset

### TBX

Export writes `<building version="4" width="W" height="H">`. Import accepts
integer versions **4, 5, 6, 7** using the v4 reference structure; newer palette
details remain subject to the diagnostics below. Versions 1–3 and unknown
versions are rejected. There is no speculative migration of older tile names.

- Local dimensions are 2–64 tiles on either side. There are 1–32 ordered
  `<floor>` elements. Their indices are local z=0..31. `floor/@level` is not a
  standard signed-level mechanism and is reported as unsupported.
- Root `<room Name="hall" InternalName="hall" Color="160 180 200" .../>`
  definitions are referenced by 1-based integers in `<floor><rooms>` CSV.
  Zero means no room. This grid is exactly **W × H** values in row-major order.
  Up to 35 used room identities per floor are retained independently, even if
  several identities share the same internal name.
- Native `<tile_entry category="floors|exterior_walls|interior_walls|doors|windows">`
  contains `<tile enum="Floor|North|West|NorthDoor|..." tile="sheet_index"/>`.
  Entry references are 1-based; 0 means none. Tile suffixes normalize decimal
  leading zeros. Unknown or mismatched referenced categories are reported or
  rejected; arbitrary extension-only tags do not count as supported geometry.
- Implicit room perimeter walls and room floor palettes are imported. A
  differing material on the opposite room face is reported; the core model
  has one material per edge.
- Native `<object type="wall" x="..." y="..." dir="W" length="..." Tile="..."
  InteriorTile="0" ExteriorTrim="0" InteriorTrim="0"/>` is supported.
  **A wall object's W direction runs west-to-east and produces north edges;
  its N direction produces west edges.** Overlapping explicit wall lines reject.
- `<object type="door|window" ... dir="N|W" Tile="..."/>` uses edge directions
  directly. Openings on automatic room walls select `NorthDoor`, `WestWindow`,
  etc. from that entry, rather than assuming an arbitrary sprite offset.
  Frames, curtains, shutters and separate interior facing tiles are reported.
- `<user_tiles><tile tile="sheet_index"/>...</user_tiles>` is a 1-based palette.
  `<floor><tiles layer="Floor|Walls|Walls2|Doors|Doors2|Windows|Windows2|Furniture|Furniture2|Furniture3|Furniture4">`
  is CSV of exactly **(W+1) × (H+1)** values. The extra east/south row/column
  stores boundary walls, not additional room cells.
- Export uses explicit wall/opening objects, a native `Floor` user layer and
  native furniture user layers. Generated room and building palettes are set
  to 0 to avoid unrequested automatic walls or duplicate floors.
- Native furniture definitions (`furniture/entry[@orient]/tile[@x,@y,@name]`,
  referenced by zero-based `FurnitureTiles`) can be flattened on explicit
  lossy import. The tile chosen for the supplied orientation is used directly;
  no sprite transformation is inferred. Grouping/grime settings are reported.

TBX export **rejects all negative MinLevel values even with allowLossy**.
It never silently shifts a basement to ground level. Use TMX for signed levels;
WorldEd may position an independently created local TBX below ground separately.

### TMX

Export writes **TileZed TMX version 2.0**, `orientation="levelisometric"`, map
tile size **64 × 32**, finite dimensions **(W+1) × (H+1)**. Layer names are bare
`Floor`, `Walls`, etc.; **every tile layer and RoomDefs group has an explicit
signed `level` attribute**. Version 2.0 is the native TileZed schema version,
not a claim about a newer standard Tiled release.

Import accepts native version 2.0 and legacy/Tiled version strings
1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10:

- In 2.0, `level` is mandatory and layer names are bare. In 1.x, names may
  use `-1_Floor`, `0_Furniture`, `-1_RoomDefs`, etc.; the prefix determines the
  floor. An optional `level` must agree with that prefix (or zero without a
  prefix). This matches the native reader's version distinction.
- Floors must be contiguous, include ground floor 0, and fit **z=-32..31**.
  Each needs nonempty RoomDefs. Missing intermediate floors are rejected,
  rather than invented. Multilevel legacy `isometric` maps are rejected due
  to their per-floor diagonal offsets. Single-floor `isometric` and
  `orthogonal` maps with 64×32 tiles are also accepted.
- Maps have dimensions **3–65** and reserve the last row/column for walls.
  Local building dimensions are always `map.width-1` and `map.height-1`.
  RoomDefs must fit that envelope. This is deliberately a BuildingEd-sized
  building TMX importer, not an arbitrary world-cell TMX crop operation.
- Supported tile layers, on every level: **Floor, Walls, Walls2, Doors,
  Doors2, Windows, Windows2, Furniture, Furniture2, Furniture3, Furniture4**.
  Empty other layers can be ignored; nonempty other layers are reported.
- Data encoding is **CSV** or unencoded XML `<data><tile gid="..."/>...</data>`.
  Exactly map.width × map.height values are required. **base64, every
  compression attribute, infinite maps, chunks and group layers are rejected**.
- Tilesets are **inline image atlases** named after game sheets. Tiles are
  64×128, with 8 columns, no spacing or margin. `tilecount` can be supplied or
  derived from the declared image size; ranges must not overlap. The image
  declaration is metadata only and is never read. External `source="*.tsx"`
  and image-collection tilesets are rejected even if a file is locally present.
- GID 0 is empty; other GIDs select the unique atlas range:
  `sheetName + "_" + (gid - firstgid)`. All high transform bits
  (`0xF0000000`, horizontal/vertical/diagonal/hex rotation) are rejected.
  No undocumented flag-to-sprite conversion is attempted.
- Export orders sheets by ordinal name, allocates firstgid sequentially from
  1, and rounds the highest used local index up to a complete 8-column row.
  Atlas local indices are bounded to 0..16383. The XML references
  `sheetName.png` next to the TMX. It does not package or download artwork.
  Supply those PNGs or resolve their paths in TileZed before using the map.
- Walls are interpreted only with verified catalog north/west orientations.
  Known combined NW sprites can be decomposed with an explicit diagnostic.
  SE pillars/unknown shapes are reported. Ambiguous opening orientation is
  reported; the importer never infers it from an arbitrary custom tile index.
- Room metadata uses `<objectgroup name="RoomDefs" level="Z">` with
  unrotated rectangular `<object name="InternalName#id" type="room" .../>`.
  Repeated #id/name runs belong to one room. In levelisometric/orthogonal,
  object x/width are tile units ×64 and y/height are tile units ×32.
  Single-floor isometric uses ×32 on both axes, per the native reader.
  Subtile coordinates, overlaps and rectangles in the wall border reject.
- Layer offsets, hidden/translucent layers and transforms reject. Polygons,
  tile objects, rotated room rectangles and other object groups are reported.

### Losses and defaults

Unsupported diagnostics include roofs/weather cover, stair semantics and
cutouts, static models, wall decorations/trim, square attributes, custom
properties/loot/basement-access settings, unknown room names, room display names
different from internal names, navigation flags, nonrepresentable outside-room
or stacked objects, furniture grouping and unrecognized XML attributes/elements.
Unknown internal room names use `hall` **only when explicitly allowing loss**;
`emptyoutside` maps to the editor's `balcony` room. Missing floor or opening
sprites likewise report the editor default before it can be used. There is no
silent substitution during strict import.

World placement, building identity/name/type, material/style presets, source
geography and edit history are outside the local exchange contract. Geometry
and concrete supported tile IDs are carried; imports become a new local
`house` named `Imported building`, with no automatic roof. Import IDs are
deterministic SHA-256-derived IDs for reproducible previews. **Allocate a fresh
application identity when adding the same imported building more than once.**
Keep original provenance separately: required mods, asset source ownership and
attribution trigger a blocking export diagnostic rather than disappearing
without notice.

## Limits and diagnostics

- XML: at most **8 MiB UTF-8**, and at most 8 MiB characters before parsing.
  The browser file picker reads UTF-8 text. Request JSON is capped at **16 MiB**
  even for chunked requests; JSON depth is at most 32. Both byte caps apply,
  so heavily JSON-escaped XML may hit the request cap first.
- DTD processing is **Prohibit**, `XmlResolver=null`; no input causes an implicit
  filesystem or network read. The XML declaration does not grant permission
  to fetch a schema. The parser preflights before constructing an XDocument.
- Depth <=16 (XmlReader depth), <=150,000 elements, <=32 attributes per element,
  names <=128 characters and attribute values <=4096 characters.
- At most **1,000,000 decoded tile/room grid values**, 1024 TMX layers/groups,
  512 TMX atlases, 8192 TBX entries, 32768 TBX user tile IDs, 2240 TBX room
  definitions, 32 TBX user layers per floor, 16384 TBX objects per floor.
  Core limits also apply: 35 room identities and 512 object anchors per floor.
- Diagnostics cap at 200 plus a terminal error. Codes, severity, path and message
  are returned. Invalid structure produces `error`; omitted unsupported content
  produces `unsupported`. `allowLossy=true` accepts **only unsupported** loss,
  never structural errors or safety-limit violations.

## Core / HTTP integration (parent-owned)

No existing Program, CLI or workshop file is changed by this feature.
The parent has installed the endpoint mapping, Import-menu controls and test
provider; the snippets below remain the integration contract for future hosts.

The parent CLI dispatch exposes:

```text
simplepzmap building-import <building.tbx|building.tmx> --out building.pzbuilding.json [--allow-lossy] [--report report.json]
simplepzmap building-export <building.pzbuilding.json> --format <tbx|tmx> --out building.tbx [--allow-lossy] [--report report.json]
```

1. In `src/SimplePZMapmaker.Web/Program.cs`, with
   `using SimplePZMapmaker.Web.Services;`, invoke once after app construction
   and before `app.Run()`:

   ```csharp
   app.MapBuildingExchangeEndpoints();
   ```

2. The endpoint contracts are POST JSON:

   ```json
   {"format":"tbx","xml":"<building .../>","allowLossy":false}
   ```

   to `/api/building/exchange/import`, or:

   ```json
   {"format":"tmx","building":{"width":12,"height":10,"design":{}},"allowLossy":false}
   ```

   to `/api/building/exchange/export`. The second example illustrates the
   envelope; supply a complete valid Building design, not an empty design.

   Both return `{success,building,xml,diagnostics}`. A domain rejection is a
   normal HTTP 200 preview with `success:false` and **both payloads null**.
   Malformed JSON/request bodies return 400; wrong content type returns 415;
   oversized bodies return 413. An export success supplies XML; an import
   success supplies a detached Building. No route applies or saves any state.

3. In `tests/SimplePZMapmaker.Tests/Program.cs`, concatenate
   `BuildingExchangeTests.Cases()` into `all` with the other case providers.
   No csproj edits or NuGet packages are needed. The new provider includes
   independent native fixtures, TBX/TMX cross-roundtrips, deterministic exports,
   signed floors, direction/padding regressions, malicious XML/GIDs/limits,
   unsupported-loss gating and a real ephemeral Kestrel endpoint check.

   Run after other .NET builds finish:

   ```powershell
   $env:PZMAP_TEST_FILTER = 'building exchange'
   dotnet run --project tests/SimplePZMapmaker.Tests
   Remove-Item Env:PZMAP_TEST_FILTER
   ```

4. Direct core consumers use:

   ```csharp
   using SimplePZMapmaker.Core.Exchange;
   var preview = BuildingExchange.Import(xml, "tbx", allowLossy: false);
   var file = BuildingExchange.Export(building, "tmx", allowLossy: false);
   ```

## Frontend integration (parent-owned)

Import the named function from `./building-exchange.js` and mount it in a
parent-created host after constructing the workshop. It creates only its own
controls and reads no global project or editor state:

```javascript
import {mountBuildingExchange} from './building-exchange.js';

const exchangeHost = document.createElement('div');
document.querySelector('.building-tools').append(exchangeHost);
const buildingExchange = mountBuildingExchange({
  container: exchangeHost,
  getBuilding: () => workshop.draft,
  // Optional: existing api(path, body), which must return parsed JSON.
  request: api,
  onDiagnostics: diagnostics => { /* optional presentation/logging */ },
  onImport: async (candidate, context) => {
    // Parent implementation: validate a cloned candidate, including installed
    // resources, version gates, and destination constraints BEFORE mutating.
    // Then perform ONE transaction in the draft or building library.
    // Do not apply individual floors/objects incrementally.
    await importBuildingCandidate(candidate, context);
  }
});
```

`importBuildingCandidate` is a parent-owned callback, not an included function.
For the existing workshop, use its edit transaction boundary (`workshop.change`)
or the existing building-library save flow after validation. Retain destination
identity/position deliberately when replacing a draft. Check the draft/project
revision after asynchronous validation so an import cannot overwrite newer
edits. Importing to the library should allocate a new ID and preserve the
diagnostic report alongside the operation. Full `/api/building/validate` may
reject inaccessible buildings; exchange intentionally validates structural
design with `requireAccess:false` so imported drafts remain repairable.

Call `buildingExchange.refresh()` when selection/draft availability changes;
call `dispose()` when removing the host. Repeated mounting on the same host
returns the original controller. `onImport` is called once only for an accepted
detached candidate; it must reject on failure and own atomic application,
history/persistence, resource checks and app-specific version upgrades.

The default transport is same-origin JSON fetch. The controls block duplicate
operations and show diagnostics with textContent. The omission checkbox starts
off and is passed explicitly as allowLossy. Import reads only a chosen .tbx or
.tmx file; export downloads `building.tbx` or `building.tmx` only after success.

## Verification boundary

Automatic balcony boundary railings retain their `carpentry_02_100/101` sprites in TBX. TMX reports railings that lack a verified catalog orientation instead of replacing them with full walls.

The repository C# runner covers independent upstream-schema fixtures, supported round trips, malformed/bounded XML and real HTTP endpoints. `scripts/verify-shared-authoring.mjs` exercises the integrated Import menu and opens a TBX draft in the real editor, then verifies that the map remains unchanged. BuildingEd/TileZed GUI loading with their image files remains a separate interoperability check; automated tests do not claim that external GUI validation.
