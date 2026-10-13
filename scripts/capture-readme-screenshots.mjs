import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48766';
const output=new URL('../docs/screenshots/',import.meta.url);await mkdir(output,{recursive:true});
const browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1800,height:1200},deviceScaleFactor:1});const errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(60000);let token;
await page.route('**/api/recovery/**',r=>r.request().method()==='GET'?r.fulfill({status:404}):r.fulfill({json:{saved:true}}));
await page.route('**/api/world/recovery/**',r=>r.request().method()==='GET'?r.fulfill({status:404}):r.fulfill({json:{saved:true}}));
const shot=async name=>page.screenshot({path:fileURLToPath(new URL(name,output)),fullPage:true});
try{
 await page.goto(base);await page.waitForFunction(()=>document.querySelector('#game-status')?.textContent.includes('found'));
 await page.locator('#project-file').setInputFiles(fileURLToPath(new URL('../examples/HousingTemplates/housing-templates.pzmap.json',import.meta.url)));await page.waitForFunction(()=>document.querySelector('#building-count').textContent==='24');
 assert.ok(!/[А-Яа-яЁё]/.test(await page.locator('body').innerText()));await shot('01-editor.png');await page.locator('#open-resources').click();await page.waitForFunction(()=>document.querySelectorAll('#asset-grid .asset-card').length>0);await shot('05-game-resources.png');await page.locator('#asset-close').click();
 await page.locator('#open-building-editor').click();await page.locator('[data-housing-preset="urban-panel-five"]').click();await page.waitForFunction(()=>document.querySelector('#be-visual-canvas')?.dataset.state==='ready');
 await page.locator('#be-all-levels').check();await page.waitForFunction(()=>document.querySelector('#be-visual-canvas')?.dataset.state==='ready');await shot('02-building-editor.png');await page.locator('#be-cancel').click();
 await page.locator('#open-world').click();await page.locator('#world-file').setInputFiles(fileURLToPath(new URL('../examples/Worlds/World-DEM.pzworld.json',import.meta.url)));await page.waitForFunction(()=>document.querySelectorAll('#world-section-list button').length===2&&!document.querySelector('#operation-dialog').open);
 await page.locator('#world-height-overlay').check();for(let i=0;i<3;i++)await page.locator('#world-zoom-in').click();await shot('03-world.png');await page.locator('#world-close').click();
 await page.locator('#new-map').click();await page.locator('#new-kind').selectOption('blank');await page.locator('#new-form [type=submit]').click();await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
 await page.locator('#open-agent-session').click();await page.locator('[data-connect]').click();await page.waitForFunction(()=>document.querySelector('[data-token]').value.length===64);token=await page.locator('[data-token]').inputValue();
 await page.locator('.agent-plan-controls summary').click();await page.locator('[data-plan-count]').fill('8');await page.locator('[data-plan-levels]').fill('5');await page.locator('[data-plan-create]').click();await page.locator('[data-plan-review]').waitFor({state:'visible'});
 await page.setViewportSize({width:1800,height:2200});await page.locator('#agent-session-dialog').evaluate(e=>e.scrollTop=0);await page.locator('#agent-session-dialog').screenshot({path:fileURLToPath(new URL('04-agent.png',output)),mask:[page.locator('[data-token]')],maskColor:'#20262d'});
 assert.deepEqual(errors,[]);await writeFile(new URL('../work/logs/english-screenshots.json',import.meta.url),JSON.stringify({status:'passed',screenshots:5,errors},null,2));console.log('PASS five actual English application screenshots: main editor, building sprites, stitched DEM world and constrained agent proposal.');
}finally{if(token)await fetch(base+'/api/agent/session/'+token+'/close',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});await browser.close();}
