import test from 'node:test';
import assert from 'node:assert/strict';
import {blank} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {globalBuildingMask,validateGlobalProtection,sharedLocalDecorations} from '../src/SimplePZMapmaker.Web/wwwroot/world-protection.js';
import {validateSharedDecorations,worldAuthoringVersion,sharedDecorationId} from '../src/SimplePZMapmaker.Web/wwwroot/world-geography.js';
import {validateDesign,refreshEdges} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';

// A three-tile counter whose first two tiles end section 100 and whose last tile starts section 101.
const counter=()=>({id:'shared-counter',x:100*256+254,y:100*256+40,asset:'fixtures_counters_01_0',parts:[{dx:0,dy:0,tile:'fixtures_counters_01_0'},{dx:1,dy:0,tile:'fixtures_counters_01_1'},{dx:2,dy:0,tile:'fixtures_counters_01_2'}]});
const world=()=>{const p=blank(),q=blank();q.worldX=101;q.spawns=[];return {format:'SimplePZWorld',version:7,id:'a'.repeat(32),name:'World',maps:[{id:'b'.repeat(32),project:p},{id:'c'.repeat(32),project:q}],decorations:[counter()]};};

test('a shared decoration is clipped into each section it reaches',()=>{
 const w=world(),[left]=sharedLocalDecorations(w,w.maps[0].project),[right]=sharedLocalDecorations(w,w.maps[1].project);
 assert.equal(left.x,254);assert.deepEqual(left.parts.map(p=>p.tile),['fixtures_counters_01_0','fixtures_counters_01_1']);
 assert.equal(right.x,-2);assert.deepEqual(right.parts.map(p=>p.dx),[2]);
 assert.equal(worldAuthoringVersion(w),8);
});

test('shared decorations protect their tiles from local objects and water',()=>{
 const w=world(),p=w.maps[1].project,mask=globalBuildingMask(w,p);assert.ok(mask.has(40*p.width));
 p.decorations=[{id:'bush',x:0,y:40,asset:'vegetation_ornamental_01_0',parts:[{dx:0,dy:0,tile:'vegetation_ornamental_01_0'}]}];
 assert.throws(()=>validateGlobalProtection(p,mask),/shared world building or decoration/);
 p.decorations=[];p.terrain[40*p.width]=3;assert.throws(()=>validateGlobalProtection(p,mask),/flood/);
});

test('shared decoration validation mirrors the core rules',()=>{
 validateSharedDecorations(world());
 const gap=world();gap.decorations[0].x=101*256+254;assert.throws(()=>validateSharedDecorations(gap),/empty part/);
 const wet=world();wet.maps[1].project.terrain[40*256]=3;assert.throws(()=>validateSharedDecorations(wet),/water/);
 const spawn=world();spawn.maps[0].project.spawns.push({x:255,y:40});assert.throws(()=>validateSharedDecorations(spawn),/spawn/);
 const twice=world();twice.decorations.push({...counter(),id:'other',x:100*256+255});assert.throws(()=>validateSharedDecorations(twice),/another shared decoration/);
 assert.equal(sharedDecorationId('world-road-1-2'),false);assert.equal(sharedDecorationId(crypto.randomUUID()),true);
});

test('a building under 6x6 is a valid one-room design',()=>{
 const b={id:'shed',name:'Shed',kind:'warehouse',x:10,y:10,width:3,height:2,design:{material:'wood',roof:'flat',shape:'rect',variant:1,floors:[{cells:'111111',rooms:{'1':'storage'},edges:[],objects:[]}]}};
 refreshEdges(b,0);validateDesign(b);
 assert.throws(()=>validateDesign({...b,width:1,height:6,design:{...b.design,floors:[{...b.design.floors[0],cells:'111111'}]}}),/2–512/);
});
