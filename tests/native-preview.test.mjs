import test from 'node:test';
import assert from 'node:assert/strict';
import {nativeChunkPlan} from '../src/SimplePZMapmaker.Web/wwwroot/map-native-preview.js';
test('large native overview covers a 2048 map in 64 bounded cell requests',()=>{
 const plan=nativeChunkPlan({width:2048,height:2048,zoom:.2,pan:{x:450,y:95},viewport:{width:900,height:600},level:0});
 assert.equal(plan.length,64);assert.ok(plan.every(c=>c.size===256&&c.stride===8&&c.x%256===0&&c.y%256===0));assert.equal(new Set(plan.map(c=>c.key)).size,64);
});
test('detailed viewport requests only visible native tiles and respects cell seams',()=>{
 const plan=nativeChunkPlan({width:2048,height:2048,zoom:8,pan:{x:450,y:-4096},viewport:{width:900,height:600},level:0});
 assert.ok(plan.length>0&&plan.length<96);assert.ok(plan.every(c=>c.size===32&&c.stride===1&&c.x%256+c.size<=256&&c.y%256+c.size<=256&&c.x>=0&&c.y>=0&&c.x+c.size<=2048&&c.y+c.size<=2048));
});
test('floor-height culling includes sprites shifted from signed basement levels',()=>{
 const view={width:2048,height:2048,zoom:8,pan:{x:450,y:-4096},viewport:{width:900,height:600}};
 const ground=nativeChunkPlan({...view,level:0}),basement=nativeChunkPlan({...view,level:-16}),upper=nativeChunkPlan({...view,level:15});
 assert.ok(Math.min(...basement.map(c=>c.x+c.y))<Math.min(...ground.map(c=>c.x+c.y)));
 assert.ok(Math.max(...upper.map(c=>c.x+c.y))>Math.max(...ground.map(c=>c.x+c.y)));
});
test('native viewport ignores invalid camera state',()=>{
 assert.deepEqual(nativeChunkPlan({width:256,height:256,zoom:0,pan:{x:0,y:0},viewport:{width:900,height:600}}),[]);
});
