# Loaded workspaces and furniture

Goal: allow every saved section to be resident and editable above the portable JSON limit, retaining revision checks and section saves. Improve generated furnishing without changing authored projects automatically.

Architecture: load independently validated section snapshots sequentially into an atomic client cache. The existing section session selects cached rasters and saves only the active section. The world overview displays all cached sections; shared geometry keeps the existing protected context. Portable JSON, ingress and export budgets remain independent. Loading can be cancelled without replacing current edits.

- [x] Add session tests for aggregate sizes above 128 MiB, cached switching, revision conflicts, cancellation and edits during loading; observe failures.
- [x] Add a compact section response option, client cache, progress and an explicit load-all action with memory guidance. Verify overview and section save/switch in the browser.
- [x] Reproduce furnishing faults: room retagging keeps old furnishings, greedy placement can disconnect doorways, and generic fallback ignores usage.
- [x] Use room-aware placement with protected circulation and occupied model footprints; refresh generated furniture after import room tags. Keep deterministic seeds and legacy template mode.
- [x] Run focused tests, full suites and browser checks; document limitations and observed results.

Follow-up from the user's actual canvas check: resident data in the overview alone did not fulfil the editing workflow. Add a continuous main canvas using bounded thumbnails and existing building plans, world fit/minimap and resident-aware layer counts. Click a neighbour to save/switch cached ownership while rebasing the camera. Keep section validation, storage and Undo boundaries; native sprites remain scoped to the active section. The browser regression now covers the main canvas plus painting/Undo, not only the World overview.
