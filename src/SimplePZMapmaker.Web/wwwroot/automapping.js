const copy = value => structuredClone(value);
const MAX_BYTES = 1048576;

export function automappingOptions(project, selection = {}, settings = {}) {
    if (!project || !Number.isInteger(project.width) || !Number.isInteger(project.height)) throw Error('Open a map section first.');
    const options = {
        x: selection.x ?? 0, y: selection.y ?? 0,
        width: selection.width ?? Math.min(256, project.width), height: selection.height ?? Math.min(256, project.height),
        curbs: settings.curbs ?? true, drains: settings.drains ?? true, markings: settings.markings ?? true,
        parking: settings.parking ?? true, parkingEntry: settings.parkingEntry ?? 'parking-stall',
        parkingLayer: settings.parkingLayer ?? 'lines', maximumDecorations: settings.maximumDecorations ?? 2000,
        reservedCells: copy(settings.reservedCells ?? [])
    };
    if (['x', 'y', 'width', 'height', 'maximumDecorations'].some(key => !Number.isSafeInteger(options[key]))
        || options.x < 0 || options.y < 0 || options.width < 1 || options.height < 1 || options.width > 256 || options.height > 256
        || options.x + options.width > project.width || options.y + options.height > project.height
        || options.maximumDecorations < 1 || options.maximumDecorations > 4000
        || !['lines', 'bumpers'].includes(options.parkingLayer)
        || !Array.isArray(options.reservedCells) || options.reservedCells.length > 8192
        || options.reservedCells.some(c => !c || !Number.isSafeInteger(c.x) || !Number.isSafeInteger(c.y)
            || c.x < 0 || c.y < 0 || c.x >= project.width || c.y >= project.height))
        throw Error('Choose an in-map rectangle of 1–256 tiles, a 1–4000 decoration limit and valid reserved passage cells.');
    return options;
}

export function automappingSummary(plan) {
    if (!plan || !Array.isArray(plan.decorations) || plan.decorations.length > 4000 || !plan.baseFingerprint || !plan.catalogFingerprint)
        throw Error('The server returned an invalid street preview.');
    const counts = Object.entries(plan.placed ?? {}).map(([kind, count]) => `${count} ${kind}`).join(', ');
    const skips = Object.entries(plan.skipped ?? {}).map(([reason, count]) => `${count} ${reason}`).join(', ');
    return `${plan.decorations.length} decorations${counts ? ' (' + counts + ')' : ''}${skips ? '; skipped: ' + skips : ''}.`;
}

/** Explicit integration: applyProject must run the host's normal validation/history/save
 * path. onPreview may render ghosts; this module never paints or mutates the active map. */
