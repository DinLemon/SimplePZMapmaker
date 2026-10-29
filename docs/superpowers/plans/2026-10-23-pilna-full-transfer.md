# Full Pilna transfer plan

**Goal:** Reconstruct the complete supplied Pilna extent as connected Build 42 sections at one metre per tile, using attributed geographic and photographic evidence, with inferred interiors explicitly distinguished.

**Architecture:** Keep one geographic frame (expanded village bounds W45.88174/E45.94375/S55.53538/N55.57899 from the recovered upstream village scene, rotation −11.22°, native origin 150,150). Build section workspaces rather than loading the whole world into a 128 MiB agent/browser compatibility document. Apply agent proposals per bounded section; retain shared seam geometry. Keep source inventories, per-feature evidence and coverage metrics beside outputs. Existing user-connected map remains available while the world is built separately.

**Tech stack:** Existing .NET CLI/Core/Web, native MCP transport, source-normalization APIs, section workspace storage, native B42 probes and the existing separate sprite-mod workflow. No real Zomboid profile writes.

**Acceptance:** All terrain sections in the selected extent are present, source omissions/conflicts are resolved or explicitly remain open, surveyed parcels/fences/outbuildings retain real identities, every claimed photographic observation has actual inspected pixels plus provenance/location, and unobserved/interior details remain inferred. Do not call a baseline OSM build or a few landmark photos a complete photo-based transfer. Native resource checks, seam checks, representative and problem-area pedestrian checks, installed game-class loading and fresh browser section inspection must pass. Report game-client playtest separately.

**Input limits:** The supplied table is an input hypothesis, not proof that the named private data/scripts exist locally or that service coverage is still available. Recheck those facts. Do not collect account tokens, bypass provider blocks or disable certificate verification. Existing attributed offline exports are valid inputs; Google/Yandex public navigation is reference viewing, not an undocumented bulk-tile import adapter. District material percentages are priors, never facts about a specific house.

## Work

- [x] Inventory actual local OSM/Yandex/cadastral/testimony/photo inputs and reconcile scope with the quoted counts.
- [ ] Collect and inspect geographically verified real reference images; record capture/source dates, creator, license and uncertainty. Measure facade/fence/road/photo coverage.
- [x] Build the full common-frame OSM baseline including terrain-only sections, save a distinct live editor workspace and keep a recoverable user-map backup.
- [ ] Merge corroborated parcels, outbuildings, fences, paths, road surfaces, building storeys/material/year and resident overrides. Resolve duplicate identities, large buildings, overlap and seam exclusions rather than hiding losses.
- [ ] Design per-building exterior/environment proposals from references; preserve factual shapes and source tags. Infer layouts with the user's regional priors and access constraints. Create missing original game sprites in a separate validated mod only where references establish a need.
- [ ] Audit all remaining omissions and evidence gaps; check pedestrian access/rooms/stairs, B42 metadata, biomes, spawns and cell conflicts. Export sections/world and validate with installed game classes and an isolated server.
- [ ] Verify actual editor section workflow and record artifacts, reference coverage, inferred details, unresolved source blockers and client-playtest limits. Do not overwrite the current map or claim a complete reconstruction prematurely.

## Ownership

Main agent: scene/world/MCP workflow and NSPD checks. Independent local inventory: `work/pilna-full-transfer/source-inventory/`. Independent photographic evidence: `work/pilna-full-transfer/photo-sources/`. All other run artifacts: `work/pilna-full-transfer/`.

## Verified checkpoint 2026-10-24

Recovered upstream commit 6cccb9c86b0536eeea2a187ff43ba3f9a78fb4ad. Distinct section workspace ef6730f54920252e8201b68252667e66 revision 2 has 30 sections, 3456 placed of 5045 source buildings, 703 actual fence source identities. Thirteen frames and eight sheets inspected, not complete facade coverage. Source reconciliation and photographic limitations remain explicit in work/pilna-full-transfer/README.md, status.json and placement-audit.json. Native game-class probe and fresh section/native-view UI checks pass. Work is not marked complete.
