# Real-place transfer lessons: implementation and acceptance

User authorization: implement the reviewed TRANSFER-LESSONS.md recommendations, explicitly including its source survey and Build 42 pitfalls. The supplied document is evidence and requirements input, not permission to disable TLS, bypass provider access controls or execute its absent scripts. Preserve existing authored projects, the selected common world frame and the single preview/apply workflow.

## Architecture and order

1. Verify and fix Build 42 export hazards: distinguish editor zombie density from native intensity; bounded TBX objects; supported roofs/overlays; mod tile-definition collisions; house spawns and game/client acceptance diagnostics. Compare real installed game data where available. Record what a headless check cannot establish.
2. Extend existing source intake with cadastral/survey geometry, typed parcel tags, stable identities and provenance/confidence. Add a bounded, cached NSPD fetch adapter and CLI; convert EPSG:3857 accurately, retain holes and source attributes. Keep TLS verification and expose provider failures honestly. OSM remains the geography baseline. Yandex/Google/Avito observations, local testimony and historic maps remain separately attributed survey/reference inputs; uncertain sampled statistics are not global defaults. Expose source capabilities and exact limitations in the existing UI.
3. Extend SiteParcel and shared ParcelPlacement/FenceLines rather than adding another competing parcel layer. Preview deterministic street frontage, gates/wicket, front garden, access, yard, small ancillary buildings and rear garden within surveyed parcel boundaries. Preserve original/main buildings, manual edits, locked sites and traversable entrances. Generated children derive stable IDs from parcel source/role. Repeating a plan is idempotent and preview never mutates source. Retain or explicitly report cross-section parcels/objects rather than silently clip ownership.
4. Add regional road-surface rules with explicit surface tags taking priority. Original OSM source tags are retained. Defaults are selected profiles, never universal claims about missing source facts.
5. Add rural appearance/objects and a functional emergency-accommodation ensemble through existing catalogs/generation UI. Use real available sprites; original sprite additions require native pack/tiledef/resource ownership, a preview and licensing provenance. Existing facade overlays/trim and tile-mod authoring stay reusable. Distinguish authored assumptions from observations.
6. Add a consistent common-world deformation model for block rotation/field compression: all geometry uses the same forward/inverse mapping, preserves original WGS84 evidence and prevents folds. Independent per-section rotations are not acceptable. Keep the existing affine reference exactly compatible. Quantization and roof decomposition preserve L/T footprints and discard sub-two-tile details explicitly.
7. Integrate the parcel strategy into Generation Studio and existing source/reference tooling, with one staged preview/apply operation and stale-project protection. Surface readiness, retained source dates, limitations and validation errors clearly. No automatic replacement of the user's open project.
8. Validate pure rules and boundary failures, actual Web/CLI paths and a fresh isolated browser workflow. Use one real/synthetic complete block for street/parcel acceptance before larger worlds. Build local Release and document acceptance plus outstanding externally blocked provider/client checks honestly.

## Source coverage checklist

- OSM: existing PBF/JSON/cache/projection and source evidence.
- Cadastre: parcel/building WMS intake, bounds/cache/budgets, source IDs, original attributes, TLS and no fabricated coverage.
- Yandex cartographic/survey vectors: attributed external GeoJSON intake, no undocumented scraping dependency.
- Satellite/photos/panoramas: geographic reference bindings, actual image data and camera/date/provenance; official configured providers or supplied licensed images. References stay outside exported mods.
- Listings: attributed manual/supplied observations and a regional profile, without representing inferred interiors/material statistics as observed facts for each house.
- Local testimony: stable survey corrections with protected manual overrides.
- Historical maps: georeferenced supplied-image references, not unlicensed bundled output.

## Build 42 acceptance checklist

- Zombie native intensity scale and warnings tested against real lotheader samples; preserve editor raster/source values.
- TBX out-of-bounds furniture/overhangs rejected/reported before WorldEd; safe small/tower roofs and west-facing wall overlays addressed explicitly.
- Custom .pack/.tiles/manifest dependency and tiledef collision validation; game class probe and dry/interior spawn checks remain intact.
- Daylight/client playtest and thread-dump guidance; safe isolated cachedir, no real game-profile writes. Server startup is not proof of visual similarity or absence of client hangs.

Implementation may be split across bounded independent code slices. Do not mark a checklist complete from a plan or mock alone. Preserve the large pre-existing working-tree changes and do not commit/push without a current user request.
