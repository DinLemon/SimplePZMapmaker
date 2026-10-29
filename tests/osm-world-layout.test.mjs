import test from 'node:test';
import assert from 'node:assert/strict';
import * as osm from '../src/SimplePZMapmaker.Web/wwwroot/osm-source.js';
import {validateWorld} from '../src/SimplePZMapmaker.Web/wwwroot/world-workshop.js';

const request=(width=1,height=1,extra={})=>({bounds:osm.boundsFromCentre(0,0,width,height),metresPerTile:1,sectionSize:1024,originCellX:100,originCellY:100,...extra});
test('world layout explains padding and keeps the geographic area inside its section grid',()=>{
 assert.equal(typeof osm.worldImportPreview,'function');
 const p=osm.worldImportPreview(request(1,.5));
 assert.deepEqual(p.issues,[]);assert.equal(p.columns,1);assert.equal(p.rows,1);
 assert.equal(p.width,1024);assert.equal(p.cells,16);
 assert.ok(Math.abs(Math.max(...p.selection.map(v=>v[0]))-1000)<1e-6);
 assert.ok(Math.abs(Math.max(...p.selection.map(v=>v[1]))-500)<1e-6);
});
test('rotation changes the preview footprint and every corner stays inside the padded world',()=>{
 assert.equal(typeof osm.worldImportPreview,'function');
 for(const rotationDegrees of [-45,-11.22,0,30,45]){
  const p=osm.worldImportPreview(request(1,.5,{sectionSize:256,rotationDegrees}));
  for(const [x,y] of p.selection){assert.ok(x>=-1e-6&&x<=p.width+1e-6);assert.ok(y>=-1e-6&&y<=p.height+1e-6);}
  assert.equal(p.selection.length,4);assert.deepEqual(p.issues,[]);
 }
});
test('expanded layout is proposed without changing bounds, scale or normal small requests',()=>{
 const small=request(1,.5),legacy=osm.worldImportPreview(small);
 assert.equal(legacy.normal,true);assert.equal(legacy.requiresExpandedLayout,false);assert.equal(legacy.proposal,null);
 const input=request(4,4,{sectionSize:256}),before=structuredClone(input),p=osm.worldImportPreview(input);
 assert.deepEqual(input,before);assert.equal(p.normal,false);assert.equal(p.requiresExpandedLayout,true);
 assert.equal(p.sections,256);assert.equal(p.proposal.columns,16);assert.equal(p.proposal.rows,16);assert.equal(p.proposal.sectionSize,256);
 assert.equal(p.proposal.minimumRasterBytes,p.cells*256*256*(1+4/64));
 assert.match(p.issues.join(' '),/Use 256 connected sections/);assert.deepEqual(p.hardIssues,[]);
 assert.deepEqual(osm.worldImportPreview({...input,allowExpandedLayout:true}).issues,[]);
 assert.equal(osm.worldImportPreview({...input,allowExpandedLayout:'true'}).issues.length,1);
});
test('normal cell threshold is independent of section threshold and old angular source limits do not bound world geometry',()=>{
 const p=osm.worldImportPreview(request(8,8));
 assert.equal(p.sections,64);assert.equal(p.cells,1024);assert.equal(p.requiresExpandedLayout,true);
 assert.deepEqual(osm.worldImportPreview(request(14,2,{allowExpandedLayout:true})).issues,[]);
 assert.deepEqual(osm.worldImportPreview(request(2,10,{metresPerTile:2})).issues,[]);
 assert.deepEqual(osm.worldImportPreview(request(4,4,{metresPerTile:2})).issues,[]);
});
test('hard counts and cell coordinates cannot be approved and never offer an impossible split',()=>{
 for(const input of [request(66,66,{sectionSize:256}),request(66,66),request(2,2,{originCellX:1000}),request(1,1,{originCellX:-1})]){
  const p=osm.worldImportPreview({...input,allowExpandedLayout:true});
  assert.ok(p.hardIssues.length);assert.equal(p.proposal,null);assert.deepEqual(p.issues,p.hardIssues);
  assert.match(p.issues.join(' '),/65,536|cell coordinates/);
 }
 const edge=request(65.536,65.536,{sectionSize:256,allowExpandedLayout:true});
 // Pick exact non-boundary dimensions to avoid floating point padding.
 edge.bounds=osm.boundsFromCentre(0,0,65.535,65.535);
 assert.equal(osm.worldImportPreview(edge).sections,65536);assert.deepEqual(osm.worldImportPreview(edge).issues,[]);
 const overflow=osm.worldImportPreview(request(65.791,65.535,{sectionSize:256,allowExpandedLayout:true}));
 assert.equal(overflow.cells,65792);assert.equal(overflow.proposal,null);assert.match(overflow.issues.join(' '),/65,536/);
});
test('15552 cells retain exact area and scale with consent and bounded section raster residency',()=>{
 for(const [sectionSize,sections,composeRasterBytes,residentRasterBytes] of [[256,15552,69632,17825792],[1024,972,1114112,17825792]]){
  const input=request(36.863,27.647,{sectionSize}),before=structuredClone(input),p=osm.worldImportPreview(input);
  assert.equal(p.cells,15552);assert.equal(p.sections,sections);assert.deepEqual(p.hardIssues,[]);
  assert.equal(p.requiresExpandedLayout,true);assert.ok(p.issues.length);assert.deepEqual(input,before);
  assert.equal(p.assemblyMode,'sections');assert.equal(p.baseRasterBytes,1082916864);
  assert.equal(p.composeRasterBytes,composeRasterBytes);assert.equal(p.residentRasterBytes,residentRasterBytes);
  assert.equal(p.upperBoundResidentCells,256);assert.equal(p.proposal.upperBoundResidentCells,256);
  assert.equal(p.proposal.baseRasterBytes,p.baseRasterBytes);assert.equal(p.proposal.residentRasterBytes,p.residentRasterBytes);
  const consent=new osm.WorldLayoutConsent();consent.approve(input);
  assert.deepEqual(osm.worldImportPreview({...input,allowExpandedLayout:consent.allows(input)}).issues,[]);
  assert.equal(consent.allows({...input,metresPerTile:2}),false);assert.equal(consent.allows(input),false);
 }
});
test('resident raster estimates cover small worlds and the largest sections without claiming total RAM',()=>{
 const small=osm.worldImportPreview(request(.2,.2,{sectionSize:256}));
 assert.equal(small.baseRasterBytes,69632);assert.equal(small.composeRasterBytes,69632);assert.equal(small.residentRasterBytes,69632);
 const large=osm.worldImportPreview(request(65.535,65.535,{sectionSize:2048,allowExpandedLayout:true}));
 assert.equal(large.cells,65536);assert.equal(large.baseRasterBytes,4563402752);
 assert.equal(large.composeRasterBytes,4456448);assert.equal(large.residentRasterBytes,17825792);assert.deepEqual(large.issues,[]);
});
test('world section count errors report the shared 65536 limit',()=>{
 const world={format:'SimplePZWorld',version:1,id:'a'.repeat(32),name:'Too many sections',maps:Array.from({length:65537},()=>({}))};
 assert.throws(()=>validateWorld(world),/65,536 sections/);
});
test('expanded layout approval never expands local source intake budgets',async()=>{
 const input=request(36.863,27.647),consent=new osm.WorldLayoutConsent();consent.approve(input);
 assert.deepEqual(osm.worldImportPreview({...input,allowExpandedLayout:consent.allows(input)}).issues,[]);
 const options={fetchImpl:()=>assert.fail('oversized source must not post'),readDataUrl:()=>assert.fail('oversized PBF must not decode')};
 await assert.rejects(osm.uploadOsmSource({name:'world.json',size:64000001,text:()=>assert.fail('oversized JSON must not read')},input.bounds,options),/64 MB/);
 await assert.rejects(osm.uploadOsmSource({name:'world.pbf',size:67108865},input.bounds,options),/64 MiB/);
});
test('invalid scale and empty dimensions cannot produce a ready world layout',()=>{
 assert.equal(typeof osm.worldImportPreview,'function');
 assert.throws(()=>osm.worldImportPreview(request(1,1,{metresPerTile:0})),/metres per tile/);
 assert.throws(()=>osm.worldImportPreview({...request(),bounds:{south:0,north:0,west:0,east:0}}),/bounds/);
});
test('consent binds the exact geometry, survives source metadata changes and revokes on every geometry edit',()=>{
 const input=request(4,4,{sectionSize:256,rotationDegrees:0,warp:null});
 for(const patch of [{bounds:{...input.bounds,east:input.bounds.east+.0001}},{rotationDegrees:1},{metresPerTile:2},{sectionSize:512},{originCellX:101},{originCellY:101},{warp:{version:1}}]){
  const consent=new osm.WorldLayoutConsent();
  assert.equal(consent.allows(input),false);consent.approve(input);assert.equal(consent.allows(input),true);
  assert.equal(consent.allows({...input,sourceMode:'file',sourceId:'prepared',name:'Renamed',seed:43}),true);
  assert.equal(consent.allows({...input,...patch}),false);assert.equal(consent.allows(input),false);
 }
 const consent=new osm.WorldLayoutConsent();consent.approve(input);consent.reset();assert.equal(consent.allows(input),false);
});
