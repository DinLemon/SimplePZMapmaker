import test from 'node:test';
import assert from 'node:assert/strict';
import {roadRampCells,snapRampNode} from '../src/SimplePZMapmaker.Web/wwwroot/road-ramps.js';
import {validateLinearFeatures} from '../src/SimplePZMapmaker.Web/wwwroot/linear-editing.js';
import {worldPathCells,validateWorldInfrastructure} from '../src/SimplePZMapmaker.Web/wwwroot/world-infrastructure.js';

const ramp=(extra={})=>({id:'ramp',kind:'bridge',width:4,level:1,access:'ramps',interpolation:'polyline',points:[{x:100,y:100},{x:141,y:100}],underlay:[],decorationIds:[],generatedDecorations:[],assetSources:{},...extra});
test('ramp drawing snaps integer nodes to the selected cardinal axis without mutating anchors',()=>{
 const anchor={x:100,y:100};assert.deepEqual(snapRampNode({x:141.3,y:102.8},anchor),{x:141,y:100});
 assert.deepEqual(snapRampNode({x:98.1,y:150.7},anchor),{x:100,y:151});
 assert.deepEqual(snapRampNode({x:23.7,y:40.2}),{x:24,y:40});assert.deepEqual(anchor,{x:100,y:100});
});
test('road ramps reserve exact full-width approaches without rounded end caps',()=>{
 for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]])for(const level of [1,2])for(const width of [1,4,16]){
  const f=ramp({width,level,points:[{x:100,y:100},{x:100+dx*(40*level+1),y:100+dy*(40*level+1)}]});
  const cells=roadRampCells(f);assert.equal(cells.size,(40*level+2)*width);assert.deepEqual(worldPathCells(f),cells);
  assert.ok(cells.has('100:100'));assert.ok(cells.has(`${f.points[1].x}:${f.points[1].y}`));
  assert.doesNotThrow(()=>validateLinearFeatures({width:256,height:256,linearFeatures:[f]}));
 }
});
test('road ramp unsupported profiles cannot enter a saved map or world',()=>{
 for(const patch of [{level:0},{level:-1},{level:3},{kind:'sidewalk'},{width:1.5},{width:17},{interpolation:'catmull-rom'},{points:[{x:100,y:100},{x:140,y:100}]},{points:[{x:100,y:100},{x:141,y:101}]},{points:[{x:100.5,y:100},{x:141.5,y:100}]},{deckArtwork:[{x:120,y:100,tile:'blends_street_01_1'}]}]){
  const f=ramp(patch);assert.throws(()=>roadRampCells(f),/ramp|approach/i);
  assert.throws(()=>validateLinearFeatures({width:256,height:256,linearFeatures:[f]}));
 }
});
test('shared ramp reservations cross 255 256 once and reject an unmapped approach',()=>{
 const f=ramp({points:[{x:240,y:100},{x:281,y:100}]}),maps=[0,1].map(worldX=>({project:{worldX,worldY:0,width:256,height:256}}));
 const world={maps,linearFeatures:[f]};assert.doesNotThrow(()=>validateWorldInfrastructure(world));
 const cells=worldPathCells(f);assert.ok(cells.has('255:100')&&cells.has('256:100'));
 assert.throws(()=>validateWorldInfrastructure({...world,maps:maps.slice(0,1)}),/unmapped/);
 assert.throws(()=>validateLinearFeatures({width:256,height:256,linearFeatures:[f]}),/outside|bounds/i);
});
