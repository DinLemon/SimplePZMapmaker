import test from 'node:test';
import assert from 'node:assert/strict';
import * as output from '../src/SimplePZMapmaker.Web/wwwroot/world-build-output.js';

const importId='a'.repeat(32), workspaceId='b'.repeat(32), jobId='c'.repeat(32);
const sections=()=>({id:importId,resultKind:'sections',workspaceId,workspaceRevision:7,revision:7,key:'furnished'});
const job=(status='queued',extra={})=>({id:jobId,kind:'world',status,snapshotHash:'hash',revision:'7',createdAtUtc:'2026-10-28T00:00:00Z',progress:null,error:null,downloadUrl:status==='completed'?`/api/export/jobs/${jobId}/download`:null,bytes:status==='completed'?123:0,...extra});
const reply=(body,status=200)=>({ok:status<400,status,json:async()=>body});
function fixture(responses,result=sections()){
 const calls=[];let current=result;
 const request=async(url,options={})=>{
  calls.push({url,method:options.method??'GET',headers:options.headers,body:options.body});
  assert.ok(responses.length,`Unexpected request: ${url}`);
  const next=responses.shift();return typeof next==='function'?next(url,options):next;
 };
 const controller=output.createWorldBuildOutput({getResult:()=>current,request});
 return {controller,calls,setResult:r=>{current=r;}};
}
const naming={confirmed:true,modId:'Pilna_42',mapFolder:'Pilna.Map',modName:'Пильна & area'};
const deferred=()=>{let resolve;const promise=new Promise(r=>{resolve=r;});return {promise,resolve};};

test('existing export queue updates unlock the completed archive without a second compile-status click',async()=>{
 let publish;
 const controller=output.createWorldBuildOutput({getResult:sections,queueExport:async(kind,snapshot,options)=>{publish=options.onUpdate;return job();}});
 await controller.compile();assert.equal(typeof publish,'function');assert.equal(controller.view().downloadUrl,null);
 publish(job('running'));assert.equal(controller.view().job.status,'running');
 publish(job('completed'));assert.equal(controller.view().downloadUrl,`/api/export/jobs/${jobId}/download`);
});

test('export queue notifications cannot attach a stale or differently revised archive',async()=>{
 let publish,current=sections();
 const controller=output.createWorldBuildOutput({getResult:()=>current,queueExport:async(kind,snapshot,options)=>{publish=options.onUpdate;return job();}});
 await controller.compile();assert.equal(typeof publish,'function');publish(job('completed',{revision:'8'}));
 assert.equal(controller.view().downloadUrl,null);assert.match(controller.view().error,/revision/i);
 current={...current,key:'changed-source'};publish(job('completed'));assert.equal(controller.view().job,null);assert.equal(controller.view().downloadUrl,null);
});

test('workspace compile streams the saved workspace without reading or posting a whole world',async()=>{
 const f=fixture([reply(job()),reply(job('completed'))]);
 await f.controller.compile();await f.controller.refresh();
 assert.deepEqual(f.calls.map(c=>[c.url,c.method,c.body]),[
  [`/api/world/workspaces/${workspaceId}/export/jobs?expectedRevision=7`,'POST','{}'],
  [`/api/export/jobs/${jobId}`,'GET',undefined]
 ]);
 assert.equal(f.controller.view().downloadUrl,`/api/export/jobs/${jobId}/download`);
});

test('workspace compile requires the published revision and rejects a different archive revision',async()=>{
 const missing=fixture([],{...sections(),workspaceRevision:undefined});
 await assert.rejects(missing.controller.compile(),/revision/i);assert.equal(missing.calls.length,0);
 const changed=fixture([reply({error:'The reviewed workspace changed.'},409)]);
 await assert.rejects(changed.controller.compile(),/changed/i);assert.equal(changed.controller.view().downloadUrl,null);
 const wrongArchive=fixture([reply(job('completed',{revision:'999'}))]);
 await assert.rejects(wrongArchive.controller.compile(),/revision/i);assert.equal(wrongArchive.controller.view().downloadUrl,null);
});

test('legacy small world fetches its import result then queues that exact snapshot',async()=>{
 const world={id:'world',name:'Small',maps:[{id:'section',project:{terrain:[1,2]}}]};
 const f=fixture([reply(world),reply(job())],{...sections(),resultKind:'world',workspaceId:null});
 await f.controller.compile();
 assert.equal(f.calls[0].url,`/api/world/osm/jobs/${importId}/result`);
 assert.equal(f.calls[1].url,'/api/world/export/jobs');
 assert.deepEqual(JSON.parse(f.calls[1].body),world);
});

