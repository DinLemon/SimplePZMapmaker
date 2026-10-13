import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,validateProject,paint} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {decodeTerrainEdits} from '../src/SimplePZMapmaker.Web/wwwroot/resource-data.js';
test('agent checkpoint restores raster, dependencies and static models in one undo',()=>{
 const p=blank(),before=structuredClone(p),next=structuredClone(p);next.name='Agent edit';next.version=6;next.terrain[70]=5;next.zombies[2]=30;next.requiredMods=['ExampleMod'];next.assetSources={example_0:'ExampleMod'};next.staticModels=[{id:'a'.repeat(32),modelId:'Base.Book',sourceId:'game',x:5,y:5,z:0,offsetX:.5,offsetY:.5,offsetZ:0,rotation:90,scale:1,requiredMods:[]}];
 const history=new History();history.checkpoint(p,next);assert.deepEqual(p,next);assert.equal(history.past.length,1);assert.equal(history.undo(p),true);assert.deepEqual(p,before);assert.equal(history.redo(p),true);assert.deepEqual(p,next);
});
test('checkpoint cannot resize a map or change anything on invalid input',()=>{
 const p=blank(),before=structuredClone(p),history=new History();assert.throws(()=>history.checkpoint(p,blank(512)));assert.deepEqual(p,before);assert.equal(history.past.length,0);
});
test('static models require project format6 and valid finite coordinates',()=>{
 const p=blank();p.staticModels=[{id:'a'.repeat(32),modelId:'Base.Book',sourceId:'game',x:5,y:5,z:0,offsetX:.5,offsetY:.5,offsetZ:0,rotation:0,scale:1,requiredMods:[]}];assert.throws(()=>validateProject(p));p.version=6;assert.doesNotThrow(()=>validateProject(p));p.staticModels[0].scale=NaN;assert.throws(()=>validateProject(p));
});
test('manual ground mask marks only touched cells and is reversible even for same-color paint',()=>{const p=blank(),before=structuredClone(p),h=new History();h.begin(p);paint(p,9,9,0,1,h);h.commit(p);assert.equal(p.version,6);const mask=decodeTerrainEdits(p),index=9*p.width+9;assert.ok(mask[index>>3]&(1<<(index&7)));assert.equal(mask.reduce((sum,byte)=>sum+byte.toString(2).split('1').length-1,0),1);h.undo(p);assert.deepEqual(p,before);h.redo(p);assert.equal(decodeTerrainEdits(p)[index>>3],mask[index>>3]);});
