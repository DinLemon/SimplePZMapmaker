// Disposable real API + import/compile UI proof. Never claims client car spawning/refuelling.
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48813';
assert.equal(new URL(base).hostname,'127.0.0.1');assert.notEqual(new URL(base).port,'48779');
const out=new URL('../work/knoxmap-research/transport-import/',import.meta.url);await mkdir(out,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});
try {
 const context=await browser.newContext({viewport:{width:1400,height:1000}}),page=await context.newPage(),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 const fixture=`<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/style.css"><link rel="stylesheet" href="/osm-world-import.css"><link rel="stylesheet" href="/world-build-output.css"><link rel="stylesheet" href="/export-jobs.css"><button id="world-open">Open world</button><input id="import-lat" value="50.001"><input id="import-lon" value="30.001"><input id="import-extent" value="240"><script type="module">import {OsmWorldImport} from '/osm-world-import.js';window.importer=new OsmWorldImport({getWorld:()=>null,acceptWorld:()=>{},acceptWorkspace:()=>{},getSelection:()=>null});</script>`;
 await page.route(base+'/__transport-import-fixture',route=>route.fulfill({contentType:'text/html',body:fixture}));
 await page.goto(base+'/__transport-import-fixture');await page.locator('#world-import-osm').click();
 await page.locator('#osm-world-advanced').evaluate(n=>n.open=true);await page.locator('#osm-world-size').selectOption('256');
 const request=await page.evaluate(()=>window.importer.request()),b=request.bounds,mx=111320*Math.cos((b.north+b.south)/2*Math.PI/180),ll=([x,y])=>[b.west+x/mx,b.north-y/111320];
 const feature=(id,properties,type,coordinates)=>({type:'Feature',id,properties,geometry:{type,coordinates}});
 const ring=(x,y,w,h)=>[[x,y],[x+w,y],[x+w,y+h],[x,y+h],[x,y]].map(ll);
 const source={type:'FeatureCollection',features:[
  feature('street',{highway:'residential',lanes:'2',sidewalk:'both',oneway:'no'},'LineString',[[10,20],[225,20]].map(ll)),
  feature('parking',{amenity:'parking','pz:direction':'N','pz:vehicles':'junkyard',name:'Contour fixture'},'Polygon',[[[100,40],[200,40],[200,85],[170,85],[170,140],[100,140],[100,40]].map(ll),ring(120,60,10,20)]),
  feature('fence',{barrier:'fence'},'LineString',[[155,40],[155,140]].map(ll)),
  feature('water',{natural:'water'},'Polygon',[ring(110,105,15,20)]),
  feature('station',{amenity:'fuel',name:'Source fuel site'},'Polygon',[ring(25,30,40,35)]),
  feature('pump',{man_made:'fuel_pump'},'Point',ll([35.2,42.2])),
  feature('inactive',{amenity:'fuel',disused:'yes'},'Point',ll([210,190])),
  feature('house',{building:'house','building:levels':'2'},'Polygon',[ring(205,75,15,15)])
 ]};
 await page.locator('#osm-world-source').selectOption('file');await page.locator('#osm-world-source-file').setInputFiles({name:'transport-source.geojson',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(source))});
 await page.waitForFunction(()=>window.importer.source.saved&&!window.importer.sourceBusy);
 await page.locator('#osm-world-start').click();await page.waitForFunction(()=>!window.importer.busy&&window.importer.result,{},{timeout:120000});
 const imported=await page.evaluate(()=>window.importer.result);assert.equal(imported.resultKind,'sections');
 async function get(path){const r=await context.request.get(base+path);assert.ok(r.ok(),`${path}: ${r.status()} ${await r.text()}`);return r.json();}
 const manifest=await get(`/api/world/workspaces/${imported.workspaceId}`);assert.equal(manifest.sections.length,1);
 const section=await get(`/api/world/workspaces/${imported.workspaceId}/sections/${manifest.sections[0].id}`),p=section.member.project;
 const road=p.linearFeatures.find(f=>f.sourceTags.highway==='residential');assert.ok(Math.abs(road.width-7.4)<1e-8);assert.equal(road.sourceTags['pzmap:width-source'],'lanes');assert.equal(road.sourceTags.sidewalk,'both');
 const rows=p.gameZones.filter(z=>z.type==='ParkingStall');assert.ok(rows.length>1);let stalls=0;
 for(const z of rows){assert.equal(z.direction,'N');assert.equal(z.vehicleDistribution,'junkyard');assert.equal(z.height,5);assert.equal(z.width%3,0);stalls+=z.width*z.height/15;
  for(let y=z.y;y<z.y+z.height;y++)for(let x=z.x;x<z.x+z.width;x++){
   assert.ok(x>=100&&x+1<=200&&y>=40&&y+1<=140&&(x+1<=170||y+1<=85),'Car outside concave lot');
   assert.ok(x+1<=120||x>=130||y+1<=60||y>=80,'Car inside courtyard hole');assert.notEqual(p.terrain[y*p.width+x],3,'Car in water');
  }
 }
 const pumps=p.decorations.filter(d=>d.id.startsWith('fuel-'));assert.equal(pumps.length,1);assert.equal(pumps[0].x,35);assert.equal(pumps[0].y,42);assert.ok(['location_shop_fossoil_01_12','location_shop_fossoil_01_14'].includes(pumps[0].asset));
 assert.ok(p.sourceFeatures.some(f=>f.tags.man_made==='fuel_pump'));assert.ok(p.importSummary.notes.some(n=>/disused|abandoned/i.test(n)));
 const queued=page.waitForResponse(r=>new URL(r.url()).pathname===`/api/world/workspaces/${imported.workspaceId}/export/jobs`);
 await page.locator('[data-build="compile"]').click();const jobId=(await (await queued).json()).id;
 await page.locator('[data-build="download"]:visible').waitFor({timeout:120000});
 const job=await get('/api/export/jobs/'+jobId),download=await context.request.get(base+job.downloadUrl);assert.equal(job.status,'completed');assert.ok(download.ok());const bytes=await download.body();
 await writeFile(new URL('TransportSource-B42.zip',out),bytes);await writeFile(new URL('section.json',out),JSON.stringify(p));await writeFile(new URL('source.geojson',out),JSON.stringify(source));
 assert.match(await page.locator('[data-build="state"]').innerText(),/completed/i);
 const fixtureMod='TransportFixture_'+jobId.slice(0,8);
 await page.locator('[data-build="modId"]').fill(fixtureMod);await page.locator('[data-build="mapFolder"]').fill(fixtureMod);await page.locator('[data-build="modName"]').fill('Transport fixture');
 await page.locator('[data-build="confirm"]').check();
 const installation=page.waitForResponse(r=>r.request().method()==='POST'&&r.url().includes(`/api/export/jobs/${jobId}/download?install=true`));
 await page.locator('[data-build="install"]').click();const installedResponse=await installation;assert.ok(installedResponse.ok());const installed=await installedResponse.json();
 assert.match(installed.modsDirectory.replaceAll('\\','/'),/work\/knoxmap-research\/transport-isolated\/mods$/);
 await page.screenshot({path:fileURLToPath(new URL('transport-completed.png',out)),fullPage:true});assert.deepEqual(errors,[]);
 const closed=structuredClone(source);closed.features.find(f=>f.id==='station').properties={'disused:amenity':'fuel',name:'Closed source station'};
 await page.locator('#osm-world-source-file').setInputFiles({name:'transport-closed.geojson',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(closed))});
 await page.waitForFunction(()=>window.importer.source.saved&&!window.importer.sourceBusy);
 await page.locator('#osm-world-start').click();await page.waitForFunction(()=>!window.importer.busy&&window.importer.result,{},{timeout:120000});
 const closedImport=await page.evaluate(()=>window.importer.result),closedManifest=await get(`/api/world/workspaces/${closedImport.workspaceId}`);
 const closedProject=(await get(`/api/world/workspaces/${closedImport.workspaceId}/sections/${closedManifest.sections[0].id}`)).member.project;
 assert.equal(closedProject.decorations.filter(d=>d.id.startsWith('fuel-')).length,0,'Closed parent station created a live mapped child pump');
 assert.ok(closedProject.sourceFeatures.some(f=>f.tags['disused:amenity']==='fuel'),'Closed site context/evidence disappeared');assert.deepEqual(errors,[]);
 await writeFile(new URL('api-browser-proof.json',out),JSON.stringify({status:'passed',workspace:imported.workspaceId,revision:manifest.revision,roadWidth:road.width,widthOrigin:road.sourceTags['pzmap:width-source'],parkingRows:rows.length,stalls,pumps:pumps.length,sha256:createHash('sha256').update(bytes).digest('hex'),bytes:bytes.length,browserErrors:errors,streamedExport:true,closedParentPointSuppressed:true,sourceLifecycleRetained:true,isolatedInstall:true,modsDirectory:installed.modsDirectory,fixtureMod,clientCarsRefuelling:'not tested'},null,2));
 console.log(`PASS transport import: 7.4m lane-derived road, ${stalls} contained stalls/${rows.length} native rows, ${pumps.length} native pump, section workspace, streamed compile, no browser errors.`);
} finally {await browser.close();}
