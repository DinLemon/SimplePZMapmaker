import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,validateProject,History,paint} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {validateDesign} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import {validateWorld} from '../src/SimplePZMapmaker.Web/wwwroot/world-workshop.js';
const model=()=>({id:'a'.repeat(32),modelId:'Base.Test',sourceId:'game',x:50,y:50,z:0,profile:'obstacle',footprint:[{dx:0,dy:0},{dx:1,dy:0}]});
test('complete ground model footprints reject secondary water spawn building and blocker collisions',()=>{
 for(const mutate of [p=>p.terrain[50*p.width+51]=3,p=>p.spawns.push({x:51,y:50}),p=>p.buildings.push({id:'house',name:'House',kind:'house',x:51,y:50,width:8,height:8}),p=>p.staticModels.push({...model(),id:'b'.repeat(32),x:51,footprint:[]})]){const p=blank();p.version=9;p.staticModels=[model()];mutate(p);assert.throws(()=>validateProject(p));}
 const valid=blank();valid.version=9;valid.staticModels=[model()];assert.equal(validateProject(valid),valid);
});
test('terrain painting protects every model footprint cell with and without pending history',()=>{
 for(const withHistory of [false,true]){const p=blank();p.version=9;p.staticModels=[model()];const h=withHistory?new History():null;if(h)h.begin(p);paint(p,51,50,3,1,h);assert.equal(p.terrain[50*p.width+51],0);paint(p,52,50,3,1,h);assert.equal(p.terrain[50*p.width+52],3);if(h){h.commit(p);h.undo(p);assert.equal(p.terrain[50*p.width+52],0);h.redo(p);assert.equal(p.terrain[50*p.width+52],3);}}
});
test('interactive project format promotion and metadata remain a single reversible change',()=>{
 const p=blank(),h=new History();h.begin(p);p.staticModels.push(model());h.commit(p);assert.equal(p.version,9);h.undo(p);assert.equal(p.version,4);assert.deepEqual(p.staticModels,[]);h.redo(p);assert.equal(p.version,9);assert.equal(p.staticModels[0].footprint.length,2);
});
test('source footprint metadata validates legacy closed rings and rejects broken geographic evidence',()=>{
 const b={kind:'house',width:8,height:8};validateDesign(b);const ring=[{longitude:30,latitude:50},{longitude:31,latitude:50},{longitude:31,latitude:51},{longitude:30,latitude:50}];validateDesign({...b,sourceOutline:[ring],footprintOrigin:'source-grid'});
 for(const sourceOutline of [null,[[...ring.slice(0,3)]],[[...ring.slice(0,3),{longitude:32,latitude:50}]],[[...ring.slice(0,3),{longitude:Infinity,latitude:50}]]])assert.throws(()=>validateDesign({...b,sourceOutline}));assert.throws(()=>validateDesign({...b,footprintOrigin:'invented'}));
});
test('world serialization promotes new section authoring without changing its raster or legacy worlds',()=>{
 const p=blank();p.version=9;p.staticModels=[model()];const terrain=p.terrain,world={format:'SimplePZWorld',id:'c'.repeat(32),name:'World',version:1,maps:[{id:'d'.repeat(32),project:p}]};validateWorld(world);assert.equal(world.version,3);assert.equal(world.maps[0].project.terrain,terrain);const legacy={...world,version:1,maps:[{id:'e'.repeat(32),project:blank()}]};validateWorld(legacy);assert.equal(legacy.version,1);
});
