import {test} from 'node:test';
import assert from 'node:assert/strict';
import {blank,paint,line,canPlace,History,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
test('brush clips at edges and preserves buildings from water',()=>{
 const p=blank(256);p.buildings.push({id:'a',kind:'house',x:10,y:10,width:12,height:10,name:'Дом'});
 paint(p,-1,0,3,4);assert.equal(p.terrain[0],3);paint(p,12,12,3,2);assert.equal(p.terrain[12*256+12],0);assert.equal(p.terrain.length,65536);
});
test('road line connects both endpoints',()=>{const p=blank(256);line(p,2,2,20,20,2,1);assert.equal(p.terrain[2*256+2],2);assert.equal(p.terrain[20*256+20],2);assert.equal(p.terrain[11*256+11],2);});
test('buildings cannot overlap or cross cells',()=>{
 const p=blank(512);const b={x:20,y:20,width:12,height:10,kind:'house',id:'a',name:'Дом'};assert.equal(canPlace(p,b),true);p.buildings.push(b);assert.equal(canPlace(p,{...b,x:22}),false);assert.equal(canPlace(p,{...b,x:250}),false);
});
test('undo and redo restore terrain and objects together',()=>{
 const p=blank(256), h=new History();h.begin(p);paint(p,20,20,2,1,h);p.buildings.push({id:'a',kind:'house',x:50,y:50,width:12,height:10,name:'Дом'});h.commit(p);
 h.undo(p);assert.equal(p.terrain[20*256+20],0);assert.equal(p.buildings.length,0);h.redo(p);assert.equal(p.terrain[20*256+20],2);assert.equal(p.buildings.length,1);
});
test('malformed project and water spawns rejected',()=>{assert.throws(()=>validateProject({}));const p=blank(256);p.terrain[p.spawns[0].y*256+p.spawns[0].x]=3;assert.throws(()=>validateProject(p));});
