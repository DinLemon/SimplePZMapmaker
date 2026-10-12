# Building workshop independent review resolution

A fresh whole-change reviewer inspected the final feature and reproduced four P2 defects. One consolidated fix pass addressed them:

- Generated stair corridors split equal room codes into disconnected components. Door placement now uses connected regions; generated designs validate before return. The formerly failing675-case matrix (69 failures) passes, including6×6 and64×64 shapes.
- Template export/import replaced building-specific attribution with the outer list. Both lists are now validated and merged; a live roundtrip with an empty outer envelope preserves the author, and native attribution includes map/building/library sources.
- Geographic import submitted a blank project when there were no manual buildings, losing the saved library. Both UI paths now carry library/provenance independently. Live GeoJSON and offline OSM request-path regressions pass with only a library present.
- Furniture emitted partial bed/couch/wardrobe sprites and ignored direction. One shared JSON catalog defines complete two-square beds and single-square chairs/drawers/counters/sinks. Generation, both validators, workshop preview/removal and native export reserve their full cells. Installed Facing/SpriteGridPos checks match every arrangement.

Verification:41 core tests,19 Node tests and five browser suites pass; no build warnings/errors. Pilna native structure and installed-game loader/sprite probe pass. No complete graphical cell traversal, character walking, roof rendering, loot or multiplayer verification is claimed.
## Basements0.3.1 review resolution

One fresh independent review reproduced two P2 defects. Adding a basement globally regenerated all old stair links; signed views still selected the union of hidden geometry. The consolidated pass now installs stairs only on the new adjacent pair, reuses valid manual west/north runs, protects existing furniture/manual edges and keeps unrelated floor data unchanged. Rendering and selection share a visible-level predicate; missing levels and basement-only extensions cannot be selected through the surface. Live browser regressions compare every old floor with a manual west-facing staircase fixture and test all/ground/negative/missing-level selection.

The independent135-case16+16 generation/validation sweep passed. An additional ground-hotel-layout regression fixes reception accidentally following the bottom array index. Final46 core/22 Node tests and six browser suites pass; the installed-game signed loader probe and independent native readback pass. Character traversal, full cellar rendering/lighting and gameplay remain unverified.
## Resources0.4.0 review resolution

One fresh independent whole-change review found three defects; one consolidated pass resolves them.

1. Manual world decorations previously inherited automatic forest trees underneath. Composer now suppresses procedural vegetation over every explicit object part, preserving source terrain and using the same result in scene/export. A reproduced failing test is now green.
2. Decoded atlas pages had no lifetime/concurrency limit. The local LRU cache and scene requirements are bounded by192MiB estimated RGBA pixels; at most two pages decode together. Current scene pages are protected from eviction; closing/reloading releases them. A metadata-based oversized scene is rejected before decoding with an actionable message. Mock decoder regression verifies eviction/pinning/concurrency; actual browser preview/catalog flows pass. These limits do not represent a measured total browser RSS/GPU bound.
3. Applying a style to legacy buildings without a design counted no-op changes. The endpoint counts only eligible designs; the API regression confirms changed0/designnull for a legacy house.

Preserve-import with decoration/surface-only edits was independently checked and already worked. Source selection/PNG indexing, complete-object references, native preview/export, format gates, procedural styles, history and packaging were included in the review. Installed-game loading and sprite properties are verified separately from full character/lighting gameplay.
