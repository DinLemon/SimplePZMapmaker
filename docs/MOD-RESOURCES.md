# Mod resources

Open **Game resources** and its mod-resource controls. The G: Steam Workshop root is prefilled. Enter the installed game version, discover mods, select them and activate them. Additional roots are entered one per line; only G: roots are accepted. The verified installation reports **42.21**, so the initial compatibility value is `42.21.0`.

The catalog selects a compatible variant such as `42`, `42.0` or `42.20`; `common` resources load before that variant. Legacy mods with a root `mod.info` are also read. A legacy mod's actual game compatibility is determined by its author: absent `versionMin`/`versionMax` does not prove compatibility.

Dependencies from `require` activate automatically before dependants. The selected mod order controls remaining conflicts; diagnostics identify replaced tiles/images. An incomplete/corrupt package does not replace the working catalog. Disabling mods restores game resources. Roots, version and active IDs are saved in `work/assets/mods.json`.

The catalog/building palette can filter by game, all active mods or one mod, and cards show the mod ID. Registered `tiledef` resources support binary `tdef`0/1 and text definitions, PNG texture packs 0/1 and complete sprite grids. Sprites without registered properties are not placed as unknown physical objects; missing images are shown explicitly. The editor never executes mod Lua.

Projects retain required mod IDs and the source of chosen tiles. If another mod takes over a tile of the same name, validation stops export and asks for the original owner. Export writes `require` into its original `mod.info`; third-party images, textures and definitions are not copied. Map recipients install those mods separately.

## Local API

- `GET /api/assets/mods`: discovered mods, active IDs, layers and diagnostics.
- `POST /api/assets/mods/discover`: `{ "roots": ["./workshop-content"], "gameVersion": "42.21.0" }`.
- `POST /api/assets/mods/activate`: `{ "ids": ["Erikas_Tiles"] }`; dependencies resolve automatically.
- `GET /api/assets/search?source=Erikas_Tiles`: one mod; `source=game` and `source=mod` filter groups.
- Search, descriptions and compound layouts return `source`, `modId` and `requirements`.

## Verification

Generated definition fixtures test truncated files, size limits, dependency order, changed tile ownership and export without third-party payloads. The installed **Erika's Tiles** was checked through binary definitions, PNG atlas reads, activation and deactivation. Browser checks covered discovery, toggles, source filtering, images and rejection of C: mod roots without losing the active selection.

The installed game version was also read through `zombie.core.Core`. Installed `ChooseGameInfo.readModInfoAux` instructions remove backslashes from `require` before comma splitting; canonical unprefixed IDs are compatible. Local evidence is in `work/logs/mod-assets-tests.log`, `work/logs/mod-native-info-probe.log` and `work/mod-inspect/mod-bytecode.txt`. These files and third-party assets are excluded from Git.

Before activation, 0.7 validates the full dependency chain and displays the missing ID, a path such as city → furniture → tiles, and an action: install the Workshop mod, add its root or check its version. Cycles are shown too. Failed activation retains the working catalog. Ownership tracking includes the invisible game collision sprite.
