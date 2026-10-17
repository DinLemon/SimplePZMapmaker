# Transfer items 2, 4–7 and 9 — 2026-10-17

- **Building tags (item 4):** `pz:kind`, `pz:style`, `roof:colour` (named or `#rrggbb`, matched to roof sets by colours measured from the installed sprites), `pz:wall` and `pz:trim` are applied to section and shared imports and reach the native lot; unknown `pz:*` keys and unusable values are reported, never fatal.
- **Attached buildings (item 2):** `pz:attached`/`pz:door-to` place a building against another with no free line; the larger one owns the single shared wall, the smaller one keeps its own entrance, and a door joins them. Map format 13.
- **Mod export (item 6):** every export takes a mod id, map folder and title, can move `media/maps` under `common/`, and installs into `Zomboid/mods` without touching folders it did not create.
- **Command line (items 7 and 9):** `simplepzmap build/validate/export/tiles/probe`; see [CLI](CLI.md). `--game` checks every exported tile name against the installed definitions.
- **Tile mods (item 5):** `tiles` writes a PZPK v1 atlas and tdef v1 definitions from PNG frames, copying properties from installed tiles.
- **Seam check (contract §3):** every source building and fence is placed in one section, once at world level, or kept as evidence with a reason.
- Real Pilna OSM through `build --strict --game`: 26 sections, 1,640 section and 61 shared buildings, 0 lost or duplicated, 2,471 tile names found; installed game classes loaded 416 cells, 425,984 chunks and 9,565 rooms. Suites pass **404 C# tests and 262 JavaScript tests**.

# Remaining streamed-world features — 2026-10-17

- **Buildings under 6×6:** footprints from 2×2 tiles generate one accessible room; stairs fit only along a side of at least 6 tiles. Imported outlines are no longer inflated to 6×6, and tiny sheds/kiosks keep one floor with a report note. Geometry drafts, the building workshop and new one-room presets accept the smaller sizes.
- **Imported fences across seams:** the OSM world importer promotes crossing barrier lines once into the world fence layer, merges per-section gates and keeps authored lines local. Preserve edits keeps authored world fences.
- **Shared decorations:** world format 8 stores decoration groups once in world tiles. They are validated against every section, clipped into each section, protected in isolated editing, kept on their prior ground by preserve-edits reimport, stored in section workspaces and exported identically by streamed and complete-world exports.
- Suites pass **392 C# tests and 261 JavaScript tests**. A live browser session against the installed game catalog placed a 3×3 kiosk, promoted a real two-tile bed across a seam, moved, removed and exported it, and edited it through the world inspector.

# Streamed world editor verification — 2026-10-17

- Integrated the companion rotation, native fence layer, barrier import and seam ownership changes from `develop`; retained polygon zones, density and reviewed room metadata. The combined suites pass **377 C# tests and 257 JavaScript tests**.
- Section workspaces pin immutable revisions, read/save independent sections, retain old revisions and export without materializing a complete world. Explicit compatibility limits remain: 48 MiB per section, 16 MiB shared metadata, 128 MiB monolithic world JSON and 256 MiB compressed native output.
- The self-contained browser test uses **13 synthetic 1024×1024 sections**, with a house and fence crossing a seam plus a shared road. Switching loads only the requested section; committed edits persist. Polygon density, resource-backed fence drawing with a wicket, reload restoration, historical section review and locked undo were exercised through the UI.
- Independent native decoding accepts the selected section (**16 cells, 16,384 chunks, 16 rooms**) and combined world (**208 cells, 212,992 chunks, 20 room fragments**). Streamed authoring source hashes match the pinned index. Installed game classes accept the section and report **8 upper rooms and 6 stair tiles**. No interactive game session was started.
- Private populated Pilna scenes were not used for acceptance. The illustrated [guide](STREAMED-WORLD-EDITOR.md) records the isolated-lighting boundary. Public packaging excludes private art and local installation paths.

# Real-place transfer development verification — 2026-10-16

