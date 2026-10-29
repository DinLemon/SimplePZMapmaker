# Supplemental geographic sources

Use **Import → Real places & sites → Additional geographic sources**. This extends the existing OSM/source workflow; preparing a source does not change the open map.

1. Select the supplied source, its meaning (parcel/building/other), coordinate system, attribution, license/reference/date and observed/inferred status. Prepare a GeoJSON file, up to 16 MiB. EPSG:3857 is converted to WGS84; the input CRS is never guessed. Polygon holes and MultiPolygons survive. Full original attributes stay in the prepared GeoJSON, including nested cadastral properties. Keep that file.
2. A supplement alone does not contain OSM streets, water or vegetation. Select the saved baseline GeoJSON or Overpass JSON and **Merge prepared supplement with baseline**. Equal stable identities are replaced by the later source. Different identities are retained even when their geometry overlaps; transfer checks must resolve those conflicts. Each file and the combined browser source are bounded to 16 MB.
3. Save the prepared/combined source through the browser, or choose **Use as saved import source**, then explicitly import the selected area. For a world, save the combined GeoJSON and select it in **World from OpenStreetMap → Saved file**. Import replacement and manual-edit preservation follow the existing preview/apply rules. Source preparation itself never replaces a map.
4. Use **Tools → Generation Studio → Surveyed parcels · rural yards** to complete a whole local parcel. Cadastral `pz:parcel=yes` boundaries identify supplied parcels; ordinary OSM land-use areas still describe use, not ownership. Locked sites and manual edits remain protected. Parcels crossing the selected area/section are retained and explicitly reported; they are not silently clipped into separate ownership groups.

## NSPD sampling

Expand **Sample NSPD for this geographic map**, check the plan, then download the checked sample. The adapter uses only `https://nspd.gov.ru/api/aeggis/v3/{36048|36049}/wms`, GetFeatureInfo, WMS 1.3.0, EPSG:3857 and a fixed official Referer. TLS verification stays enabled; redirects, custom endpoints and certificate bypasses are unsupported.

The default step is **9 ground metres**, adjusted for Mercator latitude. An explicit CLI `--grid` requests a coarse sample instead. Planning rejects more than 2,048 layer-samples, more than 25 km² or invalid/oversized extents before downloading. Requests are serialized/rate-limited, with a 20-second request timeout, 20-minute operation budget, 2 MiB per response, 16 MiB total response budget and 512 MiB cache. The cache is under `<workspace>/work/cadastre-cache`, with seven-day freshness.

Valid point responses are cached independently. On cancellation, provider failure or timeout, repeat the same selection to resume those points. Returned cadastral identities deduplicate repeated hits, and already covered polygons skip later points separately for each layer. Progress counts actual planned layer-samples and cached/covered hits. **Completing the grid does not prove a complete parcel inventory**: a narrow parcel or small outbuilding can fall between samples.

A live one-point check on 2026-10-23 timed out after 20 seconds in this environment, with zero completed points. See `work/transfer-acceptance/nspd-live.json`. The fixtures exercise schema, caching, resume and errors; they are not evidence of current provider availability. The offline attributed GeoJSON path remains usable when NSPD is unavailable.

```powershell
dotnet run --project src/SimplePZMapmaker.Cli -- fetch-cadastre --bounds 54.7800,32.0400,54.7810,32.0410 --out survey.geojson --step-metres 9 --layers parcels,buildings --expected-region 67
```

## Provenance and corrections

Prepared features retain source/author/license/reference, source/retrieval dates, cadastral identifiers, region exceptions and per-field evidence. Concise essential tags have priority in the project's 64-tag/300-character evidence budget. Full nested metadata remains in the external source; bounded concise credits also reach exported `ATTRIBUTION.txt`. Inferred floor counts remain marked inferred after import.

Local testimony can explicitly set `pz:replaces` to a baseline's exact source identity. Only a `LocalSurvey` source may do this; coordinate proximity never grants replacement. Normalization retains the target identity, and composition performs the explicit replacement. Existing manual project edits still take priority during a preserving reimport.

`GET /api/geographic/sources` describes actual adapters: OSM baseline/PBF cache; NSPD sampling; attributed supplied Yandex vectors/local surveys/listing observations; supplied/digitized historical maps; owned image references. Mapillary search/download already exists in visual references when `PZMAP_MAPILLARY_TOKEN` is configured. Google/Yandex panoramas are external navigation links. No undocumented scraper, API token collection or unlicensed image redistribution is added.

POST endpoints: `/api/cadastre/plan`, `/fetch`, `/fetch/stream` (NDJSON), `/normalize`, `/combine`. They use the editor's existing same-origin JSON gate. No endpoint accepts arbitrary server paths or arbitrary remote URLs.

Canonical identities are carried in `pz:source_id`. Composing saved OSM data does not add another `geo-` prefix. Raw legacy GeoJSON `id` mapping is unchanged. A local `pz:replaces` value is the exact displayed source identity; replacement supplies the complete geometry and properties, not a sparse tag patch. Conflicting replacements of just one part of an unsplit MultiPolygon are rejected; supply separate Polygon features with explicit part identities, or replace its whole identity. CLI scene `sources` use the same canonical composition in listed order, with later complete features replacing equal identities.
