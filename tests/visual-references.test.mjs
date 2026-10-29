import test from 'node:test';
import assert from 'node:assert/strict';
import {externalReferenceLinks,validateOwnPhoto,nearestReferences,createReferenceClient} from '../src/SimplePZMapmaker.Web/wwwroot/visual-references.js';

test('navigation links preserve longitude/latitude and reject nonfinite coordinates',()=>{
 const links=externalReferenceLinks(44.2,55.8);
 assert.equal(new URL(links.google).searchParams.get('query'),'55.8,44.2');
 assert.equal(new URL(links.yandex).searchParams.get('ll'),'44.2,55.8');
 assert.throws(()=>externalReferenceLinks(NaN,10));
 assert.throws(()=>externalReferenceLinks(10,91));
});
test('own uploads reject unsupported formats and oversized data before reading',()=>{
 assert.doesNotThrow(()=>validateOwnPhoto({type:'image/jpeg',size:200}));
 assert.throws(()=>validateOwnPhoto({type:'image/svg+xml',size:200}),/PNG|JPEG/);
 assert.throws(()=>validateOwnPhoto({type:'image/png',size:8*1024*1024+1}),/8 MiB/);
});
test('nearest references include zero coordinates and put unknown locations last',()=>{
 const references=[{id:'unknown'},{id:'far',longitude:1,latitude:0},{id:'near',longitude:0,latitude:0}];
 assert.deepEqual(nearestReferences(references,0,0).map(r=>r.id),['near','far','unknown']);
 assert.equal(references[0].id,'unknown');
});
test('client uses selected source ID rather than accepting a download URL and carries revision',async()=>{
 const calls=[];const client=createReferenceClient(async(url,options)=>{calls.push([url,options]);return {ok:true,json:async()=>({id:'saved'})};});
 await client.importMapillary('a'.repeat(64),7,{scope:'world',memberId:'b'.repeat(32)},'1234');
 assert.match(calls[0][0],/mapillary\/import$/);
 const body=JSON.parse(calls[0][1].body);
 assert.equal(body.sourceId,'1234');assert.equal(body.revision,7);assert.equal(body.target.scope,'world');assert.equal(body.url,undefined);
 await assert.rejects(client.importMapillary('a'.repeat(64),7,{},'https://evil.test/'),/source ID/);
 assert.equal(calls.length,1);
});
test('client reports capability errors instead of turning them into empty search results',async()=>{
 const client=createReferenceClient(async()=>({ok:false,status:503,json:async()=>({error:'Mapillary requires PZMAP_MAPILLARY_TOKEN.'})}));
 await assert.rejects(client.search('a'.repeat(64),1,{}),/PZMAP_MAPILLARY_TOKEN/);
});

test('own photo uploads use bounded JSON compatible with the application mutation gate',async()=>{
 let request;
 const client=createReferenceClient(async(url,options)=>{request={url,...options};return {ok:true,json:async()=>({id:'saved'})};});
 const file=new Blob([new Uint8Array([137,80,78,71])],{type:'image/png'});
 await client.upload('a'.repeat(64),3,{scope:'map',buildingId:'house'},file,{author:'Photographer',license:'own-photo'});
 assert.equal(request.headers['Content-Type'],'application/json');
 const body=JSON.parse(request.body);assert.equal(body.mimeType,'image/png');assert.equal(body.base64,'iVBORw==');assert.equal(body.revision,3);
 const abort=new AbortController();abort.abort();await assert.rejects(client.upload('a'.repeat(64),3,{},file,{},abort.signal),{name:'AbortError'});
});
