# OSM search and small-area import recovery — 2026-10-21

## Faults and changes

- Nominatim correctly returned a JSON array, but the shared download metadata reader called `TryGetProperty` on that array. This produced `InvalidOperationException`, which the HTTP middleware translated to a misleading 400 about a bad project/geodata file. The metadata reader now checks JSON kinds; search responses must be arrays. Both empty and populated results work on download and after application restart/cache reuse. Optional null or malformed timestamp metadata does not break a valid map response.
- During the reported failure all configured Overpass hosts timed out from this machine. A single small-area request can now try the **read-only** [OSM map API](https://wiki.openstreetmap.org/wiki/API_v0.6#Retrieving_map_data_by_bounding_box:_GET_/api/0.6/map) after the primary Overpass host fails. Remaining Overpass hosts are still tried when that backup refuses the request or exceeds the response budget.
- The backup is restricted to the existing single-area threshold (at most 0.01° latitude and 0.02° longitude). Tiled/world imports continue to use Overpass or regional extracts; the main OSM API is not used for bulk tiled downloads. The independent small-area cache namespace prevents backup results from replacing a world source.
- The map endpoint returns node references. The adapter resolves full ways and relation members, preserves original OSM IDs and tagged point features, and skips incomplete contours with persistent source warnings. Rejected relations do not suppress complete independently tagged ways. Response sizes, cancellation, cache promotion and source metadata remain bounded/atomic.
- The editor's current map remains unchanged when search/download fails or is cancelled. Real source availability still varies; large areas should use a regional extract or saved GeoJSON when Overpass is unavailable.

## Dense single-section imports

- The 6 km Smolensk extract with a 2048×2048 output reproduced `Invalid project objects.`. The importer could add more than the project's 4,000-building limit and only rejected the completed project. After bounding buildings, the same scene exposed a second unchecked limit: automatic street/yard dressing could exceed 10,000 decorations.
- Import now respects both existing section capacities. Authored buildings and decorations retained on reimport consume capacity first. Additional otherwise placeable buildings are counted under `Section building limit (4000)` and have their source evidence retained. The bounded skipped-outline preview still contains at most 256 records; aggregate counts and source evidence are separate.
- The response explicitly recommends smaller world sections when buildings are omitted. The persisted street-dressing note reports omissions at the decoration limit. These limits are per section, independently of the geographic width and output tile dimensions; this fix does not promise every source building fits in one section.
- Direct validation now names the invalid building, spawn, attribution, surface or decoration collection instead of combining these failures into two generic messages.
- A successful import also exposed the old 48 MB file limit: this scene saves as **86,729,784 bytes**. Project file opening, world-map file import, recovery, agent sessions, example loading and section storage/index validation now use a bounded **128,000,000-byte** limit. The web host retains its 128 MiB request limit, leaving room for JSON envelopes. Checked-design proposal budgets remain separate.
- Five C# regressions cover dense buildings with/without a retained authored building, dense parking decoration with/without 9,999 authored props, and recovery of geographic evidence above 48 MB. Browser file-reader tests also cover oversize rejection before reading, the inclusive limit and cancellation; section-index tests cover large saved sections. All **439 C#** and **299 Node** tests pass; Release builds without warnings/errors.
- The live Smolensk 6 km / 2048×2048 workflow applies **12,645 features**, places **4,000 of 11,493 source buildings**, retains **16,158 evidence records**, and reports **4,736 capacity omissions** separately from unsuitable geometry/placement. Dressing reaches **10,000 props** and reports **111 further capacity omissions**. The real editor validates, downloads and reopens the 86.7 MB map; a failed reimport leaves it intact. The file and screenshot are under ignored `work/osm-browser/large/`.

## Verification

- The original live `/api/search` returned HTTP 400 for both `Смоленск` and `Smolensk`; the same searches now return selectable results in the actual in-app browser.
- Twelve new C# regressions were observed failing before their corresponding fixes. The full suite is **434/434**; Node remains **296/296**. Release builds with zero errors/warnings.
- Live UI: search for Smolensk, then import a 400 m Louisville area with the default preserve-edits setting and 512-tile output. **389 features / 17 placed buildings** were applied to the editor. A deliberately failed reimport retained that map and displayed the upstream error.
- A separate live UI run selected Smolensk and its regional extract: the 43,872,990-byte PBF downloaded, and the selected local area imported **609 features / 120 placed buildings**. Failed-reimport preservation also passed on this result.
- Source HTTP, cache, incomplete ways/relations, independent ways, optional-source refusal, oversized-body rejection before buffering, tiled-source isolation and optional timestamp metadata are covered by deterministic tests.
- Independent review identified three fallback edge cases; all were fixed and reviewed again without remaining blockers.

Live checks are intentionally outside network-independent CI:

```powershell
npm ci --prefix tools/BrowserSmoke
$env:PZMAP_TEST_URL = 'http://127.0.0.1:48777'
$env:PZMAP_BROWSER_CHANNEL = 'chrome'
node scripts/verify-osm-search-import.mjs
# Optional: verify the Smolensk regional-extract workflow instead.
$env:PZMAP_OSM_REGIONAL = '1'
node scripts/verify-osm-search-import.mjs
# Optional: reproduce the dense 6 km, 2048×2048 case, including save/reopen.
$env:PZMAP_OSM_LARGE = '1'
node scripts/verify-osm-search-import.mjs
```

Start the application with an isolated `PZMAP_WORKSPACE`. Evidence is written under ignored `work/osm-browser/`; the user's Zomboid directory is not used. The actual editor on port 48777 was restarted with the fixed Release assembly.
