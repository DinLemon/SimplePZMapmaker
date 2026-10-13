import test from 'node:test';
import assert from 'node:assert/strict';
import {isoProject,isoHit,nearestEdge,zoomAt,placementCheck} from '../src/SimplePZMapmaker.Web/wwwroot/visual-workshop.js';
import {BuildingWorkshop} from '../src/SimplePZMapmaker.Web/wwwroot/workshop.js';

test('isometric picking selects the same cell at signed levels, pan and zoom',()=>{
 for(const z of [-16,-2,0,8,15])for(const scale of [.08,.5,2.8])for(const [x,y] of [[0,0],[31,18],[63,63]]){
  const transform={x:347,y:-81,scale},point=isoProject(x+.35-.5,y+.64-.5,z,transform),hit=isoHit(point.x,point.y,z,transform);
  assert.equal(hit.x,x);assert.equal(hit.y,y);assert.ok(Math.abs(hit.fx-x-.35)<1e-10);
 }
});
test('wheel zoom preserves the world cell below the pointer',()=>{
 const old={x:100,y:200,scale:.4},anchor={x:409,y:276},before=isoHit(anchor.x,anchor.y,-3,old);
 const next=zoomAt(old,anchor,1.7),after=isoHit(anchor.x,anchor.y,-3,next);
 assert.ok(Math.abs(before.fx-after.fx)<1e-10);assert.ok(Math.abs(before.fy-after.fy)<1e-10);
});
test('south and east boundary clicks address canonical north/west edges',()=>{
 assert.deepEqual(nearestEdge({x:4,y:2,fx:4.5,fy:2.99}),{x:4,y:3,edge:'n'});
 assert.deepEqual(nearestEdge({x:4,y:2,fx:4.99,fy:2.5}),{x:5,y:2,edge:'w'});
});
const building=()=>({width:6,height:6,design:{minLevel:0,floors:[{cells:'1'.repeat(36),rooms:{1:'livingroom'},objects:[],edges:[]}]}});
const bed=(x,y)=>({x,y,kind:'asset',direction:'n',asset:'furniture_bedding_01_69',parts:[{dx:0,dy:0,tile:'furniture_bedding_01_69'},{dx:0,dy:1,tile:'furniture_bedding_01_68'}]});
test('whole objects reject edge overflow, occupied tails, partitions and blocked doorways',()=>{
 const b=building();assert.equal(placementCheck(b,0,bed(2,2)).ok,true);
 assert.match(placementCheck(b,0,bed(2,5)).reason,/контур/);
 b.design.floors[0].objects.push({x:2,y:3,kind:'seat',direction:'n'});
 assert.match(placementCheck(b,0,bed(2,2)).reason,/занята/);
 b.design.floors[0].objects=[];b.design.floors[0].edges.push({x:2,y:3,edge:'n',kind:'wall'});
 assert.match(placementCheck(b,0,bed(2,2)).reason,/стен/);
 b.design.floors[0].edges=[{x:2,y:2,edge:'w',kind:'door'}];
 assert.match(placementCheck(b,0,bed(2,2)).reason,/проход/);
});
test('stair landing from the lower floor is reserved even on the upper floor',()=>{
 const b=building();b.design.floors.push(structuredClone(b.design.floors[0]));
 b.design.floors[0].objects.push({x:2,y:0,kind:'stairs',direction:'n'});
 assert.match(placementCheck(b,1,bed(2,2)).reason,/лестниц/);
 assert.equal(placementCheck(b,1,bed(4,2)).ok,true);
});

