import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';

// Exercises the real dialog and controllers with local HTTP fixtures; no geographic download.
// PZMAP_WORLD_WIZARD_BROWSER=1 node --test tests/osm-world-wizard.test.mjs
test('expanded world wizard browser contract',{skip:process.env.PZMAP_WORLD_WIZARD_BROWSER!=='1'},async t=>{
 const {chromium}=await import(process.env.PZMAP_PLAYWRIGHT_MODULE?pathToFileURL(process.env.PZMAP_PLAYWRIGHT_MODULE).href:'../tools/BrowserSmoke/node_modules/playwright/index.mjs');
 const root=new URL('../src/SimplePZMapmaker.Web/wwwroot/',import.meta.url);
 const server=createServer(async(req,res)=>{
  try{
   if(req.url==='/'){res.setHeader('Content-Type','text/html');res.end('<!doctype html><html lang="en"><title>World wizard</title><link rel="stylesheet" href="/osm-world-import.css"><body style="--line:#465249;--ink:#16201b;--panel:#202a24;--paper:#eee;--muted:#bbb;--accent:#dbb370;background:#16201b;color:#eee"><button id="world-open">Open world</button></body></html>');return;}
   const file=new URL('.'+req.url,root);if(!file.href.startsWith(root.href)){res.writeHead(403).end();return;}
   res.setHeader('Content-Type',file.pathname.endsWith('.js')?'text/javascript':'text/css');res.end(await readFile(file));
  }catch{res.writeHead(404).end();}
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
 try{
  browser=await chromium.launch({headless:true,channel:process.env.PZMAP_BROWSER_CHANNEL||'chrome',executablePath:process.env.PZMAP_BROWSER_EXECUTABLE});
  async function setup(km=3,heightKm=km){
   const page=await browser.newPage({viewport:{width:1280,height:1000}}),calls=[],errors=[];page.setDefaultTimeout(5000);
   page.on('pageerror',e=>errors.push(e.message));
   await page.route('**/api/**',async route=>{
    const request=route.request(),body=request.postDataJSON(),url=new URL(request.url()).pathname;
    if(url==='/api/export/jobs'){await route.fulfill({json:[]});return;}
    calls.push({url,body});
    if(url==='/api/world/osm/plan'){await route.fulfill({json:{columns:12,rows:12,width:3072,height:3072,cells:144}});return;}
    if(url==='/api/world/osm/jobs'){await route.fulfill({json:{id:'a'.repeat(32)}});return;}
    if(url==='/api/world/osm/jobs/'+'a'.repeat(32)){await route.fulfill({json:{status:'completed',...(km<.3?{resultKind:'world'}:{resultKind:'sections',workspaceId:'b'.repeat(32),workspaceRevision:1}),progress:{phase:'completed'}}});return;}
    if(url==='/api/world/osm/jobs/'+'a'.repeat(32)+'/result'){await route.fulfill({json:{id:'legacy-world',name:'Small world',maps:[]}});return;}
    await route.fulfill({status:400,json:{error:'Unexpected fixture request '+url}});
   });
   await page.goto(`http://127.0.0.1:${server.address().port}`);
   await page.evaluate(async ([km,heightKm])=>{
    const {OsmWorldImport}=await import('/osm-world-import.js'),{boundsFromCentre}=await import('/osm-source.js');
    window.currentWorld=null;window.accepted=[];window.backArea=null;
    window.wizard=new OsmWorldImport({getWorld:()=>currentWorld,acceptWorld:world=>accepted.push(world),acceptWorkspace:id=>accepted.push(id),onBackToArea:area=>{window.backArea=area;}});
    document.querySelector('#osm-world-size').value='256';document.querySelector('#osm-world-source').value='overpass';
    window.initialArea=boundsFromCentre(0,0,km,heightKm);wizard.open({selection:initialArea});
   },[km,heightKm]);
   return {page,calls,errors};
  }
  await t.test('proposal retains the chosen area, needs an explicit click and performs no network work',async()=>{
   const {page,calls,errors}=await setup();
   try{
    assert.equal(await page.locator('#osm-world-expanded-approve').textContent(),'Use 144 connected sections');
    assert.match(await page.locator('#osm-world-expanded-summary').innerText(),/12.*12.*256/);
    const memory=await page.locator('#osm-world-expanded-memory').innerText();
    assert.match(memory,/9\.6 MiB.*disk.*work/i);assert.match(memory,/not peak RAM/i);
    assert.match(memory,/saved incrementally.*current section.*bounded neighbou?ring window/i);
    assert.match(memory,/resident raster.*9\.6 MiB/i);assert.match(memory,/buildings.*sources.*separate/i);
    assert.equal(await page.locator('#osm-world-start').isEnabled(),false);
    assert.equal(await page.locator('#osm-world-advanced').getAttribute('open'),null);
    assert.equal(await page.locator('#osm-world-source-details').getAttribute('open'),null);
    await page.locator('#osm-world-expanded-approve').click();
    assert.equal(await page.locator('#osm-world-start').isEnabled(),true);
    const request=await page.evaluate(()=>wizard.request());assert.equal(request.allowExpandedLayout,true);assert.deepEqual(request.bounds,await page.evaluate(()=>initialArea));assert.equal(request.metresPerTile,1);
    assert.equal(calls.length,0);assert.deepEqual(errors,[]);
   }finally{await page.close();}
  });
  await t.test('15552-cell proposal needs consent without coarsening or automatic source requests',async()=>{
   const {page,calls,errors}=await setup(36.863,27.647);
   try{
    assert.equal(await page.locator('#osm-world-expanded-approve').textContent(),'Use 15552 connected sections');
    assert.match(await page.locator('#osm-world-expanded-summary').innerText(),/144 columns.*108 rows.*256.*15,552/);
    const memory=await page.locator('#osm-world-expanded-memory').innerText();
    assert.match(memory,/1 GiB.*disk.*work/i);assert.match(memory,/not peak RAM/i);
    assert.match(memory,/saved incrementally.*current section.*bounded neighbou?ring window/i);
    assert.match(memory,/resident raster.*17 MiB/i);assert.match(memory,/buildings.*sources.*separate/i);
    assert.match(memory,/64 MB/);assert.equal(await page.locator('#osm-world-start').isEnabled(),false);
    await page.locator('#osm-world-source').selectOption('auto');
    await page.locator('#osm-world-expanded-approve').click();
    let request=await page.evaluate(()=>wizard.request());
    assert.equal(request.allowExpandedLayout,true);assert.equal(request.preferSections,true);assert.equal(request.metresPerTile,1);assert.equal(request.sectionSize,256);
    assert.deepEqual(request.bounds,await page.evaluate(()=>initialArea));assert.equal(calls.length,0);
    await page.locator('#osm-world-width').fill('36.864');assert.equal(await page.locator('#osm-world-start').isEnabled(),false);
    assert.equal(await page.evaluate(()=>wizard.request({allowUnapproved:true}).allowExpandedLayout??false),false);
    assert.equal(calls.length,0);assert.deepEqual(errors,[]);
   }finally{await page.close();}
  });
  await t.test('bounds, scale, size, origin and rotation edits revoke approval; name and source edits retain geometry consent',async()=>{
   const {page,calls}=await setup();
   try{
    await page.locator('#osm-world-expanded-approve').click();
    await page.locator('#osm-world-name').fill('Renamed');
    await page.locator('#osm-world-source').selectOption('regional');
    assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);
    await page.locator('#osm-world-advanced > summary').click();
    for(const [id,val] of [['rotation','1'],['east','0.014'],['scale','1.01'],['x','101'],['y','101']]){
     await page.locator('#osm-world-'+id).fill(val);
     assert.equal(await page.locator('#osm-world-expanded-approve').isVisible(),true,id);
     assert.equal(await page.locator('#osm-world-start').isEnabled(),false,id);
     await page.locator('#osm-world-expanded-approve').click();
    }
    await page.locator('#osm-world-size').selectOption('512');
    assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout??false),false);
    assert.equal(calls.length,0);
   }finally{await page.close();}
  });
  await t.test('approval is sent to plan and job; ready result and consent survive going back unchanged',async()=>{
   const {page,calls,errors}=await setup();
   try{
    await page.locator('#osm-world-expanded-approve').click();
    await page.locator('#osm-world-start').click();await page.locator('#osm-world-apply').waitFor({state:'visible'});
    assert.equal(calls.filter(c=>c.url==='/api/world/osm/plan')[0].body.allowExpandedLayout,true);
    assert.equal(calls.filter(c=>c.url==='/api/world/osm/jobs')[0].body.allowExpandedLayout,true);
    const result=await page.evaluate(()=>structuredClone(wizard.result));
    await page.locator('#osm-world-back-to-area').click();await page.evaluate(()=>wizard.open({selection:backArea}));
    assert.equal(await page.locator('#osm-world-apply').isVisible(),true);assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);assert.deepEqual(await page.evaluate(()=>wizard.result),result);
    assert.equal(await page.locator('#osm-world-result-step').isVisible(),true);
    await page.locator('#osm-world-source').selectOption('regional');assert.equal(await page.locator('#osm-world-apply').isVisible(),false);assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);
    assert.deepEqual(errors,[]);
   }finally{await page.close();}
  });
  await t.test('hard limits show no approval, while small layouts use the legacy request',async()=>{
   for(const [km,hard] of [[66,true],[.2,false]]){
    const {page,calls}=await setup(km);
    try{
     assert.equal(await page.locator('#osm-world-expanded-approve').isVisible(),false);
     assert.equal(await page.locator('#osm-world-start').isEnabled(),!hard);
     if(hard){assert.match(await page.locator('#osm-world-size-preview').innerText(),/65,536/);assert.equal(await page.locator('#osm-world-layout-grid').isVisible(),false);}
     else assert.equal(await page.evaluate(()=>Object.hasOwn(wizard.request(),'allowExpandedLayout')),false);
     assert.equal(calls.length,0);
     if(!hard){
      await page.locator('#osm-world-start').click();await page.locator('#osm-world-apply').waitFor({state:'visible'});
      assert.equal(await page.evaluate(()=>wizard.result.resultKind),'world');
      assert.equal(calls.find(c=>c.url==='/api/world/osm/jobs').body.allowExpandedLayout,undefined);
      await page.locator('#osm-world-apply').click();assert.equal(await page.evaluate(()=>accepted[0].id),'legacy-world');
     }
    }finally{await page.close();}
   }
  });
  await t.test('preserved expanded frame, approval and ready result survive unchanged area/source round trips',async()=>{
   const {page,errors}=await setup();
   try{
    await page.evaluate(()=>{
     currentWorld={id:'existing',name:'Keep edits',geoReference:{bounds:initialArea,metresPerTile:1,originCellX:100,originCellY:100,rotationDegrees:0},maps:[{id:'section',project:{width:256,height:256,worldX:100,worldY:100,seed:42}}]};
     wizard.initialized=false;wizard.open();
    });
    assert.equal(await page.locator('#osm-world-preserve').isChecked(),true);
    await page.locator('#osm-world-expanded-approve').click();
    await page.locator('#osm-world-start').click();await page.locator('#osm-world-apply').waitFor({state:'visible'});
    const result=await page.evaluate(()=>structuredClone(wizard.result));
    await page.locator('#osm-world-back-to-area').click();await page.evaluate(()=>wizard.open({selection:backArea,source:wizard.source}));
    assert.equal(await page.locator('#osm-world-preserve').isChecked(),true);assert.equal(await page.locator('#osm-world-apply').isVisible(),true);
    assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);assert.deepEqual(await page.evaluate(()=>wizard.result),result);
    await page.evaluate(()=>{wizard.rememberSource('c'.repeat(32));});
    assert.equal(await page.locator('#osm-world-apply').isVisible(),false);assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);
    assert.equal((await page.evaluate(()=>wizard.selectedSource(new AbortController().signal))).sourceId,'c'.repeat(32));
    await page.evaluate(()=>wizard.apply());assert.deepEqual(await page.evaluate(()=>accepted),[]);assert.deepEqual(errors,[]);
   }finally{await page.close();}
  });
  await t.test('narrow layouts keep the proposal first and fit within the dialog',async()=>{
   const {page,errors}=await setup(36.863,27.647);
   try{
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.locator('#osm-world-dialog').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);
    assert.match(await page.locator('#osm-world-expanded-memory').innerText(),/resident raster.*17 MiB/i);
    const overview=await page.locator('.osm-world-overview').boundingBox(),area=await page.locator('#osm-world-area-step').boundingBox();assert.ok(overview.y<area.y);
    await page.locator('#osm-world-expanded-approve').click();assert.equal(await page.evaluate(()=>wizard.request().allowExpandedLayout),true);assert.deepEqual(errors,[]);
   }finally{await page.close();}
  });
 }finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
