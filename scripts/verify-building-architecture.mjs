import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765',out=new URL('../work/architecture-ui/',import.meta.url);await mkdir(out,{recursive:true});
const presets=await (await fetch(base+'/api/building/presets')).json();assert.ok(presets.length>=24);assert.equal(new Set(presets.map(p=>p.id)).size,presets.length);
const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1600,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(60000);
const file=name=>fileURLToPath(new URL(name,out));
async function ready(){await page.waitForFunction(()=>document.getElementById('be-visual-canvas')?.dataset.state==='ready');}
async function draft(name){const event=page.waitForEvent('download');await page.locator('#be-template-export').click();await(await event).saveAs(file(name));return JSON.parse(await readFile(file(name),'utf8')).building;}
try{
 await page.goto(base);await page.waitForFunction(()=>document.getElementById('game-status').textContent.includes('найден'));
 await page.locator('#open-building-editor').click();assert.equal(await page.locator('[data-housing-preset]').count(),24);
 await page.locator('[data-housing-preset="village-brick-cottage"]').click();await ready();
 const original=await draft('before.pzbuilding.json');assert.equal(original.design.floors.length,2);
 await page.locator('#be-wing-side').selectOption('south');await page.locator('#be-wing-width').fill('4');await page.locator('#be-wing-depth').fill('2');await page.locator('#be-balcony-add').click();await ready();
 const balcony=await draft('balcony.pzbuilding.json');assert.equal(balcony.height,original.height+2);assert.equal(balcony.design.floors.length,2);assert.ok(Object.values(balcony.design.floors[0].rooms).includes('balcony'));assert.deepEqual(balcony.design.floors[0].objects,original.design.floors[0].objects);assert.deepEqual(balcony.design.floors[1].objects,original.design.floors[1].objects);
 const scene=await(await fetch(base+'/api/preview/building/draft',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({building:balcony,level:0})})).json();assert.ok(scene.sprites.some(s=>['carpentry_02_100','carpentry_02_101'].includes(s.tile)),'Actual exported railing sprites');
 await page.screenshot({path:file('balcony.png'),fullPage:true});await page.locator('#be-undo').click();await ready();const undone=await draft('undone.pzbuilding.json');assert.equal(undone.height,original.height);assert.deepEqual(undone.design,original.design);
 await page.locator('#be-wing-side').selectOption('east');await page.locator('#be-wing-width').fill('6');await page.locator('#be-wing-depth').fill('3');await page.locator('#be-annex-add').click();await ready();const annex=await draft('annex.pzbuilding.json');assert.equal(annex.width,original.width+3);assert.equal(annex.design.floors[0].rooms[Object.keys(annex.design.floors[0].rooms).at(-1)],'livingroom');
 await page.locator('#be-facade').selectOption('grey');await page.locator('#be-facade-apply').click();await ready();const facade=await draft('facade.pzbuilding.json');assert.ok(facade.design.floors.every(f=>f.edges.some(e=>e.wallTile?.startsWith('walls_exterior_house_01_'))));assert.deepEqual(facade.design.floors[0].objects,annex.design.floors[0].objects);
 await page.locator('#be-roof').selectOption('hipped');await ready();await page.locator('#be-all-levels').check();await ready();for(const id of ['be-facade-apply','be-balcony-add','be-annex-add'])assert.ok(await page.locator('#'+id).isDisabled());await page.screenshot({path:file('facade-roof.png'),fullPage:true});await page.locator('#be-all-levels').uncheck();await ready();
 assert.deepEqual(errors,[]);await writeFile(file('result.json'),JSON.stringify({status:'passed',presets:presets.length,balcony:balcony.height,annex:annex.width,errors},null,2));console.log('PASS actual architecture UI:24 preset cards, balcony native rails, annex, façade, undo, roof preview and readonly controls');
}finally{await browser.close();}
