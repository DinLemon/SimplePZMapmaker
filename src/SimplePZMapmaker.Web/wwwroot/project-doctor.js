export const maximumTileDefinitionBytes = 32 * 1024 * 1024;

// Works with the host's JSON-only, same-origin POST policy; never changes middleware.
export async function readDoctorTileFile(file, { request = doctorRequest, signal } = {}) {
    signal?.throwIfAborted();
    if (!file || file.size > maximumTileDefinitionBytes) throw Error('Tile definition or snapshot exceeds 32 MiB.');
    if (/\.json$/i.test(file.name)) {
        const snapshot = JSON.parse(await file.text());
        signal?.throwIfAborted();
        if (!snapshot || !Array.isArray(snapshot.tilesets)) throw Error('Choose an exported tile snapshot JSON.');
        return snapshot;
    }
    if (!/\.tiles$/i.test(file.name)) throw Error('Choose a binary .tiles file or exported snapshot .json. External .tiles.txt is unsupported.');
    const bytes = new Uint8Array(await file.arrayBuffer());
    signal?.throwIfAborted();
    if (bytes.length > maximumTileDefinitionBytes) throw Error('Tile definition exceeds 32 MiB.');
    // Bound spread argument counts even for large files. A multiple of three preserves base64 chunk boundaries.
    const chunks = [];
    for (let offset = 0; offset < bytes.length; offset += 24576)
        chunks.push(btoa(String.fromCharCode(...bytes.subarray(offset, offset + 24576))));
    return request('/api/project-doctor/tiles/read', {
        method: 'POST', body: { base64: chunks.join(''), fileName: file.name }, signal
    });
}

