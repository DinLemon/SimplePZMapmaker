# Housing templates: architecture, references and limits

Updated 2026-10-13. The 24 plans are hand-authored for SimplePZMapmaker in `HousingTemplates.cs`, `HousingTemplates.Additional.cs` and `HousingTemplates.Regional.cs`. Room sizes, outlines, door links and stair cores are explicit. They do not call `BuildingDesigner.Generate` and are not copies of published architectural drawings. Real photographs/descriptions provide visual and typological references; each plan is an original adaptation to the PZ grid and available vanilla tiles.

## Original village plans

- `village-izba`:16×14, one above-ground level and cellar z=-1. Ground rooms are living room, kitchen, bedroom, vestibule, pantry and porch; the cellar contains passage/storage. Wooden facade, gabled roof and a side five-cell stair run from vestibule to cellar. Separate kitchen/bedroom adapt the traditional living area to gameplay.
- `village-veranda`:20×16, one level with seven room codes. The plastered main volume has kitchen, bedroom, living room, bathroom and hall. A wooden side veranda projects into a porch. Two footprint steps and a side entrance route distinguish it from the rectangular izba. Shed roof.

Primary cultural reference: [Ministry of Culture / Artefact: peasant life, the Russian izba and farmyard](https://ar.culture.ru/ru/article/krestyanskiy-byt-russkaya-izba-i-podvore). Its layout discussion explains a vestibule between porch and living space, storage, and a cellar below/beside the house; these inform the entrance buffer and cellar.

Primary museum reference: [Semenkovo: U. E. Pudova's house](https://www.semenkovo.ru/ru/monuments/dom-ue-pudovoy). It describes successive living/utility areas, side porches, a shed-roof side extension and the main gabled roof. The template veranda reinterprets that extension; it does not claim the museum building has this exact veranda. Neither plan reproduces the whole house.

## Original settlement plan

`settlement-two-storey`:30×24, two above-ground levels, four apartments per level/eight total. One central entrance/stair landing. Each apartment has a private hall, living room, bedroom, kitchen and bathroom;21 codes per floor. Gabled roof and vanilla brick palette.

Photo/type reference: [PhotoBuildings, series 1-204, project 1-204-112](https://www.photobuildings.com/projects/171/). The catalog describes two levels, brick walls and eight apartments. The template uses this scale/count, but its interior and central core are original; exact correspondence is not claimed.

## Original urban plans

- `urban-brick-five`:48×26, five levels and two independent entrances. Each has its own external opening, stairs between levels and four two-room apartments per landing. Each apartment has living room, bedroom, kitchen, bathroom and balcony. Eight apartments per floor/40 total; regular windows, vertical balconies, brick walls and flat roof.
- `urban-panel-five`:48×24, five levels/two independent entrances with four compact single-room apartments per landing. A private hall leads to living room/kitchen, with bathroom reached from kitchen. Living room has seating/bed. Apartment/core proportions differ from the brick plan. Eight apartments per floor/40 total, balconies, gray walls and flat roof.
- `urban-panel-nine`:32×30, nine levels, one entrance/staircase. Four apartments around the core, each with hall, living room, bedroom, kitchen, bathroom and balcony, plus landing storage.26 codes per floor/36 apartments total. Gray walls/flat roof; no functional lift or refuse chute.

Brick reference: [RussianRealty: series 1-447](https://www.russianrealty.ru/useful/serii_domov/43740/). Photographs/descriptions show a five-level sectional brick block with repeated windows/balconies and usually four apartments per landing. The template preserves sectional repetition with original rooms/game proportions. Its ground-level projections are open platforms, unlike the source's usual absence of first-level balconies.

Panel reference: [PhotoBuildings:1-464 A-1 /1-464 A-2 /1-464 A-13](https://photobuildings.com/projects/622/). The real five-level multi-section block is reduced to two sections with an original compact single-room layout; this is not an exact 1-464 A drawing.

Taller panel reference: [PhotoBuildings: series 1-464 D](https://photobuildings.com/projects/164/?lang=ru). Real heights/section variants inform repeated floors. The original four-apartment tower is not a copy of 1-464 D-89 or another specific variant.

## Files, formats and vanilla resources

`examples/HousingTemplates/` now contains 24 individual `*.pzbuilding.json` plans, `housing-templates.pzmap.json`, `catalog.json` and `HousingTemplates-B42.zip`. Import one template through the JSON template control or open the project to view all buildings. The initial six-plan package used `SimplePZBuilding` version 3/project 5; current readers also support later formats, including native model behavior in template 5/project 7.

The two original sectional blocks use 35 room codes per floor:32 apartment codes, two independent `hall` cores and one `balcony` code. Sharing the balcony code conserves the room alphabet; each balcony still has a separate physical outline and private door, with no link between apartments/entrances. Native lotheader stores multiple rectangles in one `emptyoutside` group.

Existing vanilla styles are `cottage`, `brick` and `modern`. Explicit wood-extension walls use `walls_exterior_house_01_48…59`; gray panel walls use `walls_exterior_house_02_64…75`. Floors, door/window pairs, furniture and roofs use vanilla palettes. Stairs use `carpentry_02_*`; balconies use vanilla railing. Flat roofs are horizontal slabs; pitched roofs use `roofs_30_*` families.

The initial six-scene check found 98 distinct IDs and zero absent entries in installed `newtiledefinitions.tiles.txt`. This establishes resource availability, not a character playthrough. The current 24-plan check is recorded below.

References persist in `Building.Attribution`, templates, projects and exported `ATTRIBUTION.txt`. Reference photos, museum images, game atlases and game files are not bundled. Original JSON plans use the project license; externally linked material retains its owners' rights.

## Initial six-plan validation and placement

`HousingTemplateTests.Cases()` checks distinct topologies, independent instances, metadata roundtrips, independent entrances without apartment-to-apartment passages, apartment contents, all-level stairs, stair openings, cellar signed levels, native room rectangles, 1,024 chunk offsets and tile/room indices. These tests need no game installation.

The sample fits one native cell of 256×256 tiles at origin **100,100**. Initial positions: izba(16,16), veranda(56,16), two-level house(96,16), brick block(16,72), panel block(80,72), tower(144,72); spawn(12,12). Buildings/railings do not overlap.

**Enable separately:** this origin overlaps other demonstration maps. Change the world origin and export again to combine them. The archive does not install itself or change saves. Native structure/resource checks are complete; character traversal remains a separate playtest.

## Six additional plans, 2026-10-13

`HousingTemplates.Additional.cs` adds original plans for a barn/canopy(24×18), garage/workshop(18×14), shop(24×20), two-level school(30×32), three-level brick block(36×28) and four-level L-shaped block(40×36). Utility/public rooms have appropriate game room types and real stair connections. The L-shaped house keeps an empty courtyard behind its wings.

Utility-building visual reference: [Semenkovo museum catalog](https://www.semenkovo.ru/ru/taxonomy/term/3). Brick housing/school context: [Culture.ru: twentieth-century architecture](https://www.culture.ru/materials/258275/arkhitektura-xx-veka-ot-moderna-do-formalizma). Corner volume: [PhotoBuildings: project 1-405-5](https://photobuildings.com/projects/2228/). The shop's functional layout is original and is not attributed to a residential reference.

At this 12-plan stage the first six positions remained, with additions at y=128/184 in the same cell. Verification found 99 vanilla IDs/zero missing; the independent reader accepted 1,024 chunks, 758 rooms and 12 building groups. Installed game classes accepted 580 upstairs rooms, two cellar rooms and 96 stair tiles. Character traversal/balance were not checked.

## Twelve regional additions in 0.7

All are original game adaptations. Catalog links provide type references, not claims of exact historical/building-series correspondence.

- Village: `village-log-long`(24×16, long vestibule/utility half), `village-brick-cottage`(22×20, two levels/four independent rooms per level), `village-farmstead`(28×24, utility wing/open terrace), `village-bathhouse`(14×12, wooden bathhouse).
- Settlement: `settlement-duplex`(32×20, two independent households), `settlement-row-four`(48×18, four independent two-level sections), `settlement-clinic`(28×28, two-level outpatient clinic), `settlement-courtyard`(36×32, two levels around an open courtyard).
- Urban: `urban-compact-six`(28×26, six levels), `urban-perimeter-four`(48×40, four levels/open courtyard/two stairs), `urban-shop-house`(30×28, retail ground floor/two smaller residential levels), `urban-townhouse-row`(42×20, three independent three-level blocks).

Row-house entrances connect only to their own stairs. Courtyard centers stay open on every level; kitchens/bathrooms are accessed through private living spaces. The shop-house has genuinely stepped upper outlines, not just renamed rooms. Pitched roofs use verified `roofs_30_*` and the occupied footprint, avoiding balconies/courtyard voids.

## Manual architectural tools

The building editor's facade/balcony/annex controls alter exterior wall resources or add volume to the selected level. Choose side, width along the wall and depth. Annexes receive a room/door; balconies receive outdoor floor, a door and railings. Resizing preserves rooms, furniture, resource choices and models on every level. North/west extension adjusts local coordinates and building origin to keep the existing map position. One Undo reverses it; exceeding 64 tiles or finding no suitable wall rejects the operation.

Facade choices include wooden/light siding, red/light brick, stone and gray masonry. Authored interior walls/door leaves remain. Roof shape and game palette use the existing appearance controls; pitched geometry respects terraces, annexes and courtyards instead of empty rectangular bounds.

Verification 2026-10-13:24 plans in one native cell of 256×256 tiles; independent reader 1,024 chunks/1,073 rooms; installed-game classes 781 upstairs rooms, two underground rooms and 180 stair tiles. All 180 used vanilla IDs exist in the installed game. This checks formats/resources; character traversal remains a separate task.