- Original geographic evidence now includes point observations, bounded source tags/addresses/access roles, campus/land-use areas, barriers and stable identities. Tiled imports retain nodes; regular/dense PBF nodes and barrier/healthcare ways reach the source layer.
- Editable polygon sites support complete object membership, boundary vertices, reviewed atomic translation/copy, generation pins and grouped history. Real source geometry stays separate from authored geometry. Reimport preserves groups across source order/label changes and remaps legacy building IDs by source identity.
- Source audits expose omitted buildings, footprint drift, coarse scale, inferred floors, entrance outcomes and incomplete association budgets. Live checks abort and invalidate old work; service coverage remains unknown and distances are explicitly straight-line.
- Fresh local suites: **336 C# tests and 183 JavaScript tests pass**. Independent review reproduced and resolved anonymous-GeoJSON identity collapse, reordered-import membership loss, mutable evidence IDs, PBF source omissions and sub-tile association/budget problems.
- Actual browser checks on the self-contained application verify source inspection, reviewed group edits/moves, undo/redo, pins, save/load, live invalidation and a real Smolensk round trip. The cached regional snapshot retains **1,954 source observations, 772 building outlines, 132 entrances and 624 facility/shop observations**; 426 buildings fit this 1,024-tile section and 320 mapped areas become editable groups. Remaining source buildings are evidence with skip diagnostics, not claimed as placed structures.
- A source-derived two-floor fixture exports as native B42 data: the independent decoder accepts **1,024 chunks, 10 rooms and one building**; installed game classes also confirm **five upper rooms and three stair tiles**. No interactive game session was started.
- Integrated the concurrent wall-overlay development commit without dropping source/site changes; the combined suite includes its two native-overlay regressions.
- Updated illustrated [real-place guide](REAL-PLACE-TRANSFER.md), README and package documentation. Public files exclude local installation paths and private generated art. Existing active mod configuration remains available.

# Generation studio development verification — 2026-10-16

- Contextual districts cover era, land use, density, setbacks, floors and courts; legacy new-map generation remains available. Actual pedestrian audits verify mixed seeds and every era/use combination with three floors. Generated fallback buildings use clear stair halls and doors; enclosed generated courts have a usable ground gate.
- Protected fill/regeneration preserves imported, authored, edited, pinned and boundary-crossing buildings plus protected terrain and resources. An explicit regression removes all 29 eligible generated buildings despite their own entrance paving while preserving manually authored paving.
- Server-owned bounded previews are fingerprint-bound to the raw map and optional world snapshot. Section previews/audits include shared crossing buildings, roads, rivers and bridges without persisting projections. Projection retains generation metadata.
- Fresh local suites: **316 C# tests and 178 JavaScript tests pass**. Actual browser workflows verify district controls, drawing a rectangle, provenance, pins/areas, two alternatives, failed/cancelled preview invalidation, audit navigation, apply/undo/redo/save/reload and shared-world section history.
- A newly generated district exports successfully. An independent native decoder and installed game reader accept **1,024 chunks, 295 rooms, 82 upper rooms, 34 basement rooms and 81 stair tiles**. No game session or character walkthrough was started.
- The illustrated [Generation studio guide](GENERATION-STUDIO.md) describes controls, retained content, incomplete diagnostic coverage and troubleshooting. Art references and private assets remain outside public Git data.
- The self-contained runtime also passes an isolated no-game/no-shared-runtime check: 12 generated buildings, protected regeneration/apply, zero structural errors, and explicitly incomplete native-resource coverage. Mod resources and static model definitions still require their catalogs.

# Verification status — 0.11.2, 2026-10-15

## Game installation and resource paths

- Steam discovery reads registered installations, modern/legacy library metadata and the game's actual manifest folder. Synthetic fixtures verify library selection, malformed metadata, saved manual choices and fallback from stale configured paths.
- A real browser loads installed game atlases through one alternate drive and a generated mod's tile definitions and OBJ model through another. Manual quoted input, Enter, Find installed game, activation and catalog retention after an invalid selection pass with no browser runtime errors. The temporary drive mappings target existing workspace/game folders; no game files are changed.
- The resource path validator no longer imposes a drive letter. Absolute paths, quoted input and game executable paths are accepted; relative paths and filesystem links remain unsupported. Network shares are supported by path handling but were not exercised against a live remote server.
- Local regression coverage is **270 C# tests, 168 JavaScript tests and 23 authentic installed-library lighting scenarios**. The self-contained Windows build starts without an installed game/shared .NET Runtime, serves the editor, saves/reopens projects and exports independently readable native maps, including a shared bridge.
- README and the mod-resource guide include the new connection controls and screenshots24–25. Cache and settings locations remain under the editor workspace.