test('unavailable export can retry and failed or expired jobs can be recompiled',async()=>{
 const f=fixture([reply({error:'Assets unavailable; planning remains available.'},400),reply(job()),reply(job('failed',{error:'Disk full'})),reply(job()),reply(job('expired')),reply(job())]);
 await assert.rejects(f.controller.compile(),/Assets unavailable/);
 assert.equal(f.controller.view().busy,false);
 await f.controller.compile();await f.controller.refresh();
 assert.equal(f.controller.view().downloadUrl,null);
 await f.controller.compile();await f.controller.refresh();await f.controller.compile();
 assert.equal(f.calls.filter(c=>c.method==='POST').length,4);
});

test('transient status failure hides download but retains the matching job for status retry',async()=>{
 const f=fixture([reply(job('completed')),reply({error:'Temporarily offline'},503),reply(job('completed'))]);
 await f.controller.compile();await assert.rejects(f.controller.refresh(),/offline/);
 assert.equal(f.controller.view().downloadUrl,null);
 await f.controller.refresh();
 assert.ok(f.controller.view().downloadUrl);assert.equal(f.calls.filter(c=>c.method==='POST').length,1);
});

test('install requires an explicit boolean confirmation, validates names and uses only JSON POST after status',async()=>{
 const f=fixture([reply(job('completed')),reply(job('completed')),reply({installed:'test-mods/Pilna_42',modsDirectory:'test-mods'})]);
 await f.controller.compile();
 for(const confirmed of [false,undefined,'true',1])await assert.rejects(f.controller.install({...naming,confirmed}),/confirm/i);
 for(const mapFolder of ['../mods','C:\\mods','map/folder','map%2ffolder','.hidden','a\n','CON','NUL.txt','maps.'])await assert.rejects(f.controller.install({...naming,mapFolder}),/folder|identifier/i);
 for(const modId of ['../bad','a?install=true','COM1','a'.repeat(65)])await assert.rejects(f.controller.install({...naming,modId}),/identifier|id/i);
 for(const modName of ['', 'title\nMods=bad','x'.repeat(81)])await assert.rejects(f.controller.install({...naming,modName}),/name/i);
 assert.equal(f.calls.length,1);
 await f.controller.install(naming);
 assert.deepEqual(f.calls[1],{url:`/api/export/jobs/${jobId}`,method:'GET',body:undefined,headers:undefined});
 assert.deepEqual(f.calls[2],{
  url:`/api/export/jobs/${jobId}/download?install=true&modId=Pilna_42&mapFolder=Pilna.Map&modName=%D0%9F%D0%B8%D0%BB%D1%8C%D0%BD%D0%B0+%26+area`,
  method:'POST',headers:{'Content-Type':'application/json'},body:'{}'
 });
 assert.equal(f.calls.filter(c=>c.url.includes('install=true')&&c.method==='GET').length,0);
});

test('never installs a running, failed, expired or mismatched archive even if previously completed',async()=>{
 for(const next of [job('running'),job('failed'),job('expired'),job('completed',{id:'d'.repeat(32)})]){
  const f=fixture([reply(job('completed')),reply(next)]);await f.controller.compile();
  await assert.rejects(f.controller.install(naming),/ready|completed|match/i);
  assert.equal(f.calls.length,2);assert.equal(f.controller.view().downloadUrl,null);
 }
});

test('malicious identifiers are rejected before use and server download URLs never become links',async()=>{
 for(const id of ['../bad','a?install=true','https://evil.test/', 'c'.repeat(31)]){
  const f=fixture([reply(job('completed',{id}))]);await assert.rejects(f.controller.compile(),/job|identifier/i);
  assert.equal(f.controller.view().downloadUrl,null);
 }
 const f=fixture([reply(job('completed',{downloadUrl:'https://evil.test/?install=true'}))]);
 await f.controller.compile();assert.equal(f.controller.view().downloadUrl,`/api/export/jobs/${jobId}/download`);
 for(const result of [{...sections(),workspaceId:'../bad'},{...sections(),id:'../bad'},{...sections(),resultKind:'unknown'}]){
  const invalid=fixture([],result);await assert.rejects(invalid.controller.compile(),/import|identifier|workspace/i);assert.equal(invalid.calls.length,0);
 }
});

