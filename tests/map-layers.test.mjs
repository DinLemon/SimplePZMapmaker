import test from 'node:test';
import assert from 'node:assert/strict';
import {MapLayerState, sectionOverlays, sourceOverlays, projectPoint,installMapLayers} from '../src/SimplePZMapmaker.Web/wwwroot/map-layers.js';

test('hiding and ordering layers changes view state without deleting project objects',()=>{
 const project={buildings:[{id:'house'}],terrain:[1,2]},before=structuredClone(project),layers=new MapLayerState();
 layers.setVisible('buildings',false);layers.setOrder(['buildings','terrain']);
 assert.equal(layers.isVisible('buildings'),false);assert.equal(layers.canEdit('buildings'),false);
 assert.deepEqual(project,before);assert.deepEqual(layers.getState().order.slice(0,2),['buildings','terrain']);
 const snapshot=layers.getState();snapshot.layers.terrain.visible=false;assert.equal(layers.isVisible('terrain'),true);
});
test('locked layers reject edits even after being made visible and unknown layers fail closed',()=>{
 const layers=new MapLayerState();layers.setLocked('roads',true);layers.setVisible('roads',false);layers.setVisible('roads',true);
 const data={width:3};assert.throws(()=>layers.guardEdit('roads',()=>data.width=4),/locked/i);assert.equal(data.width,3);
 assert.throws(()=>layers.guardEdit('misspelled',()=>data.width=5),/layer/i);layers.setLocked('roads',false);layers.guardEdit('roads',()=>data.width=6);assert.equal(data.width,6);
});
test('section overlays retain unloaded neighbours in active section coordinates',()=>{
 const context={project:{worldX:100,worldY:200},memberId:'a',world:{maps:[{id:'a',project:{}}]},manifest:{sections:[{id:'a',name:'Active',worldX:100,worldY:200,width:256,height:256},{id:'b',name:'East',worldX:101,worldY:200,width:512,height:256}]}};
 const overlays=sectionOverlays(context);assert.deepEqual(overlays.map(s=>[s.id,s.x,s.y,s.width,s.loaded,s.active]),[['a',0,0,256,true,true],['b',256,0,512,false,false]]);
 assert.equal(overlays[1].label,'East · unloaded');
});
test('source overlays use projected evidence and preserve original geographic rings',()=>{
 const feature={id:'source',geometryKind:'polygon',sourceGeometry:[[{longitude:44,latitude:55}]],geometry:[[{x:3,y:4},{x:6,y:4},{x:6,y:7},{x:3,y:4}]]},p={sourceFeatures:[feature]},before=structuredClone(p);
 assert.deepEqual(sourceOverlays(p)[0].points,[{x:3,y:4},{x:6,y:4},{x:6,y:7},{x:3,y:4}]);assert.deepEqual(p,before);
});
test('screen projection follows native isometric transform and floor height',()=>{
 assert.deepEqual(projectPoint({x:10,y:6,z:2},{zoom:4,pan:{x:100,y:50},isometric:true}),{x:116,y:58});
 assert.deepEqual(projectPoint({x:10,y:6},{zoom:4,pan:{x:100,y:50},isometric:false}),{x:140,y:74});
});
test('single manifest neighbour remains unloaded when the active member is absent from its rows',()=>{
 const [s]=sectionOverlays({project:{worldX:100,worldY:200},memberId:'active',manifest:{sections:[{id:'neighbour',name:'East',worldX:101,worldY:200,width:256,height:256}]}});
 assert.equal(s.loaded,false);assert.equal(s.active,false);assert.equal(s.label,'East · unloaded');
});
test('source site outer and hole boundaries both remain visible without changing geometry',()=>{
 const rings=[[{x:0,y:0},{x:20,y:0},{x:20,y:20},{x:0,y:0}],[{x:5,y:5},{x:8,y:5},{x:8,y:8},{x:5,y:5}]];
 const project={sites:[{id:'parcel',name:'Courtyard',boundary:rings}]};assert.equal(sourceOverlays(project).length,2);assert.deepEqual(sourceOverlays(project)[1].points,rings[1]);
});
test('annotation order follows the visible overlay controls without reordering physical sprite layers',()=>{
 const strokes=[],ctx={save(){},restore(){},beginPath(){},moveTo(){},lineTo(){},closePath(){},setLineDash(){},stroke(){strokes.push(this.strokeStyle);},fillText(){}};
 const project={worldX:0,worldY:0,width:256,height:256,sourceFeatures:[{id:'line',geometryKind:'line',geometry:[[{x:1,y:1},{x:2,y:2}]]}]},layers=installMapLayers({getProject:()=>project});layers.setVisible('source',true);
 layers.setOrder(['source','sections']);layers.draw(ctx,{project});assert.deepEqual(strokes,['#6bc8e3','#edc57e']);
 strokes.length=0;layers.setOrder(['sections','source']);layers.draw(ctx,{project});assert.deepEqual(strokes,['#edc57e','#6bc8e3']);
});
