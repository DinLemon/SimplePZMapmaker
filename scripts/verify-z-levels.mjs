const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765';
import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {writeFile,readFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const output=new URL('../work/qa-results/z-levels/',import.meta.url);await mkdir(output,{recursive:true});
const api=async(path,body)=>{const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});assert.equal(response.status,200,await response.clone().text());return response.json();};
const b=await api('/api/building/design',{building:{id:'full-z-browser',name:'64 signed levels',kind:'house',x:40,y:40,width:12,height:10},levels:32,basements:32,seed:42,shape:'rect'});
assert.equal(b.design.minLevel,-32);assert.equal(b.design.floors.length,64);assert.equal(b.design.roof,'none');
const p=await api('/api/generate',{size:256,kind:'blank'});p.version=10;p.buildings=[b];const fixture=fileURLToPath(new URL('full64.pzmap.json',output));await writeFile(fixture,JSON.stringify(p));
const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1600,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
async function save(name){const event=page.waitForEvent('download');await page.locator('#save-project').click();const file=fileURLToPath(new URL(name,output));await(await event).saveAs(file);return JSON.parse(await readFile(file,'utf8'));}
try{
 await page.goto(base);await page.waitForFunction(()=>document.querySelector('#game-status').textContent.includes('found'));
 const options=await page.locator('#map-level option').evaluateAll(items=>items.map(o=>({value:o.value,text:o.textContent})));assert.equal(options.length,65);assert.deepEqual(options[1],{value:'-32',text:'Basement -32'});assert.deepEqual(options.at(-1),{value:'31',text:'Floor 32'});assert.equal(await page.locator('#placement-levels').getAttribute('max'),'32');assert.equal(await page.locator('#placement-basements').getAttribute('max'),'32');
 await page.locator('#project-file').setInputFiles(fixture);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&document.querySelector('#building-count').textContent==='1');
 await page.locator('#placement-levels').fill('32');await page.locator('#placement-basements').fill('32');await page.locator('#map-level').selectOption('31');
 await page.locator('#open-building-editor').click();await page.locator('#building-list button').filter({hasText:'64 signed levels'}).click();await page.locator('#building-dialog').waitFor({state:'visible'});
 assert.equal(await page.locator('#be-floors button').count(),64);assert.equal(await page.locator('#be-roof').inputValue(),'none');assert.equal(await page.locator('#be-add-floor').isDisabled(),true);assert.equal(await page.locator('#be-add-basement').isDisabled(),true);
 await page.locator('#be-floors button').first().click();assert.equal(await page.locator('#be-floors [aria-pressed=true]').getAttribute('aria-label'),'Basement -32');await page.locator('#be-floors button').last().click();assert.equal(await page.locator('#be-floors [aria-pressed=true]').getAttribute('aria-label'),'Floor 32');
 await page.waitForFunction(()=>document.querySelector('#be-visual-canvas').dataset.state==='ready'&&document.querySelector('#be-visual-state').textContent.includes('Floor z=31'));
 await page.screenshot({path:fileURLToPath(new URL('../docs/screenshots/11-full-z-range.png',import.meta.url)),fullPage:true});
 // A roof selection must fail atomically instead of writing a 65th level.
 await page.locator('#be-roof').selectOption('flat');assert.equal(await page.locator('#be-roof').inputValue(),'none');assert.match(await page.locator('#be-validation').textContent(),/roof|z=32/i);
 const templateEvent=page.waitForEvent('download');await page.locator('#be-template-export').click();const templateFile=fileURLToPath(new URL('full64.pzbuilding.json',output));await(await templateEvent).saveAs(templateFile);const template=JSON.parse(await readFile(templateFile,'utf8'));assert.equal(template.version,7);assert.equal(template.building.design.floors.length,64);
 await page.locator('#be-cancel').click();await page.locator('#building-template-file').setInputFiles(templateFile);await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&document.querySelector('#building-preset').options.length===10);
 const saved=await save('full64-roundtrip.pzmap.json');assert.equal(saved.version,10);assert.equal(saved.buildingLibrary.at(-1).design.minLevel,-32);assert.equal(saved.buildingLibrary.at(-1).design.floors.length,64);assert.equal(saved.buildings[0].design.roof,'none');
 for(const level of ['-32','31']){await page.locator('#map-level').selectOption(level);await page.locator('#view-native').click();await page.waitForTimeout(250);assert.equal(await page.locator('#map-level').inputValue(),level);const result=await api('/api/preview/building',{building:b,level:Number(level)});assert.equal(result.minLevel,Number(level));assert.equal(result.maxLevel,Number(level));assert.ok(result.sprites.length>0);assert.ok(result.sprites.every(s=>s.z===Number(level)));}
 assert.deepEqual(errors,[]);console.log('PASS 64 real UI selectors, authored floors, depth/height caps, signed labels, roof rejection, template7/map10 roundtrip and native previews at -32/+31');
}finally{await browser.close();}
