// Runs only against a disposable editor workspace; never opens the user's live tab.
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48811';
assert.equal(new URL(base).hostname,'127.0.0.1');assert.notEqual(new URL(base).port,'48779');
const output=new URL('../work/knoxmap-research/',import.meta.url);await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});
try {
 const context=await browser.newContext({viewport:{width:1400,height:1000}}),page=await context.newPage(),errors=[],calls=[];
 page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.url().startsWith(base+'/api/'))calls.push({path:new URL(r.url()).pathname,method:r.method()});});
 const fixture=`<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/style.css"><link rel="stylesheet" href="/osm-world-import.css"><link rel="stylesheet" href="/world-build-output.css"><link rel="stylesheet" href="/export-jobs.css"><button id="world-open">Open world</button><input id="import-lat" value="50.001"><input id="import-lon" value="30.001"><input id="import-extent" value="200"><script type="module">import {OsmWorldImport} from '/osm-world-import.js';window.importer=new OsmWorldImport({getWorld:()=>window.testWorld??null,acceptWorld:()=>{},acceptWorkspace:()=>{},getSelection:()=>null});</script>`;
 await page.route(base+'/__knoxmap-fixture',route=>route.fulfill({contentType:'text/html',body:fixture}));
 await page.goto(base+'/__knoxmap-fixture');await page.locator('#world-import-osm').click();
 await page.locator('#osm-world-advanced').evaluate(n=>n.open=true);await page.locator('#osm-world-size').selectOption('256');
 const request=await page.evaluate(()=>window.importer.request()),b=request.bounds,mx=111320*Math.cos((b.north+b.south)/2*Math.PI/180),ll=(x,y)=>[b.west+x/mx,b.north-y/111320],a=-11.22*Math.PI/180;
 const source={type:'FeatureCollection',features:[
  {type:'Feature',id:'street',properties:{highway:'residential'},geometry:{type:'LineString',coordinates:[ll(30,100),ll(30+140*Math.cos(a),100+140*Math.sin(a))]}},
  {type:'Feature',id:'house',properties:{building:'house','building:levels':'1'},geometry:{type:'Polygon',coordinates:[[[65,50],[80,50],[80,65],[65,65],[65,50]].map(p=>ll(...p))]}}
 ]};
 await page.locator('#osm-world-source').selectOption('file');await page.locator('#osm-world-source-file').setInputFiles({name:'grid-fixture.geojson',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(source))});
 await page.waitForFunction(()=>window.importer.source.saved&&!window.importer.sourceBusy);
 await page.locator('#osm-world-grid-detect').click();await page.waitForFunction(()=>!window.importer.busy&&window.importer.gridReview);
 const measured=await page.evaluate(()=>window.importer.gridReview);assert.ok(Math.abs(measured.alignment.rotationDegrees-11.22)<1e-6);
 assert.equal(await page.locator('#osm-world-rotation').inputValue(),'0');
 await page.screenshot({path:fileURLToPath(new URL('street-grid-review.png',output)),fullPage:true});
 await page.locator('#osm-world-grid-apply').click();assert.ok(Math.abs(Number(await page.locator('#osm-world-rotation').inputValue())-11.22)<1e-6);
 assert.equal(await page.locator('#osm-world-grid-apply').isVisible(),false);
 const planRequest=page.waitForRequest(r=>r.url()===base+'/api/world/osm/plan');await page.locator('#osm-world-plan').click();const planned=(await planRequest).postDataJSON();
 await page.waitForFunction(()=>!window.importer.busy);assert.equal(planned.sourceMode,'file');assert.equal(planned.sourceId,measured.sourceId);
 const shape=(x,y)=>({type:'Polygon',coordinates:[[[x,y],[x+15,y],[x+15,y+15],[x,y+15],[x,y]].map(p=>ll(...p))]});
 const supplement={type:'FeatureCollection',features:[
  {type:'Feature',properties:{id:'existing',type:'building',class:'residential'},geometry:shape(65,50)},
  {type:'Feature',properties:{id:'missing',type:'building',class:'residential',num_floors:2,names:{primary:'Overture fixture'}},geometry:shape(110,35)}
 ]};
 await page.locator('#osm-world-overture').evaluate(n=>n.open=true);
 await page.locator('#osm-world-overture-file').setInputFiles({name:'overture-fixture.geojson',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(supplement))});
 const supplemented=page.waitForResponse(r=>r.url()===base+'/api/world/osm/supplement/overture');
 await page.locator('#osm-world-overture-add').click();const added=await (await supplemented).json();assert.equal(added.added,1);assert.equal(added.existing,1);
 await page.waitForFunction(()=>!window.importer.busy);assert.equal(await page.locator('#osm-world-grid-apply').isVisible(),false);
 const composeRequest=page.waitForRequest(r=>r.url()===base+'/api/world/osm/jobs');await page.locator('#osm-world-start').click();assert.equal((await composeRequest).postDataJSON().sourceId,added.source.id);
 await page.waitForFunction(()=>!window.importer.busy&&window.importer.result,{},{timeout:120000});
 let imported=await page.evaluate(()=>window.importer.result);assert.equal(imported.resultKind,'sections');assert.equal(imported.workspaceRevision,1);
 // A second editor changes this workspace between import review and compile.
 const manifest=await (await context.request.get(base+`/api/world/workspaces/${imported.workspaceId}`)).json();
 const sectionId=manifest.sections[0].id,section=await (await context.request.get(base+`/api/world/workspaces/${imported.workspaceId}/sections/${sectionId}`)).json();
 section.member.project.name+=' edited';
 const edited=await context.request.post(base+`/api/world/workspaces/${imported.workspaceId}/sections/${sectionId}`,{data:{project:section.member.project,expectedRevision:manifest.revision}});assert.ok(edited.ok());
 const rejected=page.waitForResponse(r=>new URL(r.url()).pathname===`/api/world/workspaces/${imported.workspaceId}/export/jobs`);
 await page.locator('[data-build="compile"]').click();assert.equal((await rejected).status(),409);assert.equal(await page.locator('[data-build="download"]').isVisible(),false);
 // A fresh import publishes a distinct accepted revision rather than silently taking the edit.
 await page.locator('#osm-world-seed').fill('43');await page.locator('#osm-world-seed').press('Tab');await page.locator('#osm-world-start').click();
 await page.waitForFunction(()=>!window.importer.busy&&window.importer.result,{},{timeout:120000});imported=await page.evaluate(()=>window.importer.result);
 assert.equal(await page.locator('.world-build-output').isVisible(),true);
 const queueRequest=page.waitForRequest(r=>new URL(r.url()).pathname===`/api/world/workspaces/${imported.workspaceId}/export/jobs`);
 await page.locator('[data-build="compile"]').click();const queued=await queueRequest,job=await (await queued.response()).json();
 assert.equal(queued.postData(),'{}');assert.equal(new URL(queued.url()).searchParams.get('expectedRevision'),String(imported.workspaceRevision));assert.equal(job.revision,String(imported.workspaceRevision));
 const read=async path=>{const response=await context.request.get(base+path);assert.ok(response.ok(),await response.text());return response.json();};
 let native;for(let i=0;i<240;i++){native=await read('/api/export/jobs/'+job.id);if(native.status==='completed')break;assert.ok(['queued','running'].includes(native.status),native.error);await page.waitForTimeout(250);}
 assert.equal(native.status,'completed');
 await page.locator('[data-build="compile"]').click();await page.locator('[data-build="download"]:visible').waitFor();
 assert.equal(await page.locator('[data-build="install"]').isEnabled(),false);
 // Tests write only to this disposable server's configured PZMAP_MODS_DIR.
 const modId='KnoxPipelineFixture_'+job.id.slice(0,8);
 await page.locator('[data-build="modId"]').fill(modId);await page.locator('[data-build="mapFolder"]').fill(modId);await page.locator('[data-build="modName"]').fill('Knox pipeline fixture');
 assert.equal(await page.locator('[data-build="install"]').isEnabled(),false);
 await page.locator('[data-build="confirm"]').check();
 const installedResponse=page.waitForResponse(r=>r.url().includes(`/api/export/jobs/${job.id}/download?install=true`));await page.locator('[data-build="install"]').click();
 const installed=await (await installedResponse).json();assert.match(installed.modsDirectory.replaceAll('\\','/'),/work\/knoxmap-research\/isolated\/mods$/);
 assert.ok((await page.locator('[data-build="state"]').innerText()).includes('Installed')||await page.locator('[data-build="confirm"]').isChecked()===false);
 const archive=await context.request.get(base+`/api/export/jobs/${job.id}/download`);assert.ok(archive.ok());await writeFile(new URL('pipeline-fixture-B42.zip',output),await archive.body());
 assert.ok(!calls.some(c=>c.path===`/api/world/workspaces/${imported.workspaceId}/project`),'Compile loaded a complete world');
 const blocked=await context.request.post(base+'/api/world/osm/alignment',{headers:{Origin:'https://untrusted.example'},data:planned});assert.equal(blocked.status(),403);
 const incomplete={type:'way',id:2,tags:{highway:'residential'},geometry:[{lon:b.west+.0001,lat:b.south+.0001},{}]},good={...incomplete,id:1,geometry:[{lon:b.west+.0001,lat:b.south+.0001},{lon:b.east-.0001,lat:b.south+.0001}]};
 const damaged=await (await context.request.post(base+'/api/osm/sources',{data:{name:'incomplete.json',bounds:b,json:JSON.stringify({elements:[good,incomplete]})}})).json();
 const incompleteReview=await context.request.post(base+'/api/world/osm/alignment',{data:{...planned,sourceMode:'file',sourceId:damaged.id}});assert.equal(incompleteReview.status(),400);assert.match((await incompleteReview.json()).error,/incomplete geometr/i);
 assert.ok(calls.some(c=>c.path==='/api/world/osm/alignment'&&c.method==='POST'));assert.deepEqual(errors,[]);
 await writeFile(new URL('street-grid-browser-proof.json',output),JSON.stringify({status:'passed',rotation:planned.rotationDegrees,sourceId:planned.sourceId,explicitApply:true,reusedMeasuredSource:true,errors,calls},null,2));
 await writeFile(new URL('grid-fixture.geojson',output),JSON.stringify(source));await writeFile(new URL('grid-request.json',output),JSON.stringify(planned));
 await writeFile(new URL('pipeline-browser-proof.json',output),JSON.stringify({status:'passed',sourceId:added.source.id,supplement:{added:added.added,existing:added.existing},workspaceId:imported.workspaceId,workspaceRevision:imported.workspaceRevision,exportId:job.id,streamedCompile:true,staleWorkspaceRejected:true,incompleteSourceRejected:true,installed,isolatedDestination:true,errors,calls},null,2));
 await page.waitForFunction(()=>[...document.querySelectorAll('.export-jobs article p')].some(p=>p.textContent.includes('completed')));
 await page.screenshot({path:fileURLToPath(new URL('pipeline-completed.png',output)),fullPage:true});
 console.log('PASS area → rotated streets → supplemented furnished buildings → streamed native compile → explicit isolated install; no browser errors.');
} finally {await browser.close();}
