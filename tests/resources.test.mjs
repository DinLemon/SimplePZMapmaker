import test from 'node:test';
import assert from 'node:assert/strict';
import {SpriteImages} from '../src/SimplePZMapmaker.Web/wwwroot/resources.js';
test('atlas cache evicts unpinned pages, bounds decode concurrency and rejects oversized scenes',async()=>{
 const originalImage=globalThis.Image,originalFetch=globalThis.fetch;let active=0,maximum=0,decodes=0;
 globalThis.Image=class{naturalWidth=2048;naturalHeight=4096;async decode(){active++;maximum=Math.max(maximum,active);decodes++;await new Promise(r=>setTimeout(r,2));active--;}};
 globalThis.fetch=async(url,options)=>({ok:true,json:async()=>JSON.parse(options.body).map(id=>({id,page:id,pageWidth:2048,pageHeight:4096,sprite:{}}))});
 try{const cache=new SpriteImages();await cache.load(Array.from({length:15},(_,i)=>'tile_'+i));assert.equal(maximum,2);assert.ok(cache.bytes<=cache.budget);assert.equal(cache.pages.size,6);const count=decodes,owner=cache.beginScene();await assert.rejects(()=>cache.load(Array.from({length:7},(_,i)=>'tile_'+i),owner),/192МБ/);assert.equal(decodes,count);cache.endScene();const small=cache.beginScene();await cache.load(['tile_0','tile_1'],small);await cache.load(Array.from({length:10},(_,i)=>'tile_'+(20+i)));assert.ok(cache.pages.has('tile_0')&&cache.pages.has('tile_1'));assert.ok(cache.bytes<=cache.budget);cache.clear();assert.equal(cache.pages.size,0);assert.equal(cache.bytes,0);}finally{globalThis.Image=originalImage;globalThis.fetch=originalFetch;}
});
