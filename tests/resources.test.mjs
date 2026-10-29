import test from 'node:test';
import assert from 'node:assert/strict';
import {SpriteImages} from '../src/SimplePZMapmaker.Web/wwwroot/resources.js';
import * as resources from '../src/SimplePZMapmaker.Web/wwwroot/resources.js';

test('object palette search opts into grouping before pagination and retains raw and facing filters',()=>{
 assert.equal(typeof resources.resourceSearchUrl,'function');
 let url=new URL(resources.resourceSearchUrl({role:'object',q:'Hospital bed',source:'mod-one',facing:'N',offset:12,limit:12}),'http://localhost');
 assert.equal(url.searchParams.get('wholeObjects'),'true');assert.equal(url.searchParams.get('offset'),'12');assert.equal(url.searchParams.get('limit'),'12');assert.equal(url.searchParams.get('source'),'mod-one');assert.equal(url.searchParams.get('facing'),'N');
 url=new URL(resources.resourceSearchUrl({role:'object',rawTiles:true}),'http://localhost');assert.equal(url.searchParams.get('wholeObjects'),'false');
 url=new URL(resources.resourceSearchUrl({role:'wall'}),'http://localhost');assert.equal(url.searchParams.get('wholeObjects'),'false');
});

test('whole-object thumbnail draws all cropped parts in isometric depth order and fits their combined bounds',async()=>{
 const cache=new SpriteImages(),draws=[],clears=[],ctx={clearRect:(...v)=>clears.push(v),drawImage:(...v)=>draws.push(v)};
 const sprite={x:1,y:2,width:80,height:120,offsetX:24,offsetY:40,frameWidth:128,frameHeight:256};
 const a={id:'bed_0',page:'atlas',sprite,wholeObject:true,parts:[{dx:0,dy:1,tile:'bed_0'},{dx:0,dy:0,tile:'bed_1'}]};
 cache.meta.set('bed_1',{id:'bed_1',page:'atlas',sprite:{...sprite,x:90,width:50,offsetX:30}});cache.pages.set('atlas',{});
 cache.load=async ids=>{assert.deepEqual(ids,['bed_0','bed_1']);return true;};
 const canvas={width:120,height:130,isConnected:true,getContext:()=>ctx,setAttribute(){}};
 await cache.thumbnail(canvas,a);assert.equal(draws.length,2);assert.equal(draws[0][1],90);assert.equal(draws[1][1],1);assert.equal(clears.length,1);
 for(const d of draws){assert.ok(d[5]>=3.999999&&d[6]>=3.999999);assert.ok(d[5]+d[7]<=116.000001&&d[6]+d[8]<=126.000001);}
 assert.ok(draws[1][5]<draws[0][5]);assert.ok(draws[1][6]>draws[0][6]);
});

test('a missing part never renders a misleading partial whole-object thumbnail',async()=>{
 const cache=new SpriteImages(),draws=[];cache.pages.set('atlas',{});cache.load=async()=>true;
 const canvas={width:100,height:94,isConnected:true,getContext:()=>({clearRect(){},drawImage:(...v)=>draws.push(v)}),setAttribute(k,v){this[k]=v;}};
 await cache.thumbnail(canvas,{id:'bed_0',page:'atlas',sprite:{width:20,height:20},wholeObject:true,parts:[{dx:0,dy:0,tile:'bed_0'},{dx:1,dy:0,tile:'bed_1'}]});
 assert.equal(draws.length,0);assert.match(canvas['aria-label'],/not available/i);
});
test('atlas cache evicts unpinned pages, bounds decode concurrency and rejects oversized scenes',async()=>{
 const originalImage=globalThis.Image,originalFetch=globalThis.fetch;let active=0,maximum=0,decodes=0;
 globalThis.Image=class{naturalWidth=2048;naturalHeight=4096;async decode(){active++;maximum=Math.max(maximum,active);decodes++;await new Promise(r=>setTimeout(r,2));active--;}};
 globalThis.fetch=async(url,options)=>({ok:true,json:async()=>JSON.parse(options.body).map(id=>({id,page:id,pageWidth:2048,pageHeight:4096,sprite:{}}))});
 try{const cache=new SpriteImages();await cache.load(Array.from({length:15},(_,i)=>'tile_'+i));assert.equal(maximum,2);assert.ok(cache.bytes<=cache.budget);assert.equal(cache.pages.size,6);const count=decodes,owner=cache.beginScene();await assert.rejects(()=>cache.load(Array.from({length:7},(_,i)=>'tile_'+i),owner),/192MB/);assert.equal(decodes,count);cache.endScene();const small=cache.beginScene();await cache.load(['tile_0','tile_1'],small);await cache.load(Array.from({length:10},(_,i)=>'tile_'+(20+i)));assert.ok(cache.pages.has('tile_0')&&cache.pages.has('tile_1'));assert.ok(cache.bytes<=cache.budget);cache.clear();assert.equal(cache.pages.size,0);assert.equal(cache.bytes,0);}finally{globalThis.Image=originalImage;globalThis.fetch=originalFetch;}
});
