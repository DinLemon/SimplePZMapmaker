const pzmapTestBase=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765';
import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1440,height:960}});
let release=()=>{};
try{
 await page.goto(pzmapTestBase);await page.waitForFunction(()=>document.querySelector('#game-status')?.textContent.includes('найден'));
 const project=await page.request.post((pzmapTestBase+'/api/generate'),{data:{kind:'blank',size:256}}).then(r=>r.json());
 for(const [path,body] of [['/api/import/geojson',{project,json:null}],['/api/import/osm',{project,bounds:null}]])assert.equal((await page.request.post(pzmapTestBase+path,{data:body})).status(),400,'Malformed nested null should return400');
 let started;const start=new Promise(r=>started=r),gate=new Promise(r=>release=r);
 await page.route('**/api/import/geojson',async route=>{started();await gate;try{await route.continue();}catch{}});
 await page.locator('#open-import').click();await page.locator('#geodata-file').setInputFiles(fileURLToPath(new URL('../work/qa-results/geodata.geojson',import.meta.url)));await start;
 assert.equal(await page.locator('#operation-dialog').isVisible(),true,'Progress must be in a top-layer modal');
 const button=page.locator('#cancel-operation'),box=await button.boundingBox();
 assert.equal(await page.evaluate(({x,y})=>document.elementFromPoint(x,y)?.closest('#cancel-operation')!==null,{x:box.x+box.width/2,y:box.y+box.height/2}),true,'Cancel must be reachable above initiating dialog');
 assert.equal(await page.locator('.topbar').evaluate(e=>e.inert),true,'Project mutation must be frozen during operation');
 const name=await page.locator('#project-name').inputValue();await button.click();await page.locator('#operation-dialog').waitFor({state:'hidden'});assert.equal(await page.locator('#project-name').inputValue(),name);release();
 console.log('PASS HTTP null validation, delayed-import mutation freeze, accessible cancellation and project preservation');
}finally{release();await browser.close();}