test('changed input identity drops the archive even when the import id stays the same',async()=>{
 for(const change of [{revision:8},{key:'new furniture'},{workspaceId:'e'.repeat(32)},{resultKind:'world',workspaceId:null},{id:'f'.repeat(32)}]){
  const f=fixture([reply(job('completed'))]);await f.controller.compile();
  f.setResult({...sections(),...change});await f.controller.refresh();
  assert.equal(f.controller.view().downloadUrl,null);assert.equal(f.controller.view().job,null);
 }
});

test('reset and new imports discard late compile results without unlocking newer operations',async()=>{
 const old=deferred(),fresh=deferred();const f=fixture([()=>old.promise,()=>fresh.promise]);
 const pending=f.controller.compile();f.controller.reset();f.setResult({...sections(),revision:8});
 const newer=f.controller.compile();old.resolve(reply(job('completed')));await pending;
 assert.equal(f.controller.view().busy,true);assert.equal(f.controller.view().job,null);
 fresh.resolve(reply(job('completed',{id:'d'.repeat(32)})));await newer;
 assert.equal(f.controller.view().job.id,'d'.repeat(32));
});

test('stale legacy GET cannot queue a world and stale status cannot install',async()=>{
 const delayed=deferred();const f=fixture([()=>delayed.promise],{...sections(),resultKind:'world',workspaceId:null});
 const compiling=f.controller.compile();f.setResult({...sections(),key:'changed'});delayed.resolve(reply({id:'old-world',maps:[]}));await compiling;
 assert.equal(f.calls.length,1);assert.equal(f.controller.view().job,null);
 const status=deferred(),g=fixture([reply(job('completed')),()=>status.promise]);await g.controller.compile();
 const installing=g.controller.install(naming);g.controller.reset();status.resolve(reply(job('completed')));await installing;
 assert.equal(g.calls.length,2);assert.equal(g.controller.view().job,null);
});

test('parent lock prevents actions and a lock during status validation prevents installation',async()=>{
 const status=deferred(),f=fixture([reply(job('completed')),()=>status.promise]);
 f.controller.setLocked(true);await assert.rejects(f.controller.compile(),/locked|busy/i);assert.equal(f.calls.length,0);
 f.controller.setLocked(false);await f.controller.compile();const pending=f.controller.install(naming);
 f.controller.setLocked(true);status.resolve(reply(job('completed')));await assert.rejects(pending,/locked/i);assert.equal(f.calls.length,2);
 f.controller.setLocked(false);assert.ok(f.controller.view().downloadUrl);
});

test('revoking confirmation while checking status prevents POST; naming is captured at click time',async()=>{
 const delayed=deferred(),f=fixture([reply(job('completed')),()=>delayed.promise]);await f.controller.compile();
 let confirmed=true;const options={...naming,authorize:()=>confirmed};const pending=f.controller.install(options);
 confirmed=false;options.modId='Changed';delayed.resolve(reply(job('completed')));
 await assert.rejects(pending,/confirm/i);assert.equal(f.calls.length,2);
});

test('compile double click creates one job; checking progress does not enqueue it again',async()=>{
 const delayed=deferred(),f=fixture([()=>delayed.promise,reply(job('running'))]);const pending=f.controller.compile();
 await assert.rejects(f.controller.compile(),/busy/i);delayed.resolve(reply(job()));await pending;
 await f.controller.compile();assert.equal(f.calls.filter(c=>c.method==='POST').length,1);
});

test('expired server reference permits recompiling instead of trapping the user in status retries',async()=>{
 const f=fixture([reply(job('completed')),reply({error:'Export job not found.'},404),reply(job())]);
 await f.controller.compile();await assert.rejects(f.controller.refresh(),/not found/);
 await f.controller.compile();assert.equal(f.calls[2].method,'POST');
});

