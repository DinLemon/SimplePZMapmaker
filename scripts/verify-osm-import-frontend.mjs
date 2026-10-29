// Isolated Chrome fixture: local JS/CSS and stubbed API routes only.
// No Debug server, external downloads or existing editor session is needed.
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {resolve,relative,isAbsolute,sep} from 'node:path';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48778';
const root=new URL('../src/SimplePZMapmaker.Web/wwwroot/',import.meta.url);
const workspace=resolve(fileURLToPath(new URL('../',import.meta.url))),outputDirectory=resolve(workspace,'work','osm-frontend-qa');
const outputRelative=relative(workspace,outputDirectory);
assert.ok(outputRelative&&outputRelative!=='..'&&!outputRelative.startsWith('..'+sep)&&!isAbsolute(outputRelative),'QA output must stay inside this workspace');
const html=await readFile(new URL('index.html',root),'utf8'),app=await readFile(new URL('app.js',root),'utf8');
const dialog=html.slice(html.indexOf('<dialog id="import-dialog"'),html.indexOf('</dialog>',html.indexOf('<dialog id="import-dialog"'))+9);
const wiring=app.slice(app.indexOf("$('open-import').onclick="),app.indexOf("$('export-map').onclick="));
const worldWiring=app.split(/\r?\n/).find(line=>line.startsWith('const osmWorldImport=new OsmWorldImport('));
assert.ok(worldWiring,'Use the production world import callbacks in the fixture');
assert.ok(dialog.includes('import-source-file'));assert.ok(wiring.includes('singleOsmSource.ensure'));
const fixtureHtml=`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/style.css"><link rel="stylesheet" href="/osm-world-import.css"><button id="open-import">Import a place</button><button id="world-open">Open world</button><button id="open-world">World workshop</button><dialog id="world-dialog"><div class="world-inspector"></div></dialog><p id="busy-message"></p><input id="geodata-file" type="file" hidden>${dialog}<script type="module" src="/__osm-fixture.js"></script>`;
const fixtureJs=`import {OsmWorldImport} from '/osm-world-import.js';import {OsmSourceSelection} from '/osm-source.js';import {blank,geographicBase,boundsFromSearchResult} from '/editor.js';
const $=id=>document.getElementById(id);let busy=false,project=blank(512),selectedPlaceBounds=null;const history={past:[]};
const state={world:{id:'original',name:'Original world',maps:[],geoReference:null},adoptions:0,applications:0,appliedCallbacks:0,errors:[]};
function adopt(p){project=p;state.adoptions++;}function toast(){};
async function api(url,body,signal){const r=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal});const data=await r.json();if(!r.ok)throw Error(data.error);return data;}
let taskController;async function task(message,fn){if(busy)return;busy=true;taskController=new AbortController();try{await fn(taskController.signal);}catch(e){state.errors.push(e.message);}finally{busy=false;}}
const worldWorkshop={get world(){return state.world;},editedId:null,current:()=>project,task,update(){},fit(){},acceptWorld(w){state.world=w;state.applications++;}};
const {WorldWorkspaces}=await import('/world-workspaces.js');worldWorkshop.workspaces=new WorldWorkspaces(worldWorkshop);
$('open-world').onclick=()=>$('world-dialog').showModal();
${worldWiring}
const onApplied=osmWorldImport.onApplied;osmWorldImport.onApplied=async()=>{if($('osm-world-dialog').open)throw Error('Callback must run after closing the import dialog.');await onApplied?.();state.appliedCallbacks++;};
${wiring}
document.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>$(b.dataset.close).close());
window.fixture={ui:osmWorldImport,state,workspaces:worldWorkshop.workspaces,source:singleOsmSource,abortTask:()=>taskController.abort(),get project(){return project;},get busy(){return busy;},get sourceBusy(){return singleOsmSourceBusy;},setWorld(w){state.world=w;},snapshot:()=>JSON.stringify({project,world:state.world})};`;

