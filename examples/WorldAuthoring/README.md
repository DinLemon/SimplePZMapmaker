# World authoring showcase

Open `world-authoring.pzworld.json` with **World → Open the world**. It contains two independent 256×256 sections at cells `(100,100)` and `(101,100)`.

- A shared river occupies both sides of the section seam.
- A native bridge crosses the seam at z=2, above the river, with continuous guard rails and supports.
- Native stairs connect both banks to the bridge. The east stair access is a second overlapping shared feature.
- A ground road passes beneath the bridge. Supports leave that road clear.
- Each section contains one generated three-floor apartment building, with six apartments, real room definitions, furnishings and internal staircases. Reusable entrance and apartment modules are included in each section's building library.

Select a path under **Shared roads and bridges** to inspect or edit its absolute tile coordinates. **Open native section** displays the selected section's native geometry. The bridge continues into the adjacent section in the complete world export.

`WorldAuthoring-B42.zip` is the combined native Build 42 mod. It contains the editable source world. All content is synthetic; the source uses original game tile identifiers and includes no game assets, Workshop content, external geographic data, credentials or machine-specific paths.

The fixed cell coordinates may overlap another map mod. Use the archive only in a separate **new** test world. Extract the mod folder into your user mod directory, enable it, and select the **World authoring showcase** map. The example is not installed automatically.

## Reproduce the files

From the repository root in PowerShell:

```powershell
. ./scripts/WorkspaceEnv.ps1
dotnet run --project tools/WorldAuthoringExample
node scripts/verify-native-map.mjs examples/WorldAuthoring/WorldAuthoring-B42.zip
./scripts/verify-game-runtime.ps1 -MapArchive examples/WorldAuthoring/WorldAuthoring-B42.zip
```

The generator references only the Core project and uses fixed names, identifiers, seeds and ZIP timestamps. The installed-game reader requires your own Build 42 installation through `PZMAP_GAME_PATH` or `-GamePath`; it reads the installed runtime and resources and writes probe files below the repository's `work` directory. It does not start a game session or walk a character through the map.
