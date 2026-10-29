import test from 'node:test';
import assert from 'node:assert/strict';
import {checkedPlaceBounds, placeBoundsFromCorners, mapLinks} from '../src/SimplePZMapmaker.Web/wwwroot/place-map.js';

test('place bounds preserve exact finite coordinates and return only an independent geographic rectangle', () => {
  const input = Object.freeze({west:45.123456789123, south:55, east:45.125, north:55.002, extra:'ignored'});
  const result = checkedPlaceBounds(input);
  assert.deepEqual(result, {west:45.123456789123, south:55, east:45.125, north:55.002});
  assert.notEqual(result, input);
  result.west = 46;
  assert.equal(input.west, 45.123456789123);
  assert.deepEqual(checkedPlaceBounds({west:-179, south:-84, east:179, north:84}),
    {west:-179, south:-84, east:179, north:84});
});

test('place bounds reject empty reversed antimeridian and wrapped rectangles without clamping', () => {
  const good = {west:10, south:20, east:11, north:21};
  for (const bad of [null, undefined, [], {},
    {...good, west:11}, {...good, west:12}, {...good, south:21}, {...good, south:22},
    {...good, west:170, east:-170}, {...good, west:370, east:371},
    {...good, west:-179.000001}, {...good, east:179.000001},
    {...good, south:-84.000001}, {...good, north:84.000001}]) {
    assert.throws(() => checkedPlaceBounds(bad), /bounds|longitude|latitude|rectangle/i);
  }
});

test('place bounds reject nonnumbers and nonfinite values in every coordinate', () => {
  for (const field of ['west', 'south', 'east', 'north']) {
    for (const value of [NaN, Infinity, -Infinity, '10', null, undefined, true]) {
      assert.throws(() => checkedPlaceBounds({west:10, south:20, east:11, north:21, [field]:value}));
    }
  }
});

test('corner selection accepts either click direction and rejects degenerate or worldwrapped clicks', () => {
  assert.deepEqual(placeBoundsFromCorners({lat:21, lng:11}, {lat:20, lng:10}),
    {west:10, south:20, east:11, north:21});
  assert.deepEqual(placeBoundsFromCorners({lat:20, lng:11}, {lat:21, lng:10}),
    {west:10, south:20, east:11, north:21});
  for (const point of [{lat:20, lng:10}, {lat:20, lng:11}, {lat:85, lng:11},
    {lat:21, lng:370}, {lat:NaN, lng:11}, {lat:21, lng:null}, null]) {
    assert.throws(() => placeBoundsFromCorners({lat:20, lng:10}, point));
  }
});

test('map links address the selected rectangle in OSM and its centre in Google Maps', () => {
  const links = mapLinks({west:10, south:20, east:12, north:22});
  const osm = new URL(links.openStreetMap), google = new URL(links.googleMaps);
  assert.equal(osm.origin + osm.pathname, 'https://www.openstreetmap.org/');
  assert.equal(osm.searchParams.get('bbox'), '10,20,12,22');
  assert.equal(google.origin + google.pathname, 'https://www.google.com/maps/search/');
  assert.equal(google.searchParams.get('api'), '1');
  assert.equal(google.searchParams.get('query'), '21,11');
  assert.throws(() => mapLinks({west:170, south:20, east:-170, north:22}));
});

