# Project Doctor and tile compatibility

Open **Tools → Project Doctor** in the editor. The report and tile snapshot comparison share one panel. The headless commands are `simplepzmap doctor <map|world> --game <path> --report report.json` and `simplepzmap tile-compare before.tiles --against after.tiles --report report.json`. `doctor --strict` also fails on incomplete coverage.

## Host integration

Core uses SDK default source inclusion: `Core/Diagnostics/*.cs` needs no `.csproj` registration. Web static files also use the existing `wwwroot` pipeline.

The editor already registers the following in Web startup, after `builder.Build()` and the existing JSON/same-origin middleware, alongside the other endpoint mappings and before `app.Run()`:

```csharp
using SimplePZMapmaker.Web.Services;

app.MapProjectDoctor();
```

The extension returns `IEndpointRouteBuilder`. It consumes the already registered `GameAssetCatalog` and `StaticModelCatalog` singletons. No additional service registration, middleware change, content-type exemption, or package is needed. Keep the existing request-body limit (128 MiB), local Host check and Origin check.

All POST requests are **application/json**. Binary `.tiles` uploads use a JSON base64 DTO, not an octet-stream body. The decoded binary limit is exactly 32 MiB (33,554,432 bytes); canonical padded base64 is bounded at 44,739,244 characters. The server checks encoded length and estimated decoded length before allocation, then verifies actual decoded length. Whitespace and malformed base64 are rejected. Uploaded filenames are display labels only, never paths to open. A browser checks file size before reading or encoding it.

### Menu/panel integration

`project-doctor.js` has no startup side effects. Import and mount from the parent's menu handler:

```javascript
import { mountProjectDoctor } from './project-doctor.js';

// Call this from the parent menu. container and state access belong to the parent.
function openDoctor(container, getCurrentSnapshot, focusDoctorLocation) {
    const doctor = mountProjectDoctor(container, {
        getSnapshot: getCurrentSnapshot,
        navigate: focusDoctorLocation
    });
    // For a disposable panel, call doctor.destroy() on unmount; the editor reuses its dialog.
    return doctor;
}

// Standalone raw map:
// openDoctor(panel, () => ({ project: currentProject }), focusLocation);
// World (raw world, selected member ID; do not also supply a projected project):
// openDoctor(panel, () => ({ world: currentWorld, memberId: selectedMemberId }), focusLocation);
```

The parent implements `getCurrentSnapshot` using its real state variables; the module does not read editor globals. `navigate(location)` is optional. `location.x/y/z` are section-local tiles, `cellX/cellY` are absolute native cells, and `memberId/buildingId` identify owners. Findings without exact coordinates have no invented location. Use `memberId` to select a section before focusing local tiles.

Optional dependencies:

- `request(url, {method, body, signal})`: returns parsed JSON, throws on HTTP failures, and sends object bodies as JSON. The default exported `doctorRequest` uses same-origin `fetch`, `Content-Type: application/json`, abort signals and the host's `{error}` response.
- `download(fileName, object)`: saves JSON; the default uses a Blob download and revokes its URL.

Mount returns `{ready, check, reloadCatalogs, destroy}`. `ready` waits for the initial installed catalog list. Project checking is explicit. `destroy` aborts pending requests and removes owned DOM. A project changed during a check discards the stale response. The parent's stylesheet controls appearance; the module injects no inline styles or scripts, respecting the current CSP.

The panel provides a crop editor, coverage statuses, severity filter, location navigation, report export, installed definition selectors, external binary/JSON inputs, snapshot export, comparison, searchable difference pages (200 rows), and a selected-record inspector showing both property maps and structural records. All file/property/message values use text nodes. Reports retain all differences even when the visible list is paginated. Sprite side-by-side image previews and editing/merging are not implemented.

## Endpoints

