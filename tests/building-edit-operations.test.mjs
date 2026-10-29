import test from 'node:test';
import assert from 'node:assert/strict';
import {transactBuildingEdit,editBuilding,applyBuildingEdit} from '../src/SimplePZMapmaker.Web/wwwroot/building-edit-operations.js';
import {BuildingWorkshop} from '../src/SimplePZMapmaker.Web/wwwroot/workshop.js';
import {editNativeBuilding} from '../src/SimplePZMapmaker.Web/wwwroot/main-native-operations.js';
import {normalizeBuilding,refreshEdges,DraftHistory,validateDesign} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import {transformBuilding} from '../src/SimplePZMapmaker.Web/wwwroot/building-transform.js';

const tile='floors_interior_tilesandwood_01_0';
const point=(x=3,y=3)=>({x,y,fx:x+.5,fy:y+.05});
function building(){
 const b=normalizeBuilding({id:'house',name:'House',kind:'house',x:40,y:40,width:8,height:8,design:{minLevel:-1,material:'brick',roof:'flat',shape:'rect',variant:42,floors:Array.from({length:2},()=>({cells:'1'.repeat(64),rooms:{1:'livingroom',2:'kitchen'},objects:[],edges:[]}))}});
 b.design.floors.forEach((_,i)=>refreshEdges(b,i));return normalizeBuilding(b);
}
function workshop(b,level=0){
 const w=Object.create(BuildingWorkshop.prototype);
 Object.assign(w,{draft:b,level,room:'2',history:new DraftHistory(),visual:{allLevels:false},saving:false,error:null,update(){},message(text,error){this.error=error?text:null;}});return w;
}
const asset={id:'furniture_bedding_01_69',parts:[{dx:0,dy:0,tile:'furniture_bedding_01_69'},{dx:0,dy:1,tile:'furniture_bedding_01_68'}]};
const model={modelId:'Base.Bottle',sourceId:'game',profile:'obstacle',footprint:[{dx:0,dy:0},{dx:1,dy:0}]};

test('a transaction validates its isolated candidate after mutation and leaves the source byte-for-byte unchanged on failure',()=>{
 const b=building(),before=JSON.stringify(b),floor=b.design.floors[0];
 assert.throws(()=>transactBuildingEdit(b,c=>{c.name='Changed';c.design.floors[0].floorTiles[27]=tile;c.design.floors[0].objects.push({x:7,y:7,kind:'bed',direction:'n'});}),/contour|Furniture/);
 assert.equal(JSON.stringify(b),before);assert.strictEqual(b.design.floors[0],floor);
});
test('successful transactions return detached state and accept a replacement from existing draft controls',()=>{
 const b=building(),replacement=building();replacement.name='Replacement';
 const result=transactBuildingEdit(b,()=>replacement);assert.equal(result.name,'Replacement');assert.notStrictEqual(result,replacement);result.design.floors[0].cells='2'.repeat(64);assert.equal(replacement.design.floors[0].cells,'1'.repeat(64));assert.equal(b.name,'House');
});

for(const [draftTool,nativeTool,brush,extra] of [
 ['room','room',{},{}],['footprint','footprint',{},{}],['erase','erase-floor',{},{}],['floor-tile','floor',{id:tile},{floorTile:tile}],
 ['wall','wall',{n:'walls_exterior_house_01_0',w:'walls_exterior_house_01_1'},{wallPair:{n:'walls_exterior_house_01_0',w:'walls_exterior_house_01_1'}}],
 ['door','door',{n:'fixtures_doors_01_5',w:'fixtures_doors_01_4'},{doorPair:{n:'fixtures_doors_01_5',w:'fixtures_doors_01_4'}}],
 ['window','window',{n:'fixtures_windows_01_1',w:'fixtures_windows_01_0'},{windowPair:{n:'fixtures_windows_01_1',w:'fixtures_windows_01_0'}}],
 ['open','open',{},{}],['asset-object','object',asset,{asset}],['stairs','stairs',{direction:'n'},{}]
])test(`workshop ${draftTool} and native ${nativeTool} produce identical edits at a signed floor`,t=>{
 if(draftTool==='stairs'){const descriptor=Object.getOwnPropertyDescriptor(globalThis,'document');globalThis.document={getElementById:()=>({value:'n'})};t.after(()=>{if(descriptor)Object.defineProperty(globalThis,'document',descriptor);else delete globalThis.document;});}
 const b=building(),before=structuredClone(b),w=workshop(b);Object.assign(w,{tool:draftTool,...extra});
 const p=nativeTool==='stairs'?point(3,1):point();
 if(['room','footprint','erase','floor-tile'].includes(draftTool))w.paintArea(p,p);
 else w.change(()=>w.place(p));
 assert.equal(w.error,null);assert.deepEqual(w.draft,editNativeBuilding(b,-1,p,nativeTool,brush,'kitchen'));assert.deepEqual(b,before);
});

