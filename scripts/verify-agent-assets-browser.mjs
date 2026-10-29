// Real browser / mocked HTTP contract test. No application server or build is started.
// PZMAP_PLAYWRIGHT_MODULE may point to a bundled playwright/index.mjs.
import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {fileURLToPath,pathToFileURL} from 'node:url';
const playwright=process.env.PZMAP_PLAYWRIGHT_MODULE;
const {chromium}=await import(playwright?pathToFileURL(playwright).href:new URL('../tools/BrowserSmoke/node_modules/playwright/index.mjs',import.meta.url).href);
const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL,executablePath:process.env.PZMAP_BROWSER_EXECUTABLE});
const page=await browser.newPage({viewport:{width:1100,height:900},acceptDownloads:true}),errors=[],calls=[];
page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(10000);
page.on('console',message=>{if(message.type()==='error')console.error('Browser:',message.text());});
const base='http://agent-assets.test',token='a'.repeat(64),worldToken='e'.repeat(64),draftId='b'.repeat(32),memberId='c'.repeat(32),buildingId='f'.repeat(32),referenceId='d'.repeat(32);
let serverRevision=1,source=null,scope='map',draft=null,conflict=false;
const pixel=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1sAAAAASUVORK5CYII=','base64');
const fixture=`<!doctype html><meta charset="utf-8"><title>Agent assets browser regression</title><style>body{background:#20251f;color:#eee;font:14px sans-serif}dialog{background:#252b24;color:#eee}button,input,textarea,select{font:inherit}button{padding:8px}[hidden]{display:none!important}</style><div class="topbar"><nav></nav></div><p id="message"></p><script type="module">
 import {AgentSession} from '/agent-session.js';import {blank} from '/editor.js';
 window.editor=blank();window.applied=0;window.version=0;window.target={scope:'map',buildingId:'${buildingId}'};window.clipboardText='';
 Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async value=>{window.clipboardText=value;}}});
 window.world={maps:[{id:'${memberId}',project:blank()}],buildings:[],decorations:[]};
 window.session=new AgentSession({project:()=>window.editor,world:()=>window.world,apply:async p=>{window.editor=p;window.applied++;},applyWorld:async w=>{window.world=w;window.applied++;},changeVersion:()=>window.version,getReferenceTarget:mode=>window.target,busy:()=>false,message:(text)=>document.querySelector('#message').textContent=text});
 clearInterval(window.session.timer);window.ready=true;
 </script>`;