- `POST /api/project-doctor/check`: `ProjectDoctorRequest` with exactly one `project` or `world`, optional world `memberId`, and `access: {x,y,width,height,maximumNodes}`. Default crop is 128×128; maximum is 256×256. Returns `ProjectDoctorReport`.
- `GET /api/project-doctor/tiles/catalogs`: `{available,catalogs:[{id,source,fileName,format}],note}`. Only configured game text definitions and the registered definitions of active mod layers are listed; arbitrary files in those folders are not loaded. IDs are opaque SHA-256 identifiers for source/path selection, **not** file-content hashes. The snapshot's `sha256` is the complete file-content hash. Select IDs from this response; caller filesystem paths are not accepted.
- `GET /api/project-doctor/tiles/snapshot?catalogId=<id>`: reads a currently installed/active definition and returns `TileDefinitionSnapshot`. Re-list after changing game/mod catalogs. Game files follow `GameAssetCatalog`'s text-file policy; active mods prefer binary over a text companion. A single-file snapshot describes that file, not the complete merged load-order catalog.
- `POST /api/project-doctor/tiles/read`: `{"base64":"<canonical base64>","fileName":"baseline.tiles"}`. Returns a binary snapshot. It first runs the existing `TileDefinitionReader.Read`, rewinds, then retains additional structure with the Core reader. Unsupported version, truncation, trailing bytes, oversized counts/lines, and duplicate names/properties are rejected.
- `POST /api/project-doctor/tiles/compare`: `{"before":<snapshot>,"after":<snapshot>}`. Returns structural/property differences, source hashes/counts, replacement findings, notes and coverage. Uses the effective active `GameAssetCatalog` for cross-definition replacement resolution when available. Invalid/duplicate numeric slots and inconsistent declared geometry are rejected.

The host's existing exception middleware converts invalid request `ArgumentException`s to HTTP 400. Catalog I/O can still fail if an installation changes during the request; retry after refreshing the resource catalog. No filesystem changes are made by these endpoints.

External inputs supported by the browser are binary `.tiles` and exported snapshot `.json`, each up to 32 MiB. A binary file is posted through the JSON upload endpoint; JSON snapshots are parsed locally and validated when compared. External `.tiles.txt` files are explicitly unsupported because the existing text reader accepts installed file paths, not arbitrary uploaded text. Installed `.tiles.txt` files use that existing reader and are property-only snapshots. An empty/unrecognized text parse is rejected instead of presented as a clean catalog.

## Checks and coverage

Findings contain `severity` (`error`, `warning`, `info`), `category`, stable `code`, `message`, and optional structured `location`. Each category has one aggregate check status: `pass`, `warning`, `fail`, `partial`, `unavailable`, `unsupported`, or `not-applicable`. Report `complete` means all supplied checks had complete coverage; it is independent of whether findings exist. Overall status is `error` for any error, otherwise `warning` for warnings/incomplete coverage, otherwise `pass`.

Existing implementations are reused:

- Structure: `ProjectValidator.Validate` / `WorldAssembler.Validate`.
- Resources: host adapters to `AssetRules.Project`, `StaticModelCatalog.Validate`, and `WorldEndpoints.Validate` (including shared world resources). Offline resources are `unavailable`. Saved provider declarations are not compatibility evidence.
- Access: `WalkabilityAnalyzer.Analyze` / `.World`, retaining issue codes, building IDs, coordinates and coverage notes. World analysis projects shared authoring transiently. Crops, node/issue budgets, legacy designs, missing native tile physics and section boundaries retain partial status. There is no whole-world route guarantee.
- Seams: `WorldAssembler.SeamWarnings`; no repair call. Its text warnings have no precise coordinates; its existing 64-warning cap becomes partial coverage when reached. A standalone map has no supplied neighbors and is `not-applicable`.
- Zones: `GameZones.ValidateProject`, `ZombieDensityRegions.ValidateProject`, `RoomRegionRules.ValidateProject`, separately from other categories, for every raw map section.
- Cell conflicts: `CellConflicts.Scan` / `.Cells` / `.Check`. Web scans the configured game and **active mod layers only**, never arbitrary caller folders. Inactive Workshop/local mods remain unchecked. The existing scanner ignores inaccessible folders, skips links, and caps recursion at eight levels, so Web scan coverage stays partial. No display-name-only exclusion hides a conflict with an unrelated map. A caller-supplied complete `ProjectDoctorContext.InstalledMaps` can provide an explicitly scoped complete scan.

Each potentially normalizing validator receives a fresh clone. Independent categories continue after an existing validator rejects input; a validator's first-error behavior is stated in the report. No renumbering, repair, source normalization or dependency stamping is written back to the submitted map/world.

PZW/TMX/TBX/TSX source paths, PNG declarations and safe cleanup are `unsupported`: a native JSON project does not contain those source files. Tile drift is `unavailable` in a general project check until an explicit baseline is compared using the tile endpoint. Consequently the broad project report intentionally remains incomplete while these capabilities are outside the supplied scope.

## Tile identity and comparison

