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

## PilnaVillage world

`village-scene.json` builds the whole settlement as one geographic world at 1 m per tile: 1,024-tile sections under a reference rotated −11.22° to follow the streets. From the repository root:

```powershell
dotnet run --project src/SimplePZMapmaker.Cli -- build examples/Pilna/village-scene.json --out work/PilnaVillage.pzworld.json --export work/PilnaVillage.zip --strict --save-workspace --game "<Build 42 folder>"
dotnet run --project src/SimplePZMapmaker.Cli -- server-check work/PilnaVillage.zip --game "<Build 42 folder>"
```

Result on 2026-10-17: 26 sections, 1,640 section buildings and 61 shared buildings across seams; the seam check reports 0 lost and 0 duplicated outlines (88 kept as evidence: 42 water or road, 27 outlines over 64 tiles, 18 overlaps, 1 crossing outline rejected at world level). All 2,471 exported tile names exist in the game. The structural verifier, the installed-class probe (416 cells, 425,984 chunks, 9,565 rooms, 249 stair tiles) and the dedicated server check pass: the server loads the mod, its metagrid holds the sampled Pilna buildings with their rooms, and it stops cleanly. The build takes about five minutes; the world JSON is about 200 MB, so open it through the saved section workspace.

Both Pilna builds start at native cell 150,150. Cells 100–121 are taken by Raven Creek, a popular Workshop map, and loading the two together would overwrite one with the other; `simplepzmap conflicts` lists such overlaps for the maps you have installed.

The OpenStreetMap source contains almost no fences, gates or sheds, so the village has none. Field-survey fences and outbuildings can be added as GeoJSON `sources` with the transfer-contract tags.

The server logs that the spawn points stand outside buildings (`initSpawnBuildings: no room or building`): the exporter places spawns on open dry ground, while vanilla maps spawn players inside houses. No client joined the server, so spawning itself was not tested.
