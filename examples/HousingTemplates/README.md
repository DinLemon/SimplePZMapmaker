# Housing and public-building templates

24 original adaptations of village, small-town and urban architecture. Detailed [references and limits](../../docs/HOUSING-REFERENCES.md).

- `village-izba`:16×14, one level, vestibule, porch and cellar.
- `village-veranda`:20×16 house with side veranda.
- `settlement-two-storey`:30×24, two levels/eight apartments.
- `urban-brick-five`:48×26, five levels/two independent entrances.
- `urban-panel-five`:48×24, five levels/two entrances.
- `urban-panel-nine`:32×30, nine-level tower without a functional lift.
- `village-barn`:24×18, storage, workshop and open canopy.
- `village-workshop`:18×14 garage/workshop with storage and office.
- `settlement-shop`:24×20, sales floor, storage and staff rooms.
- `settlement-school`:30×32, two-level school/classrooms/central stairs.
- `urban-classic-three`:36×28, three-level brick house/two apartments per floor.
- `urban-corner-four`:40×36, four-level L-shaped block/courtyard.
- `village-log-long`:24×16 long northern-style house.
- `village-brick-cottage`:22×20 two-level brick cottage.
- `village-farmstead`:28×24 house with utility wing/terrace.
- `village-bathhouse`:14×12 wooden bathhouse.
- `settlement-duplex`:32×20, two independent households.
- `settlement-row-four`:48×18, four two-level sections.
- `settlement-clinic`:28×28, two-level outpatient clinic.
- `settlement-courtyard`:36×32, two levels/open courtyard.
- `urban-compact-six`:28×26, compact six-level block.
- `urban-perimeter-four`:48×40, four levels/courtyard/two stairs.
- `urban-shop-house`:30×28, shops under two smaller residential levels.
- `urban-townhouse-row`:42×20, three independent three-level blocks.

For one building, import its `.pzbuilding.json` through the template JSON control. `catalog.json` lists descriptions and references. Open `housing-templates.pzmap.json` to inspect all 24 plans; `HousingTemplates-B42.zip` exports them together. Rooms carry TileZed furniture (template format 8, map format 14); `simplepzmap housing-templates examples/HousingTemplates` regenerates every file from the built-in catalog.

The sample occupies one native cell of 256×256 tiles at origin **100,100**, spawn(4,4). Enable separately from other examples at these coordinates, or move origin and export again. It contains vanilla tile identifiers, not reference photographs or game artwork. Each plan is editable by rooms, edges, levels and resources.
