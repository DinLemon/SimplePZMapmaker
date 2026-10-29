# Detailed terrain outlines in real-place imports

Large forests, water bodies and land-use relations can cross many sections. The earlier importer estimated their cost as every source vertex on every raster row, plus a section-sized bounding box. A valid detailed perimeter outside the current section could therefore exceed the 40-million-operation estimate before any terrain was drawn. Splitting a world into sections did not remove that false rejection.

Terrain rasterization now buckets each non-horizontal edge by the first pixel-centre row it crosses. Only active edges participate in that row's intersections; spans are clipped to the current section and filled in source order. Original coordinates, polygon holes and the half-tile boundary convention are retained. There is no automatic source simplification or change to world scale.

The 40-million processing budget remains, measured against edge preparation, active-edge visits/sorting and actual filled cells across the section's terrain polygons. Cancellation is checked during preparation and on every row. A genuinely excessive repeated fill fails on the private candidate, preserving the input map and the world import's atomic publication behavior. Source bytes, feature/point counts, shared objects and section limits remain independent.

## Retrying a retained source

1. Keep the original area, scale and source selection.
2. With the updated application running, choose **Retry world build**. The prepared source is reused; another regional PBF download is unnecessary.
3. If the new message specifically reports the actual terrain processing budget, use smaller **section dimensions**, such as 512 instead of 1024 tiles, while keeping the area's bounds and metres per tile. This creates more connected sections without shrinking the selected place.

Regression checks cover a detailed covering forest, off-section geometry, water holes and pixel-centre boundaries, independent-section continuity, and rejection of excessive actual painting without input mutation.

## Dense-source validation

The map format is inferred from buildings and geographic evidence. The evidence validator snapshots that result once per read-only validation call, rather than rescanning the whole map for each source record. No persistent version cache is introduced: later edits are still detected on the next call. This also reduces repeated validation costs while reading dense sections for seam-building placement.
