// Run against a disposable editor with a saved workspace larger than 128 MiB.
// PZMAP_TEST_URL=... PZMAP_TEST_WORKSPACE_ID=... node scripts/verify-loaded-workspace.mjs
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const base=process.env.PZMAP_TEST_URL,id=process.env.PZMAP_TEST_WORKSPACE_ID;
assert.ok(base&&/^[a-f0-9]{32}$/.test(id),'Set an isolated editor URL and workspace ID.');
const out=new URL('../work/loaded-world-verification/',import.meta.url);await mkdir(out,{recursive:true});
const manifest=await (await fetch(`${base}/api/world/workspaces/${id}`)).json();
assert.ok(manifest.sections.reduce((n,s)=>n+s.bytes,manifest.metadataBytes)>128*1024*1024);
const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL});
const page=await browser.newPage({viewport:{width:1440,height:900}}),errors=[],downloads=[];
page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.method()==='GET'&&r.url().includes('/sections/'))downloads.push(r.url());});page.setDefaultTimeout(180000);
const idle=()=>page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
async function openWorld(){await page.locator('[data-command-menu=project]>summary').click();await page.locator('#open-world').click();}
try{
 await page.goto(base,{waitUntil:'domcontentloaded'});await idle();await openWorld();
 await page.locator('#world-workspaces>summary').click();await page.locator('[data-ws-list]').click();await idle();await page.locator('[data-ws-world]').selectOption(id);await idle();
 assert.equal(await page.locator('[data-ws-open-world]').isEnabled(),true);await page.locator('[data-ws-open-world]').click();await idle();
 assert.match(await page.locator('[data-ws-state]').textContent(),new RegExp(`All ${manifest.sections.length} sections`));
 assert.equal(await page.locator('#world-section-list button[data-loaded=true]').count(),manifest.sections.length);
 assert.equal(await page.locator('#world-canvas').getAttribute('data-loaded-sections'),String(manifest.sections.length));
 assert.equal(await page.locator('#world-save').isEnabled(),true);assert.equal(await page.locator('#world-export').isEnabled(),true);assert.match(await page.locator('#world-state').textContent(),/All .* sections are loaded/);
 await page.screenshot({path:fileURLToPath(new URL('all-loaded.png',out))});
 const [first,second]=manifest.sections;await page.locator(`[data-member-id="${first.id}"]`).click();await page.locator('#world-edit-section').click();await idle();
 await page.waitForFunction(()=>Number(document.querySelector('#map-canvas').dataset.residentSections)>0,{},{timeout:3000});
 assert.equal(await page.locator('#map-canvas').getAttribute('data-resident-sections'),String(manifest.sections.length),'The editor must display every resident section, not just the overview');
 assert.equal(await page.locator('#map-canvas').getAttribute('data-visible-sections'),String(manifest.sections.length),'Fit must include the entire world');
 // Browsing an unrelated saved index must not change the canvas's owner.
 const otherId='eeeeeeeeeeeeeeeeeeeeeeeeeeeeee22';
 if((await fetch(`${base}/api/world/workspaces/${otherId}`)).status===404){
  const project=await (await fetch(`${base}/api/generate`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({kind:'blank',size:256})})).json();
  const created=await fetch(`${base}/api/world/workspaces/${otherId}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({world:{format:'SimplePZWorld',version:1,id:otherId,name:'Browsing index verification',maps:[{id:'e'.repeat(32),project}],buildings:[]},expectedRevision:null})});assert.equal(created.status,200,await created.text());
 }
 await openWorld();await page.locator('[data-ws-list]').click();await idle();await page.locator('[data-ws-world]').selectOption(otherId);await idle();await page.locator('#world-close').click();
 const camera=await page.locator('#map-canvas').evaluate(c=>({...c.dataset})),box=await page.locator('#map-canvas').boundingBox();
 const x=(second.worldX-first.worldX)*256+second.width/2,y=(second.worldY-first.worldY)*256+second.height/2;
 await page.mouse.click(box.x+Number(camera.worldPanX)+x*Number(camera.worldZoom),box.y+Number(camera.worldPanY)+y*Number(camera.worldZoom));await idle();
 await page.waitForFunction(id=>document.querySelector('#map-canvas').dataset.activeSection===id,second.id);
 const moved=await page.locator('#map-canvas').evaluate(c=>({...c.dataset}));
 assert.equal(moved.worldZoom,camera.worldZoom,'Switching must preserve zoom');
 assert.ok(Math.abs(Number(moved.worldPanX)-(Number(camera.worldPanX)+(second.worldX-first.worldX)*256*Number(camera.worldZoom)))<.001,'Switching must preserve geographic camera position');
 await page.screenshot({path:fileURLToPath(new URL('continuous-editor.png',out))});
 const beforePaint=await (await fetch(`${base}/api/world/workspaces/${id}/sections/${second.id}?includeSections=false`)).json(),index=512*second.width+512,value=beforePaint.member.project.terrain[index]===2?5:2;
 await page.locator(`[data-terrain="${value}"]`).click();await page.locator('#brush-size').fill('16');
 const activeBox=await page.locator('#map-canvas').boundingBox();await page.mouse.click(activeBox.x+Number(moved.worldPanX)+512*Number(moved.worldZoom),activeBox.y+Number(moved.worldPanY)+512*Number(moved.worldZoom));
 await page.locator('#save-project').click();await idle();
 const painted=await (await fetch(`${base}/api/world/workspaces/${id}/sections/${second.id}?includeSections=false`)).json();
 assert.equal(painted.member.project.terrain[index],value,'Brush must write to the clicked section, not the previous active section');
 await page.locator('#undo').click();await page.locator('#save-project').click();await idle();
 const undone=await (await fetch(`${base}/api/world/workspaces/${id}/sections/${second.id}?includeSections=false`)).json();assert.deepEqual(undone.member.project.terrain,beforePaint.member.project.terrain,'Undo must restore only this section');
 await openWorld();await page.locator('[data-ws-world]').selectOption(id);await idle();await page.locator(`[data-member-id="${first.id}"]`).click();await page.locator('#world-edit-section').click();await idle();
 const name='Edited resident section '+Date.now();await page.locator('#project-name').fill(name);await page.locator('#project-name').press('Tab');
 await openWorld();await page.locator('#world-save').click();await idle();await page.locator(`[data-member-id="${second.id}"]`).click();await page.locator('#world-edit-section').click();await idle();
 await openWorld();await page.locator(`[data-member-id="${first.id}"]`).click();await page.locator('#world-edit-section').click();await idle();
 assert.equal(await page.locator('#project-name').inputValue(),name);assert.equal(downloads.length,manifest.sections.length,'Switching fetched a resident section again');
 const saved=await (await fetch(`${base}/api/world/workspaces/${id}/sections/${first.id}?includeSections=false`)).json();assert.equal(saved.member.project.name,name);assert.ok(saved.revision>manifest.revision);assert.deepEqual(saved.sections,[]);
 assert.deepEqual(errors,[]);await writeFile(new URL('browser-result.json',out),JSON.stringify({pass:true,sections:manifest.sections.length,storedBytes:manifest.sections.reduce((n,s)=>n+s.bytes,manifest.metadataBytes),downloads:downloads.length,savedRevision:saved.revision,errors},null,2));
 console.log(`PASS ${manifest.sections.length} resident sections above 128 MiB; edit, save and cached switching`);
}catch(e){await page.screenshot({path:fileURLToPath(new URL('failure.png',out))}).catch(()=>{});console.error({errors,state:await page.locator('[data-ws-state]').textContent().catch(()=>null)});throw e;}finally{await browser.close();}
