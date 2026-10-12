import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {readFile,mkdir,stat,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';

const root=process.env.PZMAP_QA_URL||'http://127.0.0.1:48765',out=new URL('../work/qa-results/',import.meta.url);
const projectName='Смоленск — центр 1,5 км';
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1600,height:1100}}),errors=[];
page.on('pageerror',e=>errors.push(e.message));
let searchItems,importResult,importRequest,importStatus,routeError;

// Capture the real server payload through APIRequestContext before forwarding it unchanged.
// Large imported projects can disappear from Chromium's DevTools response-body cache.
async function forward(route,capture){
 let upstream;
 try{
  upstream=await route.fetch({timeout:270000});const body=await upstream.body();capture(JSON.parse(body.toString('utf8')),upstream.status(),route.request().postDataJSON());
  await route.fulfill({response:upstream,body});
 }catch(e){routeError=e;await route.abort().catch(()=>{});}finally{await upstream?.dispose();}
}
await page.route('**/api/search',route=>forward(route,data=>searchItems=data));
await page.route('**/api/import/osm',route=>forward(route,(data,status,request)=>{importResult=data;importStatus=status;importRequest=request;}));
async function idle(timeout=30000){await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open,null,{timeout});if(routeError)throw routeError;}
async function download(button,name,timeout=30000){const event=page.waitForEvent('download',{timeout});await page.locator(button).click();const file=await event;assert.equal(await file.failure(),null);const path=fileURLToPath(new URL(name,out));await file.saveAs(path);return path;}

try{
 await page.goto(root);await page.waitForFunction(()=>document.querySelector('#game-status').textContent.includes('найден'));
 await page.locator('#new-map').click();await page.locator('#new-name').fill(projectName);await page.locator('#new-kind').selectOption('blank');await page.locator('#new-size').selectOption('1024');await page.locator('#new-form [type=submit]').click();await idle();
 await page.locator('#open-import').click();await page.locator('#place-query').fill('Смоленск');await page.locator('#search-form [type=submit]').click();await idle(60000);await page.locator('#place-results button').first().waitFor();
 assert.ok(Array.isArray(searchItems),'Nominatim search response was not captured');
 const cityIndex=searchItems.findIndex(item=>['city','town'].includes(item.addresstype||item.type)&&/^Смоленск(?:,|$)/.test(item.display_name)&&+item.lat>=54.6&&+item.lat<=54.95&&+item.lon>=31.8&&+item.lon<=32.3);
 assert.ok(cityIndex>=0,'Nominatim must return Smolensk city, rather than its region or district');const city=searchItems[cityIndex];await page.locator('#place-results button').nth(cityIndex).click();
 assert.equal(await page.locator('#import-source').inputValue(),'smolensk-extract');assert.equal(await page.locator('#use-place-bounds').isChecked(),false);await page.locator('#import-extent').selectOption('1500');await page.locator('#import-size').selectOption('1024');assert.match(await page.locator('#import-area-state').textContent(),/1500 × 1500 м/);
 await page.screenshot({path:fileURLToPath(new URL('smolensk-source-choice.png',out)),fullPage:true});
 const started=performance.now();await page.locator('#import-osm-button').click();await idle(270000);assert.equal(importStatus,200,importResult?.error);assert.equal(importRequest.useRegionalExtract,true);await page.locator('#import-dialog').waitFor({state:'hidden'});
 assert.ok(importResult.importedFeatures>1000);assert.ok(importResult.project.buildings.length>100);assert.ok(importResult.warnings.some(w=>/дата данных: \d{4}-\d{2}-\d{2}/.test(w)),'The actual regional snapshot date must be visible');
 // Import names the project after the search query; give the saved example its descriptive title.
 await page.locator('#project-name').fill(projectName);await page.locator('#project-name').dispatchEvent('change');
 const saved=await download('#save-project','smolensk-ui.pzmap.json'),project=JSON.parse(await readFile(saved,'utf8'));
 assert.equal(project.name,projectName);assert.deepEqual(project.bounds,importRequest.bounds);assert.equal(project.width,1024);assert.equal(project.height,1024);assert.equal(project.buildings.length,importResult.project.buildings.length);assert.equal(project.importSummary.sourceBuildings,importResult.sourceBuildings);assert.ok(project.attribution.some(a=>a.includes('OpenStreetMap')));assert.ok(project.importSummary.notes.some(n=>/дата данных: \d{4}-\d{2}-\d{2}/.test(n)));
 await page.screenshot({path:fileURLToPath(new URL('smolensk-loaded.png',out)),fullPage:true});await page.locator('#export-map').click();const archive=await download('#download-mod','smolensk-ui-B42.zip',120000);assert.ok((await stat(archive)).size>100000,'Native export archive must contain a substantial map payload');await idle(120000);
 assert.deepEqual(errors,[]);const report={status:'PASS live Smolensk city search/source selection/regional import/UI/save/native ZIP',name:project.name,city:city.display_name,bounds:importRequest.bounds,elapsedMs:Math.round(performance.now()-started),features:importResult.importedFeatures,sourceFeatures:importResult.sourceFeatures,sourceBuildings:importResult.sourceBuildings,placed:project.buildings.length,source:importResult.warnings.filter(w=>w.includes('выгрузк'))};await writeFile(new URL('smolensk-ui-report.json',out),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}finally{await browser.close();}
