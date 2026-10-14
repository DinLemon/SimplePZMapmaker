# Check native lighting inside the editor

![Native colored light calculated by the installed game library](screenshots/09-native-lighting.png)

## Requirements

Use the Windows editor with an installed Build 42 copy of Project Zomboid. Open **Game resources**, select the game installation and enable any required resource mods. The check reads the game's bundled Java runtime, `projectzomboid.jar` and `Lighting64.dll`. These game files remain in your installation; the editor package contains its own lighting adapter.

The lighting calculation runs without opening a game session or touching game saves. Its working files stay in the editor workspace. If the installed native interface is incompatible or the adapter is missing, the editor reports that the check is unavailable. It does not substitute an approximate light field.

## Run a check

1. Open a map, or open a stitched world and choose **Edit section**.
2. Choose **Game view** and one signed floor. Pan and zoom to the area you want to inspect.
3. Choose **Check lighting** above the main canvas. The inspector opens **Native lighting check** and calculates the visible area.
4. Set **Area** to 16, 32 or 64 tiles. **Tile X** and **Tile Y** identify its top-left map tile. **Visible area** recenters these coordinates on the current camera; **Calculate** runs the current inputs.
5. Choose **Sky color**, **Ambient** and whether **Room lights powered** applies. These are controlled scenario inputs. To check an authored light in darkness, use black sky color, ambient 0 and disable room lights.
6. Select **Light field** to inspect the native RGB values, or **Tint game view** to multiply those values over the resource view. **Show result** hides or displays the overlay. Hover over an output tile to read its RGB and dark value.
7. Select another floor or edit the map, then calculate again. Old results are discarded when their scene or floor changes. **Cancel** interrupts a running check; the operation creates no project edit or undo entry.

## Author light sources and doors

Select a registered model in **Edit in Game view** to open **Object gameplay**. Choose **Native light**, or enable **Emit native light** on a compatible scenery object. Set red, green and blue strengths, a radius from 1 through 20 tiles and **Light active**, then apply the object properties. Active light assumes power is available for this scenario.

Native door objects have a north or west tile edge, open/closed state, lock state and key ID. Their authored open/closed state participates in the light calculation. Tile walls, windows, floors, roofs and stair openings come from the exported scene geometry. Stitched-world checks include nearby section geometry and shared buildings within the calculation halo.

## Read the result accurately

The installed `LightingJNI` library computes the native light field. The editor visualizes the returned cell values. **Engine and assumptions** records the native frame, library hash and scene-input hash, plus any unsupported scene features. The response also records the game JAR and adapter hashes.

The browser does not reproduce the game's FBO renderer, material shaders or model-mesh shadows. Sky color, ambient strength and room power are explicit inputs; game time, weather, erosion, live switches and character visibility are not simulated. This check is useful for authored light propagation and geometry, with those assumptions visible. It does not claim a character playthrough or exact rendered-game screenshot.
