# Source-aware Sites Implementation Plan

> **For agentic workers:** Root integrates independent source-import, site-authoring and transfer-audit agents, then verifies the complete editor.

**Goal:** Make editable sites serve accurate real-place transfer with traceable source evidence.
**Architecture:** Bounded source/site models in Core, import through existing projections, pure atomic group operations and read-only transfer/service checks; Web validation and browser history reuse the existing editor.
**Tech Stack:** Existing .NET 10, ES modules, Node tests, Playwright.
**Spec:** `docs/superpowers/specs/2026-10-16-source-aware-sites-design.md`

## Global constraints and review focus

- Exact original source geometry is evidence; game geometry is quantized/adapted and manual edits remain distinct.
- No inferred cadastral boundaries or invented services; preserve source holes, identities, scale and supported entrance semantics.
- Group mutation is atomic and respects object footprints, source metadata, source/provider limits and world-section boundaries.
- Every new field survives old-file defaults, history, reimport and shared-world projection; live results invalidate on edits.
- Bound work, honor cancellation, keep all local writes under the workspace, and run no game session.

## Tasks

- [x] Source domain/import: geometry+tags/nodes/addresses/entrances/site association, Overpass/PBF/tiled identity handling; deterministic/preserve-edit regressions.
- [x] Site domain: validate records; atomic group translation/copy/rename/pins and generation protections; geometry and source-preservation tests.
- [x] Transfer diagnostics: evidence drift, source entrance outcomes, real service points and qualified routing/coverage; caps/no-mutation tests.
- [x] Frontend: source/site controls, grouped history, overlays, diagnostics/evidence focus and opt-in live checks; JS tests and real browser scenarios.
- [x] Delivery: full suites, import/native/runtime verification, screenshots/guide, app package, independent review, public-path audit and GitHub integration.

Verification: 334 C# / 183 JS; self-contained browser with 1,954 real Smolensk source records; native decoder and installed-class reader passed. Independent review findings were fixed and covered by regression cases. Publication checked separately against Git status and remote refs.
