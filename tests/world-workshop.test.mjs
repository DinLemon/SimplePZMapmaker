import test from 'node:test';
import assert from 'node:assert/strict';
import {blank} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {WorldSession,worldBounds,placementIssue,validateWorld,worldCellAt} from '../src/SimplePZMapmaker.Web/wwwroot/world-workshop.js';
const id=n=>n.toString(16).padStart(32,'0');
const section=(n,x=100+n,y=100)=>({id:id(n),project:{...blank(),name:'Section '+n,worldX:x,worldY:y}});
test('replacing nine large sections retains the latest undo above the normal history budget',()=>{const before={id:id(95),name:'Before',format:'SimplePZWorld',version:1,maps:Array.from({length:9},(_,i)=>({id:id(i+1),project:{...blank(1024),worldX:100+i*4,worldY:100}}))},after=structuredClone(before);after.name='After';const s=new WorldSession(before);s.replace(after);assert.equal(s.history.past.length,1);assert.equal(s.undo(),true);assert.equal(s.world.name,'Before');assert.equal(s.redo(),true);assert.equal(s.world.name,'After');});
test('large history refuses an oversized replacement before losing the original',()=>{const before={id:id(96),name:'Before',format:'SimplePZWorld',version:1,maps:Array.from({length:8},(_,i)=>({id:id(i+1),project:{...blank(2048),worldX:100+i*8,worldY:100}}))},after=structuredClone(before);after.name='After';const s=new WorldSession(before);assert.throws(()=>s.replace(after),/512/);assert.equal(s.world,before);assert.equal(s.history.past.length,0);});
test('nine adjacent sections retain individual rasters and exceed the single-map editor limit',()=>{
 const session=new WorldSession({id:id(90),name:'City',format:'SimplePZWorld',version:1,maps:Array.from({length:9},(_,i)=>section(i+1))});
 const bounds=worldBounds(session.world);assert.equal(bounds.width,2304);assert.equal(bounds.cells,9);
 assert.ok(session.world.maps.every(m=>m.project.width===256&&m.project.terrain.length===65536));assert.equal('terrain' in session.world,false);
});
test('cell footprint overlap is rejected atomically, touching edges are allowed',()=>{
 const session=new WorldSession({id:id(90),name:'City',format:'SimplePZWorld',version:1,maps:[section(1,100,100),section(2,101,100)]});
 const before=JSON.stringify(session.world);assert.throws(()=>session.move(id(2),100,100),/overlap/i);assert.equal(JSON.stringify(session.world),before);assert.equal(session.history.past.length,0);
 session.move(id(2),102,100);assert.equal(session.world.maps[1].project.worldX,102);session.undo();assert.equal(session.world.maps[1].project.worldX,101);session.redo();assert.equal(session.world.maps[1].project.worldX,102);
});
test('world move history preserves fresh map edits and never clones full terrain on a move',()=>{
 const session=new WorldSession({id:id(90),name:'City',format:'SimplePZWorld',version:1,maps:[section(1,100,100)]}),terrain=session.world.maps[0].project.terrain;
 session.move(id(1),103,100);terrain[42]=2;session.undo();assert.equal(session.world.maps[0].project.terrain,terrain);assert.equal(terrain[42],2);assert.equal(session.world.maps[0].project.worldX,100);
 session.redo();assert.equal(terrain[42],2);assert.equal(session.world.maps[0].project.worldX,103);
});
test('remove/undo retains section data and rename is reversible',()=>{
 const session=new WorldSession({id:id(90),name:'City',format:'SimplePZWorld',version:1,maps:[section(1)]}),p=session.world.maps[0].project;
 p.terrain[42]=2;session.remove(id(1));session.undo();assert.equal(session.world.maps[0].project,p);assert.equal(p.terrain[42],2);
 session.rename('District');session.undo();assert.equal(session.world.name,'City');session.redo();assert.equal(session.world.name,'District');
});
test('import rejects malformed world identifiers and overlapping source files',()=>{
 const world={id:id(90),name:'City',format:'SimplePZWorld',version:1,maps:[section(1),section(2)]};validateWorld(world);
 assert.throws(()=>validateWorld({...world,id:'../outside'}),/identifier/i);
 const duplicate=structuredClone(world);duplicate.maps[1].id=duplicate.maps[0].id;assert.throws(()=>validateWorld(duplicate),/identifier/i);
 const overlap=structuredClone(world);overlap.maps[1].project.worldX=overlap.maps[0].project.worldX;assert.throws(()=>validateWorld(overlap),/overlap/i);
});
test('negative mouse coordinates and panned/zoomed canvas select integer native cells',()=>{
 assert.deepEqual(worldCellAt(399,201,{x:400,y:200,scale:20}),{x:-1,y:0});
 assert.deepEqual(worldCellAt(445,239,{x:400,y:200,scale:20}),{x:2,y:1});
 assert.match(placementIssue({maps:[]},section(1).project,-1,100),/0.*1000/);
});
test('completed world replacement is one reversible operation and partial overlapping data is rejected',()=>{
 const session=new WorldSession({id:id(90),name:'Old city',format:'SimplePZWorld',version:1,maps:[section(1)]}),old=session.world;
 const complete={id:id(91),name:'Imported city',format:'SimplePZWorld',version:1,maps:[section(2),section(3)]};session.replace(complete);assert.equal(session.world,complete);assert.equal(session.history.past.length,1);session.undo();assert.equal(session.world,old);session.redo();assert.equal(session.world,complete);
 const partial=structuredClone(complete);partial.maps[1].project.worldX=partial.maps[0].project.worldX;assert.throws(()=>session.replace(partial),/overlap/i);assert.equal(session.world,complete);assert.equal(session.history.past.length,1);
});
