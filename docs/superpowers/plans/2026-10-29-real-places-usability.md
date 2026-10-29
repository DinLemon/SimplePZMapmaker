# Real places: map selection and import

The user approved consolidating the geographic interface and requested usability tests. Routine layout choices are delegated to the implementer.

## Design

Keep the editor's existing green, ivory and amber palette and typography. One Import menu entry opens Real places. Two keyboard-accessible tabs separate choosing a geographic area from editing imported sites. The first tab gives the map most of the space, with search above it and a narrow settings column. Coordinates, map resolution, source limits and specialist data sources are disclosed in details rather than all displayed at once.

A locally bundled Leaflet map provides OSM navigation and explicit rectangular selection. Changing the viewport never silently changes the import bounds. Google Maps and OSM links open the selected location externally; Google is a reference, not an import data source. Existing single-map and streaming world import mechanisms remain authoritative. Only explicit import/apply changes the project. The selected rectangle transfers exactly to world setup; a Back to area selection action preserves it.

The imported-sites tab retains site editing, source evidence and diagnostics, with secondary evidence/checks collapsed. Rename Inspect map to Return to editor. Empty states explain that a geographic import is needed. No second import form or competing source state.

## Implementation and checks

- [x] Add a local, attributed map picker with validated bounds, selection cancellation, offline feedback and external links; test bounds and links.
- [x] Reuse the existing import controls inside the workbench, consolidate entry points, move optional controls into details, and add accessible tabs.
- [x] Connect search, coordinates, rectangle, import source invalidation, and exact bounds handoff to the existing world wizard. Protect busy operations and authored project state.
- [x] Run deterministic browser usability scenarios against the full app: discoverability, search, map selection, keyboard cancellation/tabs, external references, single-map import, world handoff/back, error recovery, offline tile failure, and narrow screen layout.
- [x] Run JS tests and a Release build. Publish an isolated preview and record screenshots/results. A scripted walkthrough is not a human participant usability study.

## External integration

Leaflet 1.9.4 is bundled locally with its licence. Only OSM tile images are added to CSP. Tile requests use browser caching and an origin referrer, with visible OSM attribution; no bulk/offline tile download. Existing local POST checks remain unchanged.

References: https://leafletjs.com/reference.html ; https://operations.osmfoundation.org/policies/tiles/

## Verification outcome

23/23 full-app scripted usability scenarios, 492/492 JS tests with map-browser cases enabled, six existing interactive-guide workflows and a fresh Release Web publish passed. Live OSM tiles loaded under the published CSP. A second review found ready-world/preserved-frame/draft/download-link regressions; all four gained reproduction checks and were fixed before the final run. The current user editor was not reloaded.
