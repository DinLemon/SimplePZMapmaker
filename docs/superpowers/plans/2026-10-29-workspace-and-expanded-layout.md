# Task-based workspace and expanded world layout

The user requested a full interface rework and a proposal to split a selected real area into more connected sections when its layout exceeds the ordinary import allowance. Routine design choices are delegated to implementation.

## Design

Use three visible workspaces: Create, Edit, Check & export. Reuse existing command elements/controllers rather than introducing competing project state. Create leads with real-place selection, blank-map generation and reopening a project. Edit gives the canvas most space, one compact tool rail, contextual settings and a collapsible inspector. Check & export gathers diagnostics, source review, resource checks and build entry points. Save is persistent; rare settings live in one menu. Keep the established green/ivory/amber palette and system typography; hierarchy and space, not decorative chrome, carry the redesign. Keyboard navigation and narrow screens are first-class.

Real places remains one selection surface. Large geographic boundaries must remain intact. The world wizard offers an explicit expanded layout, showing rows/columns, section count and minimum raster memory. The old 128-section / 768-cell allowance becomes the ordinary-import threshold, not the storage format's ceiling. Approved expanded layouts support at most 8,192 sections and 8,192 native cells under the existing global coordinate range, source budgets and cancellation rules. This is bounded support, not unlimited import or a claim that assembly is streamed. Section editing/export remain lazy; complete-world portable JSON retains its 128 MB budget.

Approving a proposal changes only the requested assembly allowance. It never starts network work, shrinks the area or changes detail. Geometry changes revoke approval. Invalid/hard-excessive layouts explain the actual constraint rather than offering an impossible split. Big imports require the section result path; old projects remain readable.

## Tasks

- [x] Backend: explicit expanded-layout flag, common count budgets, compatible section manifests/streamed export and geographic source validation, boundary and cancellation tests.
- [x] Frontend model/wizard: exact proposal, approval keyed to geometry, hard-limit diagnostics, progressive controls and regression tests.
- [x] Editor workspace: Create/Edit/Check navigation, context panels, consolidated commands, responsive layout and keyboard flow.
- [x] Integrate the real-place route, exercise actual >128-section saved-source import/read/export, and run full existing UI/guide regressions.
- [x] Review changes, publish an isolated preview and document observed results and remaining limits. Preserve existing user tabs and projects.

## Observed result

979/979 C# and 506/506 JS checks passed, with both opt-in browser contracts enabled. Full-app walkthroughs passed 10 workspace, 24 Real places and 4 expanded-layout scenarios, plus all six guide workflows. The production browser import created 129 real section files and preserved exact bounds and detail. Core coverage additionally streamed native export of 129 sections and validated cancellation and cleanup. Release Web and CLI were published to isolated work/workspace-redesign folders.

Review reproduced and fixed hidden editor returns, hidden undo, camera resets (including first-focus initialization) and closed-wizard job history. Regression coverage exercises each behavior. The user's editor on port 48814 was not restarted.
