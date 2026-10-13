# Pilna

A reproducible OpenStreetMap import of a real settlement, captured for editor 0.3.

- `osm-source.json`: saved Overpass response, 2,378 features/1,789 building outlines/439 road features.
- `recipe.json`: bounds, seed, 2048-map size and native world origin.
- `pilna.pzmap.json`: editable format 2 project; open with the ordinary **Open** button in editor 0.3 or later. The dedicated Pilna button has been removed.
- `Pilna-B42.zip`: Build 42 mod with installation instructions inside.
- `import-report.json`: outcomes, skip reasons, types, levels, provenance and shapes.
- `ATTRIBUTION.txt`: geography source and ODbL terms.

Bounds are roughly 3.7×4.7 km at 2.29 m/cell, north up.1,577 buildings from 1,789 source outlines:1,470 free-form cell outlines and 107 adapted rectangles.1,517 buildings have one level, 52 two and eight three.42 level counts came from OSM, one from height and 1,534 from assumptions. Interiors/facades are generated; this is a gameplay adaptation.

The 212 skipped outlines were 59 water/road conflicts, 103 then-unsupported cell boundaries, 49 overlaps and one oversized outline. All reasons are recorded.

Regenerate from the repository root with the editor running:

```powershell
. ./scripts/WorkspaceEnv.ps1
node scripts/build-pilna.mjs
```

The script uses the saved source and needs no network. For fresh geography, search for Pilna and select full bounds in the UI. Save before updating; the manual-edit preservation toggle protects edited buildings.

Checks accepted 64 native cells, 65,536 chunks and 7,345 rooms, including installed-game loader reads and 204 recognized stair sprites. Character traversal, roof/furniture appearance, loot and multiplayer are unverified. A railway layer is not implemented.
