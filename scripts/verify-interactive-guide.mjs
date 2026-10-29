// Fresh browser + isolated local app. Never reloads or changes the user's tab.
import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48778',out=new URL('../work/guide-acceptance/',import.meta.url);
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL||'chrome',args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const context=await browser.newContext({viewport:{width:1440,height:1100},acceptDownloads:true});
const page=await context.newPage(),errors=[],mutations=[];page.setDefaultTimeout(15000);page.on('pageerror',e=>errors.push(e.message));
page.on('request',r=>{if(r.method()==='POST')mutations.push(new URL(r.url()).pathname);});
const $=s=>page.locator(s),idle=()=>page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
async function openGuide(){await $('[data-command-menu="project"] > summary').click();await $('#open-help').click();await $('#guide-dialog').waitFor({state:'visible'});}
async function start(id){console.log('Tour:',id);await $('#guide-start-'+id).click();await $('#guide-card').waitFor({state:'visible'});}
async function next(){await $('#guide-next').click();}
async function show(){await $('#guide-show').click();await page.waitForTimeout(80);}
async function pause(){await $('#guide-pause').click();await $('#guide-overlay').waitFor({state:'hidden'});}
async function targetVisible(){await page.waitForFunction(()=>document.querySelector('#guide-overlay').dataset.targetVisible==='true');}
async function screenshot(name){await page.screenshot({path:fileURLToPath(new URL(name+'.png',out))});}
const report={};
try{
 await page.goto(base,{waitUntil:'domcontentloaded'});await idle();await $('#guide-invitation').waitFor({state:'visible'});await $('#guide-welcome-open').press('Space');await $('#guide-dialog').waitFor({state:'visible'});
 assert.equal(await $('#guide-task-list .guide-task').count(),6);
 await screenshot('task-library-en');await $('#guide-language').selectOption('ru');assert.equal(await $('#guide-hub-title').innerText(),'Интерактивный гайд');await screenshot('task-library-ru');
 const before=mutations.length;
 await start('first-map');await show();await targetVisible();const activeTool=await $('.tool-grid .active').getAttribute('data-tool');await $('#guide-step-title').press('b');assert.equal(await $('.tool-grid .active').getAttribute('data-tool'),activeTool);await next();await show();
 assert.equal(await $('#new-dialog').evaluate(e=>e.open),true);
 assert.equal(await $('#guide-overlay').evaluate(e=>e.parentElement.id),'new-dialog');
 await $('#new-name').fill('Guide acceptance map');await $('#new-kind').selectOption('blank');
 await screenshot('new-map-step');
 assert.equal(mutations.length,before,'guide navigation must not create/import/export anything');
 // Actual user action: creating a practice map is separate from guide navigation.
 await $('#new-form [type="submit"]').click();await idle();assert.equal(await $('#project-name').inputValue(),'Guide acceptance map');
 await next();await show();await targetVisible();await next();await show();await targetVisible();
 assert.equal(await $('.map-layers-workbench').evaluate(e=>e.open),true);
 await page.setViewportSize({width:390,height:700});await page.waitForTimeout(100);
 await page.waitForFunction(()=>{const r=document.querySelector('#guide-card').getBoundingClientRect();return r.x>=8&&r.right<=innerWidth-7&&r.y>=8&&r.bottom<=innerHeight-7;});const bounds=await $('#guide-card').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=391&&bounds.y>=0&&bounds.y+bounds.height<=701);await screenshot('narrow-guide');
 await page.setViewportSize({width:1440,height:1100});
 await pause();await page.reload();await idle();assert.equal(await $('#guide-overlay').isVisible(),false);assert.equal(await $('#guide-invitation').count(),0);
 await openGuide();await start('first-map');assert.equal(await $('#guide-overlay').getAttribute('data-step'),'layers');
 await $('#guide-step-title').press('Escape');assert.equal(await $('#guide-overlay').isVisible(),false);
 await openGuide();await start('first-map');await next();await next();await next();
 assert.equal(await $('#guide-dialog').evaluate(e=>e.open),true);assert.match(await $('#guide-task-list').innerText(),/Пройдено/);
 await start('first-map');assert.equal(await $('#guide-overlay').getAttribute('data-step'),'project');await pause();report.resumeCompletionRestart=true;

 // Earlier document-capture geometry shortcuts must not consume guide keys.
 if(await $('#workspace-edit').count())await $('#workspace-edit').click();
 await $('[data-tool="road"]').click();await $('.geographic-authoring-workbench > summary').filter({hasText:/^Geometry tools/}).click();await $('[data-geometry-tool="fence"]').click();
 await openGuide();await start('first-map');await $('#guide-next').press('Enter');
 assert.equal(await $('#guide-overlay').getAttribute('data-step'),'base');await $('#guide-next').press('Escape');
 assert.equal(await $('#guide-overlay').isVisible(),false);assert.equal(await $('[data-geometry-tool="fence"]').getAttribute('aria-pressed'),'true');
 await $('.geographic-authoring-workbench button').filter({hasText:'Cancel drawing'}).click();report.geometryKeyboardIsolation=true;

 // Import: editable dialog and offline choice, without a source request.
 await openGuide();await start('import');await show();await targetVisible();await next();await show();await targetVisible();
 await $('#rp-coordinates > summary').click();await $('#import-lat').fill('55.75');await $('#import-lon').fill('37.62');await next();await show();await next();await show();
 await $('#import-source').selectOption('file');await screenshot('offline-source-step');
 await $('#guide-pause').press('Escape');assert.equal(await $('#source-workbench').evaluate(e=>e.open),true);
 await $('#sw-close').click();report.offlineImport=true;

 // Nested native modals: popover follows the active modal and remains operable.
 await openGuide();await start('world');await show();await targetVisible();await next();await show();await targetVisible();
 assert.equal(await $('#guide-overlay').evaluate(e=>e.parentElement.id),'osm-world-dialog');
 await $('#osm-world-width').fill('6');await next();await show();await targetVisible();await screenshot('connected-world-step');
 // Missing controls are explicit and never auto-close the user's modal.
 await next();await next();await next();await show();
 assert.equal(await $('#osm-world-dialog').evaluate(e=>e.open),true);
 assert.equal(await $('#guide-overlay').getAttribute('data-target-visible'),'false');assert.match(await $('#guide-step-status').innerText(),/Закройте/);
 await $('#osm-world-close').click();await show();await targetVisible();
 assert.equal(await $('#world-workspaces').evaluate(e=>e.open),true);await screenshot('section-workspaces-step');await pause();await $('#world-close').click();report.worldSections=true;

 // Use a known building fixture; no guide action loads or modifies it.
 await $('#project-file').setInputFiles(fileURLToPath(new URL('../examples/Assets/resources.pzmap.json',import.meta.url)));await idle();
 await openGuide();await start('building');await show();await targetVisible();
 await next();await show();assert.match(await $('#guide-step-status').innerText(),/В библиотеке откройте здание/);
 const edit=$('#building-list button').first();await edit.click();await $('#building-dialog').waitFor({state:'visible'});
 await show();await targetVisible();assert.equal(await $('#guide-overlay').evaluate(e=>e.parentElement.id),'building-dialog');
 await $('#be-name').fill('Unapplied guide draft');await next();await show();await targetVisible();await screenshot('workshop-step');
 await pause();await $('#be-cancel').click();assert.equal(await $('#building-dialog').evaluate(e=>e.open),false);report.workshopDraft=true;

 await openGuide();await start('settlement');await show();await targetVisible();await screenshot('generation-step');
 await next();await show();await targetVisible();
 let started,release;const waiting=new Promise(resolve=>started=resolve),hold=new Promise(resolve=>release=resolve);
 await page.route('**/api/generation/studio/preview',async route=>{started();await hold;try{await route.abort('aborted');}catch{}});
 await $('#gs-preview').click();await waiting;
 await page.waitForFunction(()=>!document.querySelector('#guide-pause').disabled&&!document.querySelector('#guide-tasks').disabled&&!document.querySelector('#guide-next').disabled&&document.querySelector('#guide-show').disabled);
 await pause();await $('#gs-cancel').click();release();await page.unroute('**/api/generation/studio/preview');await page.waitForFunction(()=>!document.querySelector('#gs-close').disabled);
 await $('#gs-close').click();report.settlementPreview=true;report.guideAvailableDuringPreview=true;
 if(await $('#workspace-check').count())await $('#workspace-check').click();
 if(await $('#export-jobs').isVisible()&&await $('#export-jobs').evaluate(e=>e.classList.contains('collapsed')))await $('.export-jobs-toggle').click();
 while(await $('.export-jobs [data-export-job] button').filter({hasText:'Dismiss'}).count())await $('.export-jobs [data-export-job] button').filter({hasText:'Dismiss'}).first().click();
 await openGuide();await start('export');await show();await targetVisible();await next();await show();await targetVisible();
 await next();await next();await next();await show();assert.equal(await $('#guide-overlay').getAttribute('data-target-visible'),'false');await screenshot('empty-export-jobs');
 // User explicitly opens export and queues exactly one real native build.
 const exportCount=mutations.filter(p=>p==='/api/export/jobs').length;await $('#guide-back').click();await show();await $('#export-map').click();await $('#download-mod').click();
 await next();await show();await targetVisible();await page.waitForFunction(()=>[...document.querySelectorAll('#export-jobs [data-export-job]')].some(e=>e.textContent.includes('completed')),{},{timeout:60000});
 assert.equal(mutations.filter(p=>p==='/api/export/jobs').length,exportCount+1);await screenshot('completed-export-job');await pause();report.realNativeExport=true;
 await openGuide();await $('#guide-reference').click();assert.equal(await $('#help-dialog').evaluate(e=>e.open),true);assert.doesNotMatch(await $('#help-dialog').innerText(),/outside the building/);await $('#help-dialog [data-close]').click();report.exportReference=true;
 assert.deepEqual(errors,[]);report.errors=errors;
 // Guide navigation never queues a project mutation. Explicit user actions above
 // create/load a map, request a cancelled preview, and queue one native export.
 const allowed=['/api/generate','/api/project/validate','/api/assets/describe','/api/preview/building/draft','/api/building/modules/preset/entrance-lobby','/api/generation/studio/preview','/api/export/jobs'];assert.deepEqual(mutations.filter(p=>!allowed.includes(p)&&!p.startsWith('/api/recovery/')),[]);report.postRequests=mutations.map(p=>p.startsWith('/api/recovery/')?'/api/recovery/<isolated-test-id>':p);
 // Storage policies can block guide persistence independently of editor recovery.
 const privateContext=await browser.newContext({viewport:{width:1100,height:800}});
 await privateContext.addInitScript(()=>{
  const get=Storage.prototype.getItem,set=Storage.prototype.setItem;
  Storage.prototype.getItem=function(key){if(key==='simplepzmap.guide.v1')throw Error('Guide storage blocked');return get.call(this,key);};
  Storage.prototype.setItem=function(key,value){if(key==='simplepzmap.guide.v1')throw Error('Guide quota blocked');return set.call(this,key,value);};
 });
 const privatePage=await privateContext.newPage();privatePage.on('pageerror',e=>errors.push(e.message));await privatePage.goto(base);
 await privatePage.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
 await privatePage.locator('#guide-welcome-open').click();await privatePage.locator('#guide-start-import').click();await privatePage.locator('#guide-next').click();await privatePage.locator('#guide-pause').click();
 await privatePage.locator('[data-command-menu="project"] > summary').click();await privatePage.locator('#open-help').click();await privatePage.locator('#guide-start-import').click();
 assert.equal(await privatePage.locator('#guide-overlay').getAttribute('data-step'),'place');await privateContext.close();report.blockedStorage=true;
 assert.deepEqual(errors,[]);await writeFile(new URL('proof.json',out),JSON.stringify(report,null,2));console.log('PASS interactive guide: six workflows, real modal controls, actual practice map, resume/restart, RU/EN, 390px layout, blocked storage and no guide-generated mutations.');
}catch(error){await screenshot('failure');console.log(await page.evaluate(()=>({open:[...document.querySelectorAll('dialog[open]')].map(d=>d.id),step:document.querySelector('#guide-overlay')?.dataset,status:document.querySelector('#guide-step-status')?.textContent})));throw error;}finally{await browser.close();}