await page.route('**/*',async route=>{
 const url=new URL(route.request().url()),p=url.pathname,method=route.request().method(),data=route.request().postDataJSON();
 if(url.origin!==base)return route.abort();
 if(p==='/')return route.fulfill({contentType:'text/html',body:fixture});
 if(!p.startsWith('/api/')){
  if(!/^\/[a-z0-9.-]+\.(js|css|json)$/.test(p))return route.fulfill({status:404,body:'Unknown fixture file'});
  try{return await route.fulfill({contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.json')?'application/json':'text/css',body:await readFile(new URL('../src/SimplePZMapmaker.Web/wwwroot'+p,import.meta.url))});}catch(e){console.error('Fixture file:',p,e.message);return route.fulfill({status:404});}
 }
 calls.push({path:p,method,data});const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
 if(p==='/api/building/presets')return json([]);
 if(p==='/api/agent/session'||p==='/api/agent/world/session'){scope=p.includes('/world/')?'world':'map';source=structuredClone(data);serverRevision=1;return json({token:scope==='world'?worldToken:token,revision:serverRevision,[scope==='world'?'world':'project']:source});}
 if(p.endsWith('/close'))return json({});
 if(p.endsWith('/status'))return json({revision:serverRevision});
 if(p.endsWith('/publish')){assert.equal(data.revision,serverRevision);source=structuredClone(data[scope==='world'?'world':'project']);return json({token:scope==='world'?worldToken:token,revision:++serverRevision,[scope==='world'?'world':'project']:source});}
 if(p==='/api/agent/session/'+token||p==='/api/agent/world/session/'+worldToken)return json({token:scope==='world'?worldToken:token,revision:serverRevision,[scope==='world'?'world':'project']:source});
 if(p==='/api/visual-references/capabilities')return json({own:{available:true,message:'Own photos available.'},mapillary:{available:false,message:'Mapillary requires server PZMAP_MAPILLARY_TOKEN. Upload an owned photo.'}});
 if(p.startsWith('/api/visual-references/')){
  if(p.endsWith('/target')){assert.equal(data.revision,serverRevision);assert.equal(data.target.scope,scope);return json({buildingId:data.target.buildingId,baseRevision:serverRevision,longitude:0,latitude:0});}
  if(p.endsWith('/list'))return json([{reference:{id:referenceId,provider:'own',sourceId:'fixture',author:'Artist',license:'CC0'}}]);
  if(p.endsWith('/own')){assert.equal(data.revision,serverRevision);assert.equal(data.provenance.author,'Fixture photographer');assert.ok(data.base64);return json({id:referenceId});}
  if(p.endsWith('/image'))return route.fulfill({contentType:'image/png',body:pixel});
 }
 const root='/api/agent/tilesets';
 if(p===root){assert.equal(data.expectedRevision,serverRevision);assert.equal(data.scope,scope);if(scope==='world')assert.equal(data.memberId,memberId);
  draft={id:draftId,revision:1,baseRevision:serverRevision,scope,memberId:data.memberId,state:'draft',spec:data.spec,sha256:null,frames:data.spec.frames.map((f,index)=>({...f,index,tileId:data.spec.tileset+'_'+index,uploaded:false}))};return json(draft);}
 if(p===root+'/'+(scope==='world'?worldToken:token))return json(draft?[draft]:[]);
 if(p.endsWith('/'+draftId))return json(draft);
 if(p.endsWith('/metadata'))return json({tileId:draft.frames[0].tileId,origin:draft.sha256?'validated-native-pack':'uploaded-png',width:128,height:256,properties:{CustomName:'Fixture sign'},provenance:draft.spec.provenance});
 if(p.endsWith('/frames/0.png'))return route.fulfill({contentType:'image/png',body:pixel});
 if(p.endsWith('/frames/0')){if(conflict)return json({error:'Draft revision conflict',currentRevision:draft.revision+1},409);assert.equal(data.expectedDraftRevision,draft.revision);assert.ok(data.pngBase64);draft.revision++;draft.frames[0].uploaded=true;return json(draft);}
 if(p.endsWith('/build')){assert.equal(data.expectedDraftRevision,draft.revision);draft.revision++;draft.state='built';draft.sha256='123456';return json(draft);}
 if(p.endsWith('/mod.zip'))return route.fulfill({contentType:'application/zip',body:Buffer.from('PK fixture download')});
 if(p.endsWith('/attach')){assert.equal(data.expectedRevision,serverRevision);assert.equal(data.expectedDraftRevision,draft.revision);const project=scope==='world'?source.maps[0].project:source;project.requiredMods=[draft.spec.modId];project.assetSources={[draft.frames[0].tileId]:draft.spec.modId};return json({revision:++serverRevision,modId:draft.spec.modId,draftId,scope,memberId:draft.memberId});}
 return json({error:'Unhandled mock endpoint '+p},404);
});
const asset=name=>page.locator('[data-assets-'+name+']'),refs=()=>page.locator('.visual-references');
const idle=()=>page.waitForFunction(()=>!window.session.running);
async function click(name){await asset(name).click();await idle();}
async function fields(suffix=''){
 for(const [key,value] of Object.entries({modId:'fixture_signs'+suffix,name:'Fixture sign',tileset:'fixture_signs_01'+suffix,fileNumber:suffix?'702':'701',originalAuthor:'Artist',source:'Original PNG',reference:'Retained source',license:'CC0'}))await asset(key).fill(value);
 await asset('canRedistribute').check();
}
try{
 await page.goto(base);await page.waitForFunction(()=>window.ready);await page.locator('#open-agent-session').click();
 assert.equal(await page.locator('.topbar nav button').count(),1);
 assert.equal(await page.locator('link[href="visual-references.css"]').count(),1);
 await page.locator('#agent-assets>summary').click();assert.equal(await asset('create').isDisabled(),true);assert.equal(calls.filter(c=>c.path==='/api/agent/session').length,0);
 await page.locator('#agent-session-dialog [data-connect]').click();await idle();
 await page.locator('#agent-references>summary').click();await refs().getByText('Mapillary requires server PZMAP_MAPILLARY_TOKEN. Upload an owned photo.',{exact:true}).waitFor();
 assert.equal(await refs().getByRole('button',{name:'Find nearby licensed images'}).isDisabled(),true);
 await refs().getByRole('button',{name:'Inspect with vision agent'}).click();await page.waitForFunction(()=>window.clipboardText.includes('visual_reference_view'));
 assert.match(await page.locator('[data-vision-text]').inputValue(),new RegExp(token));assert.equal(await page.locator('[data-vision-handoff]').isVisible(),true);
 assert.equal(calls.some(c=>/\/mcp|visual_reference_view/.test(c.path)),false);
 await page.evaluate(()=>Object.defineProperty(navigator,'clipboard',{value:{writeText:async()=>{throw Error('Clipboard denied');}}}));
 await refs().getByRole('button',{name:'Inspect with vision agent'}).click();await page.locator('[data-vision-status]').filter({hasText:'Clipboard unavailable'}).waitFor();
 const png=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=128;c.height=256;const x=c.getContext('2d');x.fillStyle='#a33';x.fillRect(32,64,64,128);return c.toDataURL('image/png').split(',')[1];});
 await refs().getByLabel('Owned photo (PNG/JPEG, 8 MiB maximum)').setInputFiles({name:'owned.png',mimeType:'image/png',buffer:Buffer.from(png,'base64')});await refs().getByLabel('Photographer / author').fill('Fixture photographer');await refs().getByRole('button',{name:'Upload and bind to selection'}).click();await page.waitForFunction(()=>document.querySelector('.vr-status').textContent.includes('Images are exterior'));
 await page.evaluate(()=>{window.target={scope:'map'};window.session.localChanged();});assert.equal(await asset('create').isDisabled(),true);await page.locator('[data-reference-context]').filter({hasText:'Submit editor changes'}).waitFor();
 await page.locator('#agent-session-dialog [data-publish]').click();await idle();assert.equal(await asset('create').isEnabled(),true);
 await fields();await click('create');assert.ok(draft);assert.equal(draft.scope,'map');assert.equal(await page.evaluate(()=>window.applied),0);
 await asset('file').setInputFiles({name:'native.png',mimeType:'image/png',buffer:Buffer.from(png,'base64')});
 conflict=true;await click('upload');assert.match(await asset('status').textContent(),/Draft revision conflict/);assert.equal(draft.revision,1);conflict=false;
 await click('upload');assert.equal(draft.revision,2);await click('preview');assert.match(await asset('origin').textContent(),/uploaded-png/);
 await click('build');assert.equal(draft.revision,3);assert.equal(await asset('upload').isDisabled(),true);await click('preview');assert.match(await asset('origin').textContent(),/validated-native-pack/);
 const download=page.waitForEvent('download');await click('download');assert.equal((await download).suggestedFilename(),'fixture_signs.zip');
 const before=await page.evaluate(()=>JSON.stringify(window.editor));await click('attach');assert.equal(await page.evaluate(()=>JSON.stringify(window.editor)),before);assert.equal(await page.evaluate(()=>window.applied),0);assert.equal(await page.locator('#agent-session-dialog [data-review]').isVisible(),true);assert.equal(await asset('attach').isDisabled(),true);assert.match(await page.locator('[data-review-summary]').textContent(),/fixture_signs/);
 await page.locator('#agent-session-dialog [data-review-apply]').click();await idle();assert.equal(await page.evaluate(()=>window.applied),1);assert.deepEqual(await page.evaluate(()=>window.editor.requiredMods),['fixture_signs']);
 // A newly observed remote revision blocks both references and writes. Publish must recheck status.
 serverRevision++;await page.evaluate(()=>window.session.poll());assert.equal(await asset('create').isDisabled(),true);await page.locator('#agent-session-dialog [data-publish]').click();await idle();assert.match(await page.locator('#message').textContent(),/Review and receive/);
 await page.evaluate(()=>{window.target={scope:'world',memberId:'c'.repeat(32)};});await page.locator('#agent-session-dialog [data-mode]').selectOption('world');await idle();draft=null;
 assert.equal(await asset('create').isDisabled(),true);await page.locator('#agent-session-dialog [data-connect]').click();await idle();
 await fields('_world');await asset('directions').selectOption('nw');await click('create');assert.equal(draft.scope,'world');assert.equal(draft.memberId,memberId);assert.match(await page.locator('[data-reference-context]').textContent(),/Select a building/);
 await asset('file').setInputFiles({name:'north.png',mimeType:'image/png',buffer:Buffer.from(png,'base64')});await click('upload');assert.equal(await asset('build').isDisabled(),true);
 await asset('frames').selectOption('1');assert.equal(await asset('preview').isDisabled(),true);await asset('frames').selectOption('0');assert.equal(await asset('preview').isEnabled(),true);
 await page.evaluate(()=>{window.target=null;window.session.render();});await click('create');assert.match(await asset('status').textContent(),/Select a world section/);
 await page.setViewportSize({width:420,height:850});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 assert.deepEqual(errors,[]);const out=new URL('../work/agent-assets-browser/',import.meta.url);await mkdir(out,{recursive:true});await page.screenshot({path:fileURLToPath(new URL('agent-assets-mobile.png',out)),fullPage:true});
 console.log('PASS browser mocked HTTP: explicit session; selected references; absent Mapillary token; JSON photo upload; visible/copy vision handoff and clipboard fallback; dirty/remote guards; create→conflict→upload→preview→build→native preview→download→attach→review/import; world member context; mobile overflow; zero page errors.');
}catch(error){console.error('Page errors:',errors);throw error;}finally{await browser.close();}