test('erasing floors never silently deletes furniture or a non-anchor tile of a compound model in either mode',()=>{
 for(const occupant of ['object','model']){
  const b=building(),f=b.design.floors[0];if(occupant==='object')f.objects.push({x:3,y:3,kind:'bed',direction:'n'});else f.staticModels.push({...model,id:'a'.repeat(32),x:2,y:3,z:-1});
  const before=structuredClone(b),w=workshop(b);w.tool='erase';w.paintArea(point(),point());
  assert.match(w.error,/Remove the object, model, or staircase/);assert.strictEqual(w.draft,b);assert.deepEqual(b,before);assert.equal(w.history.past.length,0);
  assert.throws(()=>editNativeBuilding(b,-1,point(),'erase-floor'),/Remove the object, model, or staircase/);assert.deepEqual(b,before);
 }
});
test('the complete area is rolled back if a later tile or final validation fails; redo survives failure',()=>{
 const b=building(),w=workshop(b);w.history.save(b);w.draft={...structuredClone(b),name:'New name'};w.draft=w.history.undo(w.draft);
 const before=structuredClone(w.draft),future=[...w.history.future],original=w.draft;
 w.draft.design.floors[0].objects.push({x:4,y:3,kind:'seat',direction:'n'});before.design.floors[0].objects.push({x:4,y:3,kind:'seat',direction:'n'});
 w.tool='erase';w.paintArea(point(3,3),point(4,3));assert.ok(w.error);assert.strictEqual(w.draft,original);assert.deepEqual(w.draft,before);assert.deepEqual(w.history.future,future);assert.equal(w.history.past.length,0);
 const plain=building(),snapshot=structuredClone(plain);assert.throws(()=>editBuilding(plain,{tool:'erase-floor',level:-1,point:point(0,0),end:point(7,7)}),/empty/);assert.deepEqual(plain,snapshot);
});
test('workshop commits an area in one undo entry, preserves standalone source, and supports exact undo/redo',()=>{
 const template=building(),before=structuredClone(template),w=workshop(template);w.tool='floor-tile';w.floorTile=tile;w.paintArea(point(2,2),point(4,4));assert.equal(w.error,null);assert.equal(Object.keys(w.floor.floorTiles).length,9);assert.equal(w.history.past.length,1);assert.deepEqual(template,before);
 const after=structuredClone(w.draft);w.draft=w.history.undo(w.draft);assert.deepEqual(w.draft,before);w.draft=w.history.redo(w.draft);assert.deepEqual(w.draft,after);
});
test('failed workshop callbacks retain source object identity, nested references, level and history',()=>{
 const b=building(),floor=b.design.floors[0],w=workshop(b),before=structuredClone(b);
 w.change(()=>{w.draft.design.floors[0].floorTiles[27]=tile;w.level=1;w.draft.design.material='invalid';});
 assert.match(w.error,/material/);assert.strictEqual(w.draft,b);assert.strictEqual(b.design.floors[0],floor);assert.equal(w.level,0);assert.deepEqual(b,before);assert.equal(w.history.past.length,0);
});
test('an edit batch can use the same mutable primitive while the enclosing transaction owns atomicity',()=>{
 const b=building(),before=structuredClone(b);assert.throws(()=>transactBuildingEdit(b,c=>{applyBuildingEdit(c,{tool:'floor',level:-1,point:point(),brush:{id:tile}});applyBuildingEdit(c,{tool:'object',level:-1,point:point(7,7),brush:asset});}),/contour|extends/);assert.deepEqual(b,before);
});
test('model placement and erasing a non-anchor footprint tile use the same checks in both modes',()=>{
 const b=building(),w=workshop(b);Object.assign(w,{tool:'static-model',staticModel:model});w.change(()=>w.place(point()));assert.equal(w.error,null);
 const native=editNativeBuilding(b,-1,point(),'model',model);native.design.floors[0].staticModels[0].id=w.floor.staticModels[0].id;assert.deepEqual(w.draft,native);
 const source=w.draft;w.tool='erase-object';w.change(()=>w.place(point(4,3)));assert.equal(w.floor.staticModels.length,0);assert.deepEqual(w.draft,editNativeBuilding(source,-1,point(4,3),'erase-object'));
});
test('unknown tools, invalid levels, non-integer and non-finite points fail without mutating source',()=>{
 const b=building(),before=structuredClone(b);
 for(const operation of [{tool:'unknown',level:-1,point:point()},{tool:'floor',level:.5,point:point(),brush:{id:tile}},{tool:'footprint',level:-1,point:point(Infinity,2)},{tool:'room',level:-1,point:point(2.5,2)},{tool:'wall',level:-1,point:{...point(),fx:NaN}}])assert.throws(()=>editBuilding(b,operation));
 assert.deepEqual(b,before);
});
test('edge conversion preserves overlay and only compatible frame roles in both modes',()=>{
 const b=building(),f=b.design.floors[0],p=point();f.edges.push({x:3,y:3,edge:'n',kind:'wall',wallTile:'walls_exterior_house_01_0',overlayTile:'walls_overlay_01_0',edited:true});
 const brush={n:'fixtures_doors_01_5',w:'fixtures_doors_01_4'},w=workshop(b);Object.assign(w,{tool:'door',doorPair:brush});w.change(()=>w.place(p));const result=editNativeBuilding(b,-1,p,'door',brush);assert.deepEqual(w.draft,result);const edge=result.design.floors[0].edges.find(e=>e.x===3&&e.y===3&&e.edge==='n');assert.equal(edge.wallTile,null);assert.equal(edge.overlayTile,'walls_overlay_01_0');
});
test('footprint edits mark source provenance, while room and surface edits preserve source-grid origin',()=>{
 const b=building();b.footprintOrigin='source-grid';assert.equal(editBuilding(b,{tool:'room',level:-1,point:point(),room:'kitchen'}).footprintOrigin,'source-grid');assert.equal(editBuilding(b,{tool:'floor',level:-1,point:point(),brush:{id:tile}}).footprintOrigin,'source-grid');assert.equal(editBuilding(b,{tool:'erase-floor',level:-1,point:point()}).footprintOrigin,'manual-source-edit');
});
test('resource transform failures remain atomic when their result is used by a shared transaction',async()=>{
 const b=building(),before=structuredClone(b);let verified=0;
 await assert.rejects(transformBuilding(b,'cw',{references:{requiredMods:[],assetSources:{}},verifyBuilding:async()=>{if(++verified===2)throw Error('native owner changed');},edge:async tile=>tile,object:async()=>{throw Error('unexpected object');}}),/native owner changed/);assert.deepEqual(b,before);
});

