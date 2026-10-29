# Interactive guide

Open **Project → Interactive guide and help**. Choose English or Russian and a task:

- Create and edit a map: base, tools, layers, Undo and editable project saving.
- Import a real place: search or coordinates, real-world extent versus tile size, saved-file fallback and preserving edits.
- Build a connected world: one region and geographic frame, section planning, explicit build/apply, editing and exporting saved sections.
- Edit a building: searchable library, separate workshop draft, rooms, furniture and Game view.
- Preview a settlement or yards: strategy, area, preview, candidate selection and explicit apply.
- Check and export for Build 42: Project Doctor, spawns, editable backup, correct map/world export path and installation reference.

The guide outlines the actual control. **Show controls** reveals an existing menu or window; close unrelated editor windows yourself. If a step needs a selected building, usable source or completed export job, the guide explains how to obtain it. It does not invent a result or hide an unavailable control behind a fake screenshot.

**Next** advances only the guide. It never presses Create map, Import, Apply, Save, Build, Download or Install. Work on a practice map if you want to try edits. Following a control marks an interaction as seen, not a successful import or export; check the editor's own result before continuing. A completed guide means that you reached its last page, not that the map is ready for the game.

**Back**, **Pause**, **All tasks** and **Escape** let you leave a route. The task list offers **Resume** or **Restart**. Opening the app does not automatically resume actions. A dismissible first-visit invitation appears inline in Create, points to the same task list and never takes keyboard focus. Show controls reveals the matching task workspace and expands the relevant hidden settings.

Only versioned guide IDs, step numbers, completion, language and invitation dismissal are stored in browser localStorage (`simplepzmap.guide.v1`). No project, source, coordinates or credentials are stored by the guide. When storage is blocked or full, progress works for the current page; persistent resume cannot be guaranteed. Tours use the native dialog/popover support of current browsers.

The export and installation reference remains available in the task list. Saved section workspaces must be exported through their workspace controls to retain shared geometry. A complete-world JSON above 128 MiB is intentionally unavailable; this does not disable section editing or streaming export.

## Verification

Run `node --test tests/interactive-guide.test.mjs` and the full `node --test tests/*.test.mjs` suite. Against an isolated running app, run `node scripts/verify-interactive-guide.mjs` (default `http://127.0.0.1:48778`, override with `PZMAP_TEST_URL`). The browser check uses a separate Chrome context and the tracked building example `examples/Assets/resources.pzmap.json`; it does not access the user's tab. Screenshots and a bounded request-path proof go into `work/guide-acceptance/`.
