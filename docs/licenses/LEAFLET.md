# Leaflet geographic picker

Vendored **Leaflet 1.9.4**, BSD 2-Clause, in
`src/SimplePZMapmaker.Web/wwwroot/vendor/leaflet/`.
The complete upstream copyright and licence are in `vendor/leaflet/LICENSE`.

## Provenance

- [Official download page](https://leafletjs.com/download.html)
- [Official 1.9.4 release](https://github.com/Leaflet/Leaflet/releases/tag/v1.9.4)
- [Upstream licence at v1.9.4](https://github.com/Leaflet/Leaflet/blob/v1.9.4/LICENSE)
- `leaflet.js`, `leaflet.js.map`, and `leaflet.css`: unchanged distributed files
  from the official npm package, retrieved at
  `https://unpkg.com/leaflet@1.9.4/dist/<filename>` during vendoring.
- `images/*.png`: unchanged distributed images from the official release
  `https://github.com/Leaflet/Leaflet/releases/download/v1.9.4/leaflet.zip`,
  under its `dist/images/` directory. Kept so the original CSS image paths work.
- `LICENSE`: unchanged from the upstream v1.9.4 tag.

The npm JavaScript matches the SRI value published on Leaflet's download page.
The GitHub release ZIP's JavaScript identifies itself as
`1.9.4+v1.d15112c`; this picker deliberately uses the npm distribution reporting
exactly `1.9.4`. No vendored files are patched.

SHA-256 checksums (hex):

```text
DB49D009C841F5CA34A888C96511AE936FD9F5533E90D8B2C4D57596F4E5641A  leaflet.js
A7837102824184820DFA198D1EBCD109FF6D0FF9A2672A074B9A1B4D147D04C6  leaflet.css
```

No CDN is contacted at runtime. `place-map.js` loads both local stylesheets and
the shared local classic script on the first `show()`. Leaflet may expose
`window.L`; consumers only need this module's exports. Concurrent pickers share
the loading attempt. Load failures/timeouts (12 seconds per asset) resolve as a
recoverable UI error and can be retried. Destroying during loading never mounts
a map later. The shared script and styles remain available for other pickers.

## Picker contract

```js
import {mountPlaceMap} from './place-map.js';
const picker = mountPlaceMap(container, {
  getBounds: () => currentBounds,
  onSelect: bounds => { currentBounds = bounds; }
});
await picker.show(); // Parent must make its dialog/container visible first.
picker.setBounds({west:10, south:20, east:11, north:21}, {fit:false});
picker.cancel(); // Abandon the pending gesture; keep the last selection.
picker.destroy(); // Remove this instance's DOM, map, observers and listeners.
```

- `show(bounds?)` resolves `true` when ready, `false` on a lazy-load failure or
  after destruction. Explicit bounds win; otherwise it reads optional `getBounds`.
  An absent/null result from `getBounds` leaves the selection unchanged.
- `setBounds(bounds, {fit=true}={})` returns `true` for a valid rectangle or
  `false` with a recoverable status. It works before initialization and never
  calls `onSelect`. It changes only this picker's selection.
- `onSelect` receives an independent object containing exactly
  `{west,south,east,north}`, with full input precision. Two clicked/tapped corners
  or **Use visible area** are the only actions that call it. Pan/zoom, fit,
  show, retry, and cancel never publish. Callback errors are disclosed safely.
- `checkedPlaceBounds(bounds)` returns a fresh validated rectangle or throws
  `RangeError`. Coordinates must be finite numbers; longitude is within
  `[-179,179]`, latitude within `[-84,84]`, west < east and south < north.
  There is no rounding, coercion, clipping, wrapping, or antimeridian support.
- `placeBoundsFromCorners({lat,lng}, {lat,lng})` orders the two corners, then
  applies the same validation. Invalid clicks preserve the previous rectangle.
- `mapLinks(bounds)` returns `{openStreetMap,googleMaps}`: OSM's bbox and Google's
  centre search. Links use `target="_blank"` and `rel="noopener noreferrer"`.
  Links remain usable without tiles; opening remote maps still needs a network.
- While a selection gesture is active, Escape cancels it and prevents the
  containing dialog's native dismissal. Once the gesture is cancelled, a second
  Escape follows the normal dialog behavior. The parent owns closing.
- No project writes, imports, search/geocoding, or application globals are used.

## Tiles and integration

OSM tiles use the exact URL
`https://tile.openstreetmap.org/{z}/{x}/{y}.png` and normal browser HTTP caching.
There is no fetch proxy, cache-busting, tile archive, prefetch, or offline download.
Each tile image uses `referrerPolicy='strict-origin-when-cross-origin'` before
its `src` is assigned, overriding the application's global `no-referrer` for
these images. Visible **© OpenStreetMap contributors** attribution links to
[OSM copyright](https://www.openstreetmap.org/copyright), even when Leaflet
itself cannot load. OSM data and tiles have separate terms from Leaflet:
[tile usage policy](https://operations.osmfoundation.org/policies/tiles/).

The parent owns workbench/dialog wiring and CSP. Permit the local module,
Leaflet script and styles, the tile image origin, and Leaflet's dynamic layout
styles as appropriate to the host policy. No host files are changed here.
Browser tests use an isolated HTTP fixture, not the application's CSP.

The map has bounded panning and no repeated worlds. A viewport that extends
outside the accepted coordinate limits cannot be selected: zoom/pan inward or
choose two valid corners. Tile failures show a separate persistent status so
selection instructions and external links remain usable. Retry redraws only
the current view and preserves the current selection. Tile service availability
is outside the picker; there is no guaranteed offline basemap.

## Verification

Run from the repository root:

```powershell
node --test tests/place-map.test.mjs
npm test
# Optional real Leaflet/DOM/touch tests, using existing BrowserSmoke Playwright:
$env:PZMAP_PLACE_MAP_BROWSER = '1'
node --test tests/place-map.test.mjs
Remove-Item Env:PZMAP_PLACE_MAP_BROWSER
```

Browser tests default to installed Edge. Override with `PZMAP_BROWSER_CHANNEL`,
`PZMAP_BROWSER_EXECUTABLE`, or `PZMAP_PLAYWRIGHT_MODULE` (an absolute module path)
as needed. They intercept all OSM tile traffic; they exercise recovery without
automated downloads from the public tile service.
