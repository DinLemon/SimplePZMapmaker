import test from 'node:test';
import assert from 'node:assert/strict';
import {createTilesetClient,starterTilesetSpec,requirePublishedSession,referenceTarget,assetDependencyChanges,visionHandoff} from '../src/SimplePZMapmaker.Web/wwwroot/agent-assets.js';

const token='a'.repeat(64),id='b'.repeat(32);
const source=()=>({token,revision:3,remote:3,dirty:false,mode:'map',busy:()=>false});
const specInput={modId:'custom_signs',name:'Custom signs',tileset:'custom_signs_01',fileNumber:701,source:'Original PNG',reference:'Retained original',license:'CC0',originalAuthor:'Artist',canRedistribute:true};
test('references and asset creation require a published matching session',()=>{
 assert.equal(requirePublishedSession(source()).sessionToken,token);
 for(const patch of [{dirty:true},{remote:4},{token:null},{revision:0},{busy:()=>true}])assert.throws(()=>requirePublishedSession({...source(),...patch}));
 assert.throws(()=>requirePublishedSession({...source(),running:true},{idle:true}),/operation/i);
});
test('starter requires explicit identities and actual provenance and declares native frames',()=>{
 const spec=starterTilesetSpec(specInput);
 assert.equal(spec.modId,'custom_signs');assert.equal(spec.provenance.originalAuthor,'Artist');assert.equal(spec.provenance.kind,'source-authored');
 assert.deepEqual(spec.frames,[{key:'sprite',direction:'none',width:128,height:256,properties:{CustomName:'Custom signs'}}]);
 const nw=starterTilesetSpec({...specInput,directions:'nw'});assert.deepEqual(nw.frames.map(f=>f.direction),['n','w']);
 for(const patch of [{modId:''},{tileset:'bad_'},{fileNumber:99},{license:''},{canRedistribute:false}])assert.throws(()=>starterTilesetSpec({...specInput,...patch}));
});
test('host reference selection uses only VisualTargetRequest fields and needs an actual target',()=>{
 assert.deepEqual(referenceTarget({scope:'world',memberId:id,buildingId:'c'.repeat(32)},'world'),{scope:'world',memberId:id,buildingId:'c'.repeat(32)});
 assert.deepEqual(referenceTarget({longitude:0,latitude:0},'map'),{scope:'map',longitude:0,latitude:0});
 assert.throws(()=>referenceTarget(null,'map'),/Select/);
 assert.throws(()=>referenceTarget({scope:'world',memberId:id},'world'),/Select/);
 assert.throws(()=>referenceTarget({scope:'world',buildingId:id},'map'),/scope/);
 assert.throws(()=>referenceTarget({localX:12,localY:14},'map'),/Select/);
});
test('review exposes attached resource dependencies for maps and the exact world member',()=>{
 const before={requiredMods:[],assetSources:{}},after={requiredMods:['fixture_signs'],assetSources:{fixture_signs_01_0:'fixture_signs'}};
 assert.match(assetDependencyChanges(before,after).join(' '),/fixture_signs/);
 const world={maps:[{id,name:'North section',project:before}]},next={maps:[{id,name:'North section',project:after}]};
 assert.match(assetDependencyChanges(world,next,true).join(' '),/North section.*fixture_signs/);
 assert.deepEqual(assetDependencyChanges(before,structuredClone(before)),[]);
});
test('tileset requests carry source and draft revisions, including world member selection',async()=>{
 const calls=[];let revision=1;
 const client=createTilesetClient(async(url,options)=>{calls.push({url,...options});return {ok:true,json:async()=>({id,revision:revision++})};});
 await client.create({sessionToken:token,revision:3,scope:'world',memberId:'c'.repeat(32)},starterTilesetSpec(specInput));
 assert.deepEqual(JSON.parse(calls[0].body),{sessionToken:token,expectedRevision:3,scope:'world',memberId:'c'.repeat(32),spec:starterTilesetSpec(specInput)});
 await client.upload(token,{id,revision:1},0,{type:'image/png',size:3,arrayBuffer:async()=>Uint8Array.from([1,2,3]).buffer});
 assert.deepEqual(JSON.parse(calls[1].body),{expectedDraftRevision:1,pngBase64:'AQID'});
 await client.build(token,{id,revision:2});assert.deepEqual(JSON.parse(calls[2].body),{expectedDraftRevision:2});
 await client.attach({sessionToken:token,revision:3},{id,revision:3});assert.deepEqual(JSON.parse(calls[3].body),{expectedRevision:3,expectedDraftRevision:3});
 assert.equal(calls.every(c=>c.credentials==='same-origin'&&c.cache==='no-store'),true);
});
test('client blocks unsafe IDs, missing world members and oversized uploads before sending',async()=>{
 let calls=0;const client=createTilesetClient(async()=>{calls++;throw Error('unexpected network');});
 await assert.rejects(client.create({sessionToken:token,revision:3,scope:'world'},{}),/section|member/i);
 await assert.rejects(client.read(token,'../other'),/draft/i);
 await assert.rejects(client.upload(token,{id,revision:1},0,{type:'image/png',size:2097153}),/2 MiB/);
 await assert.rejects(client.upload(token,{id,revision:1},0,{type:'image/jpeg',size:3}),/PNG/);
 assert.equal(calls,0);
});
test('preview and download verify displayed draft revision around binary reads',async()=>{
 const calls=[];let revision=8;
 const client=createTilesetClient(async(url)=>{calls.push(url);return {ok:true,json:async()=>url.endsWith('/metadata')?{origin:'validated-native-pack'}:{id,revision},blob:async()=>new Blob(['pixels'])};});
 const draft={id,revision:8};const preview=await client.preview(token,draft,0);
 assert.equal(preview.metadata.origin,'validated-native-pack');assert.equal(preview.blob.size,6);assert.equal(calls.filter(c=>c.endsWith(id)).length,2);
 calls.length=0;await client.download(token,draft);assert.match(calls[1],/mod.zip$/);assert.equal(calls.length,3);
 revision=9;await assert.rejects(client.preview(token,draft,0),/draft changed/i);
});
test('HTTP conflicts retain currentRevision and do not masquerade as empty drafts',async()=>{
 const client=createTilesetClient(async()=>({ok:false,status:409,json:async()=>({error:'Conflict',currentRevision:10})}));
 await assert.rejects(client.build(token,{id,revision:8}),e=>e.status===409&&e.currentRevision===10);
});
test('binary preview/download are rejected if another agent uploads during the read',async()=>{
 for(const operation of ['preview','download']){
  let revision=1;const client=createTilesetClient(async url=>({ok:true,json:async()=>url.endsWith('/metadata')?{origin:'uploaded-png'}:{id,revision},blob:async()=>{revision++;return new Blob(['stale']);}}));
  await assert.rejects(client[operation](token,{id,revision:1},0),/draft changed/i);
 }
});
test('vision handoff contains actual tool calls, session, target and receipt requirements',()=>{
 const text=visionHandoff({sessionToken:token,revision:3,target:{scope:'world',memberId:'c'.repeat(32),buildingId:id},referenceId:'d'.repeat(32),instructions:'Do not infer unseen interiors.'},'http://localhost:1234');
 for(const value of [token,id,'visual_reference_view','visual_reference_observe','receiptId','expectedRevision','http://localhost:1234/mcp','Do not infer unseen interiors.'])assert.ok(text.includes(value),value);
 assert.match(text,/not.*automatically/i);
});
