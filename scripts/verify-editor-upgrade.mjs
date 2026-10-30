// An actual warm HTTP cache, with the pre-load-all session module cached for a year.
// Run against a disposable editor; API requests are forwarded to PZMAP_TEST_URL.
import http from 'node:http';
import {fileURLToPath} from 'node:url';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
const target=new URL(process.env.PZMAP_TEST_URL),id=process.env.PZMAP_TEST_WORKSPACE_ID;
assert.ok(target.hostname==='127.0.0.1'&&/^[a-f0-9]{32}$/.test(id),'Use an isolated localhost editor and workspace ID.');
const old=await readFile(new URL('../tests/fixtures/pre-load-all-section-session.js',import.meta.url),'utf8');assert.ok(!old.includes('async loadAll('),'Use a pre-load-all module fixture.');
let oldRequests=0,versionedRequests=0,failSections=false;
const server=http.createServer((req,res)=>{
 if(req.url==='/prime'){res.writeHead(200,{'Content-Type':'text/html','Cache-Control':'no-store'});res.end('<script type="module" src="/streamed-section-session.js"></script>Old editor cache');return;}
 if(req.url==='/streamed-section-session.js'){oldRequests++;res.writeHead(200,{'Content-Type':'text/javascript','Cache-Control':'public, max-age=31536000, immutable'});res.end(old);return;}
 if(failSections&&req.url.includes('/sections/')&&req.url.includes('includeSections=false')){res.writeHead(503,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'Fixture: section service unavailable'}));return;}
 if(req.url.startsWith('/ui/')&&req.url.endsWith('/streamed-section-session.js'))versionedRequests++;
 const upstream=http.request({hostname:target.hostname,port:target.port,method:req.method,path:req.url,headers:req.headers},response=>{res.writeHead(response.statusCode,response.headers);response.pipe(res);});upstream.on('error',e=>{res.writeHead(502);res.end(e.message);});req.pipe(upstream);
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
const browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL});const page=await browser.newPage({viewport:{width:1440,height:900}}),errors=[],modulePaths=[];page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{const path=new URL(r.url()).pathname;if(path.endsWith('streamed-section-session.js')||path.endsWith('world-workspaces.js')||path.endsWith('app.js'))modulePaths.push(path);});page.setDefaultTimeout(180000);
const out=new URL('../work/editor-upgrade/',import.meta.url);await mkdir(out,{recursive:true});const idle=()=>page.waitForFunction(()=>!document.querySelector('#operation-dialog').open);
try{
 await page.goto(base+'/prime',{waitUntil:'networkidle'});assert.equal(oldRequests,1);
 await page.goto(base,{waitUntil:'domcontentloaded'});await idle();await page.locator('[data-command-menu=project]>summary').click();await page.locator('#open-world').click();await page.locator('#world-workspaces>summary').click();await page.locator('[data-ws-list]').click();await idle();await page.locator('[data-ws-world]').selectOption(id);await idle();
 await page.locator('[data-ws-open-world]').click();await idle();const state=await page.locator('[data-ws-state]').textContent();assert.match(state,/All \d+ sections are loaded for editing/);
 assert.equal(oldRequests,1,'The test did not retain the warm old module in HTTP cache');assert.equal(versionedRequests,1,'Transitive module was not loaded from the current graph');assert.deepEqual(errors,[]);
 await page.screenshot({path:fileURLToPath(new URL('warm-cache-loaded.png',out))});
 const name=await page.locator('#project-name').inputValue(),loaded=await page.locator('#world-canvas').getAttribute('data-loaded-sections');failSections=true;await page.locator('[data-ws-open-world]').click();await idle();assert.match(await page.locator('#world-state').textContent(),/Fixture: section service unavailable/);assert.equal(await page.locator('[data-ws-state]').getAttribute('role'),'alert');assert.equal(await page.locator('#project-name').inputValue(),name);assert.equal(await page.locator('#world-canvas').getAttribute('data-loaded-sections'),loaded);
 await writeFile(new URL('result.json',out),JSON.stringify({pass:true,oldRequests,versionedRequests,state,errorVisible:true,previousMapRetained:true,errors},null,2));console.log('PASS warm old module cache cannot mix with the new editor; load-all succeeds; failures remain visible and preserve the map');
}catch(e){console.error({state:await page.locator('[data-ws-state]').textContent().catch(()=>''),oldRequests,versionedRequests,modulePaths,errors});throw e;}finally{await browser.close();await new Promise(r=>server.close(r));}
