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