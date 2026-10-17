import test from 'node:test';
import assert from 'node:assert/strict';
import {MapLayerState} from '../src/SimplePZMapmaker.Web/wwwroot/map-layers.js';
import {assertLayerChanges,terrainLayer,nativeLayer} from '../src/SimplePZMapmaker.Web/wwwroot/layer-edit-guards.js';
test('bulk changes cannot bypass a locked layer or hide a road edit as terrain',()=>{
 const state=new MapLayerState(),before={buildings:[],terrain:[0,2,6],zombies:[0]},after={...before,buildings:[{id:'house'}]};state.setLocked('buildings',true);assert.throws(()=>assertLayerChanges(before,after,null,state),/buildings/);state.setLocked('roads',true);assert.throws(()=>assertLayerChanges(before,{...before,terrain:[0,0,6]},null,state),/roads/);assert.doesNotThrow(()=>assertLayerChanges(before,{...before,name:'Rename'},null,state));
});
test('layer classification separates terrain, road, building geometry and models',()=>{
 assert.equal(terrainLayer(2),'roads');assert.equal(terrainLayer(6),'paths');assert.equal(nativeLayer({type:'model'},{}),'models');assert.equal(nativeLayer({x:3,y:3,z:1,layer:1},{width:16,terrain:[],buildings:[{x:2,y:2,width:4,height:4}]}),'buildings');
});
test('source edits and indoor models respect their separate layer locks',()=>{
 const state=new MapLayerState(),before={buildings:[{id:'home',design:{floors:[{staticModels:[]}]}}],sourceFeatures:[]};state.setLocked('models',true);assert.throws(()=>assertLayerChanges(before,{...before,buildings:[{id:'home',design:{floors:[{staticModels:[{id:'lamp'}]}]}}]},null,state),/models/);state.setLocked('source',true);assert.throws(()=>assertLayerChanges(before,{...before,sourceFeatures:[{id:'survey'}]},null,state),/source/);
});
