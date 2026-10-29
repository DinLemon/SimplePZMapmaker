# Regional transfer profiles and ensembles

This is an opt-in authored profile, not a statement about every Russian village.
The Pilna sample of 60 listings cannot establish national material/interior statistics.
Integrated in standalone/World import, CLI scenes, Generation Studio parcel completion and Street ensembles.

## Integration interfaces

```csharp
Terrain terrain = TransferRegionalProfiles.RoadTerrain(tags, profile: "ru-rural");
RegionalRoadDecision decision = TransferRegionalProfiles.ExplainRoad(tags, "ru-rural");
// Persist decision.Reason/IsInferred separately from original SourceTags.
// Do not synthesize tags["surface"] = "asphalt".
```

`RoadTerrain(IReadOnlyDictionary<string,string>, string profile="default")` returns
`Terrain`. Explicit recognized `surface` always wins, including gravel on a primary
road and asphalt on a track. Unsupported explicit surfaces retain their value and
report conservative Dirt rendering as inferred; regional asphalt defaults do not
override them. Missing/blank surface is inferred. `ru-rural` uses Road for residential,
unclassified, living_street and higher through roads; service, track, path and minor
pedestrian routes use Dirt. Default service roads remain Road; default footway,
pedestrian and cycleway use Pavement. Unknown/missing road classes use Dirt.
Road is the editor's asphalt class; sand/grass/pavers are mapped to available terrain
classes, not asserted to be visually exact reproductions. Unknown profiles reject.
`ExplainRoad` snapshots all source tags and returns Profile, Terrain, IsInferred, Reason.

Adapt the active native catalog to Core without referencing Web from Core:

```csharp
var available = activeAssets.Select(a => new RegionalSprite(
    a.Id, a.Properties, a.ModId, a.Requirements)).ToArray();
var appearance = TransferRegionalProfiles.SelectRuralAppearance(available,
    material: "wood", seed: 42, externalWall: originalBoardSetOrNull);
var nextBuilding = TransferRegionalProfiles.ApplyRuralAppearance(building, appearance);
var palette = TransferRegionalProfiles.SelectRuralPalette(available,
    color: "green", vehicleGate: configuredThreeWideStyleOrNull);
```

`activeAssets` means actual currently available sprites, not invented resource IDs
or the entire unverified browser cache. Include per-sprite `ModId` and `Requirements`.
`RuralAppearance` supplies ExternalWall (`WallSet?`), RoofTileset, FlatRoofTile,
TrimNorth, TrimWest, AssetSources, RequiredMods, Warnings. Complete native wall sets
come from WallCatalog; all referenced parts must exist, with native WallW/WallN.
An original colored board set can be supplied through `externalWall`; every part
must already be active. A pitched family is selected only if all 66 actually
referenced automatic pieces exist. Missing assets remain null with warnings. Do not
guess a nearby sprite index or manufacture a complete family from one sprite.

ApplyRuralAppearance returns a copy, preserves Modified buildings, authored edge
tiles/overlays and Edited edges, changes only exterior edges, and carries native
owners/dependencies on the returned building. Interior room boundaries are preserved.
At map/world integration merge required mods and ownership with conflict checking,
preserve manual locks and present warnings in the existing staged preview/apply.

`RuralPalette` supplies Fence (`FenceStyle?`), Objects (`EnsembleResource` roles
trim-n/w, shutters-n/w, chimney-n/w, awning-n/w, antenna-n/w), VehicleGate,
AssetSources, RequiredMods, Warnings. Original assets opt in with native CustomName
`rural-corrugated-green` / `rural-corrugated-maroon`, or `rural-trim`, `rural-shutters`,
`rural-chimney`, `rural-awning`, `rural-antenna`, plus Facing=N/W. Fences need
collideN/collideW. Wall trim, shutters and awnings need WallOverlay and attachedN/W.
Their Objects are *wall attachment bindings*, not permission to drop them as blocking
ground decorations. Chimneys/antennas need an authored roof placement/level; this
sidecar does not add a competing elevated-decoration model.

Without a configured three-wide vehicle style, keep default picket + one-tile
wicket + a separate three-tile **open vehicle gap**, and show VehicleGateWarnings.
Three wickets are not a vehicle gate. A configured style must have Width=3 and all
closed/open N/W sprite IDs available; preserve its native owners as well.

## Emergency accommodation

```csharp
var plan = StreetEnsembles.Create(project, new StreetEnsembleOptions {
    Preset = "emergency-shelter", X = 20, Y = 20, Width = 100, Height = 90, Seed = 42
}, resources); // [] uses verified vanilla S-facing small tents
// Existing preview flow displays plan.Warnings; do not silently drop them.
var next = StreetEnsembles.Apply(project, plan);
```

Minimum area is 88×72, maximum 256×256. The preset uses a school with dormitory rooms,
kitchen appliances and bathrooms; an adjacent clinic medpoint; industrial-facade
container HQ with an office; furnished toilets; and four organized two-cell tents.
This is a school adaptation of a PVR, not a tent-only camp. A true school/club replica,
distinct corrugated container shell and large army tents require separately verified
assets. The existing vanilla small tent is an explicit visual compromise.

