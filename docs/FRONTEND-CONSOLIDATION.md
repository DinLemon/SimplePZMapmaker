# Frontend consolidation — 2026-10-21

The eight approved frontend changes are implemented on `codex/frontend-consolidation`.

- **Layers:** one `MapLayerState` and one panel drive rendering and edit guards. The old visibility checkboxes and additional AND gates are removed. Density starts hidden and its brush reveals that same layer.
- **Game view:** tool changes and resource selection update the native selector before notifying the shell. Entering/exiting Game view preserves the selected local building. Late resource responses cannot restore an abandoned tool. The legacy inspector is hidden in native editing.
- **Zones:** the reviewed polygon editor also handles rectangles, vehicle zones, density and whole-room rules. The old permanent rectangle form is no longer mounted. Drawing a rectangle keeps the zone context open for preview/redraw. Changing fields invalidates the preview; applying is undoable.
- **Appearance/workshop:** one map-appearance command remains. Central workshop tools select the corresponding palette; redundant palette-role buttons and dropdown are hidden. Roof resources and registered models remain reachable through dedicated pickers. Floor-wide options and standard furniture settings follow their tools.
- **Commands:** Project, Import, Tools and Settings menus hold secondary actions; Save and Build mod stay in the header. Section preview, recovery, agent sessions and resource discovery remain reachable. Menus support Escape/focus restoration.
- **Buildings:** On map / Templates / My templates tabs share search above their results. Preset category/thumbnail support remains, as do saved-template creation, import, edit and placement. Editing replaces the saved template and upgrades the project format when needed. Current format-8 furnished templates import correctly.
- **Context:** map sidebars show terrain, path, building, object or zone settings for the current task. Native editing uses its own single contextual inspector; general project information is collapsed.
- Changing tools or views cancels unfinished geometry drawing, so it cannot capture later brush clicks. Completed geometry drafts remain available. This transition is covered by the browser regression suite.
- **Furniture:** catalog grouping happens before filtering/paging. Complete objects are the default; cards render all their parts, orientation and source. Advanced raw tiles retains individual sprites. Placement revalidates the full footprint and mod ownership. Ambiguous/incomplete grids are available in raw mode.

## Verification

- Node: **296/296 passed**; C#: **422/422 passed**.
- Release solution build: **0 errors, 0 warnings**.
- Real Chrome, 1440×900 and 1366×768: menus, layers, context, all library tabs, search/import/edit/place, workshop tool/palette changes, native/overview tool and selection transitions, JSON save/reopen.
- Rectangle and polygon preview/apply, preview invalidation, undo/redo; basement strokes, pointer cancellation, door frames, upper/basement stair links, model gameplay editing and a shared seam building.
- Isolated section workspace: one section loaded, neighbour remains unloaded, shared building read-only, save/reopen complete world retains shared geometry.
- Resource browser: four hospital-bed sprites become two complete oriented cards; pixel-bearing thumbnails, selection, orientation filter, raw mode and paging pass.
- The exported gameplay ZIP passes the native B42 structure verifier. This verifies export data; it is not a game-client playtest.
- The frontend browser suite also passes without an installed game and is included in the published-application CI startup check.
- Independent code review found two integration issues (lost section preview and template format upgrades); both were fixed and re-reviewed with no remaining blockers.

Browser evidence stays under ignored `work/frontend-verification/`, `work/resource-browser/` and `work/qa-results/`. All server writes use isolated application workspaces. The user's real Zomboid folder is not used.

## Repeat the browser checks

Start the application with an isolated `PZMAP_WORKSPACE`, then set `PZMAP_TEST_URL` to its local URL. Install the test dependency with `npm ci --prefix tools/BrowserSmoke` and install Chromium with `node tools/BrowserSmoke/node_modules/playwright/cli.js install chromium`. Alternatively, set `PZMAP_BROWSER_CHANNEL=chrome` to use installed Chrome.

```powershell
node scripts/verify-frontend-consolidation.mjs
```

With a read-only installed-game catalog available, additionally run:

```powershell
node scripts/verify-main-native-editing.mjs
node scripts/verify-gameplay-authoring.mjs
$env:PZMAP_PLAYWRIGHT_MODULE = Join-Path (Get-Location) 'tools/BrowserSmoke/node_modules/playwright/index.mjs'
node tests/resources.browser.mjs
node scripts/verify-native-map.mjs work/qa-results/gameplay-authoring.zip
```
