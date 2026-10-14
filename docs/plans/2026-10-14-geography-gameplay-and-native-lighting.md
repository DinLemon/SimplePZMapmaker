# Geography, gameplay authoring and direct native editing

The user approved source-footprint import, curved roads/rivers/railways/sidewalks/bridges, game zones and loot/vehicle authoring, interactive multi-cell objects, and editing directly in the main native view. The additional lighting check must display inside the editor, without a separate game session or character walk.

All development, temporary data, probes and caches stay in the workspace. Installed game and Workshop resources are read-only. Public text and UI remain English. Private installation folders stay in ignored local settings. No game code, libraries, textures or models are redistributed.

Independent agents own geographic contours/linear authoring, gameplay zones/object behaviors, and main native picking/editing. Root owns shared persistence contracts, integration and an isolated helper for the installed native lighting library. A real-lighting result must identify its engine provenance and scenario inputs; approximation must never be labeled real. If native computation cannot be isolated, preserve an explicit unavailable result instead of launching a game session.

Map format 9 preserves linear features, game zones and interactive authoring. Templates need format 6 for new model fields; worlds need format 3 for shared interactive structures. Earlier compatible data remains readable. Validators bound coordinates, features, underlays, models, zones, runtime calls and resource providers. Changes validate completely before atomic application and history commit.

Verification includes targeted regressions, cancellation/ownership/version checks, actual browser authoring/export/undo/reopen workflows, native lighting comparisons for opaque walls/windows/doors/roofs and lights, independent native archive readback, and extracted self-contained application startup. No in-game character traversal, loot balance or multiplayer playthrough is claimed.
