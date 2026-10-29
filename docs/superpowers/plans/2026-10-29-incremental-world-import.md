# Incremental world import implementation plan

> **For agentic workers:** Use executing-plans or subagent-driven-development task by task.

**Goal:** Import the selected 15,552-cell real place at its chosen scale into one linked, independently editable world.

**Architecture:** Save each composed section immediately into an unpublished immutable archive. Resolve shared fences using layout rectangles and bounded metadata. Resolve source buildings in original order using a prepared source catalog and only the sections intersecting each candidate and existing overlapping shared buildings. Validate the completed shared metadata against each section sequentially before atomically publishing its manifest.

**Tech stack:** .NET 10/Core/Web, section JSON blobs, browser ES modules and existing test runner.

**Spec:** User-approved sequential disk assembly with common coordinates and preserved seam objects; small-map and original-world preservation remain required.

## Constraints and review focus

- Normal consent thresholds stay 128 sections/768 cells. Expanded disk layouts allow 65,536 sections/cells. Existing source, object, coordinate and per-section budgets stay bounded.
- The compatibility all-in-memory import remains available only at <=8,192 cells; all larger requests require the disk writer. No monolithic result loading for large worlds.
- No published manifest before all sections, shared metadata and projections pass. Cancel/failure cleans only owned unpublished data. Never touch the user game profile or existing preview projects.
- Section rasters must not accumulate during composition or seam resolution. Source metadata remains bounded separately; aggregate disk/raster size is not peak memory.
- Check original source precedence, water/decoration/vehicle conflicts, gate merging, duplicate local/shared IDs, world-format promotion and padding sections.

## Tasks

- [x] Add failing incremental archive tests; implement staged writes, bounded reads/replacement, sequential projection validation and atomic commit/cleanup.
- [x] Add prepared shared-building candidates with original source order; pin parity, water rejection and source evidence in tests.
- [x] Add streamed OSM orchestrator: compose/write one section, merge shared fences, resolve building windows, validate/publish. Preserve legacy preserve-edits path within memory budget.
- [x] Update bounded layout contracts and memory explanations in frontend/API; add acceptance of 15,552 cells, hard-limit and consent regressions.
- [x] Run actual offline expanded import, seam/native projection and cancellation checks; record observed working-set evidence. Run full suites, publish an isolated preview, review and update docs.

## Observed verification

Production offline import completed 15,552 native cells in 972 sections at 1 metre/tile in 407.85 seconds. Its curated sparse source includes a seam house, fence with two gates, road, railway, river and bridge. Sampled first/second/middle/last sections keep the reference and original scale. The job reports 2 resident sections / 32 cells during seam assembly; sampled Windows process peak working set was 481 MiB (game-resource cache included). This is sparse-fixture evidence, not a dense-city RAM guarantee.

Actual browser confirms the 972-section proposal, approval without download, retained saved layout and one-section opening without complete-world JSON. Selected native export contains 16 lotheader/lotpack pairs and one authored section source (771,549-byte ZIP). Existing 129-section full native export coverage passes.

New C# coverage: 16 incremental storage cases, 11 prepared-building cases, 4 assembly cases, plus 12 updated expanded-layout cases. Full JS with browser contracts: 511/511. Final serial full C#: 1010/1010. Earlier concurrent stress runs exposed two regional idle-timing failures and a temporary-file access failure in an unrelated agent-tileset fixture; all passed on retry and in the clean full run. No tests were removed or weakened. Release DLLs and static files were hash-checked against the tested build and source.

Final isolated Web Release: work/incremental-world-import/release-final at 127.0.0.1:48822. The user's port-48821 editor was not restarted. No real Zomboid profile was changed.
