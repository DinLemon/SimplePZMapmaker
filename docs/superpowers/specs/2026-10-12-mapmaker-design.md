# SimplePZMapmaker

User intent: a map editor for non-programmers, with procedural generation, geographic import and export for Project Zomboid. Development is authorized in this directory with full discretion.

## Product decisions
- A Windows-first local .NET 10 app serving a Russian browser UI on loopback. No accounts, cloud storage, or game modifications.
- A large orthographic editable canvas, optional isometric overview, palette, inspector and explicit export workflow. Earthy charcoal #202721, paper #e8e4d8, moss #657954, river #507b85, amber #d5ad69, road #787975. System UI labels, monospaced coordinates. The map is the visual focus; no decorative dashboard cards.
- Blank, town and wilderness generation with reproducible seeds; brush, rectangle, road, building placement/removal, spawn markers and zombie density. Undo/redo, pan/zoom, layer toggles, save/load portable JSON and recovery autosave.
- OpenStreetMap bounding-box import via Overpass (fixed upstream, caching, bounded queries), plus user GeoJSON files. Streets, water, forests and footprints are converted to the editable grid; interiors are invented. Attribution survives save/export. Google scraping is excluded by provider terms.
- Buildings use a small expandable catalog of single-storey houses, shops and warehouses, with accessible doors, window walls, furniture and room definitions. This is the first delivered building catalog, not a claim of accurate real interiors.
- Export native Build 42 256-square cells / 8-square chunks as a downloadable mod ZIP, with LOT headers/packs, room definitions, spawn points, map metadata, zones, minimap data and attribution. Source project is included. No install into user saves and no overwrites outside this repository.
- Validate sizes, coordinates, imported file structure, building overlap/bounds/water, safe spawn and archive paths; reject invalid projects with actionable Russian errors.
- Map sizes 256, 512, 768, 1024; bounded import to the selected extent; incremental editor rendering and sparse buildings. Complete export is tested structurally and against an independent consumer if available. Do not describe the result as playtested without an actual game session.

## Boundaries and future extensions
True mountainous heightfields are not directly promised: SRTM elevation is different from road topology and must be researched against the game's engine. Multi-storey buildings, custom asset editing and automatic Workshop publishing are future extensions. The UI must state actual limitations clearly.

## Implementation
Core C# library owns model, validation, deterministic generation, geographic rasterization and native export. ASP.NET host owns upstream requests and ZIP delivery. Plain JavaScript ES modules own canvas editing and persistence; no bundler required. Dependencies limited to the .NET framework and browser APIs.

## Verification
Executable core regression suite covers determinism, geographic clipping, invalid geometry, metadata, full binary readback and room/spawn semantics. Node tests cover edits and history. Browser exercise uses the real server and canvas, downloads and reopens a project and export. Record honest export/game compatibility evidence in docs/STATUS.md.
