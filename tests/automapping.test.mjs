import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {automappingOptions, automappingSummary, mountAutomappingControls} from '../src/SimplePZMapmaker.Web/wwwroot/automapping.js';

const catalog = JSON.parse(await readFile(new URL('../src/SimplePZMapmaker.Web/wwwroot/street-catalog.json', import.meta.url)));

test('street options are bounded and preserve the supplied access reservation', () => {
    const paths = [{x: 2, y: 3}];
    const options = automappingOptions({width: 512, height: 256}, {x: 256, y: 0, width: 256, height: 256}, {reservedCells: paths});
    assert.deepEqual(options.reservedCells, paths);
    options.reservedCells[0].x = 7;
    assert.equal(paths[0].x, 2);
    for (const selection of [{width: 257}, {x: -1}, {height: 0}, {x: 0.5}, {x: 255, width: 2}])
        assert.throws(() => automappingOptions({width: 256, height: 256}, selection));
    assert.throws(() => automappingOptions({width: 256, height: 256}, {}, {parkingLayer: 'both'}));
    assert.throws(() => automappingOptions({width: 256, height: 256}, {}, {maximumDecorations: 4001}));
    assert.throws(() => automappingOptions({width: 256, height: 256}, {}, {reservedCells: [{x: 256, y: 0}]}));
});

test('street catalog preserves actual source layers offsets and direction IDs', () => {
    assert.equal(catalog.source.commit, '70e8b77676183e579cf9f1f7100dff6c383e28cb');
    const entry = id => catalog.entries.find(e => e.id === id);
    assert.equal(Object.keys(entry('street-curb').variants).length, 24);
    assert.deepEqual(Object.fromEntries(Object.entries(entry('drainage-grate').variants).map(([d, p]) => [d, p[0].tile])), {
        w: 'street_decoration_01_13', n: 'street_decoration_01_14', e: 'street_decoration_01_30', s: 'street_decoration_01_31'
    });
    for (const direction of ['n', 's', 'w', 'e']) {
        const parts = entry('parking-stall').variants[direction];
        assert.equal(parts.filter(p => p.layer === '0_FloorOverlay5').length, 5);
        assert.equal(parts.filter(p => p.layer === '0_Furniture').length, 3);
    }
    assert.equal(entry('parking-stall').variants.e[3].dx, -4);
    assert.equal(entry('parking-stall').variants.s[3].dy, -4);
    for (const rule of catalog.rules) {
        assert.ok(entry(rule.entry).variants[rule.variant]);
        assert.ok(rule.neighbors.length > 0 && rule.neighbors.length <= 8);
    }
});

test('street preview summaries report skips and reject invalid response shapes', () => {
    assert.equal(automappingSummary({baseFingerprint: 'map', catalogFingerprint: 'catalog', decorations: [{}], placed: {curb: 1}, skipped: {access: 3}}),
        '1 decorations (1 curb); skipped: 3 access.');
    assert.throws(() => automappingSummary({decorations: []}));
});

class Element {
    children = []; disabled = false; checked = false; value = ''; textContent = ''; files = [];
    set innerHTML(html) {
        this.nodes = new Map([...html.matchAll(/data-auto="([^"]+)"/g)].map(m => [m[1], new Element()]));
        for (const key of ['curbs', 'drains', 'markings', 'parking']) this.nodes.get(key).checked = true;
        this.nodes.get('limit').value = '2000'; this.nodes.get('parking-layer').value = 'lines';
    }
    querySelector(selector) { return this.nodes.get(selector.match(/data-auto="([^"]+)"/)[1]); }
    append(child) { this.children.push(child); }
    replaceChildren(...children) { this.children = children; }
    remove() { this.removed = true; }
}

test('street mount previews without mutation and applies through the host callback', async () => {
    const originalDocument = globalThis.document;
    globalThis.document = {createElement: () => new Element()};
    try {
        let project = {width: 256, height: 256, name: 'original'}, commits = 0;
        const requests = [], ghosts = [];
        const plan = {id: 'preview', baseFingerprint: 'map', catalogFingerprint: 'catalog', decorations: [{id: 'generated'}], warnings: [], placed: {curb: 1}};
        const host = new Element();
        const controls = mountAutomappingControls(host, {
            getProject: () => project,
            applyProject: async value => { project = value; commits++; },
            onPreview: value => ghosts.push(value),
            fetchImpl: async (url, init) => {
                requests.push({url, payload: init ? JSON.parse(init.body) : null});
                return {ok: true, json: async () => url.endsWith('/catalog') ? {catalog}
                    : url.endsWith('/preview') ? plan : {project: {...project, name: 'applied'}}};
            }
        });
        await controls.ready;
        const box = host.children[0], node = key => box.nodes.get(key);
        await node('preview').onclick();
        assert.equal(commits, 0); assert.equal(project.name, 'original');
        assert.equal(node('apply').disabled, false);
        assert.equal(requests[1].payload.catalog, null);
        assert.equal(ghosts.at(-1).id, 'preview');
        await node('apply').onclick();
        assert.equal(commits, 1); assert.equal(project.name, 'applied');
        assert.equal(node('apply').disabled, true);
        assert.equal(ghosts.at(-1), null);
        controls.dispose(); assert.equal(box.removed, true);
    } finally { globalThis.document = originalDocument; }
});

test('street mount discards a late preview when the active section changes', async () => {
    const originalDocument = globalThis.document;
    globalThis.document = {createElement: () => new Element()};
    try {
        let project = {width: 256, height: 256, name: 'first'}, resolvePreview, commits = 0;
        const host = new Element();
        const controls = mountAutomappingControls(host, {
            getProject: () => project, applyProject: () => { commits++; },
            fetchImpl: async url => {
                if (url.endsWith('/catalog')) return {ok: true, json: async () => ({catalog})};
                return await new Promise(resolve => { resolvePreview = resolve; });
            }
        });
        await controls.ready;
        const box = host.children[0], pending = box.nodes.get('preview').onclick();
        project = {...project, name: 'other section'};
        controls.refresh();
        resolvePreview({ok: true, json: async () => ({baseFingerprint: 'map', catalogFingerprint: 'catalog', decorations: [{}]})});
        await pending;
        assert.equal(commits, 0); assert.equal(box.nodes.get('apply').disabled, true);
        assert.match(box.nodes.get('status').textContent, /changed during preview/);
        controls.dispose();
    } finally { globalThis.document = originalDocument; }
});
