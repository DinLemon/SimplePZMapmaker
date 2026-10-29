# Build 42 transfer export validation — 2026-10-23

Scope: `NativeMapExporter.cs`, new `NativeZombieIntensity.cs`, `Core/Exchange/*`, new `TransferB42Tests.cs`, this document. Existing uncommitted changes, including the metadata refactor in `NativeMapExporter`, were retained. No Programs, global test registration, project schema, UI, game installation or real game profile were edited. No commits.

## Native population: evidence and policy

Installed installation: `G:/Steam/steamapps/common/ProjectZomboid`. Its `projectzomboid.jar` SHA-256 was `e1a69eb743ede60b213a0fe7f8b83d4fcab773036d256cc4543a336f3b058a33`. Bundled runtime reported Zulu OpenJDK 25.0.1+8-LTS. These observations apply to this installation, not every B42 release.

Bounded scan of **main-map** `media/maps/Muldraugh, KY/*.lotheader`:

- Enumerated 4,065 filenames; selected 256 deterministically, sorted by Python `Path`, index `i*(n-1)//255`, `i=0..255`.
- Read only the first eight bytes and last 1,024 bytes of each selected file: 264,192 bytes total. Every signature/version was `LOTH` + little-endian `1`.
- Tail histogram: `0:250642, 1:3662, 2:5854, 3:1241, 4:449, 5:31, 6:59, 7:149, 8:8, 9:11, 10:38`.
- Thus the sample maximum is **10**, not 3. This is not an exhaustive maximum or proof of a hard engine limit.
- An earlier separate 256-file sample across all map folders also included challenge maps, whose tail values reached 255. Do not mix that population with the ordinary main-map baseline. No universal legal-byte maximum is inferred.

Reproduce the main-map scan without loading whole files:

```powershell
@'
from pathlib import Path
from collections import Counter
files=sorted(Path('G:/Steam/steamapps/common/ProjectZomboid/media/maps/Muldraugh, KY').glob('*.lotheader'))
assert len(files)>=256
hist=Counter()
for i in range(256):
    with files[i*(len(files)-1)//255].open('rb') as f:
        assert f.read(8)==b'LOTH\x01\x00\x00\x00'
        f.seek(-1024,2)
        hist.update(f.read(1024))
print(len(files),256,dict(sorted(hist.items())))
'@ | python -
```

The exporter now chooses the policy `ceil(editorDensity * 10 / 255)`, using integer arithmetic. Examples: `0→0, 1→1, 25→1, 26→2, 127→5, 128→6, 254→10, 255→10`. All positive inputs remain positive, and the mapping is monotone. Invalid input is rejected rather than cast/wrapped. This is intentionally a quantized intensity, not a zombie count or a promise of identical population behavior.

The editable `Zombies` raster and embedded `project.pzmap.json` remain 0..255. Re-exporting the same project does not scale its source again. Header tails retain x-major chunk order. Shared `WriteCells` applies the same conversion to map, world and streamed-world exports. `NativeSectionCache.Key` includes the Core module version ID, so a rebuilt Core invalidates old cell fragments.

Single-map `INSTALL.txt` carries `NativeZombieIntensity.ExportNotice`. Host/UI diagnostic presentation and world/streamed-world installation notices still need integration by their owners; their Programs/exporters were outside this slice. Old installed archives/saves are not rewritten. A new export and a fresh isolated save are necessary for comparison.

## TBX/TMX bounds and furniture

- Validate **each complete furniture part coordinate before computing a linear index**. TBX has a `(width+1)*(height+1)` user grid, but its east/south wall padding is not furniture space.
- Export validates the complete design first and now guards the writer's individual placements as well. Invalid parts cannot wrap into the next row or become an indexing exception. Neither `allowLossy` nor padded wall storage permits furniture overhangs.
- Import rejects negative furniture anchors/offsets, outside parts, parts in empty footprint cells and reserved-border furniture as `error`. This also protects TMX furniture layers through the shared reader. Wall edge coordinates retain their legitimate padded border.
- Valid multipart furniture is still flattened into individual tiles only with the existing explicit grouping diagnostic/acceptance; coordinates and resource identifiers survive. Group identity, authored navigation flags, grime, stacking and stairs remain outside the lossless subset as already reported.
- TBX roof rectangles crossing the building envelope are hard errors. Valid but unrepresentable roof geometry produces an explicit `roof` diagnostic. There is no automatic envelope enlargement or footprint clipping.