// Opt in to real Leaflet/DOM lifecycle tests; no OSM traffic is sent by these tests.
// PowerShell: $env:PZMAP_PLACE_MAP_BROWSER='1'; node --test tests/place-map.test.mjs
test('standalone picker browser contract', {skip:process.env.PZMAP_PLACE_MAP_BROWSER !== '1'}, async t => {
  const {createServer} = await import('node:http');
  const {readFile} = await import('node:fs/promises');
  const {pathToFileURL} = await import('node:url');
  const {chromium} = await import(process.env.PZMAP_PLAYWRIGHT_MODULE
    ? pathToFileURL(process.env.PZMAP_PLAYWRIGHT_MODULE).href
    : '../tools/BrowserSmoke/node_modules/playwright/index.mjs');
  const root = new URL('../src/SimplePZMapmaker.Web/wwwroot/', import.meta.url);
  const html = '<!doctype html><html lang="en"><title>Place picker</title><body style="margin:0"><dialog style="width:800px;max-width:94vw"><div id="picker"></div></dialog></body></html>';
  const server = createServer(async (request, response) => {
    response.setHeader('Referrer-Policy', 'no-referrer');
    try {
      if (request.url === '/') {response.setHeader('Content-Type', 'text/html'); response.end(html); return;}
      const url = new URL('.' + request.url, root);
      if (!url.href.startsWith(root.href)) {response.writeHead(403).end(); return;}
      response.setHeader('Content-Type', url.pathname.endsWith('.js') ? 'text/javascript' :
        url.pathname.endsWith('.css') ? 'text/css' : 'image/png');
      response.end(await readFile(url));
    } catch {response.writeHead(404).end();}
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({headless:true, channel:process.env.PZMAP_BROWSER_CHANNEL || 'msedge',
      executablePath:process.env.PZMAP_BROWSER_EXECUTABLE});
    const base = `http://127.0.0.1:${server.address().port}`;
    const bounds = {west:10, south:20, east:10.02, north:20.02};
    async function setup({touch = false, failScript = false} = {}) {
      const page = await browser.newPage({viewport:{width:1000, height:850}, hasTouch:touch});
      const errors = [], requests = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.route('https://tile.openstreetmap.org/**', route => route.abort());
      page.on('request', request => requests.push(request.url()));
      if (failScript) await page.route('**/vendor/leaflet/leaflet.js', route => route.abort());
      await page.goto(base);
      await page.evaluate(async initial => {
        const module = await import('/place-map.js');
        window.selected = []; window.currentBounds = initial;
        window.picker = module.mountPlaceMap(document.querySelector('#picker'), {
          onSelect:value => window.selected.push(value), getBounds:() => window.currentBounds
        });
        document.querySelector('dialog').showModal();
      }, bounds);
      return {page, errors, requests};
    }
    await t.test('lazy loading, explicit selection, Escape, offline links and remount', async () => {
      const {page, errors, requests} = await setup();
      try {
        assert.equal(requests.some(url => url.includes('/vendor/leaflet/')), false);
        assert.equal(await page.evaluate(() => picker.show()), true);
        await page.locator('.place-map-tile-status').filter({hasText:/unavailable/i}).waitFor();
        assert.equal(await page.evaluate(() => selected.length), 0);
        assert.equal(await page.locator('.place-map-viewport img.leaflet-tile').first().getAttribute('referrerpolicy'), 'strict-origin-when-cross-origin');
        assert.equal(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('rel'), 'noopener noreferrer');
        assert.match(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href'), /bbox=10%2C20%2C10.02%2C20.02/);
        await page.locator('.place-map-viewport').press('ArrowRight');
        assert.equal(await page.evaluate(() => selected.length), 0);
        const oldLink = await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href');
        assert.equal(await page.evaluate(() => picker.setBounds({west:370, south:20, east:371, north:21})), false);
        assert.equal(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href'), oldLink);
        await page.getByRole('button', {name:'Select rectangle', exact:true}).click();
        await page.locator('.place-map-viewport').click({position:{x:220, y:160}});
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('dialog').evaluate(el => el.open), true);
        assert.equal(await page.evaluate(() => selected.length), 0);
        assert.equal(await page.getByRole('button', {name:'Cancel selection'}).isEnabled(), false);
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
        await page.evaluate(() => document.querySelector('dialog').showModal());
        await page.getByRole('button', {name:'Select rectangle', exact:true}).click();
        await page.locator('.place-map-viewport').click({position:{x:220, y:160}});
        await page.locator('.place-map-viewport').click({position:{x:440, y:260}});
        const selection = await page.evaluate(() => selected[0]);
        assert.deepEqual(checkedPlaceBounds(selection), selection);
        assert.equal(await page.evaluate(() => selected.length), 1);
        await page.getByRole('button', {name:'Fit selection', exact:true}).click();
        assert.equal(await page.evaluate(() => selected.length), 1);
        await page.getByRole('button', {name:'Use visible area', exact:true}).click();
        assert.equal(await page.evaluate(() => selected.length), 2);
        const oldGoogle = await page.getByRole('link', {name:'Google Maps', exact:true}).getAttribute('href');
        await page.evaluate(() => {currentBounds = {west:12, south:24, east:12.01, north:24.01};});
        await page.evaluate(() => picker.show());
        assert.notEqual(await page.getByRole('link', {name:'Google Maps', exact:true}).getAttribute('href'), oldGoogle);
        assert.equal(await page.evaluate(() => selected.length), 2);
        await page.evaluate(() => {picker.destroy(); picker.destroy();});
        assert.equal(await page.locator('.place-map').count(), 0);
        assert.equal(await page.evaluate(() => picker.show()), false);
        await page.evaluate(async () => {
          const {mountPlaceMap} = await import('/place-map.js');
          window.picker = mountPlaceMap(document.querySelector('#picker'));
          await picker.show({west:1, south:2, east:3, north:4});
        });
        assert.equal(await page.locator('.leaflet-container').count(), 1);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('touch taps choose two corners', async () => {
      const {page, errors} = await setup({touch:true});
      try {
        await page.evaluate(() => picker.show());
        await page.getByRole('button', {name:'Select rectangle', exact:true}).tap();
        await page.locator('.place-map-viewport').tap({position:{x:180, y:130}});
        await page.locator('.place-map-viewport').tap({position:{x:480, y:280}});
        assert.equal(await page.evaluate(() => selected.length), 1);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('failed lazy script is recoverable and preserves offline selection links', async () => {
      const {page, errors} = await setup({failScript:true});
      try {
        assert.equal(await page.evaluate(() => picker.show()), false);
        assert.match(await page.locator('.place-map-status').innerText(), /load|unavailable/i);
        const offlineLink = new URL(await page.getByRole('link', {name:'Google Maps', exact:true}).getAttribute('href'));
        const [latitude, longitude] = offlineLink.searchParams.get('query').split(',').map(Number);
        assert.ok(Math.abs(latitude - 20.01) < 1e-12);
        assert.equal(longitude, 10.01);
        assert.equal(await page.getByRole('button', {name:'Use visible area', exact:true}).isEnabled(), false);
        await page.unroute('**/vendor/leaflet/leaflet.js');
        await page.getByRole('button', {name:'Retry map', exact:true}).click();
        await page.locator('.leaflet-container').waitFor();
        await page.getByRole('button', {name:'Use visible area', exact:true}).waitFor({state:'visible'});
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('retrying tiles keeps the user selection even if getBounds still contains the initial area', async () => {
      const {page, errors} = await setup();
      try {
        await page.evaluate(() => picker.show());
        await page.locator('.place-map-tile-status').filter({hasText:/unavailable/i}).waitFor();
        await page.getByRole('button', {name:'Select rectangle', exact:true}).click();
        await page.locator('.place-map-viewport').click({position:{x:200, y:150}});
        await page.locator('.place-map-viewport').click({position:{x:470, y:270}});
        const link = await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href');
        await page.getByRole('button', {name:'Retry map', exact:true}).click();
        assert.equal(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href'), link);
        assert.equal(await page.evaluate(() => selected.length), 1);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('invalid initial and later bounds remain disclosed after successful lazy loading', async () => {
      const {page, errors} = await setup();
      try {
        await page.evaluate(() => {currentBounds = {west:170, south:20, east:-170, north:21};});
        assert.equal(await page.evaluate(() => picker.show()), true);
        assert.match(await page.locator('.place-map-status').innerText(), /antimeridian/i);
        assert.equal(await page.getByText('OpenStreetMap', {exact:true}).getAttribute('href'), null);
        await page.evaluate(() => picker.setBounds({west:10, south:20, east:11, north:21}));
        await page.evaluate(() => picker.show(null));
        assert.match(await page.locator('.place-map-status').innerText(), /valid geographic bounds/i);
        assert.match(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href'), /bbox=10%2C20%2C11%2C21/);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('concurrent lazy shows share assets and destruction during load never resurrects the map', async () => {
      const {page, errors, requests} = await setup();
      try {
        let release;
        const gate = new Promise(resolve => {release = resolve;});
        let requested;
        const ready = new Promise(resolve => {requested = resolve;});
        await page.route('**/vendor/leaflet/leaflet.js', async route => {requested(); await gate; await route.continue();});
        await page.evaluate(() => {window.loading = Promise.all([picker.show(), picker.show()]);});
        await ready;
        await page.evaluate(() => picker.destroy());
        release();
        assert.deepEqual(await page.evaluate(() => loading), [false, false]);
        assert.equal(await page.locator('.place-map').count(), 0);
        assert.equal(requests.filter(url => url.endsWith('/vendor/leaflet/leaflet.js')).length, 1);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('async show preserves exact newer bounds without republishing or mutating caller objects', async () => {
      const {page, errors} = await setup();
      try {
        let release, requested;
        const gate = new Promise(resolve => {release = resolve;});
        const ready = new Promise(resolve => {requested = resolve;});
        await page.route('**/vendor/leaflet/leaflet.js', async route => {requested(); await gate; await route.continue();});
        await page.evaluate(() => {window.loading = picker.show();});
        await ready;
        const precise = {west:45.123456789123, south:55.123456789123, east:45.125, north:55.125};
        await page.evaluate(value => {
          window.original = Object.freeze(value);
          picker.setBounds(original, {fit:false});
          currentBounds = {west:100, south:10, east:101, north:11};
        }, precise);
        release();
        assert.equal(await page.evaluate(() => loading), true);
        assert.deepEqual(await page.evaluate(() => original), precise);
        const link = new URL(await page.getByRole('link', {name:'OpenStreetMap', exact:true}).getAttribute('href'));
        assert.equal(link.searchParams.get('bbox'), '45.123456789123,55.123456789123,45.125,55.125');
        assert.equal(await page.evaluate(() => selected.length), 0);
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
    await t.test('tile images send an origin referrer despite the document policy and recover after failure', async () => {
      const {page, errors} = await setup();
      try {
        await page.evaluate(() => picker.show());
        await page.locator('.place-map-tile-status').filter({hasText:/unavailable/i}).waitFor();
        let headers;
        await page.unroute('https://tile.openstreetmap.org/**');
        await page.route('https://tile.openstreetmap.org/**', async route => {
          headers = await route.request().allHeaders();
          await route.fulfill({contentType:'image/png', body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0BsAAAAASUVORK5CYII=', 'base64')});
        });
        await page.getByRole('button', {name:'Retry map', exact:true}).click();
        await page.locator('.place-map-tile-status').waitFor({state:'hidden'});
        assert.equal(headers.referer, base + '/');
        assert.equal(headers['cache-control'], undefined);
        assert.equal(headers.pragma, undefined);
        assert.equal(await page.evaluate(() => selected.length), 0);
        await page.setViewportSize({width:390, height:800});
        assert.ok(await page.locator('.place-map').evaluate(el => el.scrollWidth <= el.clientWidth));
        if (process.env.PZMAP_PLACE_MAP_SCREENSHOT) await page.screenshot({path:process.env.PZMAP_PLACE_MAP_SCREENSHOT});
        assert.deepEqual(errors, []);
      } finally {await page.close();}
    });
  } finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
  }
});
