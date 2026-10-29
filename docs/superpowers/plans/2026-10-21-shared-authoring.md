# Shared authoring and external exchange

Goal: one reviewed settlement workflow; common placement, export and building operations; explicit, diagnosable external exchange.
Architecture: preserve MapProject/WorldProject JSON compatibility. Generation Studio owns preview/apply snapshots and uses contextual or template placement strategies. Legacy town creation remains an API compatibility route. ParcelPlacement owns blocked cells and reserved pedestrian access. Generated fences are FenceLine geometry; old decoration fences remain valid. Export readers retain their lifetimes and stream behavior, while a shared writer owns metadata serialization.
Stack: .NET 10 Core/Web/CLI, browser ES modules, custom C# runner and Node tests.
Constraints: preserve existing dirty frontend/OSM work, no real Zomboid directory writes, no destructive migrations, no silent exchange losses. All new UI entries belong to existing menus. Third-party mapping data needs source/version/license attribution.

- [x] Common parcel occupancy and passage policy used by planner, ensembles and OSM dressing; full model footprints, fences, infrastructure and authored ground protected.
- [x] New planner and ensemble fences use FenceLines, bounded plans apply atomically; old decoration projects still compose.
- [x] Contextual/templates strategies share Generation Studio preview/apply, area and protections; legacy generation removed from primary choices.
- [x] Common export metadata writer, parity and streaming lifetime tests.
- [x] Shared building transactions and validation in workshop and map editor, retaining independent draft.
- [x] Versioned street catalog with actual upstream curb/parking/drain mappings and bounded neighborhood rules; preview/apply.
- [x] Safe TBX/TMX subset codecs, unsupported-feature report, editor import/export.
- [x] Project Doctor aggregates validation/access/resources/zones/seams/conflicts with explicit unavailable coverage.
- [x] Tile snapshots compare stable IDs/properties and replacement references; accessible diagnostics.
- [x] Integrate endpoint/UI/CLI entry points, tests, source/license documentation and runtime verification.

Review focus: stale preview rejection, bounds/budgets, no unintended flattening of streamed world, preservation of authored cells and entrance paths, unsupported external entities are reported, XML does not resolve external entities, catalog application is atomic.

Verification: Release solution build has zero warnings/errors; 504/504 C# with installed Build 42, 344/344 Node. Real Chrome passes both shared authoring and existing frontend regression scenarios. CLI TBX → JSON → TMX → JSON, tile comparison and Doctor run against the installed game passed. Evidence: work/shared-csharp.txt, work/shared-node.txt, work/shared-release.txt, work/shared-browser.txt, work/shared-frontend-regression.txt, work/shared-authoring-verification/result.json. External BuildingEd/TileZed GUI loading remains explicitly outside the verified boundary of the documented exchange subset.
