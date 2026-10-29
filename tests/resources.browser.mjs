// Run against an isolated editor workspace with a read-only installed-game catalog:
// PZMAP_TEST_URL=http://127.0.0.1:48769 node tests/resources.browser.mjs
// PZMAP_PLAYWRIGHT_MODULE may point to a bundled playwright/index.mjs.
import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import path from 'node:path';
const modulePath=process.env.PZMAP_PLAYWRIGHT_MODULE;
const {chromium}=await import(modulePath?pathToFileURL(modulePath).href:'playwright');
const base=process.env.PZMAP_TEST_URL??'http://127.0.0.1:48769',output=path.resolve('work/resource-browser');
await mkdir(output,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.PZMAP_BROWSER_EXECUTABLE,channel:process.env.PZMAP_BROWSER_CHANNEL});
try{
 const page=await browser.newPage({viewport:{width:800,height:900}}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/resource-palette-test',route=>route.fulfill({contentType:'text/html',body:`<!doctype html><link rel="stylesheet" href="/style.css"><title>Resource palette regression</title>
 <dialog id="building-dialog" class="building-dialog"><section class="be-resource-palette">
 <label>Tool<select id="be-palette-role"><option value="object">Objects</option><option value="floor">Floors</option></select></label>
 <label>Category<select id="be-palette-category"><option value="">All categories</option></select></label><input id="be-palette-search" aria-label="Resource search">
 <div id="be-palette-grid" class="be-palette-grid"></div><div class="be-palette-pagination"><button id="be-palette-prev">Back</button><button id="be-palette-next">Next</button></div><p id="be-palette-status" role="status"></p>
 <section id="be-recent-section" hidden><h3>Recent</h3><div id="be-palette-recent" class="be-palette-grid"></div></section></section></dialog>`}));
 await page.goto(base+'/resource-palette-test');
 await page.evaluate(async()=>{
  const {EmbeddedResourcePalette,SpriteImages}=await import('/resources.js');
  window.chosen=[];window.palette=new EmbeddedResourcePalette({images:new SpriteImages(),choose:async(role,value)=>{window.chosen.push({role,value});}});
  document.getElementById('building-dialog').showModal();document.getElementById('be-palette-category').value='furniture';document.getElementById('be-palette-search').value='Hospital';await window.palette.search();
 });
 const cards=page.locator('#be-palette-grid .asset-card');assert.equal(await cards.count(),2);
 assert.match(await cards.first().getAttribute('title'),/Assembled object · 2 tiles/);
 await page.waitForFunction(()=>[...document.querySelectorAll('#be-palette-grid canvas')].every(c=>c.getContext('2d').getImageData(0,0,c.width,c.height).data.some((v,i)=>i%4===3&&v)));
 assert.equal(await page.locator('#be-palette-grid canvas[aria-label="Image not available"]').count(),0);
 await page.locator('.be-resource-palette').screenshot({path:path.join(output,'whole-hospital-bed.png')});
 await cards.first().click();await page.waitForFunction(()=>window.chosen.length===1);
 const selected=await page.evaluate(()=>window.chosen[0]);assert.equal(selected.role,'object');assert.equal(selected.value.parts.length,2);
 assert.deepEqual(selected.value.parts.map(p=>p.tile),['furniture_bedding_01_64','furniture_bedding_01_65']);
 await page.getByRole('combobox',{name:'Object orientation'}).selectOption('E');
 await page.waitForFunction(()=>document.querySelectorAll('#be-palette-grid .asset-card').length===1&&document.getElementById('be-palette-status').textContent.startsWith('1–1 / 1'));
 assert.match(await cards.first().getAttribute('title'),/Direction E/);
 await page.getByRole('checkbox',{name:'Advanced: raw tiles'}).check();
 await page.waitForFunction(()=>document.querySelectorAll('#be-palette-grid .asset-card').length===4);
 assert.equal(await page.getByRole('combobox',{name:'Object orientation'}).isDisabled(),true);assert.match(await cards.first().getAttribute('title'),/Raw sprite part/);
 await page.locator('.be-resource-palette').screenshot({path:path.join(output,'raw-hospital-bed.png')});
 // Exercise the interface the native editor integrates, including stable totals/pages.
 const pages=await page.evaluate(async()=>{
  const {resourceSearchUrl,createResourceCard}=await import('/resources.js');const query={role:'object',q:'Bed',category:'furniture',source:'game',facing:'S',limit:1};
  const first=await fetch(resourceSearchUrl(query)).then(r=>r.json()),second=await fetch(resourceSearchUrl({...query,offset:1})).then(r=>r.json());
  const button=createResourceCard(first.items[0],{role:'object',images:window.palette.images,className:'native-asset-card',width:100,height:74});document.body.append(button);
  return {first,second,nativeClass:button.className,nativeId:button.dataset.nativeAsset};
 });
 assert.ok(pages.first.total>1);assert.equal(pages.first.total,pages.second.total);assert.notEqual(pages.first.items[0].id,pages.second.items[0].id);assert.equal(pages.nativeId,pages.first.items[0].id);assert.equal(pages.nativeClass,'native-asset-card');
 assert.deepEqual(errors,[]);console.log('PASS browser: whole/raw cards, complete thumbnail pixels, placement, orientation, paging and native card helper');
 console.log('Screenshots: '+output);
}finally{await browser.close();}
