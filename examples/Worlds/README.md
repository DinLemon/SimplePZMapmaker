# Stitched-world examples

## Shared buildings and DEM (0.7)

Open `World-DEM.pzworld.json` in **World → Open world**; `World-DEM-B42.zip` is its native mod. Two adjacent sections contain one shared house/cellar, joined authored road/river and a native container with a Toolbox model.

Real elevation near Smolensk was downloaded from Terrain Tiles:256×256 samples, 161–269 m across the sampled source. `World-DEM-source.json` retains source, license and georeferencing. Import time is not measurement time. The layout is authored, not an OSM reconstruction. Heights appear in an overlay and `elevation.asc`; game ground remains flat. Shared rooms are split correctly between two native cells. Installed game classes read 13 rooms, four basement rooms and three basement stair tiles. Editor 0.7 or later is required for world 2/maps 7.

## Original stitched world

Open `world.pzworld.json` through **World → Open world**. Twelve adjacent sections contain buildings, forest, road and decoration across 3072×1024 cells. Another 512×512 section demonstrates a house crossing a native 256×256-cell boundary:13 sections/16 native cells total.

The first house's cellar contains a `Base.BeerBottle` model at scale 0.5. Original geometry/texture come from the installed game; the ZIP contains references and original static-item placement code.

`Worlds-B42.zip` is the combined native mod. Origin 100,100 overlaps other examples; enable separately in a new world, or move sections and export again. Game files/resources are not included.

Regenerate with the installed game, from the repository root:

```powershell
. ./scripts/WorkspaceEnv.ps1
dotnet run --project tools/WorldExample
```

## Small real Smolensk area

`Smolensk-small.pzworld.json` and `Smolensk-small-B42.zip` cover 54.778–54.780°N, 32.045–32.048°E, roughly 223×193 m. Overpass data was downloaded 2026-10-13 with source timestamp 2026-10-13T07:54:29Z. Scale 1 m/cell, padded 512×512 section, four native cells/eight buildings, origin 130,130. This tests the selected area, not the whole city. OSM/ODbL attribution is included in the source and ZIP.
