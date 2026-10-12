# Progress — docs/superpowers/plans/2026-10-12-mapmaker.md

- Workspace: new, dedicated repository on develop at the requested path. No shared main branch exists; in-place development keeps the requested path usable.
- Authorization: user supplied full carte blanche; brainstorming question/approval gates bypassed under its explicit opt-out rule.
- Execution: inline; fresh whole-branch reviewer at finish under executing-plans.
- Preflight: shared MapProject JSON contract connects core, importer, exporter and UI; serialized camelCase with string enums, arrays indexed y*width+x.
- Source research: installed PZ has Build 42 tile definitions; native LOT format inspected in GPL PZ_Mapping_Tools. No Knoxify/KnoxMap code copied.
Task 1 and 2: core generation and geographic fixtures verified, 10/10 passed. RED observed before each implementation.
Tasks 3/4: native LOT, navigation and worldmap readback fixtures pass; 14 core tests and 5 editor tests. Browser launched from project-local Chromium and actual editor creates maps without errors. Live Nominatim search succeeded.
Task 5: published app verified after fix pass. 22/22 core and 8/8 editor regressions green. Actual published-host browser cycles and review regressions green.
Final review: fresh agent, HEAD 048cd57, seven Important findings and four initially Minor notes, no Critical. Notes retained in work/final-review.md.
Final: fixed OSM ring stitching/holes — fragmented outer + island fixture RED→GREEN.
Final: fixed polygon work — 12,001-point full map took 53.98s RED; scanline raster + 40M work budget and row cancellation now passes <4s.
Final: fixed upstream buffering — 20MB fake responses with/without Content-Length RED→GREEN; headers-only response handling + body deadline.
Final: fixed nested null fields — observed real HTTP500; core guards and published HTTP400 checks green.
Final: fixed navigation flags — decoded water/tree/door flags RED→GREEN against upstream tile semantics; tree sprites excluded near spawns.
Final: fixed pending edit history — resurrection reproduction RED→GREEN in editor suite.
Final: fixed busy UI — active progress dialog, frozen project interactions, reachable cancellation, unified file load lifecycle; delayed import cancellation browser verified.
Final: Ruling: seed/bounds, footprint parity and checkout identity notes upgraded for fix — accepting an unexportable project, overlapping external walls or opening another checkout can mislead a non-programmer. All addressed with regressions/manual launcher checks. No deferred minors remain; plan progress updated.
Native verification: 16 emitted tile names found in the installed game's 29,058 named tile definitions. 1024x1024/150-building export before final fix pass took 0.87s, ZIP1,010,573 bytes. No in-game playthrough claimed.
Live network: Nominatim succeeds; public Overpass import not validated because servers returned504/500/timeout. Offline GeoJSON/Overpass fixtures work.

## Pilna continuation — docs/superpowers/plans/2026-10-12-pilna.md

- User authorized full development, real Pilna import and GitHub repository creation/push. Files and caches remain in the supplied G: workspace.
- Initial live Overpass request succeeded: 2378 features / 1789 building outlines. A later application request returned504. Captured source is committed with ODbL attribution for reproducible offline import.
- Tasks1/2: tests observed RED before2048 support and metre-scaled roads; final27 core /11 editor tests pass. Imported1439 buildings, skipped350 with explicit reasons. Published UI verifies whole-place bounds, example loading, project roundtrip and native export.
- Task3: independent fresh reviewer found history allocation, localStorage quota and stale-server acceptance. One fix pass added byte-page history/64MiB budget, workspace recovery with browser-id isolation/legacy migration, exact launcher version check. Core/editor/browser regressions, full-field undo/redo/budget experiment and launcher smoke pass. No game playthrough claimed.
- Native audit: both stored example and actual browser ZIP pass independent readback of64 cells,65,536 chunks,4281 rooms,1439 buildings and20 installed tile names.
- Task4: private DinLemon/SimplePZMapmaker created; develop pushed and made default. Source/example files verified remotely, HEAD matched0b27575. No task caches, credentials or game artwork uploaded. GitHub Windows Actions passed core/editor tests, publish and independent native verification.
- Distribution: packaged published application plus launcher/examples/licenses; extracted into a separate G: workspace. Actual packaged server passed legacy migration,2048 recovery/reload, source search bounds, save/reopen/export and launcher identity checks. Repository server restored afterward. SDK-free package requires.NET10 ASP.NET Core Runtime.

## Building workshop0.3 — approved full scope

