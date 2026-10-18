# Headless command line

`simplepzmap` builds, checks and exports maps without the browser editor. It uses the same importer, validators and native exporter as the application, so a scene built here opens unchanged in the editor.

The release package ships it as `artifacts/app/simplepzmap.exe`, next to the editor and with the same bundled runtime. From the repository, run `dotnet run --project src/SimplePZMapmaker.Cli -- <command> …`, or build it once with `dotnet build src/SimplePZMapmaker.Cli -c Release` and call `simplepzmap.exe`. CI builds [examples/CiScene](../examples/CiScene/README.md), a two-section scene with a shared building and a seam-crossing fence, with `build --strict` and checks it with `validate --source`.

Without `--game`, generated street dressing and vanilla vehicle distributions are accepted from built-in lists of Build 42 tiles and names. Tags that select game resources, such as `roof:colour`, `pz:wall` or `pz:style=tilezed:…`, still need `--game`.

| Command | What it does |
|---|---|
| `fetch --bounds <south,west,north,east> --out osm.json` | Downloads OpenStreetMap data with the editor's own Overpass client (tiled requests, retries, provider fallback, seven-day cache). |
| `build <scene.json>` | Imports GeoJSON or Overpass JSON into a geographic world, promotes seam-crossing buildings and fences to the world, runs the seam check and writes `.pzworld.json`; `--export mod.zip` also writes the Build 42 mod. |
| `validate <file>` | Validates a `.pzmap.json` or `.pzworld.json`. With `--source` it runs the seam check against the original data. |
| `export <file> --out mod.zip` | Writes the Build 42 mod of a map or world. |
| `tiles <tileset.json> --out tiles.zip` | Builds a tile mod (`.pack` + `.tiles`) from PNG frames. |
| `probe <mod.zip> --game <path>` | Loads the exported lots with the installed game's own classes (headers, chunks, rooms, every tile sprite; stairs with `--expect-stairs`). Needs the repository's `scripts/` folder and Java from the game. |
| `conflicts <mod.zip\|map\|world> --game <path>` | Lists installed maps that already use one of the map's native cells: the game's own maps (challenge maps excluded), Steam Workshop items next to the installation and the local mods folder (`--mods-dir`), all read only. It fails when the base game is hit. `build --game` runs the same check, and `--strict` then fails on it. |
| `housing-templates <dir>` | Regenerates the distributed housing examples (`examples/HousingTemplates`) from the built-in catalog: each `.pzbuilding.json`, the combined `.pzmap.json` and its mod archive, keeping ids and positions. |
| `serve <mod.zip> --game <path>` | Starts a dedicated server for playtesting in `work/playtest-server/<name>` (`--server-dir`), never in your `Zomboid` folder: the map loads next to Muldraugh with its spawn regions, the window prints the address and admin login, and `quit` stops it. `--fresh` starts a new world, `--port` sets the port (16261). See the [playtest checklist](PLAYTEST.md). |
| `server-check <mod.zip> --game <path>` | Starts the installed Build 42 dedicated server (`zombie.network.GameServer`, `-nosteam`) in a temporary cache folder with the mod and a test-only Lua probe. The probe asks the server's own metagrid for buildings at room tiles taken from the archive's authoring source, plus one empty control tile, and loads the map's spawn regions as the game does (profession keys, spawn points and how many lie in a building); the server is then stopped with `quit`. Your `Zomboid` folder is not touched. |

Common options:

- `--game <path>` points to the Build 42 installation. `build`, `validate` and `export` then check every tile name the exporter would write against the game's tile definitions and validate resources; `tiles` reads properties for `copyFrom`.
- `--strict` makes `build` fail when the seam check finds a lost or duplicated source feature.
- `--save-workspace` (`build`) also saves the world as an editor section workspace under `work/world-recovery` of the workspace (`PZMAP_WORKSPACE`, or the current folder). Open it under **World → Section workspaces**; this works for worlds larger than the 128 MB limit of a single `.pzworld.json`.
- `--report report.json` saves the seam check.
- Mod options: `--mod-id`, `--map-folder` (the server `Map=` value, defaults to the mod id), `--mod-name` (title in the mod list and map selector), `--common-maps` (puts `media/maps` under `common/` instead of `42/`), `--install` (extracts into `Zomboid/mods`, or `--mods-dir`). An install replaces only a folder that SimplePZMapmaker created earlier (it carries a `.simplepzmapmaker` marker).

