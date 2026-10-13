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

Smolensk 0.4.1 offers a separate read-only regional source from [OSM France](https://download.openstreetmap.fr/extracts/russia/central_federal_district/smolensk_oblast-latest.osm.pbf). This downloads an oblast PBF of about 44 MB and extracts the user's selected bounds locally, without using the OSM editing API or submitting changes. It is distinct from public Overpass queries and does not establish that Overpass 504 errors have ceased. The full file is capped at 64 MiB and cached for seven days under git-ignored `work/geodata-cache/` inside the requested G: workspace. The captured header timestamp is 2026-10-11T00:29:05Z; `latest` is a moving provider snapshot, so future regeneration need not yield identical data. The verification area is a 1.5 km section of central Smolensk, not the entire city or oblast. Incomplete or unsupported nested multipolygon relations are omitted with a warning, not presented as accurate closed areas. OSM attribution and the source date/cache warning are retained in the imported project's report; ODbL applies to the source geography and derived geographic database independently of the code license.

The [GitHub repository](https://github.com/DinLemon/SimplePZMapmaker) is public. Game artwork, local source downloads, temporary PBF files and extracted atlas caches remain excluded from source/package publication.

The Smolensk release example under `examples/Smolensk/` distributes the selected area's normalized OSM JSON together with its derived editable map, native archive, import recipe/source date and attribution. Its centre is 54.7789701° N/32.0471812° E, width 1.5 km and grid 1024×1024; the full oblast PBF remains a local ignored download. The example's native world origin 100,100 overlaps other included examples, so they must be enabled separately or moved and re-exported.

Building objects share a hand-authored identifier/footprint catalog, checked against the installed game's Facing/SpriteGridPos metadata. The native runtime harness is original code; it requires the user's installed game/JRE and downloads a pinned Eclipse ECJ compiler into git-ignored work/ with SHA256 verification. No proprietary classes, decompiled sources or assets are distributed. It tests parsing and selected sprite properties, not a complete graphical playthrough.

Signed native level bounds were verified against the installed IsoLot/header loaders and by loading the16-basement test map with the game's own classes. The official Build42 development overview also describes the expanded vertical range: https://projectzomboid.com/blog/news/2022/02/42-techdoid/. The editor deliberately exposes only16 basement levels, within the installed loader's limits. This does not establish graphical or character-traversal correctness.

Texture-pack page/sprite field order was researched from `TileZed/src/tiled/texturepackfile.cpp` and its header in the same mapping-tools reference. The bounded C# version0/version1 reader and browser image composer are original implementations. Atlas PNGs remain local under git-ignored `work/assets`; only identifier references, original code and geography are distributed. Tile property text is read from the user's installation; it is not bundled.

Original housing templates and their architectural references are documented in HOUSING-REFERENCES.md. Referenced photos/plans are studied as visual/type examples and are not bundled. The shipped plans are original adaptations using vanilla identifier references; lifts and exact standard-series measurements are not reproduced.
