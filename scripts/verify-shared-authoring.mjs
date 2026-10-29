// Runs in an isolated Chrome context and editor workspace; never uses the user's active map.
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const base=process.env.PZMAP_TEST_URL??'http://127.0.0.1:48778',out=new URL('../work/shared-authoring-verification/',import.meta.url);
await mkdir(out,{recursive:true});
async function api(path,body){const response=await fetch(base+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const text=await response.text();assert.equal(response.status,200,text.slice(0,1000));return JSON.parse(text);}
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const page=await browser.newPage({viewport:{width:1440,height:900}}),errors=[];page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
async function idle(){await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);}
async function menu(name,id){await page.locator(`[data-command-menu=${name}]>summary`).click();await page.locator('#'+id).click();}
async function load(p){await page.locator('#project-file').setInputFiles({name:'Shared.pzmap.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(p))});await idle();}
async function saved(name){const [download]=await Promise.all([page.waitForEvent('download'),page.locator('#save-project').click()]);await download.saveAs(fileURLToPath(new URL(name,out)));return JSON.parse(await readFile(await download.path(),'utf8'));}
try{
 const p=await api('/api/generate',{kind:'blank',size:256,name:'Shared authoring proof'});p.spawns=[{x:240,y:240}];
 for(let y=28;y<32;y++)for(let x=8;x<220;x++)p.terrain[y*256+x]=2;
 for(let x=8;x<220;x++)p.terrain[32*256+x]=6;
 await page.goto(base);await idle();await load(p);
 console.log('Studio: template preview/apply/undo');
 await menu('tools','open-generation-studio');await page.locator('#gs-strategy').selectOption('templates');await page.locator('#gs-maximum-buildings').fill('3');
 await page.locator('#gs-preview').click();await page.waitForFunction(()=>!document.querySelector('#gs-apply').disabled);assert.match(await page.locator('#gs-summary-a').textContent(),/3 added/);
 await page.screenshot({path:fileURLToPath(new URL('studio-template-preview.png',out))});await page.locator('#gs-apply').click();await page.waitForFunction(()=>document.querySelector('#gs-status').textContent.includes('Variant applied'));await page.locator('#gs-close').click();
 const populated=await saved('template-applied.pzmap.json');assert.equal(populated.buildings.length,3);assert(populated.fences.length>0);assert.equal(populated.decorations.length,0);
 await page.locator('#undo').click();assert.equal((await saved('template-undo.pzmap.json')).buildings.length,0);await page.locator('#redo').click();assert.equal((await saved('template-redo.pzmap.json')).buildings.length,3);
 console.log('Automapping: preview/apply/undo');await load(p);await menu('tools','open-automapping');
 await page.locator('#auto-area-width').fill('256');await page.locator('#auto-area-height').fill('128');await page.locator('[data-auto=preview]').click();
 await page.waitForFunction(()=>!document.querySelector('[data-auto=apply]').disabled);await page.screenshot({path:fileURLToPath(new URL('street-preview.png',out))});await page.locator('[data-auto=apply]').click();await page.waitForFunction(()=>document.querySelector('[data-auto=status]').textContent.startsWith('Applied '));
 await page.locator('#open-automapping-dialog .authoring-close').click();const streets=await saved('street-applied.pzmap.json');assert(streets.decorations.length>0);await page.locator('#undo').click();assert.equal((await saved('street-undo.pzmap.json')).decorations.length,0);
 console.log('Doctor: checks and installed catalogs');await menu('tools','open-project-doctor');await page.getByRole('button',{name:'Check project',exact:true}).click();await page.waitForFunction(()=>document.querySelector('.project-doctor [role=status]').textContent.includes('coverage')||document.querySelector('.project-doctor [role=status]').textContent.includes('Coverage'));
 const doctorText=await page.locator('.project-doctor').textContent();assert(doctorText.includes('structure: pass'));assert(doctorText.includes('resources: pass'));await page.screenshot({path:fileURLToPath(new URL('project-doctor.png',out))});await page.locator('#open-project-doctor-dialog .authoring-close').click();
 console.log('Exchange: import separate BuildingEd draft');await menu('import','open-building-exchange');
 const xml='<building version="4" width="2" height="2"><tile_entry category="floors"><tile enum="Floor" tile="floors_interior_tilesandwood_01_40"/></tile_entry><room Name="hall" InternalName="hall" Color="100 120 140" Floor="1" InteriorWall="0"/><floor><rooms>1,1,1,1</rooms></floor></building>';
 await page.locator('.building-exchange input[type=file]').setInputFiles({name:'room.tbx',mimeType:'application/xml',buffer:Buffer.from(xml)});
 await page.waitForFunction(()=>document.querySelector('#building-dialog').open);assert.equal((await page.locator('#building-dialog').count()),1);await page.screenshot({path:fileURLToPath(new URL('imported-building-draft.png',out))});await page.locator('#be-exchange').click();await page.locator('.building-exchange select').selectOption('tmx');const [draftDownload]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export selected building',exact:true}).click()]);await draftDownload.saveAs(fileURLToPath(new URL('workshop-draft.tmx',out)));await page.locator('#open-building-exchange-dialog .authoring-close').click();await page.locator('#be-cancel').click();assert.equal((await saved('after-draft.pzmap.json')).buildings.length,0);
 assert.deepEqual(errors,[]);await writeFile(new URL('result.json',out),JSON.stringify({templateBuildings:populated.buildings.length,nativeFences:populated.fences.length,streetDecorations:streets.decorations.length,doctorChecked:true,exchangeDraft:true,browserErrors:errors},null,2));console.log('PASS shared authoring browser');
}catch(error){await page.screenshot({path:fileURLToPath(new URL('failure.png',out))}).catch(()=>{});throw error;}finally{await browser.close();}
