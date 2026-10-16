# Context-aware map generation and review

The user approved all five generator improvements: district rules, meaningful building relationships, transparent geographic completion, gameplay diagnostics, and reproducible selective generation.

## Behavior

- Keep existing .NET Core/Web and browser editor. Preserve old files and legacy generation by default. Add contextual district rules for era, land use, density, setbacks, floor range and courtyards; expose these both for new maps and selected-area generation.
- Place suitable buildings facing streets with usable entrance paths and respect water, existing geometry, authored surfaces, spawns and infrastructure. Keep generated IDs and output reproducible for the same seed and variant.
- Add a Generation studio entry in the main editor. Users choose a bounded rectangle numerically or by drawing, choose fill/regenerate and district rules, preview two alternatives, inspect changes, then explicitly apply one as a single undoable operation. Preview never replaces the current map.
- Fill adds synthetic buildings in available spaces without pretending they are surveyed OSM data. Regenerate only removes eligible unmodified generated buildings fully inside the rectangle. Imported buildings, authored buildings, modified buildings, pinned IDs, protected rectangles and boundary-crossing buildings survive.
- Distinguish source/adapted/generated/authored footprints, inferred interior layouts and source/estimated floors. Show provenance colors and text in proposal maps and a filterable building evidence list.
- Persist pins, protected rectangles and accepted generation settings in project JSON, recovery, undo/redo and world-section storage. Existing maps with no generation metadata stay valid.
- Diagnose pedestrian and vertical access, disconnected streets/courtyards, spawn routes, game-zone coverage and architectural relationships (openings, room purpose, repeated facade axes). Findings have severity, category, location and focus action. State analysis limits and incomplete coverage explicitly.
- Server proposals are bounded immutable snapshots, with opaque IDs and original-project fingerprints; applying stale or client-tampered results must fail.

## Constraints and acceptance

All local writes and temporary files stay inside the project workspace. Public docs contain no personal absolute paths. Private generated art stays outside Git. No in-game character walkthrough. Preserve game/mod resource selection and current user project while testing in isolated browser/workspace sessions.

Verify core domain behavior, old format compatibility, frontend state/history and real browser flows, including partial regeneration, locks, comparisons, stale previews, audit navigation and persisted reloading. Publish updated application artifacts only after checks. Document controls with actual screenshots.