## BuildingEd roof subset and west overlays

Inspected the pinned upstream [TBX writer](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingwriter.cpp), [reader](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingreader.cpp), [roof implementation](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingobjects.cpp) and [tile categories](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/src/tiled/BuildingEditor/buildingtiles.cpp). BuildingEd's FlatTop depth `Three` places its surface on the floor above the object; arbitrary flat-roof depth values must not be treated as equivalent.

Supported in TBX: one uncapped `RoofType="FlatTop" Depth="Three"` object on the last floor, covering the full rectangular indoor footprint, with the same full footprint on every floor. Its `TopTiles` references a uniform `roof_tops` entry with all six native shape enums; `CapTiles`/`SlopeTiles` are zero. Import reconstructs `Roof="flat"` and the actual `FlatRoofTile`, rather than placing another room on the roof. Export writes the same native structure, without a private JSON metadata extension.

Small buildings and 31-floor towers have explicit tests; a requested roof at z=32 is rejected without rewriting the building. These are structural exchange fixtures, not proof that such a tower has valid stairs/access. Slopes, partial roofs, terraces, courtyards, changing footprints, balconies and TMX roof export remain unsupported; strict conversion blocks. Explicit lossy conversion reports the omitted roof and retains the original source object.

West wall overlays cannot acquire a verified mirror transform from a tile name alone. TBX/TMX export now states the orientation/mirror limitation explicitly. Nonempty imported `WallOverlay*` layers are reported; TBX wall-overlay furniture is not silently reinterpreted as ordinary furniture. TMX flip bits remain rejected. Strict conversion returns no converted building/XML; source objects are not changed. Explicit lossy acceptance may omit the reported overlay, so retain the original project. Native rendering still uses authored directional sprite IDs; no new automatic west-mirroring renderer was implemented or visually verified.

BuildingEd/TileZed GUI opening and in-game roof/weather/overlay rendering have **not** been tested. Headless XML tests do not establish those outcomes.

## Existing tile-definition and spawn paths reviewed

No second collision allocator was added. Existing `AgentTilesetStore.Create` checks `GameAssetCatalog.CheckAgentTilesetNames`, reserves workspace draft names/numbers, and `Build` checks again. The catalog checks discovered inactive and active mods for ModId, tiledef filename, numeric FileNumber, pack filename and sprite-prefix collisions, including vanilla prefixes. The build validates its produced pack/tile definition; registration repeats ownership checks. Existing `AgentTilesetTests` includes the inactive-mod/late-build-collision case; it and the native pack/properties/catalog roundtrip passed in the isolated regression runner.

Limits: this protects that agent-tileset path and discovered catalogs, not arbitrary third-party installations. Direct `TilesetBuilder.Write` validates the FileNumber range and archive structure but has no installed-mod registry, so it cannot promise global uniqueness. `TileDefinitionComparer` compares an explicit baseline; it is not an installed registration allocator. `.pack`, `.tiles`, manifest/provider ownership and map `require=` dependencies remain necessary; missing discovery/baselines must remain unavailable in diagnostics. No tiledef, pack or manifest in the installation was modified.

`SpawnPlanner` chooses free ground-floor room cells in houses with bedrooms, preferring livingroom/kitchen/bedroom, and emits profession keys. The new test checks an indoor point excludes furniture and preserves the dry authored fallback. Native fixture has a real house and embedded profession spawn Lua. `ProjectValidator` continues to reject authored spawns in water/outside the map; its authored fallback is exterior, while `SpawnPlanner` generates house points separately.

Limits: house selection currently excludes furniture but does not independently exclude every blocking static-model footprint, verify all world dry-land conditions, or prove the spawn room has a traversable exit. Those are existing-path limitations, not fixed here. Run the existing access/resource/zone/cell-conflict checks, then inspect actual profession spawn and egress in the client. The new diagnostic test confirms that Project Doctor with no resource catalog or tile baseline does not claim completion.

## Tests and actual installed-reader check

New helper: `tests/SimplePZMapmaker.Tests/TransferB42Tests.cs`, public `Cases()` (16 cases). Global registration is deliberately untouched. The integration owner should add:

```csharp
tests = tests.Concat(TransferB42Tests.Cases()).ToArray();
```