for(const [draftTool,nativeTool,extra,brush,p,error] of [
 ['floor-tile','floor',{floorTile:'invalid'},{id:'invalid'},point(),/resource/],
 ['asset-object','object',{asset},asset,point(7,7),/contour|extends/],
 ['static-model','model',{staticModel:model},model,point(7,7),/footprint/],
 ['erase-object','erase-object',{}, {},point(),/no object/]
])test(`failed ${nativeTool} runs through the real adapters without altering either input or undo history`,()=>{
 const b=building(),before=structuredClone(b),w=workshop(b);Object.assign(w,{tool:draftTool,...extra});
 if(draftTool==='floor-tile')w.paintArea(p,p);else w.change(()=>w.place(p));
 assert.match(w.error,error);assert.strictEqual(w.draft,b);assert.deepEqual(b,before);assert.equal(w.history.past.length,0);
 assert.throws(()=>editNativeBuilding(b,-1,p,nativeTool,brush),error);assert.deepEqual(b,before);
});
test('west-facing resource placement retains its exact multi-tile grid and independent brush parts in both modes',()=>{
 const b=building(),before=structuredClone(b),brush={id:asset.id,facing:'W',parts:asset.parts.map((p,i)=>({...p,dx:i,dy:0}))},w=workshop(b);
 Object.assign(w,{tool:'asset-object',asset:brush});w.change(()=>w.place(point()));const native=editNativeBuilding(b,-1,point(),'object',brush);assert.deepEqual(w.draft,native);assert.equal(w.floor.objects[0].direction,'w');
 w.floor.objects[0].parts[0].tile='furniture_bedding_01_70';assert.equal(brush.parts[0].tile,asset.id);assert.deepEqual(b,before);
});
test('drafts retain structural validation while access and stairs remain deferred to Apply',()=>{
 const b=building(),result=editBuilding(b,{tool:'floor',level:-1,point:point(),brush:{id:tile}});validateDesign(result,false);assert.throws(()=>validateDesign(result),/stairs/);
});
test('normalization and a failed dependency change never write optional defaults or resource owners into the source',()=>{
 const b=building();delete b.requiredMods;delete b.assetSources;delete b.design.floors[0].staticModels;const before=JSON.stringify(b);
 assert.throws(()=>transactBuildingEdit(b,c=>{c.design.floors[0].floorTiles[27]=tile;c.requiredMods=['bad/mod'];c.assetSources[tile]='bad/mod';}),/Dependencies/);assert.equal(JSON.stringify(b),before);
});
test('footprint expansion keeps authored data at absolute coordinates and rejects exceeding the cap atomically',()=>{
 const b=building();b.design.floors[1].staticModels.push({...model,id:'b'.repeat(32),x:3,y:3,z:0});b.design.floors[1].floorTiles[27]=tile;const before=structuredClone(b);
 const next=editBuilding(b,{tool:'footprint',level:-1,point:point(-1,-1),end:point(1,1),expandFootprint:true,room:'livingroom'});
 assert.equal(next.width,9);assert.equal(next.height,9);assert.equal(next.x,39);assert.equal(next.y,39);assert.equal(next.design.floors[1].floorTiles[40],tile);assert.equal(next.design.floors[1].staticModels[0].x+next.x,43);assert.equal(next.design.floors[1].staticModels[0].y+next.y,43);
 assert.throws(()=>editBuilding(b,{tool:'footprint',level:-1,point:point(-1,0),end:point(64,1),expandFootprint:true}),/64/);assert.deepEqual(b,before);
});
test('surface edits crossing a void and upper stair landing removal fail atomically in both adapters',()=>{
 const b=building(),f=b.design.floors[0];f.cells=f.cells.slice(0,28)+'.'+f.cells.slice(29);refreshEdges(b,0);const before=structuredClone(b),w=workshop(b);Object.assign(w,{tool:'floor-tile',floorTile:tile});w.paintArea(point(3,3),point(4,3));assert.match(w.error,/Add a floor/);assert.deepEqual(b,before);assert.equal(w.history.past.length,0);
 assert.throws(()=>editNativeBuilding(b,-1,point(4,3),'floor',{id:tile}),/Add a floor/);assert.deepEqual(b,before);
 const stairs=building();stairs.design.floors[0].objects.push({kind:'stairs',direction:'n',x:3,y:1});const snapshot=structuredClone(stairs),upper=workshop(stairs,1);upper.tool='erase';upper.paintArea(point(3,3),point(3,3));assert.match(upper.error,/staircase requires/);assert.deepEqual(stairs,snapshot);assert.throws(()=>editNativeBuilding(stairs,0,point(3,3),'erase-floor'),/staircase requires/);assert.deepEqual(stairs,snapshot);
});
test('workshop style brushes remain available while native palette edits require an explicit matching orientation',()=>{
 const b=building(),w=workshop(b);w.tool='wall';w.change(()=>w.place(point()));assert.equal(w.error,null);
 assert.throws(()=>editNativeBuilding(b,-1,point(),'wall'),/matching/);assert.throws(()=>editNativeBuilding(b,-1,point(),'wall',{w:'walls_exterior_house_01_1'}),/matching/);
});
