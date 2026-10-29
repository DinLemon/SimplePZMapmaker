import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import * as building from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import * as levels from '../src/SimplePZMapmaker.Web/wwwroot/z-levels.js';
import {blank,History,mapAuthoringVersion,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {templateProjectVersion} from '../src/SimplePZMapmaker.Web/wwwroot/building-library.js';
import {buildingTemplateFromDocument} from '../src/SimplePZMapmaker.Web/wwwroot/world-geography.js';
import {BuildingWorkshop} from '../src/SimplePZMapmaker.Web/wwwroot/workshop.js';
import {validateWorld,WorldSession} from '../src/SimplePZMapmaker.Web/wwwroot/world-workshop.js';
import {modulePlacementSettings} from '../src/SimplePZMapmaker.Web/wwwroot/building-modules.js';

const makeBuilding=(width=87,height=92)=>{
 const b=building.normalizeBuilding({id:'school',name:'School',kind:'school',x:20,y:20,width,height,
  sourceId:'osm-way-1504085667',footprintOrigin:'source-grid',
  sourceOutline:[[{longitude:32,latitude:54},{longitude:32.001,latitude:54},{longitude:32.001,latitude:54.001},{longitude:32,latitude:54}]],
  attribution:['OSM contributors'],design:{minLevel:0,material:'brick',roof:'flat',shape:'rect',variant:42,
   floors:[{cells:'1'.repeat(width*height),rooms:{1:'classroom'},edges:[],objects:[]}]}});
 building.refreshEdges(b,0);return building.normalizeBuilding(b);
};

// Run the actual event registrations with real format/validation functions.
// Loading all of app.js would initialize unrelated canvases, polling and APIs.
function register(file,prefix,bindings,receiver){
 const source=readFileSync(new URL('../src/SimplePZMapmaker.Web/wwwroot/'+file,import.meta.url),'utf8');
 const line=source.split(/\r?\n/).find(line=>line.trimStart().startsWith(prefix));
 assert.ok(line,'Event registration exists: '+prefix);
 const elements=new Map(),get=id=>{if(!elements.has(id))elements.set(id,{});return elements.get(id);};
 const scope={...building,...levels,Blob,crypto,structuredClone,mapAuthoringVersion,templateProjectVersion,validateWorld,$:get,get,...bindings};
 new Function(...Object.keys(scope),line).call(receiver,...Object.values(scope));
 return get;
}

async function exportTemplate(b,module=false){
 let output;
 const workshop={draft:structuredClone(b),saving:false,visual:{allLevels:false},
  attribution:()=>['Survey'],lock(){},message(message){throw Error(message);},
  readName(){},validateForSave:BuildingWorkshop.prototype.validateForSave,
  api:async(url,payload)=>{
   assert.equal(url,module?'/api/building/modules/transform':'/api/building/validate');
   const value=module?payload.module:payload;building.validateDesign(value);
   return module?structuredClone(value):{building:structuredClone(value)};
  },download(blob,name){output={blob,name};}};
 const run=async action=>action();
 const get=register(module?'building-modules.js':'workshop.js',module?"get('be-module-export').onclick=":"$('be-template-export').onclick=",{workshop,selected:b,run,status(){},load(){throw Error('A module is already selected.');}},workshop);
 await get(module?'be-module-export':'be-template-export').onclick();
 assert.ok(output);assert.equal(output.name,module?'module.pzbuilding.json':'building.pzbuilding.json');
 return JSON.parse(await output.blob.text());
}

async function importTemplate(document,project=blank()){
 let pending,error;
 const get=register('app.js',"$('building-template-file').onchange=",{project,history:new History(),changed(){},toast(){},
  task(_message,action){pending=action(new AbortController().signal).catch(e=>{error=e;});return pending;},
  api:async(url,b)=>{assert.equal(url,'/api/building/validate');building.validateDesign(b);return {building:b};}});
 const blob=new Blob([JSON.stringify(document)]),event={target:{files:[blob],value:'template.json'}};
 get('building-template-file').onchange(event);await pending;
 assert.equal(event.target.value,'');
 if(error)throw error;
 return project;
}

for(const module of [false,true]){
 test(`${module?'module':'workshop'} export round-trips large footprints as template 9`,async()=>{
  for(const [width,height] of [[87,92],[64,65],[65,64],[256,256]]){
   const b=makeBuilding(width,height);if(module)b.sourceKind='module:section';
   const document=await exportTemplate(b,module);
   assert.equal(document.version,9);
   const restored=buildingTemplateFromDocument(document);
   assert.deepEqual(restored,document.building);
   assert.equal(restored.design.floors[0].cells.length,width*height);
   assert.equal(restored.sourceId,'osm-way-1504085667');
   assert.deepEqual(restored.sourceOutline,b.sourceOutline);
   assert.deepEqual(document.attribution,['Survey','OSM contributors']);
  }
 });
 test(`${module?'module':'workshop'} export retains the existing small-template formats`,async()=>{
  for(const [feature,version] of [['plain',3],['imported',6],['open-roof',7],['furniture',8]]){
   const b=makeBuilding(64,64);
   if(feature!=='imported'){delete b.sourceOutline;delete b.footprintOrigin;}
   if(feature==='open-roof')b.design.roof='none';
   if(feature==='furniture')b.design.floors[0].objects.push({kind:'furniture',direction:'n',x:2,y:2,asset:'bed:0'});
   const document=await exportTemplate(b,module);assert.equal(document.version,version,feature);
   assert.doesNotThrow(()=>buildingTemplateFromDocument(document));
  }
 });
}

test('map template import accepts format 9 and serializes its library as map 16',async()=>{
 const b=makeBuilding(),project=await importTemplate({format:'SimplePZBuilding',version:9,building:b,attribution:['Survey']});
 const restored=JSON.parse(JSON.stringify(project));validateProject(restored);
 assert.equal(restored.version,16);assert.equal(restored.buildingLibrary.length,1);
 const saved=restored.buildingLibrary[0];assert.equal(saved.design.floors[0].cells.length,8004);
 assert.deepEqual(saved.sourceOutline,b.sourceOutline);assert.deepEqual(restored.attribution,['Survey','OSM contributors']);
});

test('map template import rejects a large footprint mislabeled with an older format atomically',async()=>{
 for(const version of [1,2,3,4,5,6,7,8]){
  const project=blank(),before=structuredClone(project);
  await assert.rejects(importTemplate({format:'SimplePZBuilding',version,building:makeBuilding()},project),/format 9|version 9/i);
  assert.deepEqual(project,before);
 }
});

test('small templates still import with their previous map format requirements',async()=>{
 for(const [feature,expected] of [['plain',4],['imported',9],['open-roof',10],['furniture',14]]){
  const b=makeBuilding(8,8);
  if(feature!=='imported'){delete b.sourceOutline;delete b.footprintOrigin;}
  if(feature==='open-roof')b.design.roof='none';
  if(feature==='furniture')b.design.floors[0].objects.push({kind:'furniture',direction:'n',x:2,y:2,asset:'bed:0'});
  const project=await importTemplate(await exportTemplate(b));
  assert.equal(project.version,expected,feature);assert.equal(project.buildingLibrary[0].width,8);
 }
});

test('template 9 cannot bypass the axis and floor-cell budgets on import',async()=>{
 const tooManyFloors=makeBuilding(256,256);
 tooManyFloors.design.floors=Array(5).fill(tooManyFloors.design.floors[0]);
 for(const b of [makeBuilding(513,10),makeBuilding(10,513),tooManyFloors]){
  const project=blank(),before=structuredClone(project);
  await assert.rejects(importTemplate({format:'SimplePZBuilding',version:9,building:b},project),/size|floor.*(budget|limit|262|tiles|cells)/i);
  assert.deepEqual(project,before);
 }
});

test('map adoption upgrades unstamped large buildings and templates before autosave',()=>{
 for(const library of [false,true]){
  const p=blank(512);p[library?'buildingLibrary':'buildings']=[makeBuilding()];
  const source=readFileSync(new URL('../src/SimplePZMapmaker.Web/wwwroot/app.js',import.meta.url),'utf8');
  const adopt=source.split(/\r?\n/).find(line=>line.startsWith('function adopt('));
  let versionAtChange;
  const scope={validateLocalProject:validateProject,validateGlobalProtection(){},worldMask(){},worldWorkshop:{beforeAdopt(){}},zonePanel:null,
   mapAuthoringVersion,normalizeBuilding:building.normalizeBuilding,nativeEditor:{select(){}},modelSprites:{},appearance:{reset(){}},
   createMapHistory:()=>new History(),fit(){},changed:()=>{versionAtChange=p.version;}};
  // Keep the app's assignment semantics while observing the autosave boundary.
  const execute=new Function(...Object.keys(scope),`let project,history,polygonReceiver,selectedId,selectedSpawn,gesture,dirty; ${adopt}; adopt(arguments[arguments.length-1]); return project;`);
  const adopted=execute(...Object.values(scope),p);
  assert.equal(adopted.version,16);assert.equal(versionAtChange,16);
 }
});

const makeWorld=()=>{
 const left=blank(),right=blank();right.worldX=101;left.spawns=right.spawns=[{x:1,y:1}];
 const b=makeBuilding(110,27);b.x=25820;b.y=25640;
 return {format:'SimplePZWorld',version:11,id:'a'.repeat(32),name:'School district',
  maps:[{id:'b'.repeat(32),project:left},{id:'c'.repeat(32),project:right}],buildings:[b]};
};

test('world 11 save and reopen preserve a shared large building across a seam',async()=>{
 const world=makeWorld();let output;
 const workshop={session:{world},flush(){},api:async(url,w)=>{assert.equal(url,'/api/world/validate');return {world:validateWorld(w)};},
  task:(_message,action)=>action(new AbortController().signal),download:blob=>{output=blob;},fileName:()=>world.name,state(){}};
 const get=register('world-workshop.js',"$('world-save').onclick=",{maxFile:128*1024*1024},workshop);
 await get('world-save').onclick();
 const restored=new WorldSession(JSON.parse(await output.text())).world;
 assert.equal(restored.version,11);assert.deepEqual(restored.buildings,world.buildings);
 assert.equal(restored.buildings[0].design.floors[0].cells.length,2970);
});

test('world validation promotes unstamped large section buildings and libraries before serialization',()=>{
 for(const library of [false,true]){
  const world=makeWorld();world.version=1;world.buildings=[];
  world.maps[0].project[library?'buildingLibrary':'buildings']=[makeBuilding()];
  validateWorld(world);
  const restored=JSON.parse(JSON.stringify(world));
  assert.equal(restored.maps[0].project.version,16);assert.equal(restored.version,11);
  assert.doesNotThrow(()=>new WorldSession(restored));
 }
});

test('legacy small worlds keep their format and future worlds remain rejected',()=>{
 const world=makeWorld();world.buildings=[];world.version=1;
 assert.equal(validateWorld(world).version,1);assert.equal(world.maps[0].project.version,4);
 assert.throws(()=>validateWorld({...world,version:14}),/format/i);
});

test('module subset placement still enforces the explicit 64 tile limit',()=>{
 assert.equal(modulePlacementSettings({x:63,y:63,level:0,quarterTurns:0}).x,63);
 assert.throws(()=>modulePlacementSettings({x:64,y:0,level:0,quarterTurns:0}));
});

test('standalone template projects stamp map16 for a large footprint even without source metadata',()=>{const b=makeBuilding();b.sourceOutline=[];b.footprintOrigin='authored';assert.equal(templateProjectVersion(b),16);assert.equal(templateProjectVersion(makeBuilding(8,8)),9);});
