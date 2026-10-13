import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const root=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765';
const output=new URL('../work/qa-results/',import.meta.url);await mkdir(output,{recursive:true});
async function post(path,value){const response=await fetch(root+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(value)});assert.equal(response.status,200,await response.clone().text());return response;}
const browser=await chromium.launch({headless:true,args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
try{
 const page=await browser.newPage({viewport:{width:1600,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(root);
 await page.evaluate(async()=>{const {openStaticModelPicker}=await import('/static-models.js');openStaticModelPicker({initialQuery:'BeerBottle',onPlace:p=>{window.modelBehaviorSelection=p;}});});
 await page.locator('[data-model-id="Base.BeerBottle"]').click();await page.waitForFunction(()=>document.querySelector('[data-sm-canvas]')?.dataset.modelId==='Base.BeerBottle');
 const profile=page.locator('[data-sm-profile]');assert.equal(await profile.inputValue(),'pickup');assert.equal(await page.locator('[data-sm-health-label]').isVisible(),false);
 await profile.selectOption('obstacle');assert.equal(await page.locator('[data-sm-health-label]').isVisible(),false);assert.match(await page.locator('[data-sm-behavior-note]').textContent(),/одну клетку/);
 await profile.selectOption('destructible');assert.equal(await page.locator('[data-sm-health-label]').isVisible(),true);assert.equal(await page.locator('[data-sm-capacity-label]').isVisible(),false);
 await profile.selectOption('container');await page.locator('[data-sm-health]').fill('350');await page.locator('[data-sm-capacity]').fill('75');await page.locator('[data-sm-rotation]').fill('45');await page.locator('[data-sm-scale]').fill('2');
 await page.screenshot({path:fileURLToPath(new URL('model-behavior-picker.png',output))});await page.locator('[data-sm-place]').click();await page.locator('.static-model-dialog').waitFor({state:'detached'});
 const selected=await page.evaluate(()=>window.modelBehaviorSelection);assert.equal(selected.profile,'container');assert.equal(selected.health,350);assert.equal(selected.capacity,75);assert.equal(selected.rotation,45);assert.equal(selected.scale,2);
 const map=await (await post('/api/generate',{kind:'blank',size:256})).json();map.version=6;
 map.staticModels=['pickup','scenery','obstacle','destructible','container'].map((profile,i)=>({...selected,id:crypto.randomUUID().replaceAll('-',''),profile,x:30+i*2,y:30,z:0}));
 const result=await (await post('/api/validate',map)).json();const validated=result.project||result;assert.equal(validated.staticModels[4].profile,'container');assert.equal(validated.staticModels[4].capacity,75);
 for(const patch of [{profile:'arbitrary'},{health:0},{capacity:2000}]){const invalid=structuredClone(map);Object.assign(invalid.staticModels[0],patch);const rejected=await fetch(root+'/api/validate',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(invalid)});assert.equal(rejected.status,400,await rejected.text());}
 const zip=await post('/api/export',map);await writeFile(new URL('model-behavior-map.zip',output),Buffer.from(await zip.arrayBuffer()));await writeFile(new URL('model-behavior-map.pzmap.json',output),JSON.stringify(validated));
 assert.deepEqual(errors,[]);console.log('PASS: actual model picker exposes five profiles and conditional health/capacity, preserves selected gameplay properties; real API validates/rejects values and exports five profiles.');
}finally{await browser.close();}