# Verification status — 0.11.1, 2026-10-15

## Shared worlds, counted buildings, access and checked agent design

- A clean-machine regression verifies shared native infrastructure without an installed game: structural/provider declarations remain checked, while active provider lookup waits for an available catalog. This also preserves shared paths during offline OSM reimport.
- Shared world infrastructure is authored once in absolute coordinates and projected into independent sections. Browser preview/apply/remove/history, native seam sprites, an independent LOT decoder, section workspace recovery, OSM preserve and agent publication are verified. Shared road underpasses remain clear of bridge piers; railway providers are checked.
- Parametric generation produces actual apartment units, independent entrances/stairs and reusable modules. Slab, staggered and courtyard layouts, all 32 above-ground floors and the eight-apartment native floor limit are covered. Real catalog ensembles supply complete prop footprints, gated access, parking, greenery and native lights. Standalone and world-section browser tests verify terrain, metadata, save/reload and atomic undo/redo.
- Pedestrian diagnostics report coordinates, signed floors, complete/partial crop coverage, full furniture/model footprints, native directional fences, locked doors, supported stairs, unreachable/fully blocked rooms and unsafe upper edges. Optional routes, overlays, issue focus, draft checks and stale invalidation pass browser checks. Clipped stair fragments cannot claim complete coverage.
- Official SDK clients discover **55 tools** under both protocols and verify isolated design/check/repair, genuine LightingJNI evidence, revision-protected task/source commits, world-section repair and before/after editor review. A real powered-off scenario accepts three chosen native lamps and improves measured RGB while retaining a failing result when the requested threshold remains unmet. No light estimate substitutes for unavailable native evidence.
- Native export jobs preserve immutable snapshots, progress/cancel/download and restart status. Cache tests prove unchanged sections skip expensive composition and that terrain, shared buildings, provider ownership, shared decks and height changes invalidate the appropriate data. Cancellation/corruption/eviction cases pass; actual browser queue/reload/cancellation/reuse flows pass.
- The new two-section WorldAuthoring example passes independent native readback and the installed game classes:2,048 chunks, 54 rooms, 36 upper rooms and 24 recognized stair tiles. No game session or character traversal is started.
- Final local checks pass: **265 C# tests, 168 JavaScript tests and 23 authentic installed-library lighting scenarios**; Release build has zero warnings/errors. The published self-contained application starts with no installed game or shared .NET runtime, serves the English editor, saves/reopens projects and exports independently readable native data. Package/privacy/publication evidence is recorded in PROGRESS.md.

Access analysis is a bounded static authoring graph, with doors assumed openable unless locked and no climbing/demolition. Native lighting checks one selected crop/floor/scenario; browser material rendering remains an editor preview. Roads/decks have discrete native floors and pedestrian stairs, not continuous vehicle ramps. DEM remains a measured reference layer. Section-only mode preserves shared source metadata; load the complete world to edit shared geometry. No gameplay save or multiplayer validation is claimed.

## Earlier release evidence

# Verification status — 0.10.0, 2026-10-14

## Materials, modules, infrastructure, MCP and section workspaces

