# Editor expansion: lighting, modules, infrastructure, agents and section workspaces

The user authorized all five additions together, with implementation choices delegated to the builder. Preserve the English application and public documentation, signed Build 42 levels -32..31, existing projects, revision-protected agent sessions and a fully local editor. No game session or character playthrough is part of validation.

## Intended behavior

- Material view samples the authentic installed LightingJNI field, renders textured model meshes with geometry shadows, and distinguishes browser shadow rendering from native light computation. It has bounded geometry/light caches, cancellation and disposal. Changing view settings does not alter exported authoring.
- Modules are reusable entrances, stairs, wings and annexes in the existing building library. Capture, rotate/mirror and compose produces a normal complete building, preserving rooms, stairs and resource ownership. Invalid overlap, orientation or vertical headroom rejects the transaction. Editing, history, saving and native export remain usable.
- Linear structures have a signed deck level, access stairs, rails and supports. Elevated decks preserve the original ground and permit passage beneath clear geometry. Underground paths preserve their ceiling except access shafts. Junction and stacked crossing tools preview an entire batch and apply it as one change. Continuous inclined vehicle ramps are not an engine feature and will not be claimed.
- MCP exposes bounded native lighting reads for shared maps/world sections and isolated building drafts. Gameplay zone design supports a read-only catalog and isolated, revision-bound proposals with explicit validation and commit. Existing content is retained; stale revisions reject whole operations.
- A section workspace stores a small revisioned manifest and immutable, content-addressed section files. Unchanged sections are not rewritten. A section can be read, replaced and exported on its own; all-world files stay compatible. Recovery references immutable data and falls back to a previous complete revision when current data is damaged. Export writes directly to a bounded temporary file and avoids recompressing a temporary ZIP for every section.

## Verification

Meaningful core and JavaScript tests cover transforms, overlap/access, raster preservation, signed geometry, serialization, stale commits, incremental writes, damaged manifest/blob recovery and native output. Actual browser workflows cover controls, previews, history and roundtrips. Official MCP clients check both supported protocol versions. Installed native library and native map readers verify output without starting a game. The extracted self-contained application and privacy audit are checked before publishing.

All development files and temporary artifacts live in the project workspace. Game and mod assets are read-only and excluded from publication. Screenshots mask local configuration paths.
