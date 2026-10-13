import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {placeEnvironmentDecoration,placeEnvironmentModel,moveEnvironmentSelection,environmentSelection,paintEnvironment,applyEnvironmentRegion} from '../src/SimplePZMapmaker.Web/wwwroot/environment-model.js';
const asset={id:'furniture_bedding_01_69',parts:[{dx:0,dy:0,tile:'furniture_bedding_01_69',flags:1},{dx:0,dy:1,tile:'furniture_bedding_01_68',flags:1}]};
const model={modelId:'Base.BeerBottle',sourceId:'game',requiredMods:[],offsetX:.5,offsetY:.5,offsetZ:0,rotation:0,scale:1};
test('complete environment objects reject water, occupied tails and boundaries without changing the draft',()=>{
 const p=blank();p.terrain[11*256+10]=3;const before=JSON.stringify(p);assert.throws(()=>placeEnvironmentDecoration(p,asset,10,10,'one'),/пересека/);assert.equal(JSON.stringify(p),before);
 p.terrain[11*256+10]=0;placeEnvironmentDecoration(p,asset,10,10,'one');assert.throws(()=>placeEnvironmentDecoration(p,asset,10,11,'two'),/пересека/);assert.throws(()=>placeEnvironmentDecoration(p,asset,10,255,'three'),/пересека/);assert.equal(p.decorations.length,1);
});
test('area selection includes object tails and group movement preserves canonical parts atomically',()=>{
 const p=blank();placeEnvironmentDecoration(p,asset,10,10,'one');placeEnvironmentDecoration(p,asset,12,10,'two');const selection=environmentSelection(p,{x:10,y:11},{x:12,y:11});assert.deepEqual(selection,['decor:one','decor:two']);const parts=JSON.stringify(p.decorations.map(o=>o.parts));
 moveEnvironmentSelection(p,selection,5,2);assert.equal(p.decorations[0].x,15);assert.equal(p.decorations[1].x,17);assert.equal(JSON.stringify(p.decorations.map(o=>o.parts)),parts);
 const before=JSON.stringify(p);assert.throws(()=>moveEnvironmentSelection(p,selection,250,0),/пересека/);assert.equal(JSON.stringify(p),before);
});
test('terrain paths preserve protected objects and water painting cannot drown a static model',()=>{
 const p=blank();placeEnvironmentModel(p,model,20,20,'a'.repeat(32));placeEnvironmentDecoration(p,asset,21,20,'one');const history=new History();history.begin(p);paintEnvironment(p,{x:18,y:20},{x:23,y:20},3,1,history);history.commit(p);
 assert.equal(p.terrain[20*256+18],3);assert.equal(p.terrain[20*256+20],0);assert.equal(p.terrain[20*256+21],0);assert.equal(p.terrain[20*256+128],0);history.undo(p);assert.ok(p.terrain.every(t=>t===0));history.redo(p);assert.equal(p.terrain[20*256+18],3);
});
test('regional appearance changes and model/decor history retain original geography and dependencies',()=>{
 const p=blank();p.requiredMods=['ExampleMod'];p.version=6;const terrain=p.terrain.slice(),history=new History();history.begin(p);applyEnvironmentRegion(p,{x:10,y:10},{x:20,y:25},'rural');placeEnvironmentModel(p,{...model,requiredMods:['ExampleMod']},30,30,'a'.repeat(32));history.commit(p);
 assert.deepEqual(p.terrain,terrain);assert.deepEqual(p.surfaceRegions[0],{x:10,y:10,width:11,height:16,theme:'rural'});history.undo(p);assert.equal(p.staticModels.length,0);assert.equal(p.surfaceRegions.length,0);assert.deepEqual(p.requiredMods,['ExampleMod']);history.redo(p);assert.equal(p.staticModels.length,1);assert.equal(p.version,6);
});
