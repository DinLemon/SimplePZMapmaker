# Reliable geographic imports

- [x] Route map and world imports through the same explicit source policy: automatic, Overpass, regional extract, saved file.
- [x] Prefer the independent regional PBF where supported; retain validated cache on failed refresh and disclose its date.
- [x] Accept bounded local Overpass JSON, GeoJSON and regional PBF without external requests. Save downloadable source snapshots with their bounds.
- [x] Make world import start with place/area and scale, show section/cell counts before work, and move raw coordinates to advanced settings.
- [x] Preserve existing maps/worlds on failure and cancellation. Keep source snapshots available even if later composition fails.
- [x] Verify provider failures, offline reuse, invalid sources, geographic frame preservation, and fresh browser flows; rebuild the local Release app.

Compatibility: legacy regional-extract requests and the existing single-map GeoJSON action remain supported. World composition remains section-based. The OSM editing API is never used for bulk world downloads. No real Zomboid profile is used for verification.

## Dense World completion, 2026-10-23

- [x] Separate transfer activity/hard deadlines from local extraction and world assembly; retain source/retry information.
- [x] Retain dense regional geometry within explicit World budgets; stream unrelated PBF relations and validate actual large-ring source evidence.
- [x] Fall back from the bounded portable World writer to existing independent section storage, preserving the common geographic frame and shared seam objects.
- [x] Make archive-index publication the result commit point, roll back failed new-workspace writes and preserve saved indices when old job records expire.
- [x] Browse a completed section workspace without replacing current projects/edits or fetching all rasters; retain the portable-world path for small results.
- [x] Align shared authored-plan capacity with the existing 128 MiB section capacity; retain the 1 MiB index and strict building/floor/cell validation. Reproduce the former metadata failure with 126,778,297 bytes of valid shared building plans.
- [x] Verify the final Release against the real Moscow selection: 36 linked 1,024-tile sections, 576 nonoverlapping cells, 1 metre per tile, separate browser context and explicit single-section opening.

The source/assembly stage still holds the bounded world in server memory. Whole-province generation and incremental source/section assembly remain outside the verified extent; do not imply unlimited region support from section-based result storage.
