import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48812';assert.notEqual(new URL(base).port,'48779');
const root=new URL('../src/SimplePZMapmaker.Web/wwwroot/',import.meta.url),out=new URL('../work/knoxmap-research/road-ramps/',import.meta.url);await mkdir(out,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});
try{
 const context=await browser.newContext({viewport:{width:1500,height:1000}}),page=await context.newPage(),errors=[];
 page.on('pageerror',error=>errors.push(error.message));
 const html=`<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/style.css"><button id="open">Road editor</button><p id="state"></p><script type="module">import {LinearEditingWorkshop} from '/linear-editing.js';
 async function api(path,body,signal){const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal});const v=await r.json();if(!r.ok)throw Error(v.error);return v;}
 window.current=await api('/api/generate',{kind:'blank',size:256,seed:42,name:'Ramp drawing verification'});window.applied=0;
 window.workshop=new LinearEditingWorkshop({project:()=>window.current,apply:p=>{window.current=p;window.applied++;},api,message:m=>document.querySelector('#state').textContent=m});document.querySelector('#open').onclick=()=>window.workshop.open();</script>`;
 await page.route(base+'/__ramp-browser',route=>route.fulfill({contentType:'text/html',body:html}));
 // Exercise the current authoring modules against the disposable published API.
 for(const file of ['road-ramps.js','linear-editing.js'])await page.route(base+'/'+file,async route=>route.fulfill({contentType:'text/javascript',body:await readFile(new URL(file,root),'utf8')}));
 await page.goto(base+'/__ramp-browser');await page.waitForFunction(()=>window.workshop);await page.locator('#open').click();
 await page.locator('#linear-kind').selectOption('bridge');await page.locator('#linear-level').fill('1');await page.locator('#linear-level').press('Tab');await page.locator('#linear-access').selectOption('ramps');
 await page.locator('#linear-width').fill('4');await page.locator('#linear-width').press('Tab');
 const position=async(x,y)=>page.evaluate(({x,y})=>{const w=window.workshop,b=w.canvas.getBoundingClientRect(),t=w.transform();return {x:b.left+t.ox+x*t.scale,y:b.top+t.oy+y*t.scale};},{x,y});
 for(const [x,y] of [[40.3,90.2],[120.4,92.2],[180,160]]){const at=await position(x,y);await page.mouse.click(at.x,at.y);}
 const draft=await page.evaluate(()=>window.workshop.draft);assert.equal(draft.points.length,2);assert.deepEqual(draft.points,[{x:40,y:90},{x:120,y:90}]);
 const dialog=page.locator('#linear-dialog');await page.locator('#linear-preview').click();await page.waitForFunction(()=>!window.workshop.pending&&window.workshop.previewProject);
 assert.equal(await page.evaluate(()=>window.current.linearFeatures.length),0,'Preview changed the saved map');
 await page.locator('#linear-apply').click();await page.waitForFunction(()=>window.applied===1&&!window.workshop.pending);
 const applied=await page.evaluate(()=>window.current.linearFeatures[0]);assert.equal(applied.access,'ramps');assert.deepEqual(applied.points,draft.points);
 assert.deepEqual(errors,[]);await page.screenshot({path:fileURLToPath(new URL('ramp-editor.png',out)),fullPage:true});
 await writeFile(new URL('browser-proof.json',out),JSON.stringify({status:'passed',integerCardinalDrawing:true,thirdNodePrevented:true,previewIsolated:true,applyCount:1,feature:applied.id,points:applied.points,errors},null,2));
 console.log('PASS ramp authoring browser: cardinal integer drawing, two endpoints, isolated preview, actual API apply.');
}finally{await browser.close();}
