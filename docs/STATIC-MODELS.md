# Static models: resources, behavior and export

The catalog reads registered `module/model` and `item/WorldStaticModel` declarations from active game/mod `.txt` scripts. It does not run mod Lua. Common/version/legacy layers and dependency order come from the shared resource catalog. Geometry and textures are served locally from their original owner; declaration paths cannot escape its media tree or traverse reparse points.

## Preview and placement

The viewer supports ASCII DirectX `.X` coordinates, Frame matrices and UVs; OBJ vertices/UVs with positive or negative face indices; and static FBX through local Three.js 0.186.1. Missing files and explicitly animated models produce diagnostics. Limits are 32 MB geometry and 16 MB per texture. Preview does not reproduce game shaders, animation, collision meshes or lighting; special multi-material FBX surfaces can differ. The camera fits the model automatically. Placement scale is independent of the model-script scale during export.

The game/mod 3D-model control in the building editor opens a textured geometry preview. Selecting a model creates a brush; clicking floor places it on the selected level, including basements. Removing objects removes models in that cell. Footprint/size edits remove models left without floor; copying levels gives new IDs and the correct z. Model thumbnails in game view are rendered locally from real geometry.

## Behavior profiles

- **Pickup** keeps the earlier proxy item with `WorldStaticModel`: it can be picked up and does not block movement.
- **Scenery** is non-pickup and passable.
- **Obstacle** blocks movement.
- **Destructible** adds health to a blocking object.
- **Container** blocks movement and has health/capacity. Relevant fields appear when selected. It starts empty unless an available registered loot distribution is selected; population uses the native ItemPicker APIs.

Non-pickup profiles export original `spriteModel` declarations referencing the owning registered model script and use `IsoThumpable`. Collision occupies an authored connected mask of **up to 64 game tiles in an 8×8 grid**, independent of visible scale/rotation. It is a movement blocker, not the model's physical mesh or a structural wall: roofs, stairs and rooms still use tiles. Building validation rejects models blocking doors, stairs, furniture or the only passage. Geometry can extend outside the collision cell; the user chooses sufficient visual space.

A server `Events.LoadGridsquare` handler creates the object once, marks square `modData` and replicates it to clients. Picked-up items and destroyed obstacles are not recreated on the next square load. Verified APIs include `AddWorldInventoryItem`, `setWorldScale`, `setWorldZRotation`, `setSpriteModelName`, `setBlockAllTheSquare`, `setIsThumpable`, `setMaxHealth`, `setHealth`, `setIsContainer`, `ItemContainer.setCapacity` and `transmitCompleteItemToClients`. Native SpriteModel needs the normal Build 42 FBO chunk renderer; pickup models depend on the 3D ground-items setting. Native doors expose their North/West edge, initial open state and key lock. Native light sources expose authored RGB, radius and active state. Electrical wiring, water and arbitrary model Lua behavior are not simulated. See [Gameplay authoring](GAMEPLAY-AUTHORING.md) for controls and limits.

Images, geometry and third-party Lua are not copied into exports. Models stay in the original game/mod and `require` includes real dependencies. Changed owners, disabled requirements or changed files do not silently substitute resources.

## Verification and limits

Evidence: `StaticModelTests`, `tests/static-models.test.mjs`, `scripts/verify-static-models.mjs` and `scripts/verify-static-model-runtime.ps1`. The runtime probe checks installed `projectzomboid.jar` signatures, calls `IsoThumpable.TestCollide` from four directions, verifies passable scenery, and creates a real `ItemContainer` of the specified capacity. `SpriteModel.Load` parses the generated declaration and the game LuaCompiler checks the exported handler. These are implementation checks without starting a game session. The browser probe inspects a real bottle from `.X` and `.fbx` with the owning texture. Visual matching for all third-party models, destruction in play, saved-game reload and multiplayer still need a separate playtest.

API references: [InventoryItem](https://projectzomboid.com/modding/zombie/inventory/InventoryItem.html), [IsoGridSquare](https://projectzomboid.com/modding/zombie/iso/IsoGridSquare.html), [IsoThumpable](https://projectzomboid.com/modding/zombie/iso/objects/IsoThumpable.html), [SpriteModel](https://projectzomboid.com/modding/zombie/iso/SpriteModel.html). Local implementation references are the installed game's `media/lua/shared/TimedActions/ISDropWorldItemAction.lua`, `media/lua/server/BuildingObjects/ISSimpleFurniture.lua` and `ISWoodenContainer.lua`. Their text/resources are not redistributed.

Interactive masks, doors, lights and registered loot require map format 9/template 6. Earlier native profiles retain map format 7/template 5; older pickup-only models use map 6/template 4. Older editors reject the newer format rather than lose collision/properties.
