// Real cached Moscow source: 36 linked maps, durable index, isolated browser.
// Start the job separately; this verifier never changes the user's editor tab.
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48777';
const out=new URL('../work/moscow-linked-world/',import.meta.url);await mkdir(out,{recursive:true});
const job=JSON.parse(await readFile(new URL('../work/moscow-fixed-job.json',import.meta.url),'utf8'));
const get=async path=>{const r=await fetch(base+path,{signal:AbortSignal.timeout(120000)});assert.ok(r.ok,`${path}: HTTP ${r.status} ${await (r.ok?Promise.resolve(''):r.text())}`);return r.json();};
let state,last='',deadline=Date.now()+20*60*1000;
do{
 state=await get('/api/world/osm/jobs/'+job.id);const phase=`${state.status}: ${state.progress.phase} ${state.progress.completed}/${state.progress.total}`;
 if(phase!==last){console.log(phase);last=phase;}
 if(state.status!=='running'&&state.status!=='queued')break;
 assert.ok(Date.now()<deadline,'Real import exceeded verification wait');await new Promise(r=>setTimeout(r,5000));
}while(true);
await writeFile(new URL('status.json',out),JSON.stringify(state,null,2));
assert.equal(state.status,'completed',state.error);assert.equal(state.resultKind,'sections');assert.match(state.workspaceId,/^[a-f0-9]{32}$/);
const id=state.workspaceId,manifest=await get('/api/world/workspaces/'+id);
assert.equal(manifest.sections.length,36);assert.equal(manifest.storageId,id);assert.equal(manifest.format,'SimplePZWorldSections');
const occupied=new Set();let cells=0;
for(let i=0;i<manifest.sections.length;i++){
 const s=manifest.sections[i];assert.equal(s.width,1024);assert.equal(s.height,1024);assert.equal(s.worldX,100+(i%6)*4);assert.equal(s.worldY,100+Math.floor(i/6)*4);
 for(let y=s.worldY;y<s.worldY+4;y++)for(let x=s.worldX;x<s.worldX+4;x++){const key=x+','+y;assert.ok(!occupied.has(key),'Overlapping native cells');occupied.add(key);cells++;}
}
assert.equal(cells,576);const bytes=manifest.sections.reduce((sum,s)=>sum+s.bytes,manifest.metadataBytes);assert.ok(bytes>128*1024*1024,'Real output did not exercise the old complete-world cap');
const index=await get('/api/world/osm/jobs/'+job.id+'/result');assert.equal(index.storageId,id);assert.equal(index.sections.length,36);
await writeFile(new URL('manifest.json',out),JSON.stringify(manifest,null,2));
const samples=[];for(const i of [0,17,35]){
 const s=manifest.sections[i],snapshot=await get(`/api/world/workspaces/${id}/sections/${s.id}`);
 assert.equal(snapshot.member.id,s.id);assert.equal(snapshot.member.project.worldX,s.worldX);assert.equal(snapshot.member.project.worldY,s.worldY);
 assert.equal(snapshot.shared.geoReference.metresPerTile,1);assert.equal(snapshot.shared.geoReference.originCellX,100);assert.equal(snapshot.sections.length,36);
 samples.push({index:i,id:s.id,buildings:snapshot.member.project.buildings.length,sourceFeatures:snapshot.member.project.sourceFeatures.length,sharedBuildings:snapshot.shared.buildings.length,sharedFences:snapshot.shared.fences.length});
}
const proof={jobId:job.id,workspaceId:id,sections:36,cells,tiles:6144,metresPerTile:1,bytes,samples};await writeFile(new URL('proof.json',out),JSON.stringify(proof,null,2));console.log('PASS real linked-world persistence',JSON.stringify(proof));
const browser=await chromium.launch({headless:true,channel:'chrome',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const context=await browser.newContext({viewport:{width:1440,height:1000}}),page=await context.newPage(),requests=[],errors=[];
page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.url().includes('/api/world/workspaces/'))requests.push(r.url());});
for(const path of ['recovery','world/recovery'])await context.route('**/api/'+path+'/**',route=>route.fulfill({status:route.request().method()==='GET'?404:200,json:{saved:true}}));
page.setDefaultTimeout(120000);
try{
 await page.goto(base);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);await page.locator('[data-command-menu="project"]>summary').click();await page.locator('#open-world').click();
 const ws=page.locator('#world-workspaces');await ws.locator('summary').click();await ws.locator('[data-ws-list]').click();await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
 await ws.locator('[data-ws-world]').selectOption(id);await ws.locator('[data-ws-state]').filter({hasText:'36 sections'}).waitFor();
 assert.equal(await ws.locator('[data-ws-section] option').count(),37);assert.match(await page.locator('#world-summary').textContent(),/36 sections.*0 loaded.*576 cells/);
 assert.equal(requests.some(url=>url.includes('/sections/')),false,'Browsing downloaded section rasters');
 assert.equal(await ws.locator('[data-ws-open-world]').isDisabled(),true,'Oversized worlds must not offer complete loading');
 assert.match(await ws.locator('[data-ws-size]').textContent(),/577\.2 MiB.*128 MiB/);
 assert.equal(await ws.locator('[data-ws-size]').evaluate(el=>!!(el.compareDocumentPosition(document.querySelector('[data-ws-open-world]'))&Node.DOCUMENT_POSITION_FOLLOWING)),true,'Explain the limit before the complete-load action');
 assert.equal(await ws.locator('[data-ws-open]').isEnabled(),true);assert.equal(await ws.locator('[data-ws-export-world]').isEnabled(),true);
 await ws.locator('[data-ws-open-world]').dispatchEvent('click');await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
 assert.equal(requests.some(url=>url.endsWith('/world')),false,'A forced oversized complete-load action fetched the world');
 await ws.locator('[data-ws-open-world]').scrollIntoViewIfNeeded();await page.screenshot({path:fileURLToPath(new URL('index.png',out))});
 await ws.locator('[data-ws-open]').click();await page.locator('#world-dialog').waitFor({state:'hidden'});assert.match(await page.locator('#world-editing-label').textContent(),/Workspace: Moscow 6 km verification/);
 assert.equal(requests.filter(url=>url.includes('/sections/')).length,1,'Opening one section downloaded its neighbours');
 assert.deepEqual(errors,[]);await writeFile(new URL('browser.json',out),JSON.stringify({sections:36,loaded:1,sectionRequests:1,errors},null,2));console.log('PASS real editor browses 36-map index without rasters, then explicitly opens one section');
}finally{await context.close();await browser.close();}
