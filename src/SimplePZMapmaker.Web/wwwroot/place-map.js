/** Validate, never clamp, wrap, round, coerce, or mutate incoming coordinates. */
export function checkedPlaceBounds(bounds) {
  if (!bounds || typeof bounds !== 'object') throw new RangeError('Choose valid geographic bounds.');
  const {west, south, east, north} = bounds;
  if (![west, south, east, north].every(Number.isFinite))
    throw new RangeError('Bounds must contain four finite numbers.');
  if (west < -179 || east > 179 || west > 179 || east < -179)
    throw new RangeError('Longitude must be between -179 and 179; world wrapping is not supported.');
  if (south < -84 || north > 84 || south > 84 || north < -84)
    throw new RangeError('Latitude must be between -84 and 84.');
  if (west >= east || south >= north)
    throw new RangeError('Choose a nonempty rectangle without crossing the antimeridian.');
  return {west, south, east, north};
}

export function placeBoundsFromCorners(first, second) {
  if (![first?.lat, first?.lng, second?.lat, second?.lng].every(Number.isFinite))
    throw new RangeError('Choose two finite geographic corners.');
  return checkedPlaceBounds({
    west:Math.min(first.lng, second.lng), south:Math.min(first.lat, second.lat),
    east:Math.max(first.lng, second.lng), north:Math.max(first.lat, second.lat)
  });
}