The local scratch runner links **only** the new file and existing `ExportTests.cs` and references Core. To reproduce that runner from the repository root:

```powershell
New-Item -ItemType Directory -Force work/transfer-b42 | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><ProjectReference Include="../../src/SimplePZMapmaker.Core/SimplePZMapmaker.Core.csproj"/><Compile Include="../../tests/SimplePZMapmaker.Tests/TransferB42Tests.cs" Link="TransferB42Tests.cs"/><Compile Include="../../tests/SimplePZMapmaker.Tests/ExportTests.cs" Link="ExportTests.cs"/></ItemGroup></Project>
'@ | Set-Content work/transfer-b42/TransferB42.Runner.csproj
@'
var failed=0;var passed=0;
foreach(var (name,run) in TransferB42Tests.Cases().Concat(ExportTests.Cases())) {
    try { run(); Console.WriteLine("PASS "+name); passed++; }
    catch(Exception e) { failed++; Console.WriteLine("FAIL "+name+": "+e.Message); }
}
Console.WriteLine($"RESULT {passed} passed, {failed} failed");return failed==0?0:1;
'@ | Set-Content work/transfer-b42/Program.cs
. ./scripts/WorkspaceEnv.ps1
$env:TRANSFER_B42_ARCHIVE=Join-Path $env:PZMAP_WORKSPACE 'work/transfer-b42/native-intensity.zip'
$env:TRANSFER_B42_ARTIFACTS=Join-Path $env:PZMAP_WORKSPACE 'work/transfer-b42'
dotnet run --project work/transfer-b42/TransferB42.Runner.csproj -c Release
```

Observed RED: **8 passed, 7 failed**, before production changes. Final fresh **normal Core Release build and 20/20 targeted cases passed** (16 transfer + 4 existing export cases), without skipping project references. During concurrent work there was a temporary CS0136 in `GenerationStudio.cs:25`; its owner resolved it, and the fresh Core run supersedes the intermediate cached-binary run.

Also passed a separate isolated regression runner: **12/12** = all 10 existing `BuildingExchangeTests.Cases()` (including the HTTP import/export/request-boundary test) + `AgentTilesetTests` cases `agent tileset pixels properties ZIP and catalog round trip` and `agent tileset inactive mod FileNumber and filename collisions`. Command: `dotnet run --project work/transfer-b42/regressions/Regressions.csproj -c Release`. That scratch project links those two existing test files and references the existing Web project; no registration/source changes were made to them. Total verified: **32 cases**. This is targeted coverage, not the whole suite.

The normal full test-project build subsequently failed on out-of-scope `CadastreTests.cs:152,155,160`: absent `CadastreRequest.StepMetres` / `CadastreException.Progress` (CS0117/CS1061). No such files were changed here. Fresh full-suite/CLI/browser acceptance remains pending; a targeted HTTP pass is not a browser/client playthrough.

Transfer cases cover: actual four-cell native tail expectations/order/source retention; zero and all-256-input monotonicity/boundaries; invalid density guard; valid multipart coordinates; rejected exported overhangs; negative/outside imported parts; TBX and TMX reserved-border furniture rejection; small/tower FlatTop semantics and deterministic roundtrip; z=32 rejection; independently transcribed native roof fixture and partial-roof refusal; explicit slopes/west-mirror limitations; roof overhang rejection; imported overlay layers/furniture not becoming ordinary furniture; free indoor/dry fallback spawns; unavailable diagnostic coverage.

Ran the existing `scripts/NativeGameProbe.java` with the installed JAR against the new four-cell archive. Used the existing hash-verified ECJ 3.44 compiler and dedicated `work/transfer-b42/classes`, `cache-house`, and Java `user.home` under that scratch root. Result:

```text
NATIVE_GAME_PROBE cells=4 chunks=4096 rooms=4 upperRooms=0 basementRooms=0 minLevel=0 maxRoomLevel=0 stairTiles=0 basementStairs=0 status=passed
```

This verifies signed-level ABI/header/chunk parsing, room records and sprite presence in the installed game classes. It uses `no-stairs` because this fixture is single-storey. It does not simulate population or launch a client. A separate exploratory getter assertion after `loadCell` returned zero meta-chunk intensities even where the file held positive bytes; it **failed** as a population assertion and is not acceptance evidence. Direct `IsoMetaChunk.setZombieIntensity`/`getUnadjustedZombieIntensity` reflected values through 255, further confirming that 10 is an export policy rather than a hard byte ABI restriction. No claim of population behavior or a fixed client hang follows from these headless checks.

