import {test} from 'node:test';
import assert from 'node:assert/strict';
import {blank,paint,line,canPlace,History,validateProject,boundsFromSearchResult} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
test('brush clips at edges and preserves buildings from water',()=>{
 const p=blank(256);p.buildings.push({id:'a',kind:'house',x:10,y:10,width:12,height:10,name:'Дом'});
 paint(p,-1,0,3,4);assert.equal(p.terrain[0],3);paint(p,12,12,3,2);assert.equal(p.terrain[12*256+12],0);assert.equal(p.terrain.length,65536);
});
test('road line connects both endpoints',()=>{const p=blank(256);line(p,2,2,20,20,2,1);assert.equal(p.terrain[2*256+2],2);assert.equal(p.terrain[20*256+20],2);assert.equal(p.terrain[11*256+11],2);});
test('buildings cannot overlap but can span native cells inside a section',()=>{
 const p=blank(512);const b={x:20,y:20,width:12,height:10,kind:'house',id:'a',name:'Дом'};assert.equal(canPlace(p,b),true);p.buildings.push(b);assert.equal(canPlace(p,{...b,x:22}),false);assert.equal(canPlace(p,{...b,x:250}),true);
});
test('undo and redo restore terrain and objects together',()=>{
 const p=blank(256), h=new History();h.begin(p);paint(p,20,20,2,1,h);p.buildings.push({id:'a',kind:'house',x:50,y:50,width:12,height:10,name:'Дом'});h.commit(p);
 h.undo(p);assert.equal(p.terrain[20*256+20],0);assert.equal(p.buildings.length,0);h.redo(p);assert.equal(p.terrain[20*256+20],2);assert.equal(p.buildings.length,1);
});
test('malformed project and water spawns rejected',()=>{assert.throws(()=>validateProject({}));const p=blank(256);p.terrain[p.spawns[0].y*256+p.spawns[0].x]=3;assert.throws(()=>validateProject(p));});
test('unchanged metadata focus cannot resurrect undone objects',()=>{
 const p=blank(),h=new History();h.begin(p);p.buildings.push({id:'a',kind:'house',x:30,y:30,width:12,height:10,name:'Дом'});h.commit(p);
 h.begin(p);h.undo(p);h.begin(p);paint(p,70,70,2,1,h);h.commit(p);h.undo(p);assert.equal(p.buildings.length,0);assert.equal(p.terrain[70*256+70],0);
});
test('seed and bounds are validated before save or export',()=>{for(const seed of [undefined,2**40,1.5]){const p=blank();p.seed=seed;assert.throws(()=>validateProject(p));}const p=blank();p.bounds={north:60,south:50,east:30,west:20};assert.throws(()=>validateProject(p));});
test('final usable perimeter and cell crossings match native validation',()=>{const p=blank(),b={id:'edge',kind:'house',name:'Дом',x:249,y:20,width:6,height:6};assert.equal(canPlace(p,b),true);assert.equal(canPlace(p,{...b,x:250}),false);});
test('2048 project survives browser validation',()=>{assert.equal(validateProject(blank(2048)).width,2048);});
test('search result retains entire Pilna boundary and rejects oversized cities',()=>{
 assert.deepEqual(boundsFromSearchResult({boundingbox:['55.5363','55.5781','45.8833','45.9422']}),{south:55.5363,north:55.5781,west:45.8833,east:45.9422});
 assert.equal(boundsFromSearchResult({boundingbox:['0','90','0','180']}),null);assert.equal(boundsFromSearchResult({boundingbox:['oops']}),null);
});
test('broad fill history stays compact and undo remains exact',()=>{
 const p=blank(512),h=new History();h.begin(p);for(let i=0;i<p.terrain.length;i++){h.touch('terrain',i,p.terrain[i]);p.terrain[i]=5;}h.commit(p);
 assert.ok(JSON.stringify(h.past).length<8_000_000,'Broad fill keeps hundreds of thousands of history objects');
 h.undo(p);assert.ok(p.terrain.every(v=>v===0));h.redo(p);assert.ok(p.terrain.every(v=>v===5));
});