/** OSM opens the whole rectangle; Google Maps opens its centre. */
export function mapLinks(bounds) {
  const {west, south, east, north} = checkedPlaceBounds(bounds);
  return {
    openStreetMap:`https://www.openstreetmap.org/?bbox=${encodeURIComponent([west, south, east, north].join(','))}`,
    googleMaps:`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${(south + north) / 2},${(west + east) / 2}`)}`
  };
}

const leafletLoads = new WeakMap();
const styleLoads = new WeakMap();
const asset = path => new URL(path, import.meta.url).href;
const loadTimeout = 12000;

function loadStyle(doc, path) {
  let styles = styleLoads.get(doc);
  if (!styles) styleLoads.set(doc, styles = new Map());
  const url = asset(path);
  if (styles.has(url)) return styles.get(url);
  const promise = new Promise((resolve, reject) => {
    const link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = url;
    const timer = setTimeout(() => finish(false), loadTimeout);
    function finish(ok) {
      clearTimeout(timer); link.onload = link.onerror = null;
      if (ok) resolve();
      else {link.remove(); styles.delete(url); reject(new Error('Local map stylesheet could not load.'));}
    }
    link.onload = () => finish(true); link.onerror = () => finish(false);
    doc.head.append(link);
  });
  styles.set(url, promise);
  return promise;
}

function loadLeaflet(doc) {
  if (leafletLoads.has(doc)) return leafletLoads.get(doc);
  const promise = (async () => {
    await Promise.all([loadStyle(doc, './vendor/leaflet/leaflet.css'), loadStyle(doc, './place-map.css')]);
    const view = doc.defaultView;
    if (view.L?.version === '1.9.4') return view.L;
    return new Promise((resolve, reject) => {
      const script = doc.createElement('script');
      script.src = asset('./vendor/leaflet/leaflet.js'); script.async = true;
      const timer = setTimeout(() => finish(false), loadTimeout);
      function finish(ok) {
        clearTimeout(timer); script.onload = script.onerror = null;
        if (ok && view.L?.version === '1.9.4') resolve(view.L);
        else {script.remove(); reject(new Error('Local Leaflet 1.9.4 could not load.'));}
      }
      script.onload = () => finish(true); script.onerror = () => finish(false);
      doc.head.append(script);
    });
  })();
  leafletLoads.set(doc, promise);
  // Evict failed attempts so the same button can recover after connectivity/CSP changes.
  promise.catch(() => {if (leafletLoads.get(doc) === promise) leafletLoads.delete(doc);});
  return promise;
}

/**
 * Isolated geographic picker. show() resolves true when ready, false on a recoverable
 * load failure or after destroy(). setBounds() returns false for invalid bounds.
 * Only explicit map selections invoke onSelect. Programmatic updates never do.
 * cancel() abandons the pending two-click gesture, keeping the committed rectangle.
 */
export function mountPlaceMap(container, {onSelect, getBounds} = {}) {
  const doc = container.ownerDocument;
  function element(tag, className, text) {
    const node = doc.createElement(tag);
    if (className) node.className = className;
    if (text) node.textContent = text;
    return node;
  }
  const root = element('section', 'place-map');
  root.hidden = true; root.setAttribute('aria-label', 'Geographic area picker');
  const tools = element('div', 'place-map-tools');
  function button(label, action) {
    const node = element('button', '', label);
    node.type = 'button'; node.addEventListener('click', action); tools.append(node);
    return node;
  }
  const select = button('Select rectangle', startSelection);
  select.setAttribute('aria-pressed', 'false');
  const cancelButton = button('Cancel selection', cancel);
  const visible = button('Use visible area', useVisibleArea);
  const fit = button('Fit selection', () => {if (map && selection) fitSelection();});
  // show() catches lazy failures; click handlers never leave a rejected promise behind.
  const retry = button('Retry map', () => {void show(selection ?? undefined);});
  retry.hidden = true;
  const hint = element('p', 'place-map-hint', 'Pan or zoom, then select two corners or use the visible area. Coordinates stay unchanged until you choose.');
  const viewport = element('div', 'place-map-viewport');
  viewport.tabIndex = 0; viewport.setAttribute('aria-label', 'Map: arrow keys pan, plus and minus zoom');
  const status = element('p', 'place-map-status', 'No area selected.');
  status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
  const tileStatus = element('p', 'place-map-tile-status');
  tileStatus.setAttribute('role', 'status'); tileStatus.hidden = true;
  const coordinates = element('output', 'place-map-coordinates');
  coordinates.setAttribute('aria-label', 'Selected geographic bounds');
  const links = element('nav', 'place-map-links'); links.setAttribute('aria-label', 'Selected area maps');
  const osm = element('a', '', 'OpenStreetMap'), google = element('a', '', 'Google Maps');
  for (const link of [osm, google]) {link.target = '_blank'; link.rel = 'noopener noreferrer'; links.append(link);}
  // Always visible, including when local Leaflet fails before its attribution control exists.
  const attribution = element('p', 'place-map-attribution');
  const copyright = element('a', '', '© OpenStreetMap contributors');
  copyright.href = 'https://www.openstreetmap.org/copyright'; copyright.target = '_blank'; copyright.rel = 'noopener noreferrer';
  attribution.append(copyright);
  root.append(tools, hint, viewport, attribution, coordinates, links, status, tileStatus);
  container.append(root);

  let L, map, tiles, rectangle, cornerMarker, preview, selection = null;
  let selecting = false, firstCorner = null, destroyed = false, shown = false;
  let pending = null, resizeObserver, dialog = null;

  function message(text, error = false) {
    status.textContent = text; status.classList.toggle('place-map-error', error);
  }
  function refresh() {
    select.disabled = visible.disabled = !map;
    cancelButton.disabled = !selecting;
    fit.disabled = !map || !selection;
    select.setAttribute('aria-pressed', String(selecting));
    viewport.classList.toggle('place-map-selecting', selecting);
    if (selection) {
      const urls = mapLinks(selection);
      osm.href = urls.openStreetMap; google.href = urls.googleMaps;
      coordinates.textContent = `W ${selection.west} · S ${selection.south} · E ${selection.east} · N ${selection.north}`;
    } else coordinates.textContent = 'No area selected';
    for (const link of [osm, google]) {
      if (!selection) link.removeAttribute('href');
      link.setAttribute('aria-disabled', String(!selection));
    }
  }
  function latLngBounds(bounds) {return [[bounds.south, bounds.west], [bounds.north, bounds.east]];}
  function drawSelection() {
    if (!map || !selection) return;
    if (rectangle) rectangle.setBounds(latLngBounds(selection));
    else rectangle = L.rectangle(latLngBounds(selection), {
      color:'#315c40', weight:3, fillColor:'#6b9863', fillOpacity:0.18, interactive:false
    }).addTo(map);
  }
  function fitSelection() {
    map.invalidateSize({pan:false});
    map.fitBounds(latLngBounds(selection), {padding:[24, 24], maxZoom:18, animate:false});
  }
  function clearGesture() {
    firstCorner = null; selecting = false;
    cornerMarker?.remove(); preview?.remove(); cornerMarker = preview = null;
    map?.doubleClickZoom.enable();
    refresh();
  }
  function cancel() {
    if (destroyed) return;
    clearGesture();
    message(selection ? 'Selection kept. Pan or choose another area.' : 'Selection cancelled. Choose an area.');
  }
  function startSelection() {
    if (!map || destroyed) return;
    clearGesture(); selecting = true; map.doubleClickZoom.disable(); refresh();
    message('Click or tap the first corner. Escape cancels selection.');
    viewport.focus({preventScroll:true});
  }
  function setBounds(bounds, {fit:shouldFit = true} = {}) {
    if (destroyed) return false;
    let next;
    try {next = checkedPlaceBounds(bounds);}
    catch (error) {message(`${error.message} Previous selection kept.`, true); return false;}
    selection = next; clearGesture(); drawSelection();
    if (shouldFit && map) fitSelection();
    message('Area selected. Pan freely or choose another area.');
    return true;
  }
  function publish(bounds) {
    if (!setBounds(bounds, {fit:false})) return;
    // Protect internal state even if the parent modifies the callback argument.
    try {
      Promise.resolve(onSelect?.({...selection})).catch(() => {
        if (!destroyed) message('Area selected, but the receiving form could not accept it. Choose again to retry.', true);
      });
    } catch {message('Area selected, but the receiving form could not accept it. Choose again to retry.', true);}
  }
  function useVisibleArea() {
    if (!map || destroyed) return;
    const bounds = map.getBounds();
    publish({west:bounds.getWest(), south:bounds.getSouth(), east:bounds.getEast(), north:bounds.getNorth()});
  }
  function mapClick({latlng}) {
    if (!selecting) return;
    try {
      if (!firstCorner) {
        if (!Number.isFinite(latlng.lat) || !Number.isFinite(latlng.lng) ||
          latlng.lat < -84 || latlng.lat > 84 || latlng.lng < -179 || latlng.lng > 179)
          throw new RangeError('Choose a corner within longitude ±179 and latitude ±84.');
        firstCorner = {lat:latlng.lat, lng:latlng.lng};
        cornerMarker = L.circleMarker(firstCorner, {radius:6, color:'#315c40', fillColor:'#f1ead5', fillOpacity:1, interactive:false}).addTo(map);
        message('Click or tap the opposite corner. Escape cancels selection.');
      } else publish(placeBoundsFromCorners(firstCorner, latlng));
    } catch (error) {message(`${error.message} Previous selection kept; choose another corner or press Escape.`, true);}
  }
  function mapMove({latlng}) {
    if (!selecting || !firstCorner) return;
    try {
      const bounds = placeBoundsFromCorners(firstCorner, latlng);
      if (preview) preview.setBounds(latLngBounds(bounds));
      else preview = L.rectangle(latLngBounds(bounds), {color:'#315c40', weight:2, dashArray:'6 5', fillOpacity:0.08, interactive:false}).addTo(map);
    } catch {preview?.remove(); preview = null;}
  }
  function keydown(event) {
    if (event.key !== 'Escape' || !selecting || !shown || destroyed || !root.getClientRects().length) return;
    const active = doc.activeElement;
    if (!root.contains(active) && !(dialog && active?.closest('dialog') === dialog)) return;
    event.preventDefault(); event.stopPropagation(); cancel();
  }
  function dialogCancel(event) {
    if (selecting && shown && !destroyed && root.getClientRects().length) {event.preventDefault(); cancel();}
  }
  function trackDialog() {
    const next = root.closest('dialog');
    if (next === dialog) return;
    dialog?.removeEventListener('cancel', dialogCancel);
    dialog = next; dialog?.addEventListener('cancel', dialogCancel);
  }
  async function initialize() {
    try {
      L = await loadLeaflet(doc);
      if (destroyed) return false;
      map = L.map(viewport, {
        center:[25, 0], zoom:2, minZoom:2, maxZoom:19,
        maxBounds:[[-84, -179], [84, 179]], maxBoundsViscosity:1,
        zoomAnimation:false, fadeAnimation:false, attributionControl:false
      });
      tiles = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom:19, noWrap:true, bounds:[[-84, -179], [84, 179]],
        referrerPolicy:'strict-origin-when-cross-origin', keepBuffer:0, updateWhenIdle:true
      });
      tiles.on('tileerror', () => {
        tileStatus.hidden = false;
        tileStatus.textContent = 'Map tiles unavailable. Selection and external links still work. Pan or zoom to try other tiles, or Retry map.';
        retry.hidden = false;
      });
      tiles.on('load', () => {
        if (!viewport.querySelector('.leaflet-tile.leaflet-tile-loaded')) return;
        // A partial load must not conceal other failed tiles in the same viewport.
        if (viewport.querySelector('.leaflet-tile:not(.leaflet-tile-loaded)')) return;
        tileStatus.hidden = true; retry.hidden = true;
      });
      tiles.addTo(map);
      map.on('click', mapClick); map.on('mousemove', mapMove);
      const Observer = doc.defaultView.ResizeObserver;
      if (Observer) {
        resizeObserver = new Observer(() => {if (map && !destroyed) map.invalidateSize({pan:false});});
        resizeObserver.observe(viewport);
      }
      drawSelection(); refresh();
      if (selection) fitSelection();
      else if (!status.classList.contains('place-map-error')) message('Pan or zoom, then choose an area.');
      return true;
    } catch {
      if (!destroyed) {
        resizeObserver?.disconnect(); map?.remove(); map = tiles = rectangle = null;
        refresh(); retry.hidden = false;
        message('Map could not load. Retry map; the previous selection and external links remain available.', true);
      }
      return false;
    }
  }
  async function show(bounds) {
    if (destroyed) return false;
    shown = true; root.hidden = false; trackDialog();
    try {
      if (bounds !== undefined) setBounds(bounds);
      else {
        const incoming = getBounds?.();
        if (incoming != null) setBounds(incoming);
      }
    } catch {message('Current bounds could not be read. Previous selection kept.', true);}
    if (map) {
      map.invalidateSize({pan:false});
      if (!tileStatus.hidden) {tileStatus.hidden = true; retry.hidden = true; tiles.redraw();}
      return true;
    }
    if (!pending) {
      retry.hidden = true;
      pending = initialize();
    }
    const result = await pending;
    pending = null;
    return result;
  }
  function destroy() {
    if (destroyed) return;
    destroyed = true; shown = false;
    doc.removeEventListener('keydown', keydown, true);
    dialog?.removeEventListener('cancel', dialogCancel);
    resizeObserver?.disconnect(); map?.remove();
    map = tiles = rectangle = cornerMarker = preview = null;
    root.remove();
  }
  doc.addEventListener('keydown', keydown, true);
  refresh();
  return {show, setBounds, cancel, destroy};
}