StreetEnsemblePlan now carries GateStyles alongside FenceStyles/Fences. Apply merges
styles with collision checks, validates bounds/occupancy and clear continuous routes,
rejects stale fingerprints, and returns a new validated project. Paths connect the
public sidewalk to every facility doorway and tent's south entrance. Generated fence
uses a real open single wicket and a separate three-tile vehicle gap with an explicit
missing-style warning. The preset currently does not consume a configured vehicle
GateStyle; main parcel integration can consume RuralPalette.VehicleGate. `Fences=false`
omits fence/gates. Native lighting is omitted with a warning. Parking/Greenery and
apartment dimension controls are not used by this fixed emergency layout.

Resource role `tent` can override the default with a complete bounded S-facing group;
the caller must validate sprite availability/group orientation through the catalog.
DefaultResources() returns fresh instances of the installed B42 group camping_01_2/3,
with SpriteGridPos 0,0 / 1,0, Facing=S. No random IDs or external downloads are used.
All three previous presets retain their generator branch and resource policy.

## Original asset gap and runnable authoring recipe

Vanilla catalog fixtures are useful for generic wood/brick walls, trim, small tents
and room furniture. They do not establish an original Russian green/maroon corrugated
palette with matching boards, shutters, chimney, awning and antenna. The following
recipe creates *new original procedural artwork*, with no game pixels redistributed.
It emits transparent 128×256 native frames, a native property spec and explicit
bindings; it does not claim those new IDs exist before build/attach.

The installed B42 `media/lua/server/BuildingObjects/ISDoubleDoor.lua:create` creates
parts 1–4 (`getSquare2Pos`, `getSquare3Pos`, `getSquare4Pos`). Do not set arbitrary
DoubleDoor=1,2,3 and call it a functioning linked three-wide native gate. The recipe
includes one coherent three-panel editor gate, with separate closed/open N/W artwork,
not three door/wicket objects. Its closed panels have collision and its open panels
do not. GateStyle preview/export can select that authored state. Coupled interactive
three-panel/sliding gate behavior in-game still needs a separately authored runtime
mod and client/multiplayer verification; until then retain the open vehicle gap.

The versioned script is `scripts/make-rural-assets.py` (Python standard library only). Use a free fileNumber checked against active mods; 1701 below is a candidate,
not a globally reserved number. The editor rejects collisions before build/attach.

Run `python scripts/make-rural-assets.py work/rural-original --file-number 1701`. The same bounded build/preview/attach pipeline is available through Agent assets.


Run from PowerShell (the API/session must already exist; use its current revision):

```powershell
python .\make-rural.py "$env:TEMP\pz-rural-original" --file-number 1701
# Optional native build through the existing authoring service, no project mutation:
# $env:PZMAP_SESSION_TOKEN = <existing local session token, do not log it>
python .\make-rural.py "$env:TEMP\pz-rural-original" --file-number 1701 --api http://127.0.0.1:48777 --revision 1
```

The second invocation creates one draft; do not repeat it with an already reserved
mod/tileset identity. Read/reuse that draft through existing endpoints or choose an
explicit new original identity. Tokens belong in the environment; do not publish
draft/session URLs. Inspect uploaded PNGs and the `native_*.png` reconstructed pack
previews. The drawings are original rough placeholders, not photorealistic finished
assets. Native directions, placement, shadows and alignment require a game daylight
check before treating the pack as production art.

When preview is accepted, attach the exact built draft using the existing
`tileset_mod_attach` operation (current session and draft revisions). That stores
the separate mod's owning identity and checked FileNumber. Only then resolve the
bindings' IDs against the now-active catalog and call the selection helpers. The
native mod has `42/mod.info`, `42/media/texturepacks/rural_original_01.pack`,
`42/media/rural_original_01.tiles` and `42/agent-tileset.json`; mod.info registers
pack/tiledef. Dependencies use modId=`rural_original`, never an invented `game` owner.
No files are written into the user's Zomboid profile. Keep the separate ZIP with
the map's dependency manifest; do not silently bake an unregistered sprite into a map.

## Roofs and validation

`RoofPartitions` decomposes stepped gabled/shed footprints into deterministic rectangular wings used by `RoofGeometry` and native `TileComposer`. The decomposition never fills courtyard holes. Wings narrower than two tiles keep flat weather cover; the original building outline is retained. Independently authored RoofParts and custom valley-junction artwork are not implied by automatic decomposition.

`TransferRegionalTests.Cases()` and `TransferLessonIntegrationTests.Cases()` are registered in the shared test Program. Set PZMAP_GAME_PATH to the installed B42 directory for native catalog checks and PZMAP_RURAL_RECIPE_DIR to the generated procedural source directory for native pack/pixel/ownership checks. Tests cover pure preview/apply, continuous routes, stale/obstructed plans, resource owners/manual edges, L/T/courtyard native roofs and complete tent resources.

## Current integration

The shared test registry includes these cases. Generation Studio resolves active catalog sprites into rural appearance/fence bindings; standalone/World/scene road profiles are opt-in, explicit surface tags take priority. The four-facility emergency preset is exposed in Street ensembles and validates actual complete tent groups. `RoofPartitions` now supplies native rectangular wings for stepped gabled/shed roofs and flat cover for thin wings, preserving courtyard holes; Independently authored RoofParts and seamless custom valley artwork remain outside this automatic decomposition.

`python scripts/make-rural-assets.py work/rural-original --file-number 1701` generates the original source frames/spec. The installed-game recipe test built `work/rural-original/rural_original.zip`, compared every preview pixel with its PNG, and resolved ownership/bindings. It does not assert linked in-game behavior for the three-panel gate. See TRANSFER-LESSONS-IMPLEMENTATION.md for integrated acceptance and remaining client checks.