export async function doctorRequest(url, { method = 'GET', body, signal } = {}) {
    const response = await fetch(url, {
        method, signal, credentials: 'same-origin',
        headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    const result = await response.json();
    if (!response.ok) throw Error(result.error || result.title || `Project Doctor HTTP ${response.status}`);
    return result;
}

function saveJson(name, value) {
    const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
    const link = document.createElement('a'); link.href = url; link.download = name; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Mount into a parent-owned panel. deps:
 * getSnapshot(): {project} OR {world,memberId}; raw current authoring, not a projected map.
 * request(url,{method,body,signal}): optional JSON transport returning parsed response.
 * navigate(location): optional focus callback; download(name,object): optional JSON saver.
 * No app globals or startup registration; destroy aborts requests and removes owned DOM.
 */
export function mountProjectDoctor(root, deps) {
    if (!root || typeof deps?.getSnapshot !== 'function') throw Error('Project Doctor needs a root and getSnapshot dependency.');
    const doc = root.ownerDocument;
    const request = deps.request ?? doctorRequest;
    const download = deps.download ?? saveJson;
    const controllers = new Set();
    let destroyed = false, doctorSequence = 0, comparisonSequence = 0, catalogsSequence = 0, report = null, comparison = null;
    const snapshots = { before: null, after: null }, slots = {};
    const panel = doc.createElement('section'); panel.className = 'project-doctor'; panel.setAttribute('aria-label', 'Project Doctor');
    root.append(panel);
    function element(tag, text, parent = panel) {
        const node = doc.createElement(tag); if (text !== undefined) node.textContent = text; parent.append(node); return node;
    }
    function button(text, parent, action) {
        const node = element('button', text, parent); node.type = 'button'; node.addEventListener('click', action); return node;
    }
    element('h2', 'Project Doctor');
    const status = element('p', 'Read-only checks. Coverage limits and unavailable checks remain visible.');
    status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
    const actions = element('div');
    const run = button('Check project', actions, () => check());
    const saveReport = button('Export project report JSON', actions, () => { if (report) download('project-doctor-report.json', report); });
    saveReport.disabled = true;
    const crop = element('fieldset'); element('legend', 'Access crop (section-local tiles, up to 256×256)', crop);
    const fields = {};
    for (const [key, initial] of Object.entries({ x: 0, y: 0, width: 128, height: 128 })) {
        const label = element('label', key + ' ', crop); const input = element('input', undefined, label);
        input.type = 'number'; input.min = key === 'x' || key === 'y' ? '0' : '1'; input.step = '1';
        if (key === 'width' || key === 'height') input.max = '256';
        input.value = String(initial); fields[key] = input;
    }
    const checks = element('ul'); checks.setAttribute('aria-label', 'Check coverage');
    const filterLabel = element('label', 'Finding severity '); const filter = element('select', undefined, filterLabel);
    for (const severity of ['all', 'error', 'warning', 'info']) { const option = element('option', severity, filter); option.value = severity; }
    const findings = element('ul'); findings.setAttribute('aria-label', 'Project findings');
    filter.addEventListener('change', renderFindings);

    element('h3', 'Tile compatibility');
    element('p', 'Compare original numeric slots, structural metadata and every property. Text catalogs expose properties only. No changes are saved to source files.');
    const tileStatus = element('p', 'Choose a baseline and a candidate from installed catalogs or external files.');
    tileStatus.setAttribute('role', 'status'); tileStatus.setAttribute('aria-live', 'polite');
    button('Reload installed catalogs', panel, () => loadCatalogs());
    for (const side of ['before', 'after']) {
        const group = element('fieldset'); element('legend', side === 'before' ? 'Baseline' : 'Candidate', group);
        const label = element('label', 'Installed definition ', group); const select = element('select', undefined, label);
        const empty = element('option', 'Choose installed definition', select); empty.value = '';
        const summary = element('p', 'No snapshot loaded.', group);
        const fileLabel = element('label', 'Or external .tiles / snapshot JSON ', group); const file = element('input', undefined, fileLabel);
        file.type = 'file'; file.accept = '.tiles,.json';
        const save = button('Export snapshot JSON', group, () => { if (snapshots[side]) download(`tile-${side}-snapshot.json`, snapshots[side]); });
        save.disabled = true; slots[side] = { select, summary, file, save, sequence: 0 };
        select.addEventListener('change', () => {
            if (select.value) loadSide(side, signal => request('/api/project-doctor/tiles/snapshot?catalogId=' + encodeURIComponent(select.value), { signal }));
        });
        file.addEventListener('change', () => {
            const picked = file.files?.[0];
            if (picked) { select.value = ''; loadSide(side, signal => readDoctorTileFile(picked, { request, signal })); }
        });
    }
    const tileActions = element('div');
    const compare = button('Compare tile definitions', tileActions, () => compareTiles()); compare.disabled = true;
    const saveComparison = button('Export tile report JSON', tileActions, () => { if (comparison) download('tile-compatibility-report.json', comparison); });
    saveComparison.disabled = true;
    const differenceFilterLabel = element('label', 'Filter tile differences ');
    const differenceFilter = element('input', undefined, differenceFilterLabel); differenceFilter.type = 'search';
    const paging = element('div'), pageStatus = element('span', '', paging); let page = 0;
    const previous = button('Previous differences', paging, () => { --page; renderDifferences(); });
    const next = button('Next differences', paging, () => { ++page; renderDifferences(); }); previous.disabled = next.disabled = true;
    differenceFilter.addEventListener('input', () => { page = 0; renderDifferences(); });
    const differences = element('ul'); differences.setAttribute('aria-label', 'Tile differences');
    const inspector = element('details'); element('summary', 'Inspect selected tile and properties', inspector);
    const inspection = element('pre', 'Select a difference to inspect its original slot and property maps.', inspector);
    const tileFindings = element('ul'); tileFindings.setAttribute('aria-label', 'Replacement reference findings');

    async function withSignal(action) {
        const controller = new AbortController(); controllers.add(controller);
        try { return await action(controller.signal); } finally { controllers.delete(controller); }
    }
    function showError(node, error) { if (!destroyed && error.name !== 'AbortError') node.textContent = error.message; }
    function locationText(location) {
        if (!location) return '';
        return [location.memberId && `section ${location.memberId}`, location.buildingId && `building ${location.buildingId}`,
            location.x != null && `tile ${location.x},${location.y},${location.z ?? 0}`,
            location.cellX != null && `cell ${location.cellX},${location.cellY}`, location.source, location.tile].filter(Boolean).join('; ');
    }
    function renderFindings() {
        findings.replaceChildren();
        for (const finding of report?.findings ?? []) {
            if (filter.value !== 'all' && finding.severity !== filter.value) continue;
            const row = element('li', `${finding.severity} · ${finding.category}: ${finding.message}`, findings);
            const location = locationText(finding.location); if (location) element('p', location, row);
            if (finding.location && deps.navigate) button('Show location', row, () => deps.navigate(finding.location));
        }
    }
    async function check() {
        const sequence = ++doctorSequence; report = null; saveReport.disabled = true; run.disabled = true;
        checks.replaceChildren(); findings.replaceChildren(); status.textContent = 'Checking project…';
        try {
            const snapshot = deps.getSnapshot();
            if (!snapshot || (!!snapshot.project === !!snapshot.world)) throw Error('Select one active map or world.');
            const access = Object.fromEntries(Object.entries(fields).map(([key, input]) => [key, Number(input.value)]));
            if (Object.values(access).some(value => !Number.isSafeInteger(value))) throw Error('Access crop values must be integers.');
            const scope = snapshot.world ? { world: snapshot.world, memberId: snapshot.memberId ?? null } : { project: snapshot.project };
            const identity = JSON.stringify(scope);
            const result = await withSignal(signal => request('/api/project-doctor/check', { method: 'POST', body: { ...scope, access }, signal }));
            if (destroyed || sequence !== doctorSequence) return;
            const current = deps.getSnapshot();
            const currentScope = current?.world ? { world: current.world, memberId: current.memberId ?? null } : { project: current?.project };
            if (JSON.stringify(currentScope) !== identity) { status.textContent = 'Project changed during checking. Run the check again.'; return; }
            report = result; saveReport.disabled = false;
            status.textContent = `${report.status}. ${report.complete ? 'Coverage complete for supplied checks.' : 'Partial coverage: review unavailable and unsupported checks.'}`;
            for (const item of report.checks) element('li', `${item.category}: ${item.status} — ${item.message}`, checks);
            renderFindings();
        } catch (error) { if (sequence === doctorSequence) showError(status, error); } finally { if (!destroyed && sequence === doctorSequence) run.disabled = false; }
    }
    function invalidateComparison() {
        ++comparisonSequence; comparison = null; saveComparison.disabled = true;
        differences.replaceChildren(); tileFindings.replaceChildren(); inspection.textContent = 'Select a difference to inspect its original slot and property maps.';
        page = 0; pageStatus.textContent = ''; previous.disabled = next.disabled = true;
        compare.disabled = !snapshots.before || !snapshots.after;
    }
    function renderDifferences() {
        differences.replaceChildren();
        const query = differenceFilter.value.toLowerCase();
        const rows = (comparison?.differences ?? []).filter(d =>
            [d.kind, d.tileset, d.tileId, d.field, d.before, d.after].join(' ').toLowerCase().includes(query));
        page = Math.max(0, Math.min(page, Math.ceil(rows.length / 200) - 1));
        previous.disabled = page === 0; next.disabled = (page + 1) * 200 >= rows.length;
        pageStatus.textContent = `${rows.length} matching differences; page ${page + 1} of ${Math.max(1, Math.ceil(rows.length / 200))}. `;
        for (const difference of rows.slice(page * 200, (page + 1) * 200)) {
            const row = element('li', undefined, differences);
            button(`${difference.kind}: ${difference.tileset}${difference.tileId == null ? '' : '_' + difference.tileId} ${difference.field ?? ''}`, row, () => {
                const record = snapshot => snapshot.tilesets.find(s => s.name === difference.tileset);
                const before = record(snapshots.before), after = record(snapshots.after);
                const select = set => difference.tileId == null ? set : set?.tiles.find(t => t.id === difference.tileId);
                inspection.textContent = JSON.stringify({ difference, before: select(before) ?? null, after: select(after) ?? null }, null, 2); inspector.open = true;
            });
        }
    }
    async function loadSide(side, loader) {
        const slot = slots[side], sequence = ++slot.sequence;
        snapshots[side] = null; slot.save.disabled = true; invalidateComparison(); slot.summary.textContent = 'Reading snapshot…';
        try {
            const snapshot = await withSignal(loader);
            if (destroyed || sequence !== slot.sequence) return;
            snapshots[side] = snapshot; slot.save.disabled = false;
            slot.summary.textContent = `${snapshot.source}: ${snapshot.tilesets.length} tilesets; ${snapshot.hasStructure ? 'binary structure retained' : 'properties only; structure unavailable'}. SHA-256: ${snapshot.sha256 ?? 'unavailable'}`;
            invalidateComparison(); tileStatus.textContent = 'Snapshot loaded. Compare to inspect changes.';
        } catch (error) { if (sequence === slot.sequence) showError(slot.summary, error); }
    }
    async function loadCatalogs() {
        const sequence = ++catalogsSequence;
        try {
            const result = await withSignal(signal => request('/api/project-doctor/tiles/catalogs', { signal }));
            if (destroyed || sequence !== catalogsSequence) return;
            for (const side of ['before', 'after']) {
                const slot = slots[side]; ++slot.sequence; snapshots[side] = null; slot.save.disabled = true;
                slot.select.replaceChildren(); const empty = element('option', 'Choose installed definition', slot.select); empty.value = '';
                for (const entry of result.catalogs) { const option = element('option', `${entry.source} / ${entry.fileName} (${entry.format})`, slot.select); option.value = entry.id; }
                slot.summary.textContent = 'No snapshot loaded.';
            }
            invalidateComparison(); tileStatus.textContent = `${result.available ? 'Installed catalogs loaded.' : 'Installed catalogs unavailable; external files can still be compared.'} ${result.note}`;
        } catch (error) { if (sequence === catalogsSequence) showError(tileStatus, error); }
    }
    async function compareTiles() {
        if (!snapshots.before || !snapshots.after) return;
        const sequence = ++comparisonSequence; compare.disabled = true; saveComparison.disabled = true;
        tileStatus.textContent = 'Comparing definitions…';
        try {
            const result = await withSignal(signal => request('/api/project-doctor/tiles/compare', { method: 'POST', body: { before: snapshots.before, after: snapshots.after }, signal }));
            if (destroyed || sequence !== comparisonSequence) return;
            comparison = result; saveComparison.disabled = false; differences.replaceChildren(); tileFindings.replaceChildren();
            tileStatus.textContent = `${result.status}: ${result.differences.length} differences. ${result.complete ? 'Structural and replacement checks completed.' : 'Partial coverage.'} ${result.notes.join(' ')}`;
            page = 0; renderDifferences();
            for (const finding of result.findings) element('li', `${finding.severity}: ${finding.message}`, tileFindings);
        } catch (error) { if (sequence === comparisonSequence) showError(tileStatus, error); } finally { if (!destroyed && sequence === comparisonSequence) compare.disabled = !snapshots.before || !snapshots.after; }
    }
    const ready = loadCatalogs();
    return { ready, check, reloadCatalogs: loadCatalogs, destroy() {
        destroyed = true; ++doctorSequence; ++comparisonSequence; ++catalogsSequence;
        for (const controller of controllers) controller.abort(); panel.remove();
    } };
}
