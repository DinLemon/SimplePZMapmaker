# Real places

Open **Create → Choose a real place**. This is the shared entry for choosing geographic areas and inspecting imported objects.

## Choose a place

Search for a city/address, select a result, or pan and zoom the map. **Select rectangle** takes two corner clicks/taps. Escape cancels an unfinished selection; another Escape closes the panel. **Use visible area** explicitly adopts the viewport. Panning alone keeps the existing selection.

The rectangle is the import area. Search can retain a whole-city boundary even when it exceeds one editing section. The existing world planner still checks section/cell limits; selecting a continent does not make it importable. Coordinates and area width are available in a disclosure. The single-map tile size and approximate metres per tile remain visible in the area summary.

**Set up linked world** carries the exact area and chosen source into the existing world wizard. Set scale, section size and rotation there. **Back to area selection** returns its current bounds and source to the map. A chosen saved file remains selected; compatible prepared sources are reused without relaxing either importer's JSON limit. Building a world stages sections; explicitly apply/browse the result to open it.

**Import single map** uses the existing importer and preservation controls. Selecting an area never changes the project. Cancelled/failed imports preserve it. For a whole town or region at a consistent scale, use a linked world rather than shrinking everything into one map.

The map uses OpenStreetMap raster tiles for navigation. Its **OpenStreetMap** link shows the rectangle, and **Google Maps** opens its centre externally. Google is a visual reference here, not a geometry provider. Tile availability is separate from vector-data availability: saved JSON/PBF import remains usable when the basemap or search provider is unavailable. Retry map reloads visible tiles only. No bulk tile download is provided.

## Imported objects & sites

The second tab retains site editing, original source outlines and transfer diagnostics. Source evidence and diagnostic lists are collapsed until needed. Site-name drafts survive switching task tabs. **Return to editor** closes the panel. Site changes still require review/apply and retain their existing undo behavior.

## Verification — 2026-10-29

Run against a disposable published instance, with its own `PZMAP_WORKSPACE` and `PZMAP_MODS_DIR`:

```powershell
$env:PZMAP_TEST_URL='http://127.0.0.1:48814'
node scripts/verify-real-places-usability.mjs
$env:PZMAP_PLACE_MAP_BROWSER='1'
npm test
```

The walkthrough refuses the original user-editor ports. It creates a fresh browser context and uses fixture search responses for deterministic query ordering/errors, real saved GeoJSON import and section-world build/apply, and live OSM tile images under the published CSP. It separately interrupts the import stream and tile requests to test recovery. Search-provider uptime and a human participant study are not claimed.

**23/23 walkthrough scenarios, 492/492 JS checks, six existing guide workflows and Release Web publication passed.** Four review regressions (ready-world retention, preserved-frame retention, site drafts across world setup, and stale source-download links) have dedicated reproduction coverage.

Results and reviewed screenshots are in `work/real-places-usability/results.json`, `desktop.png` and `mobile.png`. Map-library tests additionally cover delayed initialization, latest bounds, local-library load failure, origin referrer, caching and narrow layouts. See [Leaflet attribution](licenses/LEAFLET.md).

The subsequent [workspace redesign](WORKSPACE-UI.md) adds a contextual expanded-layout proposal and retains this same selection/source controller.