// Invoke the real edge-placement operation without a DOM or exporter. References
// are the installed catalog's compatible N/W tiles, including the reported case.
const wallBrush={n:'carpentry_02_81',w:'carpentry_02_80'},doorBrush={n:'carpentry_01_49',w:'carpentry_01_48'},windowBrush={n:'fixtures_windows_01_1',w:'fixtures_windows_01_0'};
const doorFrame={n:'walls_exterior_house_01_11',w:'walls_exterior_house_01_10'},windowFrame={n:'walls_exterior_house_01_9',w:'walls_exterior_house_01_8'};
function paintEdge(b,direction,kind,pairs={}){
 const p=direction==='n'?{x:1,y:0,fx:1.5,fy:.05}:{x:0,y:1,fx:.05,fy:1.5};
 BuildingWorkshop.prototype.place.call({draft:b,floor:b.design.floors[0],tool:kind,wallPair:null,doorPair:null,windowPair:null,...pairs},p);
 return b.design.floors[0].edges[0];
}
test('a real custom wall converts to a door or window without retaining its solid wall frame',()=>{
 for(const direction of ['n','w'])for(const kind of ['door','window']){
  const b=building();paintEdge(b,direction,'wall',{wallPair:wallBrush});
  const opening=paintEdge(b,direction,kind,{[kind+'Pair']:kind==='door'?doorBrush:windowBrush});
  assert.equal(opening.wallTile,undefined);assert.equal(opening.openingTile,(kind==='door'?doorBrush:windowBrush)[direction]);
  assert.equal(opening.kind,kind);assert.equal(opening.edge,direction);
 }
});
test('door and window conversions clear the former opening and frame before applying the new brush',()=>{
 for(const direction of ['n','w'])for(const oldKind of ['door','window'])for(const useBrush of [false,true]){
  const b=building(),newKind=oldKind==='door'?'window':'door';
  const old=paintEdge(b,direction,oldKind,{[oldKind+'Pair']:oldKind==='door'?doorBrush:windowBrush});old.wallTile=(oldKind==='door'?doorFrame:windowFrame)[direction];
  const next=paintEdge(b,direction,newKind,useBrush?{[newKind+'Pair']:newKind==='door'?doorBrush:windowBrush}:{});
  assert.equal(next.wallTile,undefined);assert.equal(next.openingTile,useBrush?(newKind==='door'?doorBrush:windowBrush)[direction]:undefined);
 }
});
test('portals become a wall or open passage with no stale opening references',()=>{
 for(const direction of ['n','w'])for(const oldKind of ['door','window'])for(const kind of ['wall','open'])for(const useBrush of [false,true]){
  const b=building(),old=paintEdge(b,direction,oldKind,{[oldKind+'Pair']:oldKind==='door'?doorBrush:windowBrush});old.wallTile=(oldKind==='door'?doorFrame:windowFrame)[direction];
  const next=paintEdge(b,direction,kind,useBrush?{wallPair:wallBrush,doorPair:doorBrush,windowPair:windowBrush}:{});
  assert.equal(next.openingTile,undefined);assert.equal(next.wallTile,kind==='wall'&&useBrush?wallBrush[direction]:undefined);
 }
});
test('repainting the same kind preserves compatible explicit references and replaces only the selected resource',()=>{
 for(const direction of ['n','w'])for(const kind of ['wall','door','window']){
  const b=building(),old=paintEdge(b,direction,kind);
  old.wallTile=(kind==='wall'?wallBrush:kind==='door'?doorFrame:windowFrame)[direction];
  if(kind!=='wall')old.openingTile=(kind==='door'?doorBrush:windowBrush)[direction];
  const before=structuredClone(old),same=paintEdge(b,direction,kind);assert.deepEqual(same,before);
  const alternate={n:'fixtures_doors_01_1',w:'fixtures_doors_01_0'};
  if(kind==='door'){const next=paintEdge(b,direction,kind,{doorPair:alternate});assert.equal(next.wallTile,doorFrame[direction]);assert.equal(next.openingTile,alternate[direction]);}
 }
 const b=building(),wall=paintEdge(b,'n','wall',{wallPair:wallBrush});wall.openingTile=doorBrush.n;
 assert.equal(paintEdge(b,'n','wall').openingTile,undefined);
});
