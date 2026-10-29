import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48822';
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(new URL(base).port,'48822');
const result=JSON.parse(await readFile(new URL('../work/incremental-world-import/large-import-results.json',import.meta.url),'utf8'));
const {bounds}=result,errors=[],requests=[];const browser=await chromium.launch({channel:'chrome',headless:true});
try{
 const context=await browser.newContext({viewport:{width:1440,height:1000}});const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>requests.push({method:r.method(),path:new URL(r.url()).pathname}));
 await page.route('**/api/search',r=>r.fulfill({json:[{lat:String((bounds.north+bounds.south)/2),lon:String((bounds.west+bounds.east)/2),display_name:'15552-cell fixture',boundingbox:[String(bounds.south),String(bounds.north),String(bounds.west),String(bounds.east)]}]}));
 await page.goto(base,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>document.body.dataset.workflow==='create'&&!document.querySelector('#operation-dialog').open);await page.evaluate(()=>document.querySelectorAll('dialog[open]').forEach(d=>d.close()));
 await page.getByRole('button',{name:'Choose a real place',exact:true}).click();await page.locator('#place-query').fill('Large fixture');await page.getByRole('button',{name:'Find location',exact:true}).click();await page.getByRole('button',{name:'15552-cell fixture',exact:true}).click();await page.locator('#import-world-button').click();
 const approval=page.locator('#osm-world-expanded-approve');await approval.waitFor({state:'visible'});assert.match(await approval.innerText(),/972 connected sections/);assert.match(await page.locator('#osm-world-expanded-memory').innerText(),/17 MiB/);
 await page.screenshot({path:fileURLToPath(new URL('../work/incremental-world-import/proposal.png',import.meta.url)),fullPage:true});
 const posts=requests.filter(r=>r.method==='POST').length;await approval.click();assert.equal(requests.filter(r=>r.method==='POST').length,posts);assert.equal(await approval.isHidden(),true);
 await page.locator('#osm-world-close').click();if(await page.locator('#sw-close').isVisible())await page.locator('#sw-close').click();await page.locator('#workspace-check').click();await page.getByRole('button',{name:'Export a saved world',exact:true}).click();await page.locator('[data-ws-list]').click();await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
 await page.locator('[data-ws-world]').selectOption(result.workspaceId);await page.waitForFunction(()=>document.querySelector('[data-ws-section]').options.length===973);
 assert.equal(await page.locator('[data-ws-open-world]').isDisabled(),true);await page.screenshot({path:fileURLToPath(new URL('../work/incremental-world-import/world-layout.png',import.meta.url)),fullPage:true});
 const target=result.sampledSections[0].id;await page.locator('[data-ws-section]').selectOption(target);await page.locator('[data-ws-open]').click();await page.waitForFunction(()=>!document.querySelector('#operation-dialog').open&&document.querySelector('#world-dialog').open===false,null,{timeout:120000});
 assert.equal(await page.locator('body').getAttribute('data-workflow'),'edit');
 assert.equal(requests.filter(r=>/\/api\/world\/workspaces\/[^/]+\/world$/.test(r.path)).length,0);assert.deepEqual(errors,[]);
 await writeFile(new URL('../work/incremental-world-import/browser-results.json',import.meta.url),JSON.stringify({cells:15552,sections:972,approvedWithoutNetwork:true,openedOneSection:true,completeWorldRequested:false,errors},null,2));console.log('PASS expanded proposal, 972-section layout and single-section opening without complete-world load');
}finally{await browser.close();}
