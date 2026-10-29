# KnoxMap adaptation implementation plan

> **For agentic workers:** Use superpowers:executing-plans for the local work; independent Overture and game-ramp investigations may run in parallel. Track each task below.

**Goal:** Bring useful KnoxMap capabilities into the existing SimplePZMapmaker import and authoring workflows.
**Architecture:** Keep the .NET importer, common world projection, section storage and native exporter. Adapt selected MIT algorithms into Core; connect them through the existing OSM world dialog and saved-source store. Existing projects and manual edits retain their geographic frame.
**Tech stack:** .NET 10, ASP.NET, browser ES modules, custom C# test runner, node:test.
**Spec:** User request to copy/adapt spytheeuclidean-a11y/knoxmap; upstream pinned at `5d8195f94f751264d3b4fd6b0098e6e3dcf3df64`.

## Global constraints

- Preserve all inherited working-tree changes and the running editor on port 48779.
- New MIT modules can be adapted with the upstream notice; excluded legacy Knoxify files have no published licence and are not copied. WorldEd code requires its separate GPL notices.
- No new parallel generator/editor and no silent rotation of existing worlds.
- New sources retain identity, attribution and uncertainty. An inferred roofprint is not a verified interior.
- Game checks use isolated caches; server/native verification does not establish client driving or electricity behavior.

## Review focus

- Clockwise versus counterclockwise conventions: a suggested rotation must actually straighten projected source roads.
- Different neighbourhood grids: select the dominant district rather than averaging into a direction used by neither.
- Long segments outside the chosen area: clip before measuring; remote roads must not influence the result.
- Stale source/frame or cancellation: never apply a suggestion from different bounds or mutate the live world.
- Secondary buildings: preserve holes and source identity, suppress duplicates, and report skipped/malformed geometry.

## First integration

### 1. Street-grid orientation

- [x] Add `GeographicImporter.SuggestStreetGrid(string json, GeoBounds bounds, CancellationToken token)` and a result with nullable rotation, strength, road length and district count.
- [x] Port the fourfold angular vote and 400 m district clustering from MIT `generator/rendering/gridangle.py`; add segment clipping and splitting so geometry outside the selection and source segment density cannot bias the vote.
- [x] C# tests: angle/sign and real projection, district dominance, 90-degree equivalence, long crossing/outside segments, excluded motorways/paths, weak/no evidence, malformed input and cancellation.
- [x] Add an origin-protected POST analysis endpoint using the existing source client/cache, bounded data and timeout. Return a saved source id for reuse.
- [x] Add explicit detect/apply controls to existing advanced world layout settings; lock while importing/preserving/deforming and discard stale analyses. Test the UI state in node and the real isolated browser.

### 2. Overture gap-fill adapter

- [x] Add an explicit saved-GeoJSON supplement adapter in Core; accept official Overture fields, map to existing import tags, preserve original geometry/holes, attribute output and deduplicate against mapped buildings.
- [x] Wire it to the saved-source API and existing import dialog, without requiring Python/DuckDB just to open the editor.
- [x] Verify realistic baseline + supplement fixtures, bounds, limits, duplicate sources and cancellation.

### 3. Native road ramps research gate

- [x] Verify upstream `ramps_01` assumptions against the installed Build 42 tile definitions/classes; document evidence and any limitations. Catalog now loads their binary definitions and installed texture pages; the old no-vehicle-ramps warning is corrected.
- [x] Implement the bounded cardinal profile, collisions, local/shared rendering and native export contract; keep pedestrian stair access available. Native B42 readback passes all 80 segment IDs/four directions; client driving remains an open acceptance gate.

### 4. Compile and explicit install in the import wizard

- [x] Reuse the existing native export queue and archive installation endpoint in the import dialog; expose a separate install confirmation and action for the completed archive.
- [x] Prefer section publication directly for wizard imports, preserving small portable results for older callers without the new preference.
- [x] Check stale results, names, source/frame changes, retries and explicit installation in browser module tests.
- [x] Run the actual area → source supplement → furnished world → streamed native export → isolated install chain. Read the archive with installed B42 classes, independently of our own exporter.
- [ ] Complete the separate terrain/building acceptance previews, dependency review, client gameplay gates and all remaining capabilities in `docs/KNOXMAP-PIPELINE.md`.

## Execution ledger

