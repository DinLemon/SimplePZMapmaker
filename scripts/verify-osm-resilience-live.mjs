// Real editor + real cached regional data, in a separate browser context.
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {readOsmImportStream} from '../src/SimplePZMapmaker.Web/wwwroot/osm-source.js';
const latitude=process.env.PZMAP_TEST_LAT||'43.7387',longitude=process.env.PZMAP_TEST_LON||'7.4256';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48778',out=new URL('../work/osm-resilience-live/',import.meta.url);
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL||'chrome',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const context=await browser.newContext({viewport:{width:1440,height:1000},acceptDownloads:true}),page=await context.newPage(),errors=[];
// Chrome does not reliably retain Network.getResponseBody for a consumed streaming fetch.
// Observe a clone in the page while the real editor consumes its original response.
await context.addInitScript(()=>{const original=window.fetch;window.fetch=async(...args)=>{const response=await original(...args);if(String(args[0]).endsWith('/api/import/osm/stream'))window.__osmVerificationBody=response.clone().text();return response;};});
page.on('pageerror',error=>errors.push(error.message));page.setDefaultTimeout(120000);
for(const path of ['recovery','world/recovery'])await context.route('**/api/'+path+'/**',route=>route.fulfill({status:route.request().method()==='GET'?404:200,json:{saved:true}}));
const idle=()=>page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
const menu=async(group,id)=>{await page.locator(`[data-command-menu=${group}]>summary`).click();await page.locator('#'+id).click();};
try{
 await page.goto(base);await idle();await menu('import','open-import');
 await page.locator('#import-lat').fill(latitude);await page.locator('#import-lon').fill(longitude);await page.locator('#import-extent').selectOption('400');await page.locator('#import-size').selectOption('512');await page.locator('#preserve-building-edits').uncheck();assert.equal(await page.locator('#import-source').inputValue(),'auto');
 const response=page.waitForResponse(r=>r.url().endsWith('/api/import/osm/stream'));await page.locator('#import-osm-button').click();const loaded=await response;assert.ok(loaded.ok(),`HTTP ${loaded.status()}`);const imported=await readOsmImportStream(new Response(await page.evaluate(()=>window.__osmVerificationBody),{status:loaded.status()}));await idle();
 assert.ok(imported.sourceId);assert.ok(imported.importedFeatures>0);assert.equal(await page.locator('#building-count').textContent(),String(imported.project.buildings.length));assert.equal(await page.locator('#import-dialog').isVisible(),false);
 console.log(`PASS real editor automatic map import: ${imported.importedFeatures} features, ${imported.project.buildings.length} buildings`);
 await menu('import','open-import');const pending=page.waitForEvent('download');await page.locator('#import-source-download').click();const download=await pending,sourceFile=fileURLToPath(new URL('source.json',out));await download.saveAs(sourceFile);const source=JSON.parse(await readFile(sourceFile,'utf8'));assert.ok(source.elements.length>0);
 await page.locator('#import-world-button').click();await page.locator('#osm-world-name').fill('Offline regional world');await page.locator('#osm-world-scale').fill('1');await page.locator('#osm-world-advanced>summary').click();await page.locator('#osm-world-size').selectOption('256');await page.locator('#osm-world-source').selectOption('file');
 const upload=page.waitForResponse(r=>r.url().endsWith('/api/osm/sources'));await page.locator('#osm-world-source-file').setInputFiles(sourceFile);assert.ok((await upload).ok());await page.locator('#osm-world-start').waitFor({state:'visible'});await page.waitForFunction(()=>!document.querySelector('#osm-world-start').disabled);
 const planResponse=page.waitForResponse(r=>r.url().endsWith('/api/world/osm/plan'));await page.locator('#osm-world-plan').click();const plan=await (await planResponse).json();assert.equal(plan.columns,2);assert.equal(plan.rows,2);await page.waitForFunction(()=>!document.querySelector('#osm-world-start').disabled);
 const creation=page.waitForResponse(r=>r.url().endsWith('/api/world/osm/jobs')&&r.request().method()==='POST');await page.locator('#osm-world-start').click();const job=await (await creation).json();await page.locator('#osm-world-apply').waitFor({state:'visible'});
 assert.equal(await page.locator('#building-count').textContent(),String(imported.project.buildings.length));assert.match(await page.locator('#osm-world-source-state').textContent(),/ready/);assert.equal(await page.locator('#osm-world-source-download').isVisible(),true);
 await page.screenshot({path:fileURLToPath(new URL('world-ready.png',out))});
 await page.locator('#osm-world-apply').click();await page.locator('#osm-world-dialog').waitFor({state:'hidden'});await page.locator('#world-dialog').waitFor({state:'visible'});assert.equal(await page.locator('#world-name').inputValue(),'Offline regional world');assert.equal(await page.locator('#world-section-list button').count(),4);
 await page.locator('#world-undo').click();assert.equal(await page.locator('#world-section-list button').count(),0);await page.locator('#world-redo').click();assert.equal(await page.locator('#world-section-list button').count(),4);
 const saving=page.waitForEvent('download');await page.locator('#world-save').click();const worldFile=fileURLToPath(new URL('world.pzworld.json',out));await(await saving).saveAs(worldFile);await idle();const world=JSON.parse(await readFile(worldFile,'utf8'));assert.equal(world.maps.length,4);assert.equal(world.geoReference.metresPerTile,1);
 await page.locator('#world-import-osm').click();assert.equal(await page.locator('#osm-world-preserve').isChecked(),true);assert.equal(await page.locator('#osm-world-width').isDisabled(),true);await page.setViewportSize({width:390,height:844});assert.ok(await page.locator('#osm-world-dialog').evaluate(d=>d.scrollWidth<=d.clientWidth+1));await page.screenshot({path:fileURLToPath(new URL('world-mobile.png',out))});
 assert.deepEqual(errors,[]);await writeFile(new URL('result.json',out),JSON.stringify({mapFeatures:imported.importedFeatures,mapBuildings:imported.project.buildings.length,worldSections:world.maps.length,jobId:job.id,offlineSource:true,applyShowsWorld:true,undoRedo:true,errors},null,2));
 console.log('PASS real editor source download, offline world plan/build/apply/save, visible result, undo/redo and mobile frame preservation');
}catch(error){console.error({state:await page.locator('#osm-world-state').textContent().catch(()=>null),importState:await page.locator('#import-state').textContent().catch(()=>null),errors});await page.screenshot({path:fileURLToPath(new URL('failure.png',out))}).catch(()=>{});throw error;}finally{await context.close();await browser.close();}
