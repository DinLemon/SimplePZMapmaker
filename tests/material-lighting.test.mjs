import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../src/SimplePZMapmaker.Web/wwwroot/vendor/three/three.module.js';
import {materialSettings,modelInLightArea,nativeFieldTexture,materialFieldShader,materialCamera,modelWorldPosition,wallShadowSegments} from '../src/SimplePZMapmaker.Web/wwwroot/material-lighting.js';
const result={x:10,y:20,size:8,level:-1,cells:[[0,0,-1,1,.25,0],[1,0,-1,0,0,0]]};
test('material lighting requires bounded controls and keeps models outside solved floor/crop untouched',()=>{
 assert.deepEqual(materialSettings({roughness:.3,metalness:.5,azimuth:90,elevation:45,shadows:false}),{roughness:.3,metalness:.5,azimuth:90,elevation:45,shadows:false});
 for(const patch of [{roughness:-1},{metalness:2},{azimuth:NaN},{elevation:0},{elevation:91}])assert.throws(()=>materialSettings(patch));
 assert.ok(modelInLightArea({x:10,y:20,z:-1},result));for(const m of [{x:9,y:20,z:-1},{x:18,y:20,z:-1},{x:10,y:20,z:0}])assert.equal(modelInLightArea(m,result),false);
});
test('native RGB texture preserves solved darkness and missing cells instead of inventing ambient light',()=>{
 const texture=nativeFieldTexture(result);assert.equal(texture.image.width,8);assert.deepEqual([...texture.image.data.slice(0,12)],[255,64,0,255,0,0,0,255,0,0,0,0]);assert.equal(texture.minFilter,THREE.NearestFilter);texture.dispose();
});
test('material shader samples actual world coordinates and multiplies diffuse plus specular response by native light',()=>{
 const shader={uniforms:{},vertexShader:'#include <project_vertex>',fragmentShader:'#include <opaque_fragment>'};const texture=nativeFieldTexture(result);materialFieldShader(shader,texture,result);assert.match(shader.vertexShader,/modelMatrix \* vec4\( transformed/);assert.match(shader.fragmentShader,/outgoingLight \*=/);assert.equal(shader.uniforms.nativeField.value,texture);assert.deepEqual(shader.uniforms.nativeArea.value.toArray(),[10,20,8]);texture.dispose();
});
test('3D mesh camera exactly matches native isometric axes on signed levels and separate pan/zoom',()=>{
 for(const level of [-32,-1,0,31]){const t={zoom:17,pan:{x:300,y:80},viewport:{width:1000,height:700}},camera=materialCamera(t),p=new THREE.Vector3(12,level*3,23).project(camera);assert.ok(Math.abs((p.x+1)*500-(300+(12-23)*17))<1e-6);assert.ok(Math.abs((1-p.y)*350-(80+(12+23)*17/2-level*3*17))<1e-6);}
 assert.deepEqual(modelWorldPosition({x:4,y:7,z:-1,offsetX:.2,offsetY:.8,offsetZ:.1,profile:'pickup'}),[4.2,-2.7,7.8]);
});
test('authored shadow geometry respects signed floors, open doors and window apertures',()=>{
 const building={x:10,y:20,design:{minLevel:-1,floors:[{edges:[{x:1,y:1,edge:'n',kind:'wall'},{x:2,y:1,edge:'w',kind:'door',open:true},{x:3,y:1,edge:'w',kind:'window'},{x:4,y:1,edge:'n',kind:'open'}]}]}};
 const s=wallShadowSegments({project:{buildings:[building]}},result);assert.equal(s.length,3);assert.deepEqual(s.map(x=>[x.bottom,x.top]),[[0,3],[0,.9],[2.25,3]]);assert.ok(s.every(x=>x.x!==12));assert.deepEqual(wallShadowSegments({project:{buildings:[building]}},{...result,level:0}),[]);
});