Binary tdef versions 0/1 use the same limits/layout as the existing Web reader. Snapshots retain original numeric tileset IDs (v1), image, columns/rows, stored entry count, every local numeric tile slot, whether the slot has a stored record, and all properties including custom keys and empty values. Version 0 has no numeric tileset ID; none is invented and identity coverage remains partial. Binary snapshots use the complete SHA-256 of input bytes.

Tilesets match by exact ordinal name; their numeric ID is compared as structural metadata. Tile records match by original integer slot, sorted numerically; input array order never renumbers them. Added/removed sets and slots, changed numeric IDs/images/grid/counts/stored flags, and every added/removed/changed property are separate differences. An absent property remains JSON `null`; it is distinct from an empty string. Collections/properties use deterministic ordering. JSON-supplied hashes are provenance labels supplied by the caller, not independently verified original bytes.

Non-empty `SnowTile`/`BurntTile` references resolve against the candidate snapshot and, when connected, the effective active game/mod catalog. Without the full installed catalog, cross-file targets stay unresolved and comparison remains partial. A matching property-only snapshot cannot certify numeric-ID or geometry compatibility. Missing references are warnings, not silent repairs. No game-global sprite number is synthesized from a local tileset ID, and no property merge or Snow Editor save is exposed.

## Test integration and validation

The repository uses a custom executable test registry, not xUnit. In the parent's `tests/SimplePZMapmaker.Tests/Program.cs`, register before applying `PZMAP_TEST_FILTER`:

```csharp
all = all.Concat(ProjectDoctorTests.Cases()).ToArray();
```

`ProjectDoctorTests.Cases()` covers independent aggregation, read-only resource adapters, existing seam/access diagnostics, offline/crop status, cancellation, numeric slots/metadata/hashes, deterministic comparison, property absence versus empty values, missing snow/burnt targets, malformed binary and JSON/base64 limits. The HTTP fixture starts an ephemeral loopback Kestrel app with the JSON/same-origin guard contract and exercises upload/compare/check/catalog/snapshot routes, foreign Origin rejection, binary POST rejection, malformed payloads and caller-path rejection. Its game catalog is a disposable minimal fixture and requires no installed game.

For the parent after integration and when .NET builds are permitted, run the repository's normal test executable; include `ProjectDoctorTests.Cases()` in the registry first. This contribution does **not** claim the C# tests ran. Roslyn syntax parsing is not compilation or semantic validation.

Validation performed for this contribution: the existing Node test suite passed; `node --check` passed for the module; all five added C# source files passed Roslyn syntax parsing without compilation. A real Chrome page mounted the module with an injected fixture API: both external upload inputs sent JSON/base64, the inspector displayed source properties, 401 differences paginated as 200/200/1, filtering selected one record, export retained all 401 records, an in-flight stale project response was discarded, HTML-like property values stayed inert, and unmount removed the panel. This browser check used fixture API responses and does not substitute for executing the added HTTP/C# tests after integration.

Core entry points available for the parent's CLI command:

```csharp
using SimplePZMapmaker.Core.Diagnostics;

var report = ProjectDoctor.Analyze(new ProjectDoctorRequest { Project = project }, context, token);
// context can provide installed map data and resource validation callbacks;
// omitted adapters are reported unavailable.
using var baselineStream = File.OpenRead(baselineFile);
using var candidateStream = File.OpenRead(candidateFile);
var baseline = TileDefinitionSnapshot.Read(baselineStream, Path.GetFileName(baselineFile));
var candidate = TileDefinitionSnapshot.Read(candidateStream, Path.GetFileName(candidateFile));
var compatibility = TileDefinitionComparer.Compare(baseline, candidate, installedTileExists, token);
```

The parent owns argument parsing, menu placement, report output and exit-code policy. Do not interpret `status == warning` or `complete == false` as full verification.

## Sources

The requested sources were fetched and read:

- [Project Doctor: TMX, TBX, tiles, and paths](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/docs/PZ-Project-Doctor-Tiles-and-Paths.md): read-only checking, preserve unresolved dependencies, distinguish supported formats, no silent cleanup.
- [TileDef comparator and Snow / Replacement Editor](https://github.com/Unjammer/PZ_Mapping_Tools/blob/main/docs/PZ-TileDef-Comparator-and-Snow-Editor.md): preserve numeric identity, separate structural/property changes, retain custom properties, report unresolved replacement targets. This implementation provides diagnostics rather than those editors' merge/write features.
- [ASP.NET Core minimal API parameter binding](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0#parameter-binding): JSON DTO binding, DI services and cancellation tokens for route handlers.
