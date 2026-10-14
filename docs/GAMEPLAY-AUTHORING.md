# Game zones, vehicles and interactive objects

These authoring controls are inside the main editor. They work with the installed Build 42 catalog and preserve registered resource owners. No third-party Lua is executed while discovering distributions.

## Draw a gameplay zone

1. Open the **Game zones and vehicles** panel.
2. Enter a name and choose a zone type: `TownZone`, `Forest`, `DeepForest`, `Vegitation`, `Farm`, `TrailerPark` or `Nav`.
3. Click **Draw zone rectangle**, then drag across the map. The rectangle snaps to whole ground-level game tiles.
4. Check the displayed coordinates and dimensions, then click **Apply zone**.
5. Select a saved zone to change its properties or redraw its bounds. Use **Remove** to delete it. Each accepted change is one history operation.

Authored terrain zones replace overlapping automatic terrain-zone rectangles at export. Two authored terrain zones cannot overlap. Vehicle zones are a separate layer and may overlay a terrain zone.

For vehicles, choose `Vehicle` or `ParkingStall`, then select an available registered distribution. Choose the facing direction and whether to force it. Export uses the game's `Direction` and `FaceDirection` properties. Vehicle generation remains probabilistic: a zone does not guarantee that a car will appear. Water, buildings and overlapping vehicle zones are rejected.

![An authored vehicle zone in the main Game view](screenshots/10-gameplay-zones.png)

## Edit an object's gameplay

Select a placed static model in **Game view** to show **Object gameplay**. The same properties are available in the building/environment model controls.

- **Scenery** stays passable. **Obstacle** blocks movement. **Destructible** adds health. **Container** adds health, capacity and optional registered loot.
- Mark the occupied tiles in the **Footprint** grid. The top-left anchor is fixed; the mask must be connected by tile edges. The mask supports up to 64 tiles in an 8×8 grid, independently of the model's visual scale and rotation.
- **Native door** occupies one tile edge, North or West. Set its initial open state, key lock and positive key ID. A closed door blocks that edge; an open door permits crossing. The editor does not generate or distribute matching keys automatically.
- **Native light** or **Emit native light** adds RGB values, a radius of 1–20 tiles and an initial active state. Active is an authored power assumption; it does not simulate a building's electrical wiring.
- For a container, choose **Registered loot distribution**, or leave it empty. The catalog resolves actual `ProceduralDistributions.list` registrations and records the provider's mod dependencies.

Click **Apply object gameplay**. Validation checks the entire footprint, existing blockers, available floor, protected passages and model/distribution ownership. Failure retains the previous object. Floor placement preserves signed levels, including basements.

Export writes original declarative proxy scripts and native API glue. Original model geometry and textures remain in the owning game/mod installation. Compound objects share health/removal identity; their occupancy is a tile mask, not a mesh collision hull. Roofs, structural walls and stairs still use tile resources.

## Formats and verification

Interactive fields, authored zones, editable linear features and imported source footprints require map format **9**, building-template format **6** where applicable, and world format **3**. Older model profiles retain their older formats when they do not use these fields. New editors still open older projects; old editors must reject newer formats instead of silently dropping their data.

Automated checks cover validation, export, history and browser authoring. The installed-game probe initializes the random provider and invokes native door collision/key APIs, creates a basement `IsoLightSource`, checks vehicle direction properties, and compiles original exported Lua with the game's LuaCompiler. It does not start a game client, load a save, execute external mod Lua or substitute for gameplay/multiplayer validation.
