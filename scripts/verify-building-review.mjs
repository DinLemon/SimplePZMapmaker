const pzmapTestBase=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765';
import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const output=new URL('../work/qa-results/',import.meta.url);await mkdir(output,{recursive:true});
async function api(path,body){const r=await fetch(pzmapTestBase+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});assert.equal(r.status,200,await r.clone().text());return r;}
const p=await api('/api/generate',{size:256,kind:'blank'}).then(r=>r.json()),credit='Template Author — CC BY 4.0';
const b=await api('/api/building/design',{building:{id:'credited',name:'Авторский дом',kind:'house',x:40,y:40,width:12,height:10,attribution:[credit]},levels:2,seed:42,shape:'l'}).then(r=>r.json());p.buildings=[b];
const fixture=fileURLToPath(new URL('building-credited.pzmap.json',output));await writeFile(fixture,JSON.stringify(p));
const geo={type:'FeatureCollection',features:[{type:'Feature',properties:{highway:'path'},geometry:{type:'LineString',coordinates:[[30,50],[30.003,50.003]]}}]},geoFile=fileURLToPath(new URL('building-library.geojson',output));await writeFile(geoFile,JSON.stringify(geo));
const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1600,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
async function save(name){const event=page.waitForEvent('download');await page.locator('#save-project').click();const file=fileURLToPath(new URL(name,output));await (await event).saveAs(file);return JSON.parse(await readFile(file,'utf8'));}
try{
 await page.goto(pzmapTestBase);await page.waitForFunction(()=>document.querySelector('#game-status').textContent.includes('найден'));await page.locator('#project-file').setInputFiles(fixture);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&document.querySelector('#building-count').textContent==='1');
 await page.locator('[data-tool=select]').click();const box=await page.locator('#map-canvas').boundingBox(),s=Math.min((box.width-72)/256,(box.height-90)/256);await page.mouse.click(box.x+(box.width-256*s)/2+45*s,box.y+(box.height-256*s)/2+45*s);await page.locator('#edit-building').click();
 const event=page.waitForEvent('download');await page.locator('#be-template-export').click();const templateFile=fileURLToPath(new URL('building-credit-template.json',output));await (await event).saveAs(templateFile);await page.locator('#be-cancel').click();const template=JSON.parse(await readFile(templateFile,'utf8'));assert.ok(template.attribution.includes(credit));
 // An older template can have an empty envelope while its nested building carries credit.
 template.attribution=[];await writeFile(templateFile,JSON.stringify(template));await page.locator('#building-template-file').setInputFiles(templateFile);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&document.querySelector('#building-preset').options.length===10);
 const before=await save('building-credit-roundtrip.pzmap.json');assert.ok(before.buildingLibrary[0].attribution.includes(credit));assert.ok(before.attribution.includes(credit));
 await page.locator('#open-import').click();await page.locator('#preserve-building-edits').check();await page.locator('#import-size').selectOption('256');await page.locator('#geodata-file').setInputFiles(geoFile);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&!document.querySelector('#import-dialog').open);const after=await save('building-library-after-geo.pzmap.json');assert.equal(after.buildings.length,0);assert.deepEqual(after.buildingLibrary,before.buildingLibrary);
 // Exercise the OSM button/request path with offline geography to avoid depending on Overpass.
 let osmRequestChecked=false;await page.route('**/api/import/osm',async route=>{const body=route.request().postDataJSON();assert.deepEqual(body.project.buildingLibrary,before.buildingLibrary);assert.equal(body.preserveEdits,true);const response=await api('/api/import/geojson',{project:body.project,json:JSON.stringify(geo),preserveEdits:body.preserveEdits,bounds:{west:30,east:30.003,south:50,north:50.003}});osmRequestChecked=true;await route.fulfill({status:200,contentType:'application/json',body:await response.text()});});
 await page.locator('#open-import').click();await page.locator('#import-size').selectOption('256');await page.locator('#import-osm-button').click();await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&!document.querySelector('#import-dialog').open);assert.equal(osmRequestChecked,true);const osm=await save('building-library-after-osm.pzmap.json');assert.deepEqual(osm.buildingLibrary,before.buildingLibrary);
 const zip=await api('/api/export',osm).then(r=>r.arrayBuffer());await writeFile(new URL('building-attribution.zip',output),Buffer.from(zip));assert.deepEqual(errors,[]);
 console.log('PASS template own-credit roundtrip, legacy empty envelope, library-only GeoJSON import, offline OSM UI/request preservation, and native attribution export');
}finally{await browser.close();}
