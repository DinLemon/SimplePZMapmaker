# Generation Workbench Implementation Plan

> **For agentic workers:** Use independent domain agents with explicit file ownership; root integrates the frontend and verifies the whole application. Steps use checkbox syntax.

**Goal:** Ship all five approved generator improvements through one usable map-editing workflow.
**Architecture:** Add pure settlement generation, a protected proposal/apply layer and read-only quality diagnostics to Core. Web endpoints validate resources and store bounded proposals. Browser controls use existing project history and world-aware snapshot application.
**Tech Stack:** Existing .NET 10, JavaScript ES modules, Node tests, Playwright.
**Spec:** `docs/superpowers/specs/2026-10-16-generation-workbench-design.md`

## Global Constraints

- All writes/temp under the existing project workspace; private assets stay untracked.
- Backward-compatible project JSON, deterministic seeds, no in-game walkthrough.
- Preview must preserve current project; apply must reject stale originals and preserve protected content.

## Review Focus

- Locked, imported, edited and boundary-crossing buildings survive regeneration, including terrain under them.
- History, recovery and section persistence retain protections after reload and undo/redo.
- Preview failures, cancellation and settings edits never leave an old candidate applicable.
- Large-map caps/cancellation report incomplete diagnostic coverage.
- Placement orientation and access paths remain valid near water, spawns and elevated infrastructure.

## Tasks

- [x] Settlement domain: test legacy/determinism/era/zoning/density/access, implement SettlementRules/SettlementGenerator and integrate MapGenerator.
- [x] Controlled proposals: test protections, source provenance, partial area, variants and stale/tampered apply; implement GenerationControl/GenerationStudio and bounded Web store/endpoints.
- [x] Quality audit: test architectural/road/access/zone findings, completeness and cancellation; implement GenerationQuality and endpoint.
- [x] Frontend: first test metadata history and review lifecycle; implement dialog, region selection, pins, two variants, provenance, diagnostics and world-aware apply; integrate rules into new map creation.
- [x] Integration: validate metadata on both runtimes; run full .NET/Node suites and real browser UI; fix meaningful failures and review final diff.
- [x] Delivery: actual screenshots/docs, updated app artifact, clean public-path audit, commit and push feature branch.
