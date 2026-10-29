import test from 'node:test';import assert from 'node:assert/strict';
import {objectParts,furnitureItem,validateDesign} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import {transformFacing,NativeTransformResolver,transformBuilding,transformObjectGroup} from '../src/SimplePZMapmaker.Web/wwwroot/building-transform.js';

const house=()=>({id:'furniture',name:'Furniture',kind:'house',x:10,y:10,width:6,height:5,design:{material:'wood',roof:'flat',shape:'rect',variant:0,floors:[{cells:'1'.repeat(30),rooms:{1:'livingroom'},edges:[],objects:[{kind:'furniture',asset:'bed:0',direction:'n',x:2,y:1}]}]}});
const resources=missing=>new NativeTransformResolver(async url=>{
 const tile=decodeURIComponent(url.split('/').at(-1));assert.ok(url.startsWith('/api/assets/item/'));
 if(tile===missing)throw Error('Native resource missing: '+tile);
 return {id:tile,modId:null,requirements:[],page:'local',sprite:{},properties:{}};
});

test('TileZed rotation verifies target artwork and refuses missing images atomically for buildings and selections',async()=>{
 for(const transform of [b=>transformBuilding(b,'cw',resources('furniture_bedding_01_70')),b=>transformObjectGroup(b,0,['object:0'],'cw',resources('furniture_bedding_01_70'))]){
  const b=house(),before=structuredClone(b);await assert.rejects(transform(b),/Native resource missing/);assert.deepEqual(b,before);
 }
});

test('selected TileZed rotation cannot hide missing original artwork by replacing its direction',async()=>{
 const b=house(),before=structuredClone(b);
 await assert.rejects(transformObjectGroup(b,0,['object:0'],'cw',resources('furniture_bedding_01_1')),/Native resource missing/);assert.deepEqual(b,before);
});

test('four clockwise TileZed rotations preserve the furniture family, both bed cells, ownership and source draft',async()=>{
 const b=house(),before=structuredClone(b);let next=b;
 for(const [direction,tiles] of [['e',[70,71]],['s',[69,68]],['w',[2,3]],['n',[1,0]]]){next=await transformBuilding(next,'cw',resources());assert.equal(next.design.floors[0].objects[0].direction,direction);assert.equal(objectParts(next.design.floors[0].objects[0]).length,2);for(const tile of tiles)assert.equal(next.assetSources['furniture_bedding_01_'+tile],'game');validateDesign(next,false);}
 assert.deepEqual(next.design.floors[0].objects,b.design.floors[0].objects.map(o=>({...o,style:'',parts:[]})));assert.deepEqual(b,before);
});
test('TileZed furniture resolves all four layouts and rotates with the building',()=>{
 const bed={kind:'furniture',asset:'bed:0',direction:'w',x:2,y:3};
 assert.deepEqual(objectParts(bed).map(p=>[p.dx,p.dy]),[[0,0],[1,0]]);assert.deepEqual(objectParts({...bed,direction:'s'}).map(p=>[p.dx,p.dy]),[[0,0],[0,1]]);
 assert.equal(furnitureItem('no-such','n'),undefined);assert.equal(transformFacing('W','cw'),'N');assert.equal(transformFacing('N','mirror-y'),'S');
});
test('a world with furniture in a shared building or a format 14 section is world format 9',async()=>{const {worldAuthoringVersion}=await import('../src/SimplePZMapmaker.Web/wwwroot/world-geography.js');
 const furnished={design:{floors:[{objects:[{kind:'furniture',asset:'bed:0',direction:'n',x:1,y:1}]}]}};
 assert.equal(worldAuthoringVersion({version:2,buildings:[furnished],maps:[]}),9);assert.equal(worldAuthoringVersion({version:2,buildings:[],maps:[{project:{version:14}}]}),9);assert.equal(worldAuthoringVersion({version:10,buildings:[furnished],maps:[]}),10);});
