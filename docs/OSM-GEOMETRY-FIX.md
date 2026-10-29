# Imported bridges and straight building walls

The importer previously treated `bridge=yes` as a road-like terrain stroke at
z=0. Editable strokes could paint away water, raster-only fallbacks lacked native
deck geometry, and the first 64 ordinary roads could consume the path budget
before bridges were considered.

- Road and foot bridges now take priority among the bounded editable paths.
  Ground bridge decks connect flat approach roads at z=0, while the original
  terrain remains water underneath the authored deck. The native square contains
  a dry road floor and its water navigation bit is cleared. Removing a bridge
  reveals the retained water again. Edge fences and authored props remain above
  the floor, including their blocking flags.
- Existing manually elevated decks keep their height and underlying water/roads.
  OSM `layer` is relative source evidence and is **not** treated as a native floor
  number. Use the infrastructure editor for elevated crossings and stair access.
  Railway imports retain their existing cardinal-artwork restrictions. A bridge
  that exceeds the path budget is reported instead of being painted as a fake
  road through water.
- Source buildings are aligned around their area centroid to the nearest native
  axis using a dominant wall, then complete wall segments are constrained to
  consistent horizontal/vertical coordinates before tile rasterization. This
  removes staircase walls while retaining supported concavities and courtyard
  rings. It changes the source orientation and shape; original geographic rings
  and evidence remain separate. Contours that cannot form a valid orthogonal
  footprint are skipped and counted. The same policy applies to shared buildings
  at section boundaries. Existing manually edited buildings remain protected on
  preserved reimports.
- Manual quarter-turns mark imported footprints as manually edited. Resource
  variants and ownership are checked for source/target object groups. Missing
  wall-overlay variants fail atomically instead of silently removing artwork.
- The installed B42 catalog has no sprites for the standard reference fence's
  empty gate-space slots (14/15, 22/23, 30/31, 38/39, 46/47). Built-in styles no
  longer emit these missing tiles. Gates and posts remain; custom frame resources
  are still supported.

Regression coverage includes tilted L-shaped houses and courtyards, bridge
priority after 70 ordinary roads, dry native floors/navigation over retained
water, cell seams, shared-world decks, props/fences, safe spawn placement, and
unchanged elevated crossings. The full suites pass: **449 C# / 306 Node**.

Live check (isolated workspace, installed game assets):

```powershell
$env:PZMAP_OSM_REGIONAL = '1'
$env:PZMAP_OSM_ORTHOGONAL = '1'
$env:PZMAP_BROWSER_CHANNEL = 'chrome'
node scripts/verify-osm-search-import.mjs
```

Evidence and the regenerated Smolensk project are written to ignored
`work/osm-browser/orthogonal/`. Earlier imported projects keep their existing
geometry until they are reimported; opening an old file does not straighten it.

The live 6 km / 2048×2048 run saved and reopened a **67,186,532-byte** project.
The reported `osm-way-108753648` house moved from bounding-box origin `(48,788)`
to `(49,791)` around the retained footprint centre and has six exterior corners.
Its original seven-point geographic ring remains stored. A separate native-sprite
close-up shows the simplified house; no live game client was started.

The scene retains **64 bridge paths**; **81 additional bridge lines** are counted
as omitted at path/geometry limits. The 64-path / 100,000-managed-tile section
budgets remain in force. Native preview of a retained crossing confirms a road
floor replaces its rendered water floor while source terrain remains water.
