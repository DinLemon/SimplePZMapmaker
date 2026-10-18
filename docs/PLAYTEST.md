# Playtest checklist

The automatic checks already cover a lot:

- `build --game` confirms that every tile exists in the game and checks for cell conflicts.
- `probe` loads the lots with the game's own classes.
- `server-check` has the dedicated server find buildings, rooms and spawn regions.

What they cannot see happens only in a client: drawn sprites, chunks streamed around a player, foraging and WorldGen. This list covers that part. Play one character per map and tick each line.

## Start a server for the map

```bash
simplepzmap build scene.json --out work/world.pzworld.json --export work/map.zip --strict --game "<Build 42 folder>"
```

```bash
simplepzmap serve work/map.zip --game "<Build 42 folder>"
```

`serve` installs the mod into `work/playtest-server/<archive name>/mods` and starts the dedicated server there. It never touches your `Zomboid` folder. The map loads next to Muldraugh, KY with the map's own spawn regions. The world is kept between runs; `--fresh` starts a new one, and `--port` changes the port from 16261.

The window prints:

- the address to join (`127.0.0.1:16261`);
- the admin login;
- the path of the installed mod. The client needs the same mod in its `Zomboid\mods` folder: copy it there yourself, or export with `--install`.

The server runs without Steam (`-nosteam`), so start the game with the launch option `-nosteam`. Type `quit` in the server window to save and stop.

## Arrival

- [ ] The map appears in the server's spawn selection with its `thumb.png`.
- [ ] A new character starts inside a house: living room, kitchen or bedroom, never outside or in a wall.
- [ ] A doctor, a police officer or a chef can start at a matching workplace when the map has one.

## Buildings

- [ ] Walls close at every corner. A square with plain north and west walls shows one corner piece, and outer corners have the SE pillar.
- [ ] Doors and windows of a `pz:wall` set or a `tilezed:` template use that set's own pieces, including the irregular ones such as the diner.
- [ ] Furniture stands against the walls and faces into the room: beds, dressers, kitchen counters, stove, fridge, sink, toilet, bath, sofa and TV. None of it blocks a doorway or the stairs.
- [ ] The stove, fridge and sink work, and containers hold loot that fits the room name (kitchen, bathroom, bakery, barn).
- [ ] Room names in the debug room view match the source tags (shop, amenity, `pz:room`).

## Outdoors

- [ ] Fences alternate their two pieces, have posts where asked, and every gate opens and closes. A wicket has its frame.
- [ ] Mailboxes stand at the curb and face the road, with a garbage bin beside them.
- [ ] Stop signs stand on the right side of the lesser road before the junction and face the drivers coming to it.
- [ ] Parking areas spawn cars inside the stalls (sandbox car spawn on). Each stall has its wheel stop at the end the cars face.
- [ ] Village yards have their props clear of the doors, and farms have a scarecrow.

## Nature and zones

- [ ] Foraging (debug: zone view or the foraging window) reports Forest/DeepForest in woods, TownZone near houses, FarmLand in open country and ForagingNav on roads.
- [ ] After walking away and back, WorldGen has not put jumbo trees on roads, in yards or against walls, and authored trees and bushes are still there.
- [ ] ZombiesType zones change the zombie outfits inside them.

## World seams

- [ ] Walk along a section seam. Buildings, fences and roads that cross it are continuous, with no half buildings and no double walls.
- [ ] The world map (M) shows roads, water, forest and building outlines aligned with the ground.

Report a failing line with the map coordinates (debug: player position) and a screenshot.
