# Global geography and visual agent assets implementation plan

> **For agentic workers:** Use superpowers:executing-plans or bounded parallel workers. Preserve the existing dirty worktree; do not publish or commit without a request.

**Goal:** Load previously unseen geographic areas without depending on live Overpass, and let a vision-capable agent use licensed references and create missing sprites as a separate B42 tile mod.

**Architecture:** Coordinate-selected Geofabrik extracts are downloaded resumably to a bounded disk cache and clipped by the local PBF reader using disk-backed indexes. References retain provenance and observations separately from inferred game designs. New sprites use existing tile packaging and resource ownership rules, with separate preview/build/attach operations.

**Tech stack:** .NET 10, native PBF/PNG processing, existing ASP.NET/MCP server and browser editor.

**Spec:** The user's accepted six-part proposal and explicit addition that the agent may draw missing sprites and package a separate mod.

## Global constraints

- No real Zomboid profile writes; no unrelated resets, commits or external publication.
- Google/Yandex are navigation links only; no panorama scraping or automatic image extraction.
- Own photos and licensed Mapillary references require provenance; absent API credentials are explicit capability limits.
- Image generation is performed by the connected agent's image tool; the editor must not fabricate generation or silently introduce paid calls.
- Preserve complete ways, relation rings/holes, source coordinates and manual edits. Failures/cancellation do not replace current projects or validated files.
- Source image/material rights and custom-mod dependencies survive export; sprite IDs are stable and collisions are rejected.

## Review focus

- Region borders, holes and incomplete coverage must not silently omit part of the requested area.
- Interrupted/stale regional downloads must resume only against a matching validator and never masquerade as complete.
- Large PBFs must use bounded managed memory and clean owned temporary indexes on cancellation.
- A model's uncertain image observations must not become factual geometry or overwrite an edited building.
- A stale project revision or invalid sprite must not partially attach a resource mod.

## Tasks

- [x] Implement and test disk-backed large-PBF reading, retaining parity with existing clipping/geometry behavior.
- [x] Implement bounded official region catalogue selection and resumable regional download/cache; add the global source mode and use it for automatic imports.
- [x] Integrate progress/planning into single-map and world dialogs; remove Smolensk as the automatic geographic special case while retaining legacy explicit support.
- [x] Add licensed visual reference upload/search/image tools, observations, draft workflow and editor controls.
- [x] Add agent sprite drafts, PNG previews, validated tile-mod packaging, stable IDs and revision-protected resource dependency attachment.
- [x] Register services/tools/UI, verify real regions outside Smolensk and large files, run appropriate suites and independent review, then update the local Release app.

Tests belong to the responsible component. Main worker integrates `Program.cs`, test registration and common editor mounts to avoid conflicting edits. Each task must report actual capability limitations (especially missing Mapillary token and external image-generation access).

## Completion evidence

- Real Monaco, Berlin and Tokyo/Kantō regional imports validated; 496 MiB Tokyo source resumed and scratch indexes cleaned.
- 638/638 C#, 375/375 JS; complete frontend and bounded OSM stream/browser checks.
- Real HTTP/MCP image and isolated-design transport; real Release photo/sprite build/preview/download/attach/review/receive/undo.
- Independent review findings fixed with regressions: ordinary/workspace mod recovery, preservation of internal edges, and persistence of unplaced attached frame dependencies.
- Local Release rebuilt/restarted on port 48777 with zero warnings/errors; fresh Chrome verified it. No commits or remote publication.
- Authenticated Mapillary coverage, actual-model real-photo interpretation and an in-game B42 custom-art playtest remain explicit verification limits.