export function mountAutomappingControls(host, { getProject, applyProject, getSelection = () => ({}),
    getReservedCells = () => [], onPreview = () => {}, fetchImpl = globalThis.fetch } = {}) {
    if (!host || typeof getProject !== 'function' || typeof applyProject !== 'function' || typeof fetchImpl !== 'function')
        throw Error('Street automapping requires a host, getProject, applyProject and fetch.');
    const box = document.createElement('details');
    box.className = 'geographic-authoring-workbench';
    box.id = 'automapping-controls';
    box.innerHTML = `<summary>Street catalog and automapping</summary>
        <p class="hint">Preview curbs, drainage and directional parking on the selected rectangle (up to 256 × 256 tiles). Existing objects, authored ground and entrance paths stay protected.</p>
        <label><input data-auto="curbs" type="checkbox" checked> Straight curbs</label>
        <label><input data-auto="drains" type="checkbox" checked> Drainage grates</label>
        <label><input data-auto="markings" type="checkbox" checked> Catalog marking rules</label>
        <label><input data-auto="parking" type="checkbox" checked> Mark existing 3 × 5 vehicle stalls</label>
        <label>Parking layer<select data-auto="parking-layer"><option value="lines">Painted lines</option><option value="bumpers">Wheel stops</option></select></label>
        <p class="hint">Parking lines and wheel stops share a cell in TileZed. Choose one layer for this map format.</p>
        <label>Decoration limit<input data-auto="limit" type="number" min="1" max="4000" value="2000"></label>
        <label>Updated catalog JSON<input data-auto="upload" type="file" accept=".json,application/json"></label>
        <button data-auto="download" type="button">Download catalog</button>
        <button data-auto="reset" type="button">Use bundled catalog</button>
        <p class="hint" data-auto="catalog"></p>
        <button data-auto="preview" type="button" disabled>Preview street automapping</button>
        <button data-auto="apply" type="button" disabled>Apply preview</button>
        <p class="hint" role="status" aria-live="polite" data-auto="status">Loading street catalog…</p>
        <ul data-auto="warnings"></ul>`;
    host.append(box);
    const node = key => box.querySelector(`[data-auto="${key}"]`);
    let bundled = null, custom = null, plan = null, source = null, busy = false, generation = 0, disposed = false;
    const status = message => { node('status').textContent = message; };
    const currentCatalog = () => custom ?? bundled;
    function enable() {
        for (const key of ['preview', 'upload', 'reset', 'download', 'curbs', 'drains', 'markings', 'parking', 'parking-layer', 'limit'])
            node(key).disabled = busy || !bundled;
        node('apply').disabled = busy || !plan || plan.decorations.length === 0;
    }
    function invalidate() {
        generation++; plan = null; source = null;
        node('warnings').replaceChildren();
        onPreview(null); enable();
    }
    function catalogLabel() {
        const c = currentCatalog();
        node('catalog').textContent = c ? `${c.id} · revision ${c.revision} · ${c.entries.length} entries · ${c.rules.length} rules` : '';
    }
    async function request(url, payload) {
        const response = await fetchImpl(url, payload === undefined ? undefined : {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!response.ok) throw Error(result.error || `Street automapping failed (${response.status}).`);
        return result;
    }
    async function run(action) {
        if (busy || disposed) return;
        busy = true; enable();
        try { await action(); } catch (error) { invalidate(); status(error.message); }
        finally { busy = false; if (!disposed) enable(); }
    }
    for (const key of ['curbs', 'drains', 'markings', 'parking', 'parking-layer', 'limit']) node(key).onchange = () => {
        invalidate(); status('Settings changed. Generate a fresh preview.');
    };
    node('preview').onclick = () => run(async () => {
        invalidate();
        const ticket = generation, project = copy(getProject());
        const options = automappingOptions(project, getSelection(), {
            curbs: node('curbs').checked, drains: node('drains').checked, markings: node('markings').checked,
            parking: node('parking').checked, parkingLayer: node('parking-layer').value,
            maximumDecorations: Number(node('limit').value), reservedCells: getReservedCells()
        });
        status('Checking street neighbors and protected cells…');
        const result = await request('/api/automapping/preview', { project, options, catalog: custom });
        if (disposed || ticket !== generation || JSON.stringify(getProject()) !== JSON.stringify(project))
            throw Error('The active map changed during preview. Preview again.');
        const summary = automappingSummary(result);
        plan = result; source = JSON.stringify(project);
        node('warnings').replaceChildren(...(plan.warnings ?? []).map(message => {
            const li = document.createElement('li'); li.textContent = message; return li;
        }));
        onPreview(copy(plan)); status(summary);
    });
    node('apply').onclick = () => run(async () => {
        if (!plan || JSON.stringify(getProject()) !== source) throw Error('The map changed after preview. Preview again.');
        const ticket = generation, proposed = plan;
        const result = await request('/api/automapping/apply', { project: copy(getProject()), plan: proposed, catalog: custom });
        if (disposed || ticket !== generation || JSON.stringify(getProject()) !== source)
            throw Error('The active map changed during apply. Preview again.');
        if (!result.project) throw Error('The server did not return an updated map.');
        await applyProject(result.project);
        invalidate(); status(`Applied ${proposed.decorations.length} street decorations.`);
    });
    node('upload').onchange = () => run(async () => {
        const file = node('upload').files?.[0];
        if (!file) return;
        if (file.size > MAX_BYTES) throw Error('Street catalog exceeds 1 MiB.');
        const document = JSON.parse(await file.text());
        if (document.id !== currentCatalog().id || !Number.isSafeInteger(document.revision) || document.revision <= currentCatalog().revision)
            throw Error('Keep the catalog ID and increase its revision before importing an update.');
        const result = await request('/api/automapping/catalog/validate', document);
        if (!result.catalog || !Array.isArray(result.catalog.entries) || !Array.isArray(result.catalog.rules))
            throw Error('The server returned an invalid catalog.');
        custom = result.catalog; invalidate(); catalogLabel(); status('Catalog update validated. Preview its rules before applying.');
    });
    node('reset').onclick = () => { custom = null; node('upload').value = ''; invalidate(); catalogLabel(); status('Using the bundled street catalog.'); };
    node('download').onclick = () => {
        const url = URL.createObjectURL(new Blob([JSON.stringify(currentCatalog(), null, 2) + '\n'], { type: 'application/json' }));
        const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'street-catalog.json'; anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    };
    const ready = run(async () => {
        const result = await request('/api/automapping/catalog');
        if (disposed) return;
        if (!result.catalog || !Array.isArray(result.catalog.entries) || !Array.isArray(result.catalog.rules)) throw Error('Street catalog is invalid.');
        bundled = result.catalog; catalogLabel(); status('Catalog ready. Preview does not change the map.');
    });
    return {
        ready,
        refresh() { invalidate(); status('Map or selection changed. Generate a fresh preview.'); },
        dispose() { disposed = true; invalidate(); box.remove(); }
    };
}
