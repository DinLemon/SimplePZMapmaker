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