- Project format2 with legacy1 reader;1–16 floors, independent masks/rooms/edges/objects, nine archetypes, deterministic variants, four roof shapes and three facade materials.
- Native exporter writes real upper room levels/chunks, staircase holes, terraces/balconies, roofs and complete oriented furniture. Source import reads types/levels/height/roofs and retains modified buildings ahead of fresh outlines. Library and every attribution source survive reuse/import/export.
- Actual workshop supports floor copy/remove, arbitrary contour/rooms/walls/doors/windows/openings/stairs/furniture, local history, cloned apply/cancel, shared templates, per-floor/iso views and compact map history.
- Tests observed RED for small generation, partial/bounded furniture and complete native arrangements before the review fix. One fresh reviewer produced four P2 findings; one consolidated pass resolves them (BUILDINGS-REVIEW.md). Final41 core/19 Node tests, five published-browser suites and native readers pass;675 generated legal combinations validate. Builds/publish have zero warnings/errors.
- Updated real Pilna:1577 buildings,212 explicit skips,7345 rooms,60 multistorey buildings. Stored and browser ZIPs have64 cells/65,536 chunks/68 installed tile identifiers. Source project13,665,269bytes; mod3,675,190bytes.
- Installed game JAR probe:64 cells/65,536 chunks/7345 rooms/776 upper rooms/204 stair sprites, status passed. It establishes parser and selected sprite behaviour; character traversal/visual roofs/loot/multiplayer remain unverified. All probe/compiler/cache files remain in work/ on G:.
- Distribution: ready v0.3 ZIP extracted to a fresh G: workspace. Its actual executable and -NoBrowser launcher passed identity checks, Pilna recovery/search/save/export, the expanded workshop cycle and both template/library review regressions. A QA readiness race was corrected: independent-session assertions wait for completed startup instead of the initial256 placeholder. Repository server restored after package verification.

## Basements0.3.1 — user requested -1…-16

- Added signed MinLevel with contiguous floors including ground;16 below and16 above can coexist. Project3/template2 prevent old readers relocating basement floors; previous projects1/2 and templates1 remain readable.
- Native minimum levels, signed room IDs/chunk data, ground staircase holes and unchanged remote surface are covered by readback; underground parts are omitted from the surface world map. OSM underground-level tags and regeneration/manual preservation retain signed depth.
- Workshop adds/removes bottom basements, ground stays mandatory; negative map levels, placement depth, copied floors/history/templates/source labels supported. Live Chromium cycle through-16 passes; generated32-floor map validates.
- Initial signed model/export and underground-window/entrance tests observed RED, then GREEN. Final46 core/22 Node tests and six browser suites pass. Native probe:1cell/1024chunks/160rooms/80basementrooms/min-16/93stairtiles/48basementstairs, passed. Character traversal remains unverified.
- Fresh basement reviewer reproduced global stair rewrites and hidden-level selection. One consolidated pass installs only the new link and shares visibility for rendering/hit-testing; existing west-oriented links, manual edges, rooms and furniture compare unchanged in Node/browser fixtures. Independent135-case full16+16 sweep passed. Hotel reception uses true ground index; RED→GREEN regression covers it. Final46 core/22 Node tests and six published-browser suites pass.

- Distribution: extracted0.3.1 ready archive passed exact workspace/version identity, -NoBrowser launcher, full basement/manual-preservation/visible-selection cycle and legacy Pilna recovery/search/export. Final browser basement ZIP independently validates; installed-game loading of the committed full-depth example again passes. Repository app restored before delivery.

## Installed resources0.4.0

- Added local bounded legacy0/PZPK1 PNG pack reader, primary/erosion/jumbo metadata catalog, source picker, page cache, thumbnails/search/categories and complete movable groups with facing/physics checks.
- Project4/template3 retain explicit floor/edge/opening/roof choices, complete objects and world surfaces/decor. Shared six-profile JSON drives procedural buildings and furniture; environment presets supply actual tree resources.
- NativeScene uses TileComposer for building/region previews. Browser direct workshop and standalone templates, exact manual references, histories and recovery roundtrips verified. Backend/browser optional-field normalization prevents false manual-regeneration differences.
- Real QA caught/fixed incorrect industrial wall palette, source roof palette's six absent index gaps, numeric preserve-import flag, and selected outdoor floors sealing stair holes. Six styles' emitted images and furniture groups checked against installed metadata.
- Existing browser suites and new verify-assets flow pass. Installed-game probe loads resources example1cell/1024chunks/84rooms and recognises36stairs. Full Pilna automatic styling and region preview measured1437ms/16.6MB locally. Final independent review, consolidated fixes and distribution evidence follow below.
- Independent reviewer returned three findings; one consolidated fix pass suppresses auto trees beneath all decor parts, bounds atlas pixels/scene requirements at192MiB and decode concurrency at2, releases scene pins and corrects legacy no-op counts. Focused RED tree regression is now GREEN, cache/physics/legacy API regressions pass. Final54core/24Node tests pass. Without the local catalog, automatic new maps/buildings fall back to the legacy palette while explicit resources remain validated before use/export.
- Extracted0.4.0 ready ZIP passed exact version/workspace identity, NoBrowser launcher and full assets browser cycle. A separate missing-install launch successfully generated a legacy map/building and exported a native mod; independent reader validates its tile references. Main workspace server restored with the real catalog. Ready payload contains no extracted PNG/packs/game binaries.