The same mod options are available on every HTTP export as query parameters: `modId`, `mapFolder`, `modName`, `commonMaps=true`, `install=true` (for example `POST /api/world/export?modId=PilnaVillage&install=true`). `install=true` is accepted only on POST, which the editor host limits to JSON requests from its own origin; workspace exports and job downloads accept POST with an empty JSON body for that. A GET can only rename the returned archive.

## Scene file

```json
{
  "format": "SimplePZScene", "version": 1,
  "name": "PilnaVillage",
  "source": "pilna.geojson",
  "reference": {
    "bounds": { "south": 55.5363, "north": 55.5781, "west": 45.8833, "east": 45.9422 },
    "metresPerTile": 1, "originCellX": 100, "originCellY": 100, "rotationDegrees": -11.22
  },
  "sectionSize": 1024, "seed": 42, "skipEmptySections": true,
  "export": { "modId": "PilnaVillage", "name": "Pilna village", "commonMaps": false }
}
```

- `source` is one GeoJSON FeatureCollection or Overpass JSON file; `sources` lists several GeoJSON files whose features are merged (a feature `id` seen twice is kept once). Paths are relative to the scene file.
- `reference` is the rotated geographic reference of the [transfer contract](TRANSFER-CONTRACT.md#1-coordinates). The area is cut into `sectionSize` sections; a world holds up to 128 sections and 8,192 native cells.
- Section identities follow contract section 2: the first 32 hex digits of SHA-256(`name|X,Y`), so a rebuild keeps them.
- `skipEmptySections` leaves out sections without buildings, fences or paths.

Every section imports the whole source; buildings and barrier lines crossing a section edge are stored once at world level. The import honours the building tags of contract sections 5 and 6 (`pz:kind`, `pz:style`, `roof:colour`, `pz:wall`, `pz:trim`, `pz:attached`, `pz:door-to`).

## Seam check

The check reads the source again and assigns every building outline and barrier line to exactly one outcome:

Real output for the Pilna OpenStreetMap extract in `examples/Pilna` (1 m per tile, 1024-tile sections, rotated −11.22°):

```
world PilnaVillage: 26 sections, 1640 section buildings, 61 shared buildings, 0 shared fences
seam check building: placed-section 1640, placed-world 61, evidence 88, lost 0, duplicated 0
  evidence 42: skipped: Water or road
  evidence 27: skipped: Outline too large
  evidence 18: skipped: Building overlap
  evidence 1: skipped: crossing outline rejected at world level (water, overlap or access)
seam check: passed
tiles: all 2471 references exist in the game
```

The build took about five minutes; `probe --expect-stairs` on the resulting mod loaded 416 cells, 425,984 chunks and 9,565 rooms (1,429 upper-floor rooms, 249 stair tiles) with the installed game's classes. That extract contains no barrier lines, so fences are covered by the automated tests instead.

`evidence` features were skipped for a recorded reason (water or road, overlap, outside the world, a crossing outline rejected at world level). `lost` and `duplicated` features fail the check.

## Tileset file

```json
{
  "format": "SimplePZTileset", "version": 1,
  "modId": "PilnaTiles", "name": "Pilna tiles",
  "tileset": "pilna_fence_01", "fileNumber": 300,
  "tiles": [
    { "png": "fence_w.png", "copyFrom": "fencing_01_40", "properties": { "CustomName": "Pilna fence" } },
    { "png": "fence_n.png", "copyFrom": "fencing_01_41" }
  ]
}
```

Each PNG is one 8-bit RGBA or RGB frame, normally 128×256 like the game's 2x tiles. Frames are trimmed to their opaque pixels and packed into one atlas page. Tiles are named `<tileset>_<index>` in file order, eight per row. `copyFrom` copies the properties of an installed tile (wall, fence, door flags and materials); `properties` adds or overrides keys. `fileNumber` is the `tiledef` number (100–8190) and must not clash with other active mods. The archive contains `42/mod.info` (`pack=` and `tiledef=`), `42/media/texturepacks/<tileset>.pack` and `42/media/<tileset>.tiles`.

Enable the tile mod in **Game resources** to use its tiles in the editor; maps that use them then list it in `require=` automatically.
