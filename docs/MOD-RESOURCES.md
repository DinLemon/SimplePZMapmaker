# Mod resources

Open **Game resources** and its mod-resource controls. Choose your Steam Workshop resource root. Enter the installed game version, discover mods, select them and activate them. Existing Workshop and local mod folders are suggested from Steam libraries and the standard Zomboid mods folder. Additional absolute roots are entered one per line and may be on any drive or an accessible network share. Resources are read only; caches/settings remain in the editor workspace. The verified installation reports **42.21**, so the initial compatibility value is `42.21.0`.

The catalog selects a compatible variant such as `42`, `42.0` or `42.20`; `common` resources load before that variant. Legacy mods with a root `mod.info` are also read. A legacy mod's actual game compatibility is determined by its author: absent `versionMin`/`versionMax` does not prove compatibility.

Dependencies from `require` activate automatically before dependants. The selected mod order controls remaining conflicts; diagnostics identify replaced tiles/images. An incomplete/corrupt package does not replace the working catalog. Disabling mods restores game resources. Roots, version and active IDs are saved in `work/assets/mods.json`.

The catalog/building palette can filter by game, all active mods or one mod, and cards show the mod ID. Registered `tiledef` resources support binary `tdef`0/1 and text definitions, PNG texture packs 0/1 and complete sprite grids. Sprites without registered properties are not placed as unknown physical objects; missing images are shown explicitly. The editor never executes mod Lua.

Projects retain required mod IDs and the source of chosen tiles. If another mod takes over a tile of the same name, validation stops export and asks for the original owner. Export writes `require` into its original `mod.info`; third-party images, textures and definitions are not copied. Map recipients install those mods separately.

## Game installation

Startup tries the saved selection, the configured `PZMAP_GAME_PATH` and registered Steam libraries. Discovery reads Windows Steam registration, modern or legacy `libraryfolders.vdf` and `appmanifest_108600.acf`; it does not scan whole drives. A stale saved/configured path does not block discovery. Portable installations can be selected manually in **Game resources**: paste the game folder, then **Load folder** or Enter. **Find installed game** repeats discovery and offers a choice if multiple installations exist.

Quoted paths, environment variables and pasted game executable paths are normalized. Relative paths and filesystem links are unsupported. The resource folders must be readable by the account running the editor; network shares need to be connected first. Test harnesses can set `PZMAP_DISABLE_GAME_DISCOVERY=1` to isolate a no-game session.

![Manual game folder on another drive](screenshots/24-installation-folder.png)

![Mod root on a separate drive](screenshots/25-mod-folder.png)

## Local API

Replace `<Steam library>` with your actual Steam library on any drive before sending the sample request. The placeholder is illustrative, not a usable filesystem path.

- `GET /api/assets/installations`: detected game installations and suggested mod roots.
- `POST /api/assets/refresh`: `{ "gamePath": "<absolute game folder>" }`; validates and remembers a working selection.
- `GET /api/assets/mods`: discovered mods, active IDs, layers and diagnostics.
- `POST /api/assets/mods/discover`: `{ "roots": ["<Steam library>\\steamapps\\workshop\\content\\108600"], "gameVersion": "42.21.0" }`.
- `POST /api/assets/mods/activate`: `{ "ids": ["Erikas_Tiles"] }`; dependencies resolve automatically.
- `GET /api/assets/search?source=Erikas_Tiles`: one mod; `source=game` and `source=mod` filter groups.
- Search, descriptions and compound layouts return `source`, `modId` and `requirements`.

## Verification

Generated definition fixtures test truncated files, size limits, dependency order, changed tile ownership and export without third-party payloads. The installed **Erika's Tiles** was checked through binary definitions, PNG atlas reads, activation and deactivation. Browser checks cover discovery, toggles, source filtering and images. The 0.11.2 regression maps a real Steam library and a generated mod fixture to separate drive letters: game atlas reads, mod tile definitions and OBJ model geometry all load successfully. A bad game folder keeps the working catalog.

The installed game version was also read through `zombie.core.Core`. Installed `ChooseGameInfo.readModInfoAux` instructions remove backslashes from `require` before comma splitting; canonical unprefixed IDs are compatible. Local evidence is in `work/logs/mod-assets-tests.log`, `work/logs/mod-native-info-probe.log` and `work/mod-inspect/mod-bytecode.txt`. These files and third-party assets are excluded from Git.

Before activation, 0.7 validates the full dependency chain and displays the missing ID, a path such as city → furniture → tiles, and an action: install the Workshop mod, add its root or check its version. Cycles are shown too. Failed activation retains the working catalog. Ownership tracking includes the invisible game collision sprite.
