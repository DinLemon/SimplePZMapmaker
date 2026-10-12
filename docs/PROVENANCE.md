# Code and data provenance

SimplePZMapmaker is an unofficial Project Zomboid editor. It is not affiliated with The Indie Stone. Project Zomboid artwork, executables, Java classes and extracted game assets are not distributed with it.

The C# implementation is written for this project. Native export algorithms and field ordering were developed using the GPL-2.0-or-later mapping tools as the format reference; this project is distributed under GPL-2.0-or-later (LICENSE).

Reference: [Unjammer/PZ_Mapping_Tools](https://github.com/Unjammer/PZ_Mapping_Tools), continuing Tim Baker's TileZed/WorldEd. In particular:
- `WorldEd/src/editor/lotfilesmanager256.cpp`: LOT header/pack field ordering and chunk iteration.
- `WorldEd/src/editor/navigation/chunkdatafile256.cpp`: navigation flags and compressed chunk stream.
- `WorldEd/src/editor/InGameMap/ingamemapwriter.cpp`: worldmap XML with explicit `cellSize=256`.

Mapping tools are research dependencies in git-ignored `work/`; neither their binaries nor the game assets are included in the application package. No KnoxMap or Knoxify code is copied.

OpenStreetMap geographic data is ODbL 1.0, with attribution carried in saved projects and exported archives. Publishing a derived geographic database may trigger share-alike/access obligations; code GPL licensing and map-data licensing are distinct. GeoJSON imports must be supplied with rights to reuse the source data.

Public services are queried only when requested, without autocomplete or parallel traffic. Queries are bounded, locally cached for seven days and rate limited. Search uses Nominatim; OSM preview is an OpenStreetMap embed. The application sends no project files to cloud storage. Address/region requests necessarily disclose that address/region to the selected public cartographic service. For widespread deployment, use a hosted provider under an appropriate agreement.

Building objects share a hand-authored identifier/footprint catalog, checked against the installed game's Facing/SpriteGridPos metadata. The native runtime harness is original code; it requires the user's installed game/JRE and downloads a pinned Eclipse ECJ compiler into git-ignored work/ with SHA256 verification. No proprietary classes, decompiled sources or assets are distributed. It tests parsing and selected sprite properties, not a complete graphical playthrough.

Signed native level bounds were verified against the installed IsoLot/header loaders and by loading the16-basement test map with the game's own classes. The official Build42 development overview also describes the expanded vertical range: https://projectzomboid.com/blog/news/2022/02/42-techdoid/. The editor deliberately exposes only16 basement levels, within the installed loader's limits. This does not establish graphical or character-traversal correctness.

Texture-pack page/sprite field order was researched from `TileZed/src/tiled/texturepackfile.cpp` and its header in the same mapping-tools reference. The bounded C# version0/version1 reader and browser image composer are original implementations. Atlas PNGs remain local under git-ignored `work/assets`; only identifier references, original code and geography are distributed. Tile property text is read from the user's installation; it is not bundled.