- Core 226/226 and JavaScript 150/150 pass; the Release build has zero warnings/errors. The installed native-lighting regression covers 23 authentic scenarios without opening a game session.
- Real browser checks verify native RGB applied to textured meshes, actual geometry shadow pixels, sun/material changes, authored-wall depth, cancellation, missing-asset fallbacks and asset/instance/memory budgets. Registered X and FBX previews remain usable.
- Compound modules capture complete room/object/model footprints and signed stair transitions, compose ordinary exported designs, expand bounds safely, reject unsupported/reversed resource transforms, and roundtrip through the library/templates. Browser assembly, capture, undo/redo, native preview and atomic overlap rejection pass.
- Six junction presets and signed decks preserve ground underlays, stairs, rails and support geometry; actual browser preview/export/history/group removal and negative underpasses pass. An independent LOT reader verifies cell-seam deck/underlay fixtures. Installed game classes load the tunnel fixture: 1024 chunks, 15 rooms, five basement rooms and nine recognized stair tiles (six below ground).
- Both supported official MCP clients discover 47 tools. Authentic map/world/draft lighting retains revisions; isolated map/world gameplay-zone proposals reject overlaps/stale commits. Actual browser review lists and draws zone rectangles; receive/undo/redo roundtrips preserve them.
- Section-only opening downloads one terrain section. Incremental saving retains the other section fingerprint; stale writes return409. Missing/corrupted data causes complete-revision recovery. Independent section save does not deserialize unrelated terrain. Selected native export preserves a valid standalone local source plus clipped shared-building context; independent readback verifies one cell, 1024 chunks and five shared rooms.
- Native HTTP exports build bounded seekable workspace files, write cell entries directly without intermediate section ZIPs and delete completed temporary archives after response disposal. Roof/level constraints and earlier source formats remain supported; elevated/junction authoring uses map11/world5.
- Browser mesh/material/shadow visualization is an editor preview of the authentic light field, not the game's FBO/material renderer. Infrastructure provides discrete native floors and pedestrian stairs, not continuous vehicle ramps. Paths cross native cells inside a section; separate world sections currently own separate path geometry. Section-only mode retains shared buildings in the workspace; load the complete world to view/edit them.
- No character playthrough, gameplay save or multiplayer validation is claimed. Self-contained package/CI publication evidence is appended in PROGRESS.md after those checks complete.

## Previous release evidence

# Verification status — 0.9.1, 2026-10-14

## Full signed vertical range

