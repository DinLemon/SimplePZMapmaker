import test from 'node:test';
import assert from 'node:assert/strict';
import {validateGeographicWarp,inverseGeographicWarp,warpedWorldGeographic} from '../src/SimplePZMapmaker.Web/wwwroot/geographic-warp.js';

const grid=()=>({sourceX:[0,256,512],sourceY:[0,256,512],destination:[0,256,512].flatMap(y=>[0,256,512].map(x=>({x,y})))});
const reference=(rotationDegrees=0)=>({bounds:{west:45,east:45.004,north:55.004,south:55},originCellX:100,originCellY:100,metresPerTile:1,rotationDegrees});
const near=(a,b,epsilon=1e-8)=>assert.ok(Math.abs(a-b)<=epsilon,`${a} != ${b}`);

test('warp JS splits the same source cells and inverts the destination triangles',()=>{
 const d=grid();d.destination[4]={x:320,y:180};const mesh=validateGeographicWarp(d);
 for(const [x,y,tx,ty] of [[0,0,0,0],[256,256,320,180],[128,128,160,90],[384,128,384,128],[128,384,128,384],[512,512,512,512]]){
  const p=mesh.forward(x,y);near(p.x,tx);near(p.y,ty);const q=inverseGeographicWarp(d,tx,ty);near(q.x,x);near(q.y,y);
 }
 for(let y=0;y<=512;y+=8)for(let x=0;x<=512;x+=8){const q=mesh.inverse(x,y);assert.ok(q);const p=mesh.forward(q.x,q.y);near(p.x,x);near(p.y,y);}
 assert.equal(inverseGeographicWarp(d,-1,0),null);assert.equal(inverseGeographicWarp(d,NaN,0),null);
 assert.throws(()=>mesh.forward(513,0));
 const old=mesh.forward(256,256);d.destination[4]={x:300,y:160};assert.deepEqual(mesh.forward(256,256),old);
 validateGeographicWarp(d);assert.deepEqual(inverseGeographicWarp(d,300,160),{x:256,y:256});
});
test('warp JS validation rejects folds missing vertices holes and global overlap with positive faces',()=>{
 const folded=grid();folded.destination[4]={x:900,y:20};assert.throws(()=>validateGeographicWarp(folded));
 const missing=grid();missing.destination.pop();assert.throws(()=>validateGeographicWarp(missing));
 const repeated=grid();repeated.sourceX=[0,256,256];assert.throws(()=>validateGeographicWarp(repeated));
 const tiny=grid();tiny.sourceX=[0,1e-12,512];assert.throws(()=>validateGeographicWarp(tiny));
 const huge=grid();huge.sourceY=Array.from({length:34},(_,i)=>i);assert.throws(()=>validateGeographicWarp(huge));
 const bad=grid();bad.destination[0].x=Infinity;assert.throws(()=>validateGeographicWarp(bad));
 const overlap={sourceX:[0,1],sourceY:Array.from({length:17},(_,i)=>i)};
 overlap.destination=overlap.sourceY.flatMap(y=>[1,2].map(r=>({x:10+r*Math.cos(y*Math.PI/4),y:10+r*Math.sin(y*Math.PI/4)})));
 assert.throws(()=>validateGeographicWarp(overlap),/boundary/i);
 const ref=reference();ref.warp={sourceX:[0,1],sourceY:[0,1],destination:[{x:0,y:0},{x:1,y:0},{x:0,y:1},{x:1,y:1}]};
 assert.throws(()=>validateGeographicWarp(ref.warp,ref),/cover/i);
});
test('warp JS geographic inverse has Core origin and rotation minima',()=>{
 const ref=reference(-17);ref.warp=grid();ref.warp.destination[4]={x:320,y:180};
 // Independent Core GeographicProjection.Geographic fixtures are filled below.
 for(const [x,y,lon,lat] of CORE_FIXTURES){const p=warpedWorldGeographic(ref,x,y);assert.ok(p);near(p.longitude,lon,1e-11);near(p.latitude,lat,1e-11);}
 assert.equal(warpedWorldGeographic(ref,25599,25600),null);
 const plain=reference();assert.deepEqual(warpedWorldGeographic(plain,25600,25600),{longitude:45,latitude:55.004});
 const scaled={...reference(),metresPerTile:2,originCellX:203,originCellY:207,warp:grid()};scaled.warp.destination=scaled.warp.destination.map(p=>({x:p.x*.5,y:p.y*.5}));
 const p=warpedWorldGeographic(scaled,203*256+64,207*256+32);near(p.longitude,45+256/(Math.cos((55.004+55)/2*Math.PI/180)*111320),1e-11);near(p.latitude,55.004-128/111320,1e-11);
});
const CORE_FIXTURES=[
 [25600,25600,45.00034192485489,55.00464144776632],
 [25728,25728,45.00115032069877,55.00301175376786],
 [25920,25780,45.003004006716,55.001769895718745],
 [26111,26111,45.00565175423936,54.99890810044565],
 [25856,25856,45.00215932778804,55.001456492931396],
 [25984,25728,45.005507330620546,55.002533311372346]
];
