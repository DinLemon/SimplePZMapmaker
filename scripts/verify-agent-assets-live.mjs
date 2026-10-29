// Actual editor + HTTP verification. Only recovery is virtualized to protect user autosaves.
// Launches its own Chrome process/context; never connects to an existing browser or restarts the server.
import assert from 'node:assert/strict';
import {randomUUID,randomBytes,createHash} from 'node:crypto';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {fileURLToPath,pathToFileURL} from 'node:url';

const base=process.env.PZMAP_TEST_URL??'http://127.0.0.1:48778';
assert.ok(/^http:\/\/(127\.0\.0\.1|localhost):\d+$/.test(base),'Use an explicit local Debug server origin.');
const run=randomBytes(6).toString('hex'),recoveryId=randomUUID().replaceAll('-',''),houseId=randomUUID().replaceAll('-','');
const out=new URL(`../work/agent-assets-live/${run}/`,import.meta.url),sample=new URL('../work/agent-tileset-generated-sample/',import.meta.url);
await mkdir(out,{recursive:true});
const report={run,base,status:'running',stages:[],findings:[],browserErrors:[],httpFailures:[],recoveryIsolated:true,mocks:'recovery only',output:fileURLToPath(out)};
let browser,context,page,token,stage='startup';
const modulePath=process.env.PZMAP_PLAYWRIGHT_MODULE;
const {chromium}=await import(modulePath?pathToFileURL(modulePath).href:new URL('../tools/BrowserSmoke/node_modules/playwright/index.mjs',import.meta.url).href);
const resultFile=new URL('result.json',out),apiCalls=[],recoveryCalls=[];
const cleanPath=value=>new URL(value).pathname.replace(/[a-f0-9]{64}/gi,'[session]');
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
async function checkpoint(name,action){stage=name;console.log(`Live agent assets: ${name}`);const start=Date.now();await action();report.stages.push({name,milliseconds:Date.now()-start,passed:true});await writeFile(resultFile,JSON.stringify(report,null,2));}
async function api(path,data){
 const response=await context.request.fetch(base+path,{method:data===undefined?'GET':'POST',...(data===undefined?{}:{data}),timeout:180000});
 const text=await response.text();assert.equal(response.status(),200,`${cleanPath(base+path)}: ${text.slice(0,1200)}`);return JSON.parse(text);
}
async function idle(){await page.waitForFunction(()=>!document.querySelector('#operation-dialog')?.open);}
async function agentIdle(){await page.locator('#agent-session-dialog [data-connect]').waitFor();await page.waitForFunction(()=>!document.querySelector('#agent-session-dialog [data-connect]').disabled);}
async function menu(name,id){await page.locator(`[data-command-menu=${name}]>summary`).click();await page.locator('#'+id).click();}
async function openAgent(){if(!await page.locator('#agent-session-dialog').evaluate(e=>e.open))await menu('settings','open-agent-session');}
async function closeAgent(){if(await page.locator('#agent-session-dialog').evaluate(e=>e.open))await page.locator('#agent-session-dialog [data-close]').click();}
async function expand(id){const details=page.locator('#'+id);if(!await details.evaluate(e=>e.open))await details.locator(':scope>summary').click();return details;}
async function saved(name){
 await closeAgent();const waiting=page.waitForEvent('download',{timeout:90000});await page.locator('#save-project').click();const download=await waiting;
 await download.saveAs(fileURLToPath(new URL(name,out)));return JSON.parse(await readFile(await download.path(),'utf8'));
}
const asset=name=>page.locator('[data-assets-'+name+']');
async function action(name){await asset(name).click();await agentIdle();}
async function screenshot(name){await page.screenshot({path:fileURLToPath(new URL(name,out)),fullPage:true});}
async function pixels(locator){return locator.evaluate(async image=>{await image.decode();const c=document.createElement('canvas');c.width=image.naturalWidth;c.height=image.naturalHeight;const x=c.getContext('2d');x.drawImage(image,0,0);const data=x.getImageData(0,0,c.width,c.height).data;const hash=await crypto.subtle.digest('SHA-256',data);return {width:c.width,height:c.height,visiblePixels:data.filter((v,i)=>i%4===3&&v>0).length,sha256:[...new Uint8Array(hash)].map(v=>v.toString(16).padStart(2,'0')).join('')};});}
let original,afterReceive,draft,uploadedPixels,nativePixels;
const modId='live_signs_'+run,tileset='live_signs_01_'+run,tileId=tileset+'_0';
report.modId=modId;report.tileId=tileId;
const watchdog=setTimeout(()=>{report.timeout=true;void browser?.close();},12*60*1000);
try{
 await checkpoint('isolated Chrome and recovery',async()=>{
  browser=await chromium.launch({channel:process.env.PZMAP_BROWSER_CHANNEL??'chrome',headless:true,timeout:180000,args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  context=await browser.newContext({viewport:{width:1440,height:1050},acceptDownloads:true});context.setDefaultTimeout(90000);context.setDefaultNavigationTimeout(180000);
  await context.addInitScript(id=>{localStorage.clear();sessionStorage.clear();localStorage.setItem('simple-pz-mapmaker:recovery-client:v1',id);},recoveryId);
  await context.route(/\/api\/(world\/)?recovery\//,async route=>{
   const request=route.request(),path=new URL(request.url()).pathname;assert.ok(path.includes('/'+recoveryId),'Unexpected recovery identity');
   recoveryCalls.push({path:path.replace(recoveryId,'[isolated]'),method:request.method()});
   await route.fulfill({status:request.method()==='GET'?404:200,contentType:'application/json',body:request.method()==='GET'?'{}':'{"saved":true}'});
  });
  page=await context.newPage();page.on('pageerror',e=>report.browserErrors.push(e.message));
  page.on('request',r=>{const path=new URL(r.url()).pathname;if(path.startsWith('/api/')||path==='/mcp')apiCalls.push({path:cleanPath(r.url()),method:r.method()});});
  page.on('response',response=>{const p=new URL(response.url()).pathname;if(response.status()>=400&&!/\/api\/(world\/)?recovery\//.test(p))report.httpFailures.push({path:cleanPath(response.url()),status:response.status()});});
  const caps=await api('/api/visual-references/capabilities');assert.equal(caps.mapillary.available,false,'This scenario requires an absent Mapillary token.');report.mapillary=caps.mapillary;
  assert.equal((await api('/api/agent/tilesets/capabilities')).creation.embeddedImageGeneration,false);
 });
 await checkpoint('API fixture, real file load, selected library house',async()=>{
  let fixture=await api('/api/generate',{kind:'blank',size:256,name:'Agent assets live '+run});
  const house=await api('/api/building/design',{building:{id:houseId,name:'Live reference house '+run,kind:'house',x:64,y:64,width:12,height:10},levels:1,basements:0,seed:42});
  fixture.buildings=[house];fixture.spawns=[{x:220,y:220}];fixture.version=Math.max(fixture.version,9);
  // Normalize vanilla provider metadata before the baseline even on a cold catalog with no active mods.
  // Save validates/stamps sources and otherwise makes first-attachment comparisons observe Save itself.
  const vanilla=await api('/api/assets/item/floors_interior_tilesandwood_01_40');assert.equal(vanilla.modId,null);
  fixture.assetSources={[vanilla.id]:'game'};house.assetSources={[vanilla.id]:'game'};fixture=(await api('/api/validate',fixture)).project;
  await writeFile(new URL('input.pzmap.json',out),JSON.stringify(fixture));
  await page.goto(base,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>document.querySelector('#map-summary')?.textContent.includes('256'));await idle();
  await page.locator('#project-file').setInputFiles({name:'Agent-assets-live.pzmap.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(fixture))});await idle();
  await page.waitForFunction(()=>document.querySelector('#building-count')?.textContent==='1');
  await menu('tools','open-building-editor');await page.getByRole('tab',{name:'On map',exact:true}).click();await page.locator('#building-list button').filter({hasText:house.name}).click();
  await page.locator('#building-dialog').waitFor({state:'visible'});assert.equal(await page.locator('#be-name').inputValue(),house.name);await page.locator('#be-cancel').click();
  original=await saved('editor-before.pzmap.json');assert.equal(original.buildings[0].id,houseId);
  await openAgent();assert.equal(await page.locator('#agent-session-dialog [data-token]').inputValue(),'');
  await page.locator('#agent-session-dialog [data-connect]').click();await agentIdle();token=await page.locator('#agent-session-dialog [data-token]').inputValue();assert.match(token,/^[a-f0-9]{64}$/);
  assert.deepEqual((await api('/api/agent/session/'+token)).project,original);
 });
 await checkpoint('real own-photo UI, stored pixels, manual vision handoff, absent Mapillary',async()=>{
  await expand('agent-references');const panel=page.locator('.visual-references');
  await panel.locator('.vr-target').filter({hasText:houseId}).waitFor();await panel.locator('.vr-capability').filter({hasText:report.mapillary.message}).waitFor();
  assert.equal(await panel.getByRole('button',{name:'Find nearby licensed images',exact:true}).isDisabled(),true);
  const base64=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=160;c.height=120;const x=c.getContext('2d');x.fillStyle='#202b30';x.fillRect(0,0,160,120);x.fillStyle='#8d6951';x.fillRect(28,40,104,72);x.fillStyle='#436154';x.beginPath();x.moveTo(15,42);x.lineTo(80,8);x.lineTo(145,42);x.fill();x.fillStyle='#b7d5da';x.fillRect(44,58,24,28);x.fillStyle='#30291f';x.fillRect(90,68,22,44);return c.toDataURL('image/png').split(',')[1];});
  await writeFile(new URL('synthetic-reference.png',out),Buffer.from(base64,'base64'));
  await panel.getByLabel('Owned photo (PNG/JPEG, 8 MiB maximum)').setInputFiles({name:'synthetic-test-illustration.png',mimeType:'image/png',buffer:Buffer.from(base64,'base64')});
  await panel.getByLabel('Photographer / author').fill('Verification script; synthetic illustration, not a photographed building');
  await panel.getByLabel('Photo license / permission').fill('CC0 synthetic test fixture authored by this script; no surveyed building evidence');
  const uploaded=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname.endsWith('/own'));
  await panel.getByRole('button',{name:'Upload and bind to selection',exact:true}).click();const response=await uploaded;assert.equal(response.status(),200,(await response.text()).slice(0,1000));
  const reference=await response.json();report.referenceId=reference.id;assert.equal(reference.binding.buildingId,houseId);
  const card=panel.locator('.vr-card').filter({hasText:'Verification script; synthetic illustration'});await card.waitFor();
  const image=card.locator('img'),picture=await pixels(image);assert.equal(picture.width,160);assert.equal(picture.height,120);assert.ok(picture.visiblePixels>0);
  const originalResponse=await context.request.get(base+await image.getAttribute('src'));assert.equal(originalResponse.status(),200);assert.equal(sha(await originalResponse.body()),reference.sha256.toLowerCase());
  await card.getByRole('button',{name:'Inspect with vision agent',exact:true}).click();await page.locator('[data-vision-handoff]').waitFor({state:'visible'});
  const instructions=await page.locator('[data-vision-text]').inputValue();for(const text of ['visual_reference_view','visual_reference_observe',reference.id,token,'receiptId'])assert.ok(instructions.includes(text),text);
  assert.match(instructions,/not.*automatically/i);assert.equal(apiCalls.filter(c=>c.path==='/mcp').length,0);
  assert.equal(apiCalls.some(c=>c.path.includes('/mapillary/search')),false);await screenshot('reference-handoff.png');report.referencePixels=picture;
  assert.deepEqual((await api('/api/agent/session/'+token)).project,original);
 });
 await checkpoint('real advanced draft, native PNG upload, input preview',async()=>{
  const panel=await expand('agent-assets');await panel.locator('details>summary').click();
  const sampleReport=JSON.parse(await readFile(new URL('verification.json',sample),'utf8')),sampleSpec=sampleReport.spec;
  const declared=sampleSpec.Provenance??sampleSpec.provenance,provenance=Object.fromEntries(Object.entries(declared).map(([key,value])=>[key[0].toLowerCase()+key.slice(1),value]));
  const spec={modId,name:'Live generated sign '+run,tileset,fileNumber:702,provenance,frames:[{key:'west',direction:'w',width:128,height:256,properties:{solidtrans:'',Facing:'W',CustomName:'Live generated sign'}}]};
  for(let attempt=0;attempt<8;attempt++){
   spec.fileNumber=702+attempt;await asset('spec').fill(JSON.stringify(spec,null,2));
   const waiting=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname==='/api/agent/tilesets');await action('create');const response=await waiting;const value=await response.json();
   if(response.ok()){draft=value;break;}
   assert.match(value.error??'',/reserved|collision|FileNumber|already.*use/i,JSON.stringify(value));
   if(attempt===7)assert.fail('No free FileNumber in bounded 702–709 candidates.');
  }
  assert.ok(draft);report.fileNumber=spec.fileNumber;report.draftId=draft.id;
  assert.deepEqual((await api('/api/agent/session/'+token)).project,original);
  await asset('file').setInputFiles(fileURLToPath(new URL('west-128x256.png',sample)));await action('upload');
  draft=await api(`/api/agent/tilesets/${token}/${draft.id}`);assert.equal(draft.frames[0].uploaded,true);assert.equal(draft.revision,2);
  await action('preview');await asset('origin').filter({hasText:'uploaded-png'}).waitFor();uploadedPixels=await pixels(asset('pixels'));assert.equal(uploadedPixels.width,128);assert.equal(uploadedPixels.height,256);assert.ok(uploadedPixels.visiblePixels>0);
  await screenshot('uploaded-frame.png');report.uploadedPixels=uploadedPixels;
 });
 await checkpoint('native build, pack-derived preview, actual ZIP download',async()=>{
  await action('build');draft=await api(`/api/agent/tilesets/${token}/${draft.id}`);assert.equal(draft.state,'built');assert.equal(draft.revision,3);assert.ok(draft.sha256);
  await action('preview');await asset('origin').filter({hasText:'validated-native-pack'}).waitFor();nativePixels=await pixels(asset('pixels'));assert.deepEqual(nativePixels,uploadedPixels);
  const metadata=JSON.parse(await asset('metadata').textContent());assert.equal(metadata.origin,'validated-native-pack');assert.equal(metadata.tileId,tileId);assert.equal(metadata.properties.Facing,'W');
  const preview=await context.request.get(base+`/api/agent/tilesets/${token}/${draft.id}/frames/0.png`);assert.equal(preview.status(),200);await writeFile(new URL('native-pack-preview.png',out),await preview.body());
  const waiting=page.waitForEvent('download');await action('download');const download=await waiting;assert.equal(download.suggestedFilename(),modId+'.zip');await download.saveAs(fileURLToPath(new URL(modId+'.zip',out)));
  const bytes=await readFile(await download.path());assert.equal(bytes.subarray(0,2).toString(),'PK');assert.equal(sha(bytes),draft.sha256.toLowerCase());assert.ok(bytes.includes(Buffer.from(modId+'/42/agent-tileset.json')));
  report.zip={file:modId+'.zip',bytes:bytes.length,sha256:sha(bytes)};report.nativePixels=nativePixels;await screenshot('built-native-frame.png');
  assert.deepEqual((await api('/api/agent/session/'+token)).project,original);
  assert.deepEqual(await saved('editor-before-attach.pzmap.json'),original);await openAgent();
 });
 await checkpoint('attach, unchanged editor, dependency review and real catalog registration',async()=>{
  await action('attach');await page.locator('#agent-session-dialog [data-review]').waitFor({state:'visible'});assert.match(await page.locator('[data-review-summary]').textContent(),new RegExp(modId));
  const remote=await api('/api/agent/session/'+token);assert.ok(remote.project.requiredMods.includes(modId));assert.equal(remote.project.assetSources[tileId],modId);report.attachedRevision=remote.revision;
  const item=await api('/api/assets/item/'+encodeURIComponent(tileId));assert.equal(item.id,tileId);assert.equal(item.modId,modId);assert.equal(item.properties.Facing,'W');assert.ok(item.sprite);assert.ok(item.page);
  const search=await api('/api/assets/search?q='+encodeURIComponent(tileId)+'&limit=20');assert.ok(search.items.some(i=>i.id===tileId&&i.modId===modId));
  const atlas=await context.request.get(base+'/api/assets/page/'+encodeURIComponent(item.page));assert.equal(atlas.status(),200);const bytes=await atlas.body();assert.equal(bytes.subarray(0,8).toString('hex'),'89504e470d0a1a0a');await writeFile(new URL('catalog-atlas.png',out),bytes);
  report.catalog={tileId:item.id,modId:item.modId,properties:item.properties,requirements:item.requirements,atlasBytes:bytes.length};await screenshot('dependency-review.png');
  assert.deepEqual(await saved('editor-after-attach-before-receive.pzmap.json'),original);await openAgent();
  assert.equal(await page.locator('#agent-session-dialog [data-review-apply]').isEnabled(),true);
 });
 await checkpoint('explicit Receive, then Undo restores editor dependencies',async()=>{
  // Recovery is isolated, but these request bodies are actual editor snapshots, before Save can normalize them.
  const received=page.waitForRequest(r=>r.method()==='POST'&&new URL(r.url()).pathname==='/api/recovery/'+recoveryId);
  await page.locator('#agent-session-dialog [data-review-apply]').click();await agentIdle();afterReceive=(await received).postDataJSON();await writeFile(new URL('editor-received.pzmap.json',out),JSON.stringify(afterReceive));
  assert.ok(afterReceive.requiredMods.includes(modId));assert.equal(afterReceive.assetSources[tileId],modId);assert.deepEqual(afterReceive.buildings,original.buildings);assert.deepEqual(afterReceive.terrain,original.terrain);
  await closeAgent();const undoSnapshot=page.waitForRequest(r=>r.method()==='POST'&&new URL(r.url()).pathname==='/api/recovery/'+recoveryId);await page.locator('#undo').click();await idle();const undone=(await undoSnapshot).postDataJSON();assert.deepEqual(undone.requiredMods,original.requiredMods);assert.deepEqual(undone.assetSources,original.assetSources);assert.deepEqual(undone,original);assert.deepEqual(await saved('editor-undo.pzmap.json'),original);
  assert.ok((await api('/api/agent/session/'+token)).project.requiredMods.includes(modId),'Undo remains editor-local until explicitly published.');
  assert.equal((await api('/api/assets/item/'+encodeURIComponent(tileId))).modId,modId);await screenshot('editor-undo.png');
  assert.deepEqual(report.browserErrors,[]);assert.ok(recoveryCalls.some(c=>c.method==='GET'));report.recoveryRequests=recoveryCalls;report.editorUnchangedUntilReceive=true;report.undoRestoresDependencies=true;
 });
 await checkpoint('validate attached dependency persistence without changing the editor',async()=>{
  const validated=(await api('/api/validate',afterReceive)).project;
  if(!validated.requiredMods.includes(modId)||validated.assetSources[tileId]!==modId){
   report.findings.push({title:'Validation removes an attached tile-mod dependency before its first placement',endpoint:'/api/validate',expected:{requiredMod:modId,tileProvider:tileId},actual:{requiredMods:validated.requiredMods,tileProvider:validated.assetSources[tileId]??null},neededFix:'Preserve explicitly attached dependency/provider declarations during AssetRules.StampDependencies; normal Save uses this validation response to replace editor metadata.'});
   await writeFile(new URL('validated-received.pzmap.json',out),JSON.stringify(validated));
  }
 });
 report.status=report.findings.length?'failed':'passed';if(report.findings.length){process.exitCode=1;console.error('FAIL dependency persistence: '+report.findings[0].title);}
 console.log('PASS actual Debug UI lifecycle: selected building → own photo / manual handoff → draft/upload → native build/preview → ZIP → attach/review → Receive → Undo; actual catalog tile and atlas verified.');
}catch(error){
 report.status='failed';report.failure={stage,message:error.message,stack:error.stack};console.error(`FAIL ${stage}: ${error.message}`);
 if(page){report.diagnostics={toast:await page.locator('#toast').textContent().catch(()=>''),agent:await page.locator('#agent-session-dialog [data-state]').textContent().catch(()=>''),assets:await asset('status').textContent().catch(()=>''),references:await page.locator('[data-reference-context]').textContent().catch(()=>'')};await screenshot('failure.png').catch(()=>{});}
 process.exitCode=1;
}finally{
 clearTimeout(watchdog);
 if(token&&context)try{await api('/api/agent/session/'+token+'/close',{});report.sessionClosed=true;}catch(error){report.cleanupError=error.message;}
 if(context)try{const mods=await api('/api/assets/mods');if(mods.active.includes(modId))await api('/api/assets/mods/activate',{ids:mods.active.filter(id=>id!==modId)});report.testModDeactivated=true;}catch(error){report.cleanupError=error.message;}
 await writeFile(resultFile,JSON.stringify(report,null,2));await browser?.close();console.log('Report: '+fileURLToPath(resultFile));
}