- Scope clarified by the user: the full creation pipeline and all listed features remain the destination. The inventory tracks 33 capability rows; the current integration does not claim full parity.
- Ruling: preserve inherited working-tree changes and keep this task uncommitted until requested; no unrelated cleanup or branch merge.
- Ruling: saved Overture GeoJSON is the first provider adapter. Online GeoParquet acquisition remains explicit follow-on work, not an automatic dependency installation.
- Ruling: named input dates must not become a false single date for a composed source. Retain original per-feature metadata and report the baseline date separately.
- Browser/API proof: `work/knoxmap-research/pipeline-browser-proof.json`; original map untouched, composed source contains one additional building and skips one existing building, stream export posts `{}` to the workspace export endpoint. Installation is under `work/knoxmap-research/isolated/mods`.
- Native game proof: `work/knoxmap-research/native-game-proof.txt`, 1 cell / 1,024 chunks / 38 rooms / 17 upper rooms / 3 stair tiles. Exported source includes 2 buildings, 106 furniture objects and one staircase. Client walking, driving, lift and power behavior are separate gates.
- Independent research: `work/knoxmap-research/ramps.md`, native tile definitions, mesh models and game physics code; no game artwork copied into product source.
- Ramp integration: `docs/NATIVE-ROAD-RAMPS.md`; full registered C# suite 906/906 and JS 474/474. Isolated API/streamed export/native game proof is in `work/knoxmap-research/road-ramps/`: four native cells, 960 slope tiles, all 80 IDs/four directions, actual first-floor and edge-height checks. Fresh browser authoring snaps nodes to integer cardinal profiles and preserves the map until Apply; zero browser errors. Independent review found a stair/support/guard collision, now rejected atomically in both placement orders locally and across sections.

## Follow-on adaptations (not claimed implemented by the first integration)

- Overture online acquisition: optional provider, pinned release, cancellable query, cache and download-size information; reuse saved-source adapter above.
- Road network snapping and kerb junction templates: compare with our geographic warp/street catalogue; retain one shared frame and cross-section continuity.
- Building layout checks: entrance reachability, room-use constraints, stairs clearances and furnishing audits in Project Doctor rather than a second editor.
- Population-derived zombie estimates with disclosed census date/coverage; keep manual density polygons authoritative.
- Build manifests/incremental dependency hashes and reproducible source reports, building on the existing section cache.
- Place-boundary/shape selection if it adds to current bounds workflow; preserve through roads and water deliberately.

## Verification

- Targeted C# and JS tests must fail before implementation and pass afterward.
- Run complete C#/JS suites once integration is stable; retain actual output and distinguish inherited failures.
- Isolated live API/browser check of analysis, apply, source reuse and frame locking; do not reload the user's live map.
- Record files adapted and the exact upstream commit in `docs/licenses/KNOXMAP.md`.

## Transport follow-on integration

- [x] One road-width policy for import and roadside placement; unit/estimate/lane/class hierarchy, source evidence, edited paths and a shared editable/static stroke.
- [x] Polygon-contained native parking rows with holes/occupancy/rear aisles, source refresh and authored-rectangle preservation; ambiguous legacy layouts require explicit review rather than speculative deletion.
- [x] Conservative native E/S fuel pumps, bounded forecourt/road access, provider/evidence retention, closed-site inheritance and baseline-based authored-edit preservation.
- [x] Existing export-queue notifications update the wizard automatically, still bound to the same source/workspace revision; no second poller.
- [x] Isolated live browser: 7.4 m road, 283 native stalls/31 rows, one native pump, source-to-section-to-streamed-compile-to-explicit-isolated-install, closed-parent point suppression, zero browser errors. Actual installed cell files byte-match the compiled archive.
- [x] Actual B42 native readback: 1 cell/1,024 chunks/10 rooms/5 upper rooms/3 stair tiles, positive fuel capacity and native facing, controlled quantity getter 137/0. No forced fuel/power Lua was exported.
- [ ] Client car spawns, driving/turning/ramp traversal, fuel initialization/power/hose interaction; whole-world coordinated parking/forecourt planning and remaining inventory stages.

Proofs: `work/knoxmap-research/transport-import/`; tests and precise limits: `docs/TRANSPORT-SOURCE-LAYOUT.md`.

Final transport checks: 966/966 C#, 476/476 JS; published isolated Web instance on port 48813, user editor 48779 unchanged. Review RED/GREEN regressions and actual native/installed-archive readback are retained. These checks close the transport increment, not the remaining whole-pipeline/client gates.
