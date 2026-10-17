# Streamed World Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Implement independent owned tasks with TDD, then root integrates and reviews the branch.

**Goal:** Make large real-place worlds independently editable/exportable by section, with usable layers and polygon zone authoring.

**Architecture:** Content-hashed immutable section archives provide revision-pinned bounded readers to the existing job queue/native writers. Optional zone metadata reuses existing native raster/rectangle/room representations. Browser adapters keep unloaded sections as manifest entries.

**Tech Stack:** .NET 10, ES modules, existing Node tests and Playwright.

**Spec:** `docs/superpowers/specs/2026-10-16-streamed-world-editor-design.md`; shared boundary `docs/SPZM-SCENE-CONTRACT.md`.

## Global Constraints

- Every generated/temp/cache file stays in the supplied workspace; all shell calls source WorkspaceEnv.ps1.
- No game session, no private art/scenes in public Git, no external messages to the companion developer.
- Keep stable source IDs, original WGS84 evidence, manual edits and resource dependencies; no silent required-feature loss.
- Root owns app.js, editor.js metadata/history, Program.cs route/test registration and final packaging. Agents own separate feature files and announce DTOs before editing shared models.
- Existing aggregate limits may remain only on compatibility monolithic JSON flows, not section-only authoring or streamed export.

## Review Focus

- A workspace edited while an export is queued must still export the pinned revision; pruning must not remove leased blobs.
- Unloaded shared geometry must reserve real footprints and remain visible without pretending missing neighbours are empty terrain.
- Cancellation, failed resource validation, stale section saves and rapid switches must retain the last committed data.
- Polygon edges, concave vehicle areas and density overlaps must use coverage rather than bounding rectangles.
- Companion rotation/fence DTOs must be capability checked; source evidence cannot be rewritten to emulate unsupported geometry.

### Task 1: Publish integration contracts
**Files:** docs/SPZM-SCENE-CONTRACT.md and this spec/plan.
- [x] Record ownership, coordinate/version rules, implemented/reserved fields and compatibility requirements.
- [x] Keep final API/DTO names synchronized after agents announce contracts; publish before dependent companion changes.

### Task 2: Revision-pinned streaming storage/export
**Files:** Web/Services/WorldSectionArchive.cs, WorldStore.cs, WorldWorkspaceEndpoints.cs, ExportJobStore.cs, ExportJobEndpoints.cs; new streaming exporter helpers and SectionStreamingTests.cs.
**Interfaces:** retain manifest/section routes; add minimal section context and revision-pinned workspace export jobs. Announce signatures to root.
- [x] Write RED cases for bounded isolated read/save, unrelated corrupt sections, stale revisions, aggregate budget, leased revisions, cancellation and native archive shared geometry.
- [x] Implement immutable readers/leases and streamed job snapshots; process one section at a time with resource checks and progress.
- [x] Run focused cases, verify native entries/retained metadata, document bounded memory behavior.

### Task 3: Polygon zones, density and room rules
**Files:** Core/GameZone.cs, new zone geometry/density/room rule helpers, Export/GameZonesExporter.cs, dedicated Web zone endpoints, wwwroot zone-data/panel modules and focused tests.
**Interfaces:** existing GameZone rectangle compatibility; new DTOs announced before shared-field edits. Root integrates optional metadata/history/world projection.
- [x] RED cases for polygon coverage/vehicle obstacles/rectangle decomposition, density overlaps/removal/manual edits, valid room selection/tag provenance and atomic failure.
- [x] Implement native-compatible semantics, reviewed API operations and visual zone editing.
- [x] Verify focused core/JS cases and explain quantization/unsupported source tags.

### Task 4: Layer/tool workbench
**Files:** new wwwroot map-layers.js/css, geographic-authoring-tools.js and JS tests; root app.js hook.
**Interfaces:** installMapLayers({getProject,getContext,requestDraw,...}) exposes visibility/lock state, overlay draw and nonmutating draft tools. Core-dependent tools use advertised capabilities, never fake/export unsupported data.
- [x] RED cases for view-only layer state, locked editing, snapped edge/path handles, cancellation and capability gating.
- [x] Implement layer ordering/visibility, world seam/loaded-state presentation, line and footprint drawing adapters in existing visual style.
- [x] Report exact root integration hooks and existing-core dependencies.

Workbench modules, application rendering/lock enforcement and synthetic browser acceptance are verified. Annotation ordering changes source/section overlays only; physical sprites retain their depth order.

### Task 5: Root browser streaming integration
**Files:** wwwroot/world-workspaces.js, world-workshop.js, app.js, editor.js; native context/operations as needed; JS/browser regressions.
- [x] RED lifecycle/history tests for active section switching, stale loads/saves, minimal shared context and manifest overview.
- [x] Wire root field validation/history, layer restrictions, zone UI, loaded/unloaded neighbour overlay and bounded section navigation; no full-world recovery on isolated edits.
- [x] Exercise 13-section fixture, actual APIs, save/reload, export cancellation and native decoding.

### Task 6: Review and delivery
- [x] Draft portable English usage documentation and add guides to the package allowlist.
- [x] Whole C#/JS suites, fresh self-contained browser/native probe, screenshots, English guides and local resource-path/privacy checks.
- [x] Independent review, fix material findings, fetch/merge concurrent develop changes without overwriting companion work.
- [ ] Commit/push reviewed work, update running app and package. Report actual private-fixture availability and remaining companion capabilities honestly.

## Integration checkpoint

Focused browser-module tests pass for section switching, failed/stale saves, edits during a pending download, cancelled application after recovery waits, explicit reload success/failure, manifest-only neighbours and annotation draw order. Native/browser acceptance is verified; final publication is recorded with the delivery commit. The real private 13-section Pilna scene set has not been used as an acceptance fixture; any synthetic fixture must be labelled as such.