const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL||'chrome'});
const context=await browser.newContext({viewport:{width:1400,height:1000},acceptDownloads:true});
const page=await context.newPage(),errors=[],requests=[],uploads=[],jobs=[];let outcome='completed',failPlan=false,failMap=false,failRegions=false,truncateMap=false,jobNumber=0,slowPlan=null,slowCreate=null,slowResult=null,cancelled=false,failCancel=false,retainedFailure=false,jobState=null;
const workspaceId='abcde0123456789fabcde0123456789f',sectionId='0123456789abcdef0123456789abcdef';
const sectionIndex={format:'SimplePZWorldSections',version:1,id:workspaceId,revision:1,name:'Imported sections',sections:[{id:sectionId,name:'Imported section',bytes:1024,worldX:100,worldY:100,width:512,height:512}]};
let failWorkspace=false,slowStatus=null;
page.on('pageerror',e=>errors.push(e.stack||e.message));page.setDefaultTimeout(10000);
page.on('console',message=>{if(message.type()==='error')console.error('Browser:',message.text());});
page.on('requestfailed',request=>console.error('Request failed:',request.url(),request.failure()?.errorText));
await context.route(base+'/**',async route=>{
 const path=decodeURIComponent(new URL(route.request().url()).pathname),target=resolve(fileURLToPath(root),'.'+path),within=relative(fileURLToPath(root),target);
 if(!within||within==='..'||within.startsWith('..'+sep)||isAbsolute(within)||!/[.](js|css|json)$/.test(target))return route.abort();
 return route.fulfill({body:await readFile(target),contentType:target.endsWith('.css')?'text/css':target.endsWith('.json')?'application/json':'text/javascript'});
});
await context.route('https://www.openstreetmap.org/**',r=>r.fulfill({body:'<p>OSM preview fixture</p>',contentType:'text/html'}));
await context.route(base+'/__osm-import-fixture',r=>r.fulfill({body:fixtureHtml,contentType:'text/html'}));
await context.route(base+'/__osm-fixture.js',r=>r.fulfill({body:fixtureJs,contentType:'text/javascript'}));
await context.route('**/api/**',async route=>{
 const request=route.request(),url=new URL(request.url()),path=url.pathname,body=request.method()==='POST'?request.postDataJSON():null;
 requests.push({path,body});
 const ok=data=>route.fulfill({json:data}),bad=error=>route.fulfill({status:400,json:{error}});
 if(path==='/api/search')return ok([{display_name:'<img src=x onerror=alert(1)> Test place',lat:'54.78',lon:'32.04',boundingbox:['54.77','54.79','32.03','32.05']}]);
 if(path==='/api/osm/regions/plan')return failRegions?bad('Geofabrik catalogue unavailable.'):ok({regions:[{id:'europe/germany',name:'Germany',bytes:2*1024**3,cached:false}],downloadBytes:2*1024**3,maximumFileBytes:8*1024**3,maximumCacheBytes:16*1024**3,largeFirstDownload:true,coverage:'complete'});
 if(path==='/api/osm/sources'){
  uploads.push(body);return ok({id:'source-'+uploads.length,bounds:body.bounds,source:'local file',format:body.pbfBase64?'pbf':'json',bytes:32,features:2,dataTimestamp:'2026-10-20T00:00:00Z',retrievedAtUtc:'2026-10-22T00:00:00Z',warnings:[]});
 }
 if(/^\/api\/osm\/sources\/[^/]+\/download$/.test(path))return route.fulfill({body:'{"elements":[]}',contentType:'application/json',headers:{'Content-Disposition':'attachment; filename="osm-source.json"'}});
 if(path==='/api/world/osm/plan'){
  if(slowPlan)await slowPlan;
  if(failPlan)return bad('The area exceeds the import budget.');
  if(body.sourceMode==='file'&&!body.sourceId)return bad('Choose a valid saved geographic source.');
  return ok({columns:2,rows:1,width:1024,height:512,cells:8});
 }
 if(path==='/api/world/osm/jobs'){
  if(slowCreate)await slowCreate;
  jobs.push(body);cancelled=false;return ok({id:'job-'+(++jobNumber)});
 }
 if(path.endsWith('/cancel')){if(failCancel)return bad('Cancellation service temporarily unavailable.');cancelled=true;return ok({status:'cancelled'});}
 if(path==='/api/world/workspaces')return failWorkspace?bad('Workspace index temporarily unavailable.'):ok([sectionIndex]);
 if(path==='/api/world/workspaces/'+workspaceId)return ok(sectionIndex);
 if(/^\/api\/world\/osm\/jobs\/[^/]+\/result$/.test(path)){
  if(slowResult)await slowResult;return ok({id:'ready',name:'Ready world',maps:[],geoReference:null});
 }
 if(/^\/api\/world\/osm\/jobs\/[^/]+$/.test(path)){if(slowStatus)await slowStatus;return ok({sourceId:retainedFailure?null:'downloaded-source',retainedRegionalSource:retainedFailure,progress:{phase:outcome==='running'?'region-download':'compose',completed:1,total:2,bytesReceived:outcome==='running'?1024:undefined,totalBytes:outcome==='running'?2048:undefined,source:'local fixture',dataTimestamp:'2026-10-20T00:00:00Z'},warnings:['Snapshot uses original date: 2026-10-20.'],error:outcome==='failed'?(retainedFailure?'Regional PBF downloaded, but local extraction exceeded its limit. Complete source retained for retry.':'Composition failed after source download.'):null,...jobState,status:cancelled?'cancelled':outcome});}
 if(path==='/api/import/osm/stream')return route.fulfill({contentType:'application/x-ndjson',body:JSON.stringify({kind:'progress',progress:{phase:'pbf-index',source:'Germany',completed:1,total:2}})+'\n'+(truncateMap?'':JSON.stringify(failMap?{kind:'error',error:'First source area unavailable.'}:{kind:'result',result:{project:{...body.project,name:'Imported'},importedFeatures:2,warnings:[],sourceId:'map-source'}})+'\n')});
 if(path==='/api/import/geojson')return ok({project:{...body.project,name:'GeoJSON'},importedFeatures:1,warnings:[]});
 return bad('Unexpected API request in isolated fixture: '+path);
});
const state=()=>page.locator('#osm-world-state').textContent();
const waitIdle=()=>page.waitForFunction(()=>!window.fixture.ui.busy&&!window.fixture.ui.sourceBusy&&!window.fixture.busy&&!window.fixture.sourceBusy);
const openWorld=async()=>{await page.locator('#open-import').click();await page.locator('#import-world-button').click();};
const startReady=async()=>{await page.locator('#osm-world-start').click();await page.locator('#osm-world-apply').waitFor({state:'visible'});await waitIdle();};
const jsonFile={name:'offline.json',mimeType:'application/json',buffer:Buffer.from('{"elements":[]}')};
const screenshots=process.env.PZMAP_TEST_SCREENSHOTS!=='0';
try{
 await page.goto(base+'/__osm-import-fixture');await page.waitForFunction(()=>window.fixture);
 const original=await page.evaluate(()=>window.fixture.snapshot());
 await openWorld();assert.equal(await page.locator('#osm-world-source').inputValue(),'auto');assert.equal(await page.locator('#osm-world-width').inputValue(),await page.locator('#osm-world-height').inputValue());
 // Guided form: presets never request data or change the open project; layout responds locally.
 assert.equal(await page.locator('#osm-world-preserve-row').isVisible(),false);
 assert.equal(await page.locator('#osm-world-coordinates').evaluate(d=>d.open),false);assert.equal(await page.locator('#osm-world-advanced').evaluate(d=>d.open),false);
 const requestsBeforePresets=requests.length;
 await page.locator('[data-osm-area="2"]').click();assert.equal(await page.locator('#osm-world-width').inputValue(),'2');assert.equal(await page.locator('#osm-world-height').inputValue(),'2');
 assert.equal(await page.locator('[data-osm-area="2"]').getAttribute('aria-pressed'),'true');assert.equal(await page.locator('#osm-world-layout-grid .osm-world-grid-section').count(),4);
 await page.locator('#osm-world-scale').fill('2');assert.equal(await page.locator('#osm-world-layout-grid .osm-world-grid-section').count(),1);
 assert.match(await page.locator('#osm-world-size-preview').textContent(),/16 native cells/);assert.equal(requests.length,requestsBeforePresets);
 await page.locator('#osm-world-width').fill('100');assert.equal(await page.locator('#osm-world-start').isDisabled(),true);assert.equal(await page.locator('#osm-world-plan').isDisabled(),true);assert.equal(await page.locator('#osm-world-layout-grid').isVisible(),false);assert.match(await state(),/longitude/);
 await page.locator('#osm-world-scale').fill('1');await page.locator('[data-osm-area="1"]').click();assert.equal(await page.locator('#osm-world-start').isEnabled(),true);assert.equal(await page.locator('#osm-world-layout-grid').isVisible(),true);assert.equal(requests.length,requestsBeforePresets);
 assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);
 if(screenshots){await mkdir(outputDirectory,{recursive:true});await page.locator('#osm-world-dialog').evaluate(d=>d.scrollTop=0);await page.screenshot({path:resolve(outputDirectory,'world-desktop.png')});}
 await page.locator('#osm-world-name').fill('Unstarted custom name');await page.locator('#osm-world-close').click();await openWorld();assert.equal(await page.locator('#osm-world-name').inputValue(),'Unstarted custom name');
 await page.locator('#osm-world-close').click();await page.locator('#open-import').click();await page.locator('#import-extent').selectOption('1500');await page.locator('#import-world-button').click();assert.equal(await page.locator('#osm-world-width').inputValue(),'1.5');
 await page.locator('#osm-world-query').fill('Test place');await page.locator('#osm-world-query').press('Enter');await page.locator('#osm-world-results button').waitFor();
 assert.equal(await page.locator('#osm-world-results img').count(),0);assert.match(await page.locator('#osm-world-results button').textContent(),/<img/);assert.equal(await page.locator('#osm-world-dialog').evaluate(d=>d.open),true);
 await page.locator('#osm-world-results button').click();assert.equal(await page.locator('#osm-world-lat').inputValue(),'54.78');
 await page.locator('#osm-world-width').fill('1.5');await page.locator('#osm-world-height').fill('.8');
 await page.locator('#osm-world-preview').click();assert.match(await page.locator('#osm-world-preview-map').getAttribute('src'),/bbox=/);assert.equal(await page.locator('#osm-world-preview-link').isVisible(),true);
 await page.locator('#osm-world-dialog [data-osm-region-plan]').click();await waitIdle();assert.match(await page.locator('#osm-world-source-state').textContent(),/Germany.*2 GiB/);assert.match(await page.locator('#osm-world-source-state').textContent(),/8 GiB.*16 GiB/);
 assert.match(await page.locator('#osm-world-download-summary').textContent(),/Germany.*2 GiB.*larger than your area/);
 const regionsChecked=requests.filter(r=>r.path==='/api/osm/regions/plan').length;await page.locator('#osm-world-dialog [data-osm-region-plan]').click();await waitIdle();assert.equal(requests.filter(r=>r.path==='/api/osm/regions/plan').length,regionsChecked);
 await page.locator('#osm-world-source').selectOption('global');failRegions=true;const jobsBeforeRegions=jobs.length;await page.locator('#osm-world-start').click();await waitIdle();assert.match(await state(),/catalogue unavailable/);assert.equal(jobs.length,jobsBeforeRegions);
 await page.locator('#osm-world-source').selectOption('auto');await page.locator('#osm-world-width').fill('1.51');await startReady();assert.equal(jobs.at(-1).sourceMode,'auto');assert.match(await page.locator('#osm-world-source-state').textContent(),/compatible saved snapshot/);failRegions=false;await page.locator('#osm-world-width').fill('1.5');
 failPlan=true;const jobsBefore=jobs.length;await page.locator('#osm-world-plan').click();await waitIdle();assert.match(await state(),/budget/);assert.equal(await page.locator('#osm-world-offline').isVisible(),true);assert.equal(jobs.length,jobsBefore);await page.locator('#osm-world-start').click();await waitIdle();assert.match(await state(),/budget/);assert.equal(jobs.length,jobsBefore);failPlan=false;
 await startReady();assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);assert.equal(jobs.at(-1).sourceMode,'auto');assert.match(await page.locator('#osm-world-size-preview').textContent(),/native cells/);
 const planIndex=requests.findLastIndex(r=>r.path==='/api/world/osm/plan'),jobIndex=requests.findLastIndex(r=>r.path==='/api/world/osm/jobs');assert.ok(planIndex<jobIndex);
 await page.locator('#osm-world-close').click();await openWorld();assert.equal(await page.locator('#osm-world-apply').isVisible(),true);
 await page.locator('#osm-world-name').fill('Changed settings');assert.equal(await page.locator('#osm-world-apply').isVisible(),false);await page.evaluate(()=>window.fixture.ui.apply());assert.equal(await page.evaluate(()=>window.fixture.state.applications),0);
 await page.locator('#osm-world-source').selectOption('file');await page.locator('#osm-world-source-file').setInputFiles(jsonFile);await waitIdle();assert.equal(uploads.length,1);assert.equal(await page.locator('#osm-world-source').isEnabled(),true);
 await page.locator('#osm-world-width').fill('1.6');assert.match(await page.locator('#osm-world-download-summary').textContent(),/selected; check the import/);await startReady();assert.equal(uploads.length,2);assert.equal(jobs.at(-1).sourceMode,'file');assert.equal(jobs.at(-1).sourceId,'source-2');assert.deepEqual(jobs.at(-1).bounds,uploads.at(-1).bounds);
 const beforeDownload=await page.evaluate(()=>window.fixture.snapshot()),download=page.waitForEvent('download');await page.locator('#osm-world-source-download').click();await download;assert.equal(await page.evaluate(()=>window.fixture.snapshot()),beforeDownload);
 await page.locator('#osm-world-name').fill('PBF');await page.locator('#osm-world-source-file').setInputFiles({name:'other-region.osm.pbf',mimeType:'application/octet-stream',buffer:Buffer.from([0,1,2,3])});await waitIdle();assert.equal(uploads.at(-1).pbfBase64,'AAECAw==');assert.equal(uploads.at(-1).json,undefined);
 outcome='failed';await page.locator('#osm-world-start').click();await waitIdle();assert.match(await state(),/Composition failed/);assert.match(await page.locator('#osm-world-source-download').getAttribute('href'),/downloaded-source/);assert.equal(await page.locator('#osm-world-source-download').isVisible(),true);assert.equal(await page.locator('#osm-world-apply').isVisible(),false);
 retainedFailure=true;await page.locator('#osm-world-source').selectOption('auto');await page.locator('#osm-world-start').click();await waitIdle();assert.match(await state(),/Complete source retained/);assert.doesNotMatch(await state(),/Choose a local/);assert.equal(await page.locator('#osm-world-offline').isVisible(),false);assert.equal(await page.locator('#osm-world-start').textContent(),'Retry downloaded source');assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);retainedFailure=false;
 // The 60-minute Moscow timeout retains a regional cache far larger than the upload limit.
 const partial={phase:'region-download',bytesReceived:729*1024**2,totalBytes:841*1024**2,source:'Moscow'},timeout='Import exceeded its source-processing deadline. Partial source downloads can be resumed; the original world was preserved.';
 const failJob=async status=>{jobState=status;outcome='failed';await page.locator('#osm-world-start').click();await waitIdle();};
 const assertResume=async()=>{
  assert.equal(await page.locator('#osm-world-start').textContent(),'Resume regional download');
  assert.equal(await page.locator('#osm-world-offline').isVisible(),false);assert.equal(await page.locator('#osm-world-apply').isVisible(),false);
  assert.match(await state(),/729 MiB.*retained/);assert.match(await state(),/current world is unchanged/i);assert.doesNotMatch(await state(),/choose a local|unavailable|reupload/i);
  assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);
 };
 await failJob({recovery:'resume-download',sourceId:null,progress:partial,error:timeout});await assertResume();assert.match(await state(),/841 MiB/);
 // Closing and reopening keeps the recovery action; a settings edit invalidates it.
 await page.locator('#osm-world-close').click();await openWorld();await assertResume();
 await page.locator('#osm-world-name').fill('Timeout recovery');assert.equal(await page.locator('#osm-world-start').textContent(),'Download and build world');
 // Older servers omit recovery (or send null); only incomplete timeout downloads qualify.
 for(const status of [
  {sourceId:null,progress:partial,error:timeout},
  {recovery:null,sourceId:null,progress:{...partial,totalBytes:undefined},error:'Regional download timed out.'},
  {sourceId:null,progress:{...partial,totalBytes:null},error:'Regional download timeout.'}
 ]){await failJob(status);await assertResume();}
 for(const progress of [{...partial,bytesReceived:0},{...partial,bytesReceived:841*1024**2},{...partial,phase:'compose'}]){
  await failJob({sourceId:null,progress,error:timeout});assert.equal(await page.locator('#osm-world-start').textContent(),'Download and build world');assert.equal(await page.locator('#osm-world-offline').isVisible(),true);
 }
 await failJob({sourceId:null,progress:partial,error:'Disk is full.'});assert.equal(await page.locator('#osm-world-start').textContent(),'Download and build world');
 await failJob({recovery:'retry-processing',sourceId:null,retainedRegionalSource:true,progress:{phase:'pbf-index'},error:'Local extraction deadline exceeded.'});
 assert.equal(await page.locator('#osm-world-start').textContent(),'Retry downloaded source');assert.equal(await page.locator('#osm-world-offline').isVisible(),false);assert.doesNotMatch(await state(),/choose a local|reupload/i);
 // Build retries consume the already prepared source ID without uploads or regional planning.
 await failJob({recovery:'retry-build',sourceId:'retained-build-source',progress:{phase:'compose'},error:'World build deadline exceeded.'});
 assert.equal(await page.locator('#osm-world-start').textContent(),'Retry world build');assert.equal(await page.locator('#osm-world-offline').isVisible(),false);assert.doesNotMatch(await state(),/choose a local|reupload/i);
 failPlan=true;await page.locator('#osm-world-start').click();await waitIdle();assert.equal(await page.locator('#osm-world-start').textContent(),'Retry world build');assert.equal(await page.evaluate(()=>window.fixture.ui.recoverySourceId),'retained-build-source');failPlan=false;
 const uploadsBeforeRetry=uploads.length,regionsBeforeRetry=requests.filter(r=>r.path==='/api/osm/regions/plan').length;
 jobState=null;outcome='completed';await startReady();assert.equal(jobs.at(-1).sourceMode,'file');assert.equal(jobs.at(-1).sourceId,'retained-build-source');assert.equal(uploads.length,uploadsBeforeRetry);assert.equal(requests.filter(r=>r.path==='/api/osm/regions/plan').length,regionsBeforeRetry);assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);
 await page.locator('#osm-world-name').fill('Resume cancellation');await failJob({recovery:'resume-download',sourceId:null,progress:partial,error:timeout});
 // The existing primary action resumes through the automatic cache, and remains cancellable.
 outcome='running';jobState={sourceId:null,progress:partial};const uploadsBeforeResume=uploads.length;
 let releaseResumePlan;slowPlan=new Promise(resolve=>releaseResumePlan=resolve);await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.busy);
 assert.equal(await page.locator('#osm-world-start').textContent(),'Resume regional download');releaseResumePlan();slowPlan=null;
 await page.waitForFunction(()=>window.fixture.ui.job);await page.waitForFunction(()=>document.querySelector('#osm-world-state').textContent.includes('Moscow'));
 assert.equal(jobs.at(-1).sourceMode,'auto');assert.equal(jobs.at(-1).sourceId,undefined);assert.equal(uploads.length,uploadsBeforeResume);
 // At the top of a long form, status, progress and Cancel remain visible in the sticky footer.
 for(const viewport of [{width:1400,height:1000},{width:390,height:844}]){
  await page.setViewportSize(viewport);await page.locator('#osm-world-dialog').evaluate(d=>d.scrollTop=0);
  for(const id of ['osm-world-state','osm-world-progress','osm-world-cancel'])assert.equal(await page.locator('#'+id).evaluate(e=>{const r=e.getBoundingClientRect(),d=document.querySelector('#osm-world-dialog').getBoundingClientRect();return r.height>0&&r.top>=Math.max(0,d.top)&&r.bottom<=Math.min(innerHeight,d.bottom);}),true,id+' must be visible without scrolling');
 }
 await page.setViewportSize({width:1400,height:1000});await page.locator('#osm-world-cancel').click();await waitIdle();assert.match(await state(),/cancel/i);assert.equal(await page.locator('#osm-world-start').textContent(),'Download and build world');assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);jobState=null;outcome='failed';
 await page.locator('#osm-world-source').selectOption('overpass');await page.locator('#osm-world-start').click();await waitIdle();await page.locator('#osm-world-offline').click();assert.equal(await page.locator('#osm-world-source').inputValue(),'file');assert.equal(await page.locator('#osm-world-state').evaluate(e=>e.classList.contains('error')),false);
 outcome='running';await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.job);assert.equal(await page.locator('#osm-world-source').isDisabled(),true);await page.locator('#osm-world-close').click();await openWorld();assert.equal(await page.locator('#osm-world-cancel').isVisible(),true);await page.locator('#osm-world-cancel').click();await waitIdle();assert.equal(await page.locator('#osm-world-apply').isVisible(),false);assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);
 // Cancel while creation is in flight: the eventual ID must still be cancelled.
 outcome='completed';let releaseCreate;slowCreate=new Promise(resolve=>releaseCreate=resolve);await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.creating);await page.locator('#osm-world-cancel').click();releaseCreate();slowCreate=null;await waitIdle();assert.match(await state(),/cancel/i);assert.equal(await page.locator('#osm-world-apply').isVisible(),false);
 // A failed cancellation after job creation must retain monitoring and a retry control.
 outcome='running';failCancel=true;slowCreate=new Promise(resolve=>releaseCreate=resolve);const cancelAttempts=requests.filter(r=>r.path.endsWith('/cancel')).length;
 await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.creating);await page.locator('#osm-world-cancel').click();releaseCreate();slowCreate=null;
 await page.waitForFunction(()=>window.fixture.ui.job&&!window.fixture.ui.cancelling);assert.ok(requests.filter(r=>r.path.endsWith('/cancel')).length>cancelAttempts);assert.equal(await page.locator('#osm-world-cancel').isEnabled(),true);assert.equal(await page.locator('#osm-world-source').isDisabled(),true);
 failCancel=false;await page.locator('#osm-world-cancel').click();await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.ui.job),null);assert.equal(await page.evaluate(()=>window.fixture.snapshot()),original);outcome='completed';
 // A settings change during a plan cannot create a job, even if changed by script.
 let releasePlan;slowPlan=new Promise(resolve=>releasePlan=resolve);const count=jobs.length;await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.busy);await page.evaluate(()=>{const e=document.querySelector('#osm-world-name');e.value='raced';e.dispatchEvent(new Event('input'));});releasePlan();slowPlan=null;await waitIdle();assert.equal(jobs.length,count);
 await startReady();let releaseResult;slowResult=new Promise(resolve=>releaseResult=resolve);await page.locator('#osm-world-apply').click();await page.waitForFunction(()=>window.fixture.ui.applying);await page.evaluate(()=>{const e=document.querySelector('#osm-world-name');e.value='raced result';e.dispatchEvent(new Event('input'));});releaseResult();slowResult=null;await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.state.applications),0);
 await startReady();await page.locator('#osm-world-apply').click();await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.state.applications),1);assert.equal(await page.evaluate(()=>window.fixture.state.appliedCallbacks),1);
 await page.locator('#world-dialog').evaluate(d=>d.close());
 // Oversized results browse real workspace indices through the production app callback.
 // An already edited, dirty section must remain attached to exactly the same project/session.
 await page.evaluate(index=>{const f=window.fixture,w=f.workspaces,s=w.session;Object.assign(s,{workspaceId:'1'.repeat(32),activeId:'2'.repeat(32),manifest:{...index,name:'Active workspace'},shared:{buildings:[]},savedSnapshot:JSON.stringify(f.project)});f.project.name='Unsaved section edits';s.markChanged();f.originalProject=f.project;f.originalSession=s;},sectionIndex);
 const editingSnapshot=()=>page.evaluate(()=>{const f=window.fixture,s=f.workspaces.session;return JSON.stringify({snapshot:f.snapshot(),context:s.context(),dirty:s.dirty,savedSnapshot:s.savedSnapshot,mutations:s.mutations});});
 const edited=await editingSnapshot(),applications=await page.evaluate(()=>window.fixture.state.applications),callbacks=await page.evaluate(()=>window.fixture.state.appliedCallbacks);
 const workspaceStart=requests.length;
 jobState={resultKind:'sections',workspaceId};await openWorld();await startReady();
 assert.equal(await page.locator('#osm-world-apply').textContent(),'Browse imported sections');assert.match(await page.locator('#osm-world-safety').textContent(),/current.*(unchanged|preserved)/i);assert.doesNotMatch(await page.locator('#osm-world-safety').textContent(),/replaces/);
 await page.locator('#osm-world-close').click();await openWorld();assert.equal(await page.locator('#osm-world-apply').textContent(),'Browse imported sections');
 failWorkspace=true;await page.locator('#osm-world-apply').click();await waitIdle();assert.match(await state(),/Workspace index temporarily unavailable/);assert.equal(await page.locator('#osm-world-apply').isEnabled(),true);assert.equal(await editingSnapshot(),edited);failWorkspace=false;
 await page.locator('#osm-world-apply').click();await waitIdle();
 assert.equal(await page.locator('#world-dialog').evaluate(d=>d.open),true);assert.equal(await page.locator('#world-workspaces').evaluate(d=>d.open),true);
 assert.equal(await page.locator('[data-ws-world]').inputValue(),workspaceId);assert.equal(await page.locator('[data-ws-section]').inputValue(),sectionId);
 assert.match(await page.locator('[data-ws-state]').textContent(),/Browsing does not replace/);
 assert.equal(await editingSnapshot(),edited);assert.equal(await page.evaluate(()=>window.fixture.project===window.fixture.originalProject&&window.fixture.workspaces.session===window.fixture.originalSession),true);
 assert.equal(await page.evaluate(()=>window.fixture.state.applications),applications);assert.equal(await page.evaluate(()=>window.fixture.state.appliedCallbacks),callbacks+1);
 assert.deepEqual(requests.slice(workspaceStart).filter(r=>r.path.startsWith('/api/world/workspaces')).map(r=>r.path),['/api/world/workspaces','/api/world/workspaces','/api/world/workspaces/'+workspaceId]);
 assert.equal(requests.slice(workspaceStart).some(r=>r.path.endsWith('/result')||r.path.includes('/sections/')),false,'Browsing must never fetch a world or section raster');
 await page.locator('#world-dialog').evaluate(d=>d.close());await openWorld();
 const rejectedStart=requests.length;
 // Reject malformed or inconsistent metadata before any result/world/workspace download.
 for(const metadata of [
  {resultKind:'sections'},...['',null,17,{},'a'.repeat(31),'a'.repeat(33),'g'.repeat(32),'../world','a'.repeat(32)+'\n',' '+workspaceId].map(workspaceId=>({resultKind:'sections',workspaceId})),
  {resultKind:'unknown',workspaceId},{resultKind:17,workspaceId},{resultKind:'world',workspaceId},{workspaceId}
 ]){
  jobState=metadata;await page.locator('#osm-world-start').click();await waitIdle();assert.equal(await page.locator('#osm-world-apply').isVisible(),false,JSON.stringify(metadata));assert.match(await state(),/invalid.*result|result.*invalid/i);await page.evaluate(()=>window.fixture.ui.apply());
 }
 // Completion with older settings is rejected; changing settings after completion also invalidates browsing.
 jobState={resultKind:'sections',workspaceId};let releaseStatus;slowStatus=new Promise(resolve=>releaseStatus=resolve);
 await page.locator('#osm-world-start').click();await page.waitForFunction(()=>window.fixture.ui.job);await page.evaluate(()=>{const e=document.querySelector('#osm-world-name');e.value='stale workspace';e.dispatchEvent(new Event('input'));});releaseStatus();slowStatus=null;await waitIdle();assert.equal(await page.locator('#osm-world-apply').isVisible(),false);assert.match(await state(),/older settings/);
 await startReady();await page.locator('#osm-world-name').fill('Changed after workspace completion');await page.evaluate(()=>window.fixture.ui.apply());assert.equal(await page.locator('#osm-world-apply').isVisible(),false);
 assert.equal(requests.slice(rejectedStart).some(r=>r.path.endsWith('/result')||r.path.startsWith('/api/world/workspaces')),false);assert.equal(await editingSnapshot(),edited);assert.equal(await page.evaluate(()=>window.fixture.state.applications),applications);
 await page.evaluate(()=>{const s=window.fixture.workspaces.session;s.dirty=false;s.detach();});
 // Explicit small-world metadata still uses the existing full-JSON result endpoint.
 const fullResults=requests.filter(r=>r.path.endsWith('/result')).length;
 jobState={resultKind:'world',workspaceId:null};await startReady();assert.equal(await page.locator('#osm-world-apply').textContent(),'Open imported world');
 await page.locator('#osm-world-apply').click();await waitIdle();assert.equal(requests.filter(r=>r.path.endsWith('/result')).length,fullResults+1);assert.equal(await page.evaluate(()=>window.fixture.state.applications),applications+1);
 await page.locator('#world-dialog').evaluate(d=>d.close());jobState=null;
 // Existing frames may reach the server's ±85° bounds even though the new-centre picker stops at ±84°.
 const polarReference={bounds:{south:84.499,north:84.501,west:32.04,east:32.05},metresPerTile:1,rotationDegrees:0,originCellX:100,originCellY:100};
 await page.evaluate(r=>window.fixture.setWorld({id:'polar',name:'Existing polar world',geoReference:r,maps:[{project:{width:512,height:512,seed:42}}]}),polarReference);
 await openWorld();assert.equal(await page.locator('#osm-world-start').isEnabled(),true);await page.locator('#osm-world-plan').click();await waitIdle();assert.deepEqual(requests.findLast(r=>r.path==='/api/world/osm/plan').body.bounds,polarReference.bounds);await page.locator('#osm-world-preserve').uncheck();await page.locator('#osm-world-close').click();
 // Preserve the full rotated, non-square geographic frame without rounding it.
 const reference={bounds:{south:54.778123456789,north:54.780987654321,west:32.045123456789,east:32.050987654321},metresPerTile:1.5,rotationDegrees:17.25,originCellX:126,originCellY:205};
 await page.evaluate(r=>window.fixture.setWorld({id:'rotated',name:'Rotated world',geoReference:r,maps:[{project:{width:512,height:512,seed:77}}]}),reference);
 await openWorld();assert.equal(await page.locator('#osm-world-preserve').isChecked(),true);assert.equal(await page.locator('#osm-world-width').isDisabled(),true);assert.equal(await page.locator('[data-osm-area="2"]').isDisabled(),true);assert.equal(await page.locator('#osm-world-scale').isDisabled(),true);assert.equal(await page.locator('#osm-world-preserve-row').isVisible(),true);await startReady();const body=jobs.at(-1);assert.deepEqual(body.bounds,reference.bounds);assert.equal(body.rotationDegrees,17.25);assert.equal(body.metresPerTile,1.5);assert.deepEqual(body.existingWorld.geoReference,reference);
 // A workspace result must obey the same current-frame guard, even without an input event.
 await page.locator('#osm-world-name').fill('Frame-safe workspace');jobState={resultKind:'sections',workspaceId};await startReady();
 const beforeFrameRequests=requests.length;
 await page.evaluate(()=>{window.fixture.state.world.geoReference.rotationDegrees++;});await page.locator('#osm-world-apply').click();await waitIdle();assert.match(await state(),/geographic frame changed/);assert.equal(requests.length,beforeFrameRequests);
 await page.evaluate(r=>{window.fixture.state.world.geoReference=r;},reference);await page.locator('#osm-world-name').fill('Clear stale workspace');jobState=null;
 await page.locator('#osm-world-close').click();
 // Single-map wiring uses the same upload helper; failed imports leave the map intact.
 await page.locator('#open-import').click();const beforeMap=await page.evaluate(()=>JSON.stringify(window.fixture.project));await page.locator('#import-source').selectOption('file');await page.locator('#import-source-file').setInputFiles(jsonFile);await waitIdle();assert.equal(await page.locator('#import-source').isEnabled(),true);failMap=true;await page.locator('#import-osm-button').click();await waitIdle();assert.equal(await page.evaluate(()=>JSON.stringify(window.fixture.project)),beforeMap);assert.match(await page.locator('#import-state').textContent(),/First source area/);
 failMap=false;truncateMap=true;await page.locator('#import-osm-button').click();await waitIdle();assert.equal(await page.evaluate(()=>JSON.stringify(window.fixture.project)),beforeMap);assert.match(await page.locator('#import-state').textContent(),/incomplete/);truncateMap=false;
 // A pending HTTP stream is cancelled through the same AbortSignal used by task().
 await page.evaluate(()=>{const originalFetch=window.fixture.source.fetchImpl;window.fixture.restoreFetch=()=>window.fixture.source.fetchImpl=originalFetch;window.fixture.source.fetchImpl=(url,options)=>url==='/api/import/osm/stream'?Promise.resolve(new Response(new ReadableStream({start(controller){window.fixture.streamSignal=options.signal;options.signal.addEventListener('abort',()=>controller.error(options.signal.reason),{once:true});}}))):originalFetch(url,options);});
 await page.locator('#import-osm-button').click();await page.waitForFunction(()=>window.fixture.streamSignal);await page.evaluate(()=>window.fixture.abortTask());await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.streamSignal.aborted),true);assert.equal(await page.evaluate(()=>JSON.stringify(window.fixture.project)),beforeMap);await page.evaluate(()=>window.fixture.restoreFetch());
 await page.locator('#import-osm-button').click();await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.state.adoptions),1);const mapRequest=requests.findLast(r=>r.path==='/api/import/osm/stream');assert.equal(mapRequest.body.sourceMode,'file');assert.ok(mapRequest.body.sourceId);assert.doesNotMatch(await page.locator('#import-state').textContent(),/map-source/);
 await page.locator('#open-import').click();assert.equal(await page.locator('#import-source-download').isVisible(),true);await page.locator('#import-file-button').click({noWaitAfter:true});await page.locator('#geodata-file').setInputFiles(jsonFile);await waitIdle();assert.equal(await page.evaluate(()=>window.fixture.state.adoptions),2);assert.ok(requests.some(r=>r.path==='/api/import/geojson'));
 await page.locator('#open-import').click();await page.locator('#import-world-button').click();await page.setViewportSize({width:390,height:844});assert.equal(await page.locator('#osm-world-dialog').evaluate(d=>d.scrollWidth<=d.clientWidth+1),true);
 if(screenshots){await mkdir(outputDirectory,{recursive:true});await page.screenshot({path:resolve(outputDirectory,'world-mobile.png')});}
 assert.deepEqual(errors,[]);console.log(`PASS isolated Chrome OSM frontend: workspace browsing without world/raster fetches or edit replacement, malformed/stale results, timeout/resume recovery, processing/build retries, sticky progress, search/XSS, plan-before-job, frame preservation, uploads/reupload, source downloads, cancel races, stale apply, map safety and GeoJSON compatibility (${requests.length} routed API calls).`);
}catch(error){console.error({errors,lastRequests:requests.slice(-5)});throw error;}finally{await context.close();await browser.close();}