test('browser controls use the shared queue, revoke consent on edits and keep downloads read-only',async t=>{
 let chromium;
 try{({chromium}=await import('../tools/BrowserSmoke/node_modules/playwright/index.mjs'));}
 catch(e){if(e.code==='ERR_MODULE_NOT_FOUND'){t.skip('Optional BrowserSmoke Playwright dependency is not installed.');return;}throw e;}
 const {readFile}=await import('node:fs/promises');
 const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL||'chrome'});
 t.after(()=>browser.close());
 const page=await browser.newPage({viewport:{width:360,height:850}}),calls=[],errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 let queueAttempts=0;
 await page.route('**/*',async route=>{
  const request=route.request(),url=new URL(request.url());
  if(url.pathname.startsWith('/api/')){
   calls.push({url:url.pathname+url.search,method:request.method(),body:request.postData(),headers:request.headers()});
   let body,status=200;
   if(url.pathname==='/api/export/jobs')body=[];
   else if(url.pathname===`/api/world/workspaces/${workspaceId}/export/jobs`){
    if(++queueAttempts===1){body={error:'Export assets unavailable. Retry later.'};status=400;}else body=job();
   }else if(url.pathname===`/api/export/jobs/${jobId}`)body=job('completed');
   else if(url.pathname===`/api/export/jobs/${jobId}/download`&&request.method()==='POST')body={installed:'test-only/Pilna_42'};
   else if(url.pathname===`/api/export/jobs/${jobId}/download`)return route.fulfill({contentType:'application/zip',body:Buffer.from('504b0506000000000000000000000000000000000000','hex')});
   else throw Error('Unexpected browser API: '+url.pathname);
   return route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  }
  if(['/world-build-output.js','/export-jobs.js','/world-build-output.css'].includes(url.pathname))return route.fulfill({
   contentType:url.pathname.endsWith('.css')?'text/css':'text/javascript',
   body:await readFile(new URL('../src/SimplePZMapmaker.Web/wwwroot'+url.pathname,import.meta.url),'utf8')
  });
  return route.fulfill({contentType:'text/html',body:'<!doctype html><link rel="stylesheet" href="/world-build-output.css"><dialog open><button id="current-editor">Current editor</button><div class="osm-world-outcome"></div><div class="osm-world-primary-actions"></div></dialog>'});
 });
 await page.goto('http://world-build.test/');
 await page.evaluate(async()=>{
  const {installWorldBuildOutput}=await import('/world-build-output.js');
  window.result=null;window.notices=[];
  window.build=installWorldBuildOutput({dialog:document.querySelector('dialog'),getResult:()=>window.result,status:(...args)=>window.notices.push(args)});
 });
 const section=page.locator('.world-build-output'),compile=section.getByRole('button',{name:'Compile world'}),confirm=section.locator('[data-build="confirm"]'),install=section.getByRole('button',{name:'Install compiled mod'}),download=section.locator('[data-build="download"]');
 assert.equal(await section.isVisible(),false);
 await page.evaluate(async result=>{window.result=result;await window.build.refresh();},sections());
 assert.equal(await section.isVisible(),true);
 assert.equal(await section.evaluate(e=>e.nextElementSibling.className),'osm-world-outcome');
 assert.equal(await install.isEnabled(),false);
 await compile.click();await page.waitForFunction(()=>document.querySelector('[data-build="state"]').textContent.includes('assets unavailable'));
 assert.equal(await page.locator('#current-editor').isEnabled(),true);
 await section.getByRole('button',{name:'Retry compile'}).click();
 await page.waitForFunction(()=>!document.querySelector('[data-build="compile"]').disabled);
 assert.equal(await install.isEnabled(),false);assert.equal(await download.isVisible(),false);
 await section.getByRole('button',{name:'Check compile status'}).click();await download.waitFor({state:'visible'});
 assert.equal(await page.locator('#export-jobs').count(),1);
 assert.equal(await install.isEnabled(),false);
 const href=await download.getAttribute('href');assert.match(href,/^\/api\/export\/jobs\/[a-f0-9]{32}\/download\?/);assert.doesNotMatch(href,/install=/);
 await confirm.check();assert.equal(await install.isEnabled(),true);
 await page.evaluate(()=>window.build.setLocked(true));assert.equal(await install.isEnabled(),false);assert.equal(await download.isVisible(),false);
 await page.evaluate(()=>window.build.setLocked(false));
 await section.locator('[data-build="modId"]').fill('Pilna_42');assert.equal(await confirm.isChecked(),false);
 await section.locator('[data-build="mapFolder"]').fill('../mods');await confirm.check();assert.equal(await install.isEnabled(),false);
 await section.locator('[data-build="mapFolder"]').fill('PilnaMap');await confirm.check();await install.click();
 await page.waitForFunction(()=>document.querySelector('[data-build="state"]').textContent.startsWith('Installed compiled mod'));
 assert.equal(await confirm.isChecked(),false);
 const installs=calls.filter(c=>c.url.includes('install=true'));assert.equal(installs.length,1);
 assert.equal(installs[0].method,'POST');assert.equal(installs[0].body,'{}');assert.equal(installs[0].headers['content-type'],'application/json');
 const installIndex=calls.indexOf(installs[0]);assert.equal(calls[installIndex-1].url,`/api/export/jobs/${jobId}`);
 await page.evaluate(async()=>{window.result={...window.result,revision:8};await window.build.refresh();});
 assert.equal(await download.isVisible(),false);assert.equal(await install.isEnabled(),false);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
 assert.deepEqual(errors,[]);
});
