# Production expansion0.7

All work, packages and caches remain in the project workspace. Game and Workshop installations are read-only. Existing0.6 projects/templates remain readable. Full implementation, verification and GitHub publication are authorized.

1. Model behavior: verified game-supported collision footprints and scenery/container properties, editor controls, backward-compatible default world-item behavior, persistent native placement.
2. Geography: absolute-coordinate world buildings spanning sections; clipping during coherent native export; road/water seam diagnostics and deliberate repair preserving occupied/manual terrain. Import actual elevation grids with source/projection/missing-data metadata and truthful handling of game's flat ground constraints.
3. Architecture: roof/facade/extension/balcony controls, transactional geometry editing, at least24 original editable regional templates, native output and actual palette resources.
4. Agents: map/world sessions and constrained neighborhood proposals; revision-protected commits; browser comparison before receiving changes; protocol/native tests. Proposals are isolated and never silently replace manual work.
5. Reliability: streamed bounded atomic persistence, retained recovery revisions with fallback, visible recovery selection, cheaper overview/large-history operations, actionable dependency diagnostics; self-contained Windows runtime package.

Three agents own model behavior, geography and architecture respectively. Root owns agent planning, recovery/performance/package integration and publication. Meaningful targeted regression failures precede fixes; real browser/native/runtime verification follows integration. Structural collision or gameplay claims require installed API/source proof; no fake elevation/slope support.