- Installed Build 42 `IsoChunkMap` constants and the [official constant values](https://projectzomboid.com/modding/constant-values.html#zombie.iso.IsoChunkMap.BOTTOM_LEVEL) agree: bottom -32, top 31, ground index 32 and 64 levels. UI floor 32 corresponds to z=31; z=32 is invalid.
- Core 203/203 and JavaScript 138/138 pass. Generation, OSM import, neighborhood constraints, authored models, signed scene/viewport APIs and connected stairs support 32 basements plus 32 above-ground floors.
- Roof headroom uses actual slope-piece heights. Overflow rejects before saving/export; generation chooses a valid flat roof or explicit `No automatic roof` at the upper boundary. Imported roof adaptation is recorded in the quality report.
- Native lighting 23/23 authentic fixtures pass, including z=-32 and z=31, both endpoints of a full 64-level scene and rejection of -33/+32.
- The installed game reader loads the new fixture: 1,024 chunks, 320 rooms, signed room levels -32..31 and 189 recognized stair tiles. No game session or character traversal is used.
- Fresh browser checks verify all 64 selectors, both caps, actual endpoint sprites, map10/template7 roundtrip, roof rejection and stair history. Official MCP clients create and preview a 64-level isolated draft, reject z=32 and retain the original map revision.
- Extended authoring uses map/template/world formats 10/7/4. Older compatible data retains its prior format. Editable serialization and undo preserve the signed stack and explicit roof choice.

## Previous release evidence

# Verification status — 0.9.0, 2026-10-14

## Geography, gameplay authoring and native lighting

- Core 190/190 and JavaScript 129/129 pass; native lighting runs 18 authentic installed-library fixtures without a game session.
- Fresh Chromium workflows exercise main-map floor/outline/edge/object/model editing, signed basements and stair links, native palettes, single-step undo/redo, shared world seam edits, linear previews and path moves, rail artwork, curved rivers, bridge underlays, loot/model properties and vehicle rectangles.
- Native lighting checks include solid walls, windows, open/closed native and model doors, radial and disabled colored lights, signed levels -16/-1/+31, sky, roofs, basements and powered room rectangles. Browser checks verify actual engine/scene fingerprints, signed-floor recalculation, cancellation, unavailable results, stale scenes and no project/history mutation.
- Installed API probes verify door edges/key state, light RGB/radius and vehicle direction; three original export Lua files compile and one generated declaration parses. Actual map/world ZIP regressions verify authored zones and shared loot dependencies reach the exported mod.
- Native field values are authentic engine output. Browser material/FBO rendering, model mesh shadows, live weather/switches and character visibility are outside the adapter's rendering scope. Railway artwork requires cardinal segments; bridge decks are at ground level. No character playthrough or gameplay save/multiplayer test is claimed.

## Previous release evidence

# Verification status — 0.8.0, 2026-10-14

## Editor tools and publication privacy — 0.8.0

- Core 159/159 and JavaScript 97/97 pass. Independent atlas readback checks both single maps and stitched sparse worlds against the XML polygons and enforces the installed binary reader's signed-short limits.
- Actual Chromium workflows verify main-map native sprites and physical model geometry, signed/shared basements, zoom/pan, catalog invalidation, bounded caches and 2048-tile overview; building floor/group clipboard, native transforms, alignment, diagnostics, format-8 palettes and provider conflict rejection; parcel review/atomic apply/undo and import source evidence/manual floor corrections.
- Unsupported sprite transforms reject the complete operation before changing the draft. Generated layouts preserve staircase cores and authored basements. Inactive palette references do not activate mods.
- Public history and affected historical Windows downloads receive the cleanup described in [Publication privacy](PUBLICATION-PRIVACY.md). Exact rewritten refs, retained assets and final package are audited before publication.
- On 2026-10-14 the user removed the in-game character playthrough from this task. Character traversal, lighting, loot, gameplay save/reload and multiplayer are not claimed as validated by this release.

## Earlier verification records

# Verification status — 0.7.2, 2026-10-13

## Public installation-path correction — 0.7.2

- Personal workspace and game/Workshop installation paths were removed from current public documentation, source defaults and instructions. Real screenshot path fields are masked; per-machine configuration is local and ignored by Git.
- Core 137/137 and Node 75/75 pass, including a new empty-configuration/no-game export regression. Release build has zero warnings/errors. Project debug symbols are omitted from publication and compiler source paths use a neutral prefix; public source/package checks guard against local-path disclosure.
- Earlier commits and release archives are preserved. These current-file corrections do not claim that past publication has been erased.


## English interface, installation and Actions startup — 0.7.1

- All authored UI text, accessibility labels, error messages, template descriptions, documentation and example instructions are English. Imported OSM names/tags and original source datasets retain their original language. Repository description and all seven earlier release descriptions were translated with metadata readback; historical release binaries remain unchanged.
- Core 136/136 and Node 75/75 pass; Release build has zero warnings/errors. Nineteen actual Chromium workflows pass against the English self-contained application, covering buildings, game/mod assets, basements, model profiles, environment, world assembly, planning, DEM and recovery. The official MCP client verifies both protocol versions and 38 tools.
- Five README screenshots were captured from the running application and visually reviewed. The Windows archive includes the detailed English installation guide and screenshots. All eight English native example archives pass independent cell/chunk/room readback.
- The extracted Windows package starts with an absent game and nonexistent DOTNET_ROOT, serves the English UI, validates/exports a native map, and passes real browser save/change/reopen checks. Startup checks stop only their own process in finally. The main editor is running version 0.7.1 with the installed game catalog.
- GitHub Actions now runs these published-app startup checks on pushes, pull requests and manual dispatch, and uploads the Windows ZIP plus startup logs/screenshots. It runs the application temporarily inside the job; it does not provide a permanent remotely accessible editor.
- Existing gameplay/persistence/multiplayer limits below remain unchanged. Latest release and workflow evidence are available on GitHub.

## Production expansion0.7.0

- Core136/136 and Node75/75 pass; Release build has zero warnings/errors. Official SDK clients verify38 MCP tools under both supported protocol versions, constrained map/world proposals, balanced section budgets, revision conflicts, full templates/shared building reads and signed native previews.
- Model profiles support pickup, scenery, obstacle, destructible and empty container. Installed IsoThumpable implementations block four directions, let scenery pass and create capacity75 containers; SpriteModel.Load and LuaCompiler accept exported declarations/glue. Blocking footprints are one tile, not mesh collision or structural wall support. Full graphical gameplay/persistence/multiplayer remain unverified.
-24 authored housing plans validate/export; independent reader checks1cell/1024chunks/1073rooms, installed classes recognise781upperrooms/2basementrooms/180stairs. Browser checks actual gallery, façades, balcony rails, annex footprint changes, history and roof preview.
- Shared buildings cross section borders without stored duplicates. Browser edits the shared basement house, imports/downloads DEM, displays height contrast, retains NODATA and undoes changes. Terrain Tiles example:256×256 measured samples, 161…269m across the downloaded tile. Native ground remains flat; elevation is a survey overlay/ASCII sidecar. Native reader/game loader:2cells/2048chunks/13rooms/4basementrooms/3basement stairs.
- Browser planning comparison requires explicit receive; shared house/DEM survive neighborhood edits and one-step world undo/redo. Collision sprite ownership is tracked; seam repair protects marked ground. Recovery streams bounded files, keeps three previous revisions and falls back from damaged current data. Large raster updates inspect4096 touched pixels instead of rebuilding4194304, with offscreen building culling.
- Maps7/templates5 preserve native model behavior; worlds2 preserve shared buildings/DEM. Earlier formats stay readable. Self-contained Windows publication pins.NET 10.0.12 with runtime/license notices; snapshot launches under nonexistent DOTNET_ROOT. Final extracted-package and GitHub evidence follows in PROGRESS.md.

## World, mods, models, MCP and environment0.6.0

- Core114/114 and Node60/60 pass; Release build has no warnings. Real X/FBX geometry and textures, basement placement, template4/project6 save and proxy export pass in isolated Chromium. Actual Workshop tdef/pack activation, previews, dependency/provider checks and atomic rejection of malformed mods pass against the installed game42.21.
- Sparse world API/UI validates9 adjacent sections2304×256, retained edits, overlap rejection, move/remove/history, save/open and one native export. Environment workshop validates complete sprite/model placement, actual N/W variants, group operations, ground/paths/regions and one-step map undo/redo.
- Official SDK client verifies21 MCP tools under2026-07-28 and2025-11-25. Browser sharing retains conflicting manual changes, imports an agent draft as one undoable action and revokes the session.
- Actual Overpass small Smolensk query completed with source date2026-10-13T07:54:29Z: common scale1m/tile, 4 native cells and8 buildings. UI progress/cache/date, complete-only application, world undo/redo and saved source pass. This is a223×193m area, not the entire city.
- One independent whole-delta review found four P2 issues. Consolidated fixes cover generated terrain/tree ownership, pruning unused requirements, retained large replacement undo and visible model scale. Focused executable fixtures and real geometry scale20-versus3 pixel measurements pass. Oversized replacement is refused before mutation; source world remains unchanged.
- `examples/Worlds/Worlds-B42.zip` independently validates16 cells, 16,384 chunks, 771 rooms, 14 cell-local building records and108 installed tile names. Installed game classes load580 upper rooms, 6 basement rooms and99 stair sprites, including6 basement stairs. Exported model proxy is accepted by the installed ScriptParser; placement glue is accepted by LuaCompiler without execution.
- Native Lua placement/loot/collision appearance and multiplayer behavior have not been playtested. Models are world inventory props, not structural walls. Publication/package verification is recorded in PROGRESS.md after final checks.

- Final 0.4.1 verification: core 72/72 and Node editor/model/resource/appearance 28/28 passed; Release build/publish zero errors/warnings. Eight isolated browser regression scenarios and the real Smolensk import/save/export flow passed.
- Published app in isolated headless Chromium: map editing/history/save/reopen/GeoJSON/export, delayed-import cancellation, invalid input rejection, Pilna recovery/session isolation, and the complete three-floor and basement -16 workshop cycles pass without uncaught browser errors.
- Workshop checks cover independent contours, room painting, walls/doors/windows/openings, balcony, stairs/removal/repair, furniture, floor copy/remove, material/roof, cloned draft cancellation, template roundtrip and project history. Template-only geographic imports retain the library through both GeoJSON and the offline OSM UI/request path.
- Nine archetypes use different layouts. A 675-case matrix covers legal small/maximum footprints, five shapes and three seeds; all generated designs validate. Legacy format1 remains readable; floors/library use format2, signed basements require format3/editor0.3.1+; legacy projects1/2 and templates1 remain readable.
- One independent whole-change review found four defects: disconnected small designs, lost template credit, erased library on geographic import, and partial/orientation-ignored furniture. One consolidated pass fixes all four; focused regressions pass. Object footprints/sprites share one catalog between browser and core.
- Pilna: 2048×2048,1577 buildings, 7345 native rooms, 52 two-floor and8 three-floor buildings. Source:2378 features /1789 building outlines;212 skips reported.42 level values come directly from OSM, 1 from height, 1534 are inferred.1470 contours use custom grid masks;107 are adapted rectangles. See [Pilna evidence](PILNA-STATUS.md).
- Independent ZIP/LOT reader verifies all64 cells /65,536 chunks of the stored example and a fresh browser export. All68 emitted identifiers exist in installed Build42 definitions. Furniture catalog entries also match actual Facing/SpriteGridPos properties and contain complete beds or single-square assets.
- Installed-game JAR probe loads actual lot headers/chunks using the game's own classes and tile definitions:64 cells, 65,536 chunks, 7345 rooms, 776 upper rooms, 204 recognised stair sprites. The harness confines Java caches and compiler files to work/ and does not change the installation or saves.
- Basement followup review fixed global rewrites of manual stairs and hidden-level selection. Existing old floors remain unchanged in the manual west-stair fixture; independent135-case full-depth sweep passed. See BUILDINGS-REVIEW.md.
- Signed basement example:1 cell /1024 chunks /160 rooms; installed-game loading found80 basement rooms down to-16 and48 below-ground stair sprites. Browser verifies add/remove/history/depth cap/ground preservation/project4/template3 roundtrip and signed top/iso views. A32-floor generator test covers16 below plus16 above; native staircase holes through ground and remote terrain are read back independently.
- Network search and initial real Overpass download succeeded; later requests returned504. Fixed source JSON makes regeneration reproducible offline. The offline OSM UI test does not claim fresh network success.
- History retains changed building/library entries rather than full collections;20 small terrain edits with1000 rich buildings remain under4MiB. General history budget64MiB/30 actions; draft history16MiB/30.
- Ready Windows package0.7 includes the.NET runtime, without a separate installation. Source/example/package publication is in the public [GitHub repository](https://github.com/DinLemon/SimplePZMapmaker), default branch develop. Distribution launch evidence is recorded in PROGRESS.md.

## Installed resources0.4.0

- Local catalog reads30256 identifiers from primary/erosion/jumbo definitions and indexes embedded PNG pages in six installed `.pack` files; legacy0 and PZPK1 are supported. Synthetic pack bounds/truncation tests and real page decoding pass. Game images and definitions are absent from tracked files/release payloads; only local work/assets cache contains extracted pages.
- Fresh Chromium assets flow passes direct editor entry/standalone template, six styles, actual sprite preview, floor/roof picks, template placement, whole world furniture, surface/history and exact saved-project roundtrip. Compact1280×720 workshop and1600×1100 catalog/scene screenshots were visually inspected. All six palettes' furniture Facing/SpriteGridPos and complete roof/floor/wall images were verified against installed metadata.
- One fresh whole-change review found tree/decor overlap, unbounded decoded atlases and false legacy style counts. One consolidated pass suppresses automatic vegetation over every explicit part, keeps native tree/navigation semantics, caps estimated RGBA cache/scene pages at192MiB, decodes at most two pages concurrently and counts only buildings with actual designs. Focused regressions pass; process RSS/GPU memory is not claimed to equal that pixel budget.
- Forged partial furniture and wrong-direction walls are rejected. A selected outdoor floor preserves the staircase hole above it. Project4/template3 guard appearance references; old project1–3/template1–2 remain readable.
- Full Pilna style application covers1577 buildings across all six styles; serialized project16.6MB, local apply plus region preview about1.44s in this run. This is a measured sample, not a general performance guarantee.
- Resources example:1 cell/1024 chunks/84 rooms/6 buildings/122 identifiers. Independent LOT reader and installed-game classes verify all identifiers, 28 upper rooms, 28 basement rooms and36 staircase sprites (18 underground). The game installation/saves were not modified.

## Appearance areas and Smolensk 0.4.1

- A persistent top-level “Map appearance” entry opens scope selection, preset cards, actual-sprite preview and an explicit Apply action. Area scope returns to the map for rectangle selection. Preview does not mutate the project; Apply enters history as one action. Terrain geometry, water, spawns and explicit surface/decor references remain intact. Optional building styling affects only fully contained generated designs, preserving manually edited buildings.
- Up to 128 regions are persisted in project format5. Last-applied overlap wins; identical bounds replace their prior entry; applying to the whole map clears region presets. Format4 projects remain readable and stay at4 until a region is added. Undo/redo, save/reopen, regeneration and preserve-edits imports retain region data; geographic replacement without preservation clears it.
- Core regressions decode actual four-cell `.lotpack` seams, overlap, manual surfaces/decor, water and forest sprite parity with preview. Invalid bounds/themes and a 129th region are rejected before mutation; replacement at the cap succeeds. Installed-game native QA loads 4 cells / 4096 chunks / 10 rooms, including 5 upper rooms and 3 stair sprites; all 30 emitted identifiers resolve in the installation.
- OSM online requests retry transient 504/timeout failures against separate providers and cache complete responses for seven days. Partial Overpass timeout results are rejected. Real Overpass 504 remains an external availability problem; a mocked failure/cancellation cycle proves the existing project is retained, without claiming a fresh successful online import.
- The visible source selector offers a separate read-only OSM France regional PBF for Smolensk, with an initial download of about 44 MB, a 64 MiB cap and a seven-day local cache under `work/geodata-cache/`. The captured extract timestamp is 2026-10-11T00:29:05Z. A selected centre area 1.5 km wide is the test scope, not the whole city or whole oblast. Source date/cache provenance and omitted incomplete relations are reported to the user.
- Release example `examples/Smolensk/` is a 1024×1024 map of the 1.5 km area centred at 54.7789701° N, 32.0471812° E: 352 placed buildings from 772 source outlines, 420 explicit skips. Final normalized OSM JSON, recipe/date, attribution, editable project and native archive are included; replaying the stored source reproduces terrain and building counts. It shares world coordinates starting at 100,100 with other examples: enable separately or move/re-export.
- One independent whole-delta review confirmed a PBF selection defect: crossing roads and containing polygons vanished without in-box vertices. One consolidated fix selects complete intersecting/containing geometry, stitches relation contours and retains holes. Focused regressions include the original 50,289-water-cell repro, budgets, missing nodes and cancellation. Incomplete ways/relations are omitted whole with source-wide warnings; the snapshot contains 90 incomplete ways, including objects outside the selected area.
- Actual regional file: 6,719,566 nodes, 441,498 ways and 7,286,681 references. Explicit caps: 8 million nodes, 500,000 ways, 10 million references and 30 million geometry visits. The compact node index cap estimates 192 MiB, with construction temporaries in addition; this is not total process RSS. Two final cached reads took 6.82/6.84 seconds. Cancellation after 250/2500 ms completed at 267/2510 ms without changing the cache.
- Real UI search, source choice, 1.5 km import, save and native export completed in 18.61 seconds in this run. Independent archive reader checks 16 cells / 16,384 chunks / 4,441 rooms / 164 identifiers. Installed-game classes also load all cells/chunks/rooms, including 2,812 upper rooms and 1,320 staircase sprites. These are bounded observations, not an SLA or graphical playtest.

**Character traversal, graphical in-game roofs/furniture, loot and multiplayer remain unverified.** Native class parsing and isolated staircase recognition establish only those checked behaviours. The map overview is schematic; dedicated previews draw real local sprites from the exporter's layout, without the game's renderer/lighting. Roofs and source interiors are generated approximations. B41, heightfields and a separate railway layer are not implemented.

All task files, QA, downloads and caches stay under the project workspace. The exporter references tile identifiers and distributes no game artwork. Code license is GPL-2.0-or-later; source/derived geography retain OpenStreetMap attribution and ODbL requirements.
- Extracted final0.4.1 Windows package: version/workspace identity, NoBrowser launch, installed catalog and full appearance preview/apply/history/save/reopen/cancel scenario passed. Distribution contains no game artwork/PBF/cache; main workspace server restored.

## Visual workshop 0.5.0 — 2026-10-13

- Core83/83 and Node40/40 passed. The default native-sprite workshop, embedded palette, viewport/hit/ghost/readonly/draft/history/library/project/export flow and real edge-kind conversions pass in isolated Chromium, including the extracted ready Windows package.
- One independent whole-delta review found an incompatible retained wall/opening frame. One consolidated fix and focused N/W and real-catalog conversion regressions pass; strict export validation was never bypassed.
- Six housing templates use original distinct layouts and linked architectural references. Native game classes load 641 rooms, 509 upper and2 below-ground, 78 stairs across6 buildings. Edited resource QA also loads15 rooms and6 stairs. Dedicated Pilna button removed; normal example file loading/regression passes.
- Main server restored to the project workspace. Ready payload excludes game artwork/mod downloads/caches. Character traversal and game lighting/loot/multiplayer remain unverified.
