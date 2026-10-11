# Verification status — 0.2.0, 2026-10-12

- Core regression suite: 27/27 passed. Editor regression suite: 11/11 passed. Release build and publish: zero errors/warnings.
- Actual published application in headless Chromium: canvas edits, undo/redo, project save/reopen, GeoJSON import, ZIP download, isometric view, compact viewport, cross-origin/invalid-project rejection and accessible cancellation of delayed import all pass without uncaught browser errors.
- Real Pilna: 2048×2048 map, 1439 buildings from 1789 source outlines. Whole-place search bounds, source attribution, geographical scale, legacy autosave migration, 2048 recovery after reload and independent browser sessions verified. See [Pilna evidence](PILNA-STATUS.md) and [example](../examples/Pilna/README.md).
- Independent native ZIP verifier reads all 64 cells / 65,536 chunks / 4281 rooms of both the committed Pilna mod and a fresh browser export. All 20 emitted tile names exist in installed Build 42 definitions.
- Nominatim search and initial live Overpass download succeeded. Later Overpass requests returned 504; the committed real source JSON makes the example reproducible offline. Network reliability is not claimed.
- Fresh review: three material findings fixed in one pass (large history allocation, browser storage quota, stale launcher version). Full-field history experiment verifies exact undo/redo and the 64 MiB retention budget. Original 0.1 review fixes remain covered by regression tests.
- Launcher checked with `-NoBrowser` against the published application. Temporary files, caches, autosaves, logs, QA tools and screenshots remain beneath `work/` in the requested workspace.
- Private [GitHub repository](https://github.com/DinLemon/SimplePZMapmaker), default branch develop: initial source/example HEAD verified via API; Windows Actions run38183714437 succeeded at0b27575 (tests, build, publish, native verification).
- Packaged Windows archive extracted and launched from a separate workspace on G:. Packaged server passed the Pilna browser cycle,2048 recovery and legacy migration; launcher resolves the extracted workspace. Requires .NET10 ASP.NET Core Runtime, not an SDK.

**Actual in-game playthrough, roof visibility, loot and multiplayer compatibility remain unverified.** File-format checks do not establish playability. B41, heightfields, railway geometry and multistorey buildings are not implemented; the isometric view is schematic. Building footprints/interiors are simplified and skipped outlines are reported explicitly.

The app exports a mod but does not modify the installed game or saves. Code license is GPL-2.0-or-later; source geography and derived data retain OpenStreetMap attribution and ODbL requirements. Exported files refer to game tile identifiers without distributing game artwork.
