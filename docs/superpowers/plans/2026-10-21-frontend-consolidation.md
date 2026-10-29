# Frontend consolidation implementation plan

> **For agentic workers:** Use superpowers:executing-plans for integration and focused parallel workers for independent resource and library changes.

**Goal:** Complete the eight frontend changes approved by the user and verify the final editor in a real browser.

**Architecture:** MapLayerState owns visibility. The map editor projects native tools and selections into the existing shell instead of keeping contradictory controls. One zone panel retains reviewed proposals. Contextual panels and task menus reuse existing commands and history contracts. Resource grouping and the building library have isolated modules.

**Tech Stack:** Existing .NET 10 minimal API, ES modules, CSS, Node tests and Playwright/Chrome browser verification.

**Spec:** User's eight numbered requirements and the measured review in work/ui-audit-20261021/FRONTEND-REVIEW.md.

## Global constraints

- Preserve project formats, section revision guards, shared geometry, resource dependencies, undo/redo and offline operation.
- Keep all existing tasks reachable through contextual controls or menus; retain Save and Build mod in stable positions.
- Keep all 64 native levels and the separate safe building/environment drafts.
- Write no test data into the real Zomboid folder. Use an isolated local workspace and browser context.
- Retain the existing visual palette and use no external fonts or UI libraries.

## Review focus

- Switching Top/Game view with a selected building must synchronize tool, selection, available commands and keyboard operations.
- Hidden/locked layers must still reject edits, including legacy shortcuts and world workspace operations.
- A zone preview must be invalidated after changes, and room edits must retain explicit review.
- Empty libraries, duplicate names, async preset loading and import must remain usable in all tabs.
- Grouping must preserve complete sprite footprints, orientation, source filtering, paging and access to raw tiles.

## Tasks

### 1. Layer and editor state

Files: app.js, main-native-editor.js, map-layers.js, new editor-shell.js; tests/editor-shell.test.mjs and existing native/layer tests.
- [x] Add regression coverage for native/overview tool mapping, selection on view switch and one layer visibility source; observe failure.
- [x] Remove legacy visibility controls and gates. Synchronize tools and selection on every transition without recursive callbacks.
- [x] Preserve editing guards, world/native history and signed-floor selection. Run focused tests.

### 2. Single zone editor and contextual shell

Files: app.js, index.html, style.css, new editor-shell.css, zone-panel.js as needed.
- [x] Keep the polygon panel with rectangles, vehicles, density, room review and cancellation; remove the old mounted rectangle form.
- [x] Make sidebar sections respond to the active tool/selection. Add explicit Zones and Objects entry points.
- [x] Group secondary project commands into keyboard-accessible menus, keep Save/Build mod fixed, remove duplicate appearance entry.
- [x] Reuse central workshop tools to drive the palette; remove redundant resource-role buttons/selectors from the normal view without losing roof or model selection.

### 3. Building library (independent worker)

Files: new building-library.js, housing-gallery.js/css, tests/building-library.test.mjs. Integrate through app.js after worker completion.
- [x] Introduce accessible On map / Templates / My templates tabs and common search above results.
- [x] Preserve preset thumbnails, category filters, existing building edits, creating/importing templates and library placement.
- [x] Cover empty states and async search/tab changes; run tests.

### 4. Complete-object palette (independent worker)

Files: resources.js, resource-data.js/new grouping module, native palette integration, optional catalog/API changes and focused tests.
- [x] Test whole-object grouping including mod provenance, orientation, singleton and incomplete groups; observe failure.
- [x] Show assembled-object cards by default, preserve a raw-tiles toggle, search/paging and complete placement.
- [x] Verify chosen object resolves the same complete footprint as before and preserves resource dependencies.

### 5. Integration and verification

Files: browser regression script, tests and user-facing documentation.
- [x] Run Node suite, C# suite and Release build after integration.
- [x] Run browser regression at 1440×900 and 1366×768: menus, layers, native/overview tools and selection, zone draw/preview/apply/undo, all library tabs/search/import, whole/raw resources and workshop tool selection.
- [x] Exercise an isolated section workspace, save/export and reopening without losing shared geometry.
- [x] Inspect screenshots and browser errors. Review the final diff, fix regressions and record results.

## Execution record

- Existing linked worktree reused; branch codex/frontend-consolidation starts at origin/develop.
- User explicitly approved all eight changes and requested implementation followed by verification; no further design approval is needed.

- Completed: all eight requirements; verification and review evidence is recorded in docs/FRONTEND-CONSOLIDATION.md.