Scratch evidence: `work/transfer-b42/red.log`, `green-fresh.log`, `regressions.log`, `tests-build.log`, `probe.log`, `vanilla-tail-scan.json` (per-sample tail hashes), `native-intensity.zip`, `flat-1-floors.tbx`, `flat-31-floors.tbx`. These are local verification artifacts, not committed deliverables. Author self-review only; no independent reviewer was dispatched.

## Remaining client acceptance and hang diagnostics

1. Use a new, explicitly isolated directory, e.g. `$playtestRoot=Join-Path $env:PZMAP_WORKSPACE 'work/transfer-b42/client-acceptance'`. Put extracted test mods under its `mods`; start the installed game with `-cachedir=$playtestRoot` and `-debug`, explicitly quoted as separate arguments. If starting from PowerShell, use `Start-Process -FilePath '<game>/ProjectZomboid64.exe' -WorkingDirectory '<game>' -WindowStyle Hidden -ArgumentList @('-debug',('"-cachedir='+$playtestRoot+'"'))`. This command is guidance, not an action performed here. Confirm the game's log/save path is inside that directory before creating a character. Do not reuse or delete `%UserProfile%/Zomboid`, Steam cloud saves, workshop content or a real server cache.
2. Enable the exported map and its required tile/resource mods. Create a NEW single-player save, choose the exported map, choose a profession, and start at daylight with recorded sandbox population/respawn settings. Preserve those settings across A/B tests. Compare a zero-density export with the converted-density export using separate fresh saves/caches. A single new-save pass does not prove all worlds cannot hang.
3. In the debug Lua command window, a local single-player diagnostic can use `getGameTime():setTimeOfDay(12.0)`. For the supplied fixture, `getPlayer():teleportTo(28200.5,30760.5,0)` targets local tile `(40,40)` in cell `(110,120)`. First check that destination is free/dry in the exported scene; use the chosen spawn instead if it is occupied. General conversion: `worldTileX=cellX*256+localX`, similarly Y. Installed Lua references: `media/lua/client/DebugUIs/StreamMapWindow.lua` uses `teleportTo`; `Tutorial/TutorialSetup.lua` uses `setTimeOfDay`. These API names were inspected, not executed in a client here. Do not use teleport to bypass an unverified door/stair acceptance test.
4. Check the actual spawn is inside a house where planned, free of furniture/models, dry, with working door/egress. Walk outdoors and back, cross a cell seam, climb supported stairs, inspect the small flat roof and roof/weather cover at midday, and inspect west/north overlay alignment with the relevant installed sprite resources. Open the generated TBX files in the intended BuildingEd version and inspect the FlatTop's Z placement and custom surface. The 31-floor structural fixture has no stairs and is not a traversal fixture.
5. If the client stalls, retain `console.txt`, recent logs, exact exported archive, active mod list, coordinates, elapsed time, sandbox settings and reproduction steps from the isolated root. A running server or successful header parse does not establish client responsiveness or visual fidelity.
6. Obtain a full **matching JDK 25** on the same machine/user account for attach diagnostics. The bundled game runtime here has no `jcmd`/`jstack`. Example with the actual installed JDK path: `& 'C:/path/to/jdk-25/bin/jcmd.exe' -l`, identify the game JVM PID, then `& 'C:/path/to/jdk-25/bin/jcmd.exe' $gamePid Thread.print -l > "$playtestRoot/threads-1.txt"`. Take two more captures a few seconds apart as `threads-2.txt`/`threads-3.txt`; compare repeated blocked/loading/population stacks. Alternatively use that JDK's `jstack.exe -l $gamePid`. Do not attach to an unrelated JVM or kill a process as part of collecting a dump. If attach is unavailable, record that failure; an absent dump is not proof that the game is healthy.

Still open: real client loading/population responsiveness, visual roof/weather/lighting/overlay rendering, BuildingEd GUI acceptance, arbitrary third-party tile-definition drift and static-model-aware spawn/egress acceptance. NSPD provider availability is independently unverified. Global test registration, complete-suite build and map/world/streamed notices are integrated; see TRANSFER-LESSONS-IMPLEMENTATION.md. Headless results do not certify these client checks.
