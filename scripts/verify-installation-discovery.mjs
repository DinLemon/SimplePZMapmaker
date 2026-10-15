import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {mkdirSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

// Dedicated QA server only: its catalog/settings are changed by this check.
const root=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48820';
const workspace=fileURLToPath(new URL('../',import.meta.url));
const fixture=path.join(workspace,'work','installation-qa','fixture-mods');
const output=path.join(workspace,'work','qa-results');
mkdirSync(output,{recursive:true});
async function api(url,body){const r=await fetch(root+url,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const value=await r.json();assert.ok(r.ok,value.error||url);return value;}
const environment=await api('/api/environment');assert.equal(environment.gameFound,true);
// Read an actual image before any manual refresh creates local settings/cache folders.
const initialAssets=await api('/api/assets/search?limit=120&source=game');const initialImage=initialAssets.items.find(a=>a.page);assert.ok(initialImage);const initialPage=await fetch(root+'/api/assets/page/'+initialImage.page);assert.equal(initialPage.status,200,'First auto-detected atlas must load before manual selection');
const original=environment.gamePath;
const library=path.dirname(path.dirname(path.dirname(original)));
assert.ok(existsSync(path.join(library,'steamapps')),'Expected a Steam library');
const mod=path.join(fixture,'PortableFixture');
mkdirSync(path.join(mod,'media','scripts'),{recursive:true});mkdirSync(path.join(mod,'media','models_X'),{recursive:true});
writeFileSync(path.join(mod,'mod.info'),'id=PortableFixture\nname=Portable fixture\ntiledef=portable 900\n');
writeFileSync(path.join(mod,'media','portable.tiles.txt'),'// portablefixture_0\ntile\n{\nsolidfloor = true\n}\n');
writeFileSync(path.join(mod,'media','scripts','models.txt'),'module Portable { model Lamp { mesh=Tiny, } }');
writeFileSync(path.join(mod,'media','models_X','Tiny.obj'),'v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n');
const letters=['P','Q','R','S','T','U','V','W'].filter(v=>!existsSync(v+':\\')).slice(0,2);assert.equal(letters.length,2,'Need two unused drive letters');
const mappings=[];let browser;
try{
 for(const [letter,target] of [[letters[0],library],[letters[1],fixture]]){execFileSync('subst',[letter+':',target]);mappings.push(letter);}
 const game=letters[0]+':\\steamapps\\common\\'+path.basename(original);
 browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1440,height:1050}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(root);await page.locator('#open-resources').click();await page.locator('#asset-detect').click();await page.waitForFunction(()=>!document.querySelector('#asset-refresh').disabled);assert.ok(await page.locator('#asset-installations option').count());
 await page.locator('#asset-source').fill('"'+game+'"');await page.locator('#asset-source').press('Enter');await page.waitForFunction(expected=>document.querySelector('#asset-source').value===expected&&!document.querySelector('#asset-refresh').disabled,game);
 assert.equal((await api('/api/assets/status')).gamePath,game);
 const assets=await api('/api/assets/search?limit=120&source=game');const image=assets.items.find(a=>a.page);assert.ok(image,'Actual installed sprites expected');const png=await fetch(root+'/api/assets/page/'+image.page);assert.equal(png.status,200);assert.equal(Buffer.from(await png.arrayBuffer()).subarray(1,4).toString(),'PNG');
 await page.locator('#asset-source').fill(letters[0]+':\\missing-game');await page.locator('#asset-refresh').click();await page.waitForFunction(()=>document.querySelector('#asset-status').classList.contains('error'));assert.equal((await api('/api/assets/status')).gamePath,game);assert.equal((await api('/api/assets/status')).ready,true);
 await page.locator('#asset-source').fill(game);
 await page.locator('#asset-mods').click();await page.waitForFunction(()=>!document.querySelector('#mod-discover').disabled);await page.locator('#mod-resource-roots').fill(letters[1]+':\\');await page.locator('#mod-discover').click();const checkbox=page.locator('#mod-resources-list input[value="PortableFixture"]');await checkbox.waitFor();await checkbox.check();await page.locator('#mod-resources-apply').click();await page.waitForFunction(()=>document.querySelector('#mod-resources-state').textContent.startsWith('Enabled mods: 1'));
 const tiles=await api('/api/assets/search?source=PortableFixture');assert.ok(tiles.items.some(a=>a.id==='portablefixture_0'));
 await api('/api/models/refresh',{}); // Repeated runs replace this synthetic fixture's files.
 const geometry=await api('/api/models/geometry/Portable.Lamp');assert.equal(geometry.format,'obj');assert.equal(geometry.meshes[0].indices.length,3);
 await page.screenshot({path:path.join(output,'installation-mod-any-drive.png')});
 await page.locator('#mod-resources-close').click();await page.locator('#asset-detection').evaluate(element=>element.hidden=true);await page.screenshot({path:path.join(output,'installation-any-drive.png')});
 assert.deepEqual(errors,[]);console.log('PASS: startup auto-detection; find-installed UI; quoted manual path and Enter; real game atlas on another drive; invalid selection preserves catalog; mod tiles and OBJ geometry on a separate drive; no browser errors.');
}finally{
 if(browser)await browser.close();
 try{await api('/api/assets/mods/activate',{ids:[]});await api('/api/assets/refresh',{gamePath:original});const found=await api('/api/assets/installations');await api('/api/assets/mods/discover',{roots:found.modRoots.slice(0,16),gameVersion:'42.21.0'});}finally{for(const letter of mappings.reverse())execFileSync('subst',[letter+':','/D']);}
}
