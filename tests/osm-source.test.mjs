import test from 'node:test';
import assert from 'node:assert/strict';
import {boundsFromCentre,areaDimensions,boundsKey,worldSizePreview,uploadOsmSource,sourceMode,JSON_SOURCE_LIMIT,PBF_SOURCE_LIMIT,readOsmImportStream,importOsmStream,OsmSourceSelection,regionPlanText,formatOsmProgress} from '../src/SimplePZMapmaker.Web/wwwroot/osm-source.js';

const bounds={south:54.778,north:54.780,west:32.045,east:32.048};
test('rectangular centre selection round trips and rejects empty or polar coordinates',()=>{
 const b=boundsFromCentre(54.78,32.04,2,1),size=areaDimensions(b);
 assert.ok(Math.abs(size.width-2000)<1e-6);assert.ok(Math.abs(size.height-1000)<1e-6);
 assert.throws(()=>boundsFromCentre('',32,1,1));assert.throws(()=>boundsFromCentre(90,32,1,1));
 assert.equal(boundsKey(bounds),boundsKey({east:bounds.east,west:bounds.west,north:bounds.north,south:bounds.south}));
});
test('rotated rectangular size preview counts padded sections and native cells',()=>{
 const b=boundsFromCentre(0,0,1,.5),p=worldSizePreview({bounds:b,metresPerTile:1,sectionSize:256,rotationDegrees:30});
 assert.equal(p.columns,5);assert.equal(p.rows,4);assert.equal(p.cells,20);assert.equal(p.width,1280);
 assert.throws(()=>worldSizePreview({bounds:b,metresPerTile:0,sectionSize:256}));
});
test('source modes retain legacy regional compatibility',()=>{
 assert.equal(sourceMode('smolensk-extract'),'regional');assert.equal(sourceMode('auto'),'auto');assert.equal(sourceMode('global'),'global');assert.throws(()=>sourceMode('invalid'));
});

const result={project:{name:'Лес 🌲',width:256},importedFeatures:1,warnings:[],sourceId:'download-only'};
const ndjson=items=>items.map(item=>JSON.stringify(item)+'\n').join('');
function responseChunks(text,length=7){const bytes=new TextEncoder().encode(text);let offset=0;return new Response(new ReadableStream({pull(controller){if(offset===bytes.length)return controller.close();controller.enqueue(bytes.slice(offset,offset+=Math.min(length,bytes.length-offset)));}}));}
test('NDJSON decodes arbitrary UTF8/chunk boundaries and emits progress before returning the completed result',async()=>{
 const progress={phase:'region-download',bytesReceived:1024,totalBytes:2048,source:'Germany'},seen=[];
 assert.deepEqual(await readOsmImportStream(responseChunks(ndjson([{kind:'progress',progress},{kind:'result',result}]),1),{onProgress:p=>seen.push(p)}),result);
 assert.deepEqual(seen,[progress]);assert.match(formatOsmProgress(progress),/1 KiB.*2 KiB/);
});
test('NDJSON requires a final result and a complete newline-terminated stream; late errors cannot commit a map',async()=>{
 for(const text of ['',ndjson([{kind:'progress',progress:{phase:'pbf-index'}}]),'{"kind":"result","result":',JSON.stringify({kind:'result',result}),ndjson([{kind:'result',result},{kind:'error',error:'late failure'}]),ndjson([{kind:'result',result},{kind:'result',result}])])await assert.rejects(readOsmImportStream(responseChunks(text)),/incomplete|truncated|terminal|final|late failure/i);
 await assert.rejects(readOsmImportStream(responseChunks(ndjson([{kind:'error',error:'regional source failed'}]))),/regional source failed/);
 await assert.rejects(readOsmImportStream(responseChunks(ndjson([{kind:'result',result:null}]))),/result/i);
 await assert.rejects(readOsmImportStream(new Response(new Uint8Array([255,10]))),/UTF|encoded|encoding/i);
});
test('NDJSON byte budget is checked while reading an unfinished large line',async()=>{
 await assert.rejects(readOsmImportStream(responseChunks(' '.repeat(200),3),{maxBytes:128}),/limit|large|budget/i);
});
test('abort cancels a pending reader and never returns even a previously received result',async()=>{
 let cancelled=false,controller;const abort=new AbortController();const response=new Response(new ReadableStream({start(c){controller=c;},cancel(){cancelled=true;}}));
 const pending=readOsmImportStream(response,{signal:abort.signal});controller.enqueue(new TextEncoder().encode(ndjson([{kind:'result',result}])));abort.abort();await assert.rejects(pending,{name:'AbortError'});assert.equal(cancelled,true);
 const before=new AbortController();before.abort();await assert.rejects(importOsmStream({},{signal:before.signal,fetchImpl:()=>assert.fail('aborted import must not fetch')}),{name:'AbortError'});
});
test('stream POST carries cancellation and returns the complete result; HTTP errors retain a bounded server message',async()=>{
 const abort=new AbortController();const request={bounds,sourceMode:'global'};
 assert.deepEqual(await importOsmStream(request,{signal:abort.signal,fetchImpl:async(url,options)=>{assert.equal(url,'/api/import/osm/stream');assert.equal(options.signal,abort.signal);assert.deepEqual(JSON.parse(options.body),request);return responseChunks(ndjson([{kind:'result',result}]));}}),result);
 await assert.rejects(readOsmImportStream(new Response('{"error":"first source unavailable"}',{status:502})),/first source unavailable/);
});
function selection(mode,fetchImpl){
 const status={textContent:'',classList:{toggle(){}}},label={hidden:false},file={files:[],addEventListener(){},closest:()=>label},select={value:mode,addEventListener(){}},download={setAttribute(){},removeAttribute(){}};
 const instance=new OsmSourceSelection({select,file,status,download,bounds:()=>bounds,fetchImpl});return {instance,status,select};
}
test('switching map/world setup carries a saved source without a second upload or relaxing the target size limit',()=>{
 const source=selection('file',()=>assert.fail('copying must not fetch')).instance,target=selection('auto',()=>assert.fail('copying must not fetch')).instance;
 source.selectedFile={name:'source.geojson'};source.file.files=[source.selectedFile];source.saved={key:boundsKey(bounds),data:{id:'a'.repeat(32),features:3}};
 target.copySelectionFrom(source);assert.equal(target.select.value,'file');assert.equal(target.selectedFile,source.selectedFile);assert.deepEqual(target.saved.data,source.saved.data);assert.equal(target.saved.maxJsonBytes,source.maxJsonBytes);assert.notEqual(target.saved,source.saved);
 source.saved=null;assert.ok(target.saved);
 const narrower=selection('auto',()=>assert.fail('copying must not fetch')).instance;narrower.maxJsonBytes=16_000_000;
 narrower.copySelectionFrom(target);assert.equal(narrower.selectedFile,target.selectedFile);assert.equal(narrower.saved,null);
 target.running=true;assert.throws(()=>target.copySelectionFrom(narrower),/finish/i);
});
test('copying source selection updates the download and identical input does not invalidate a ready world',()=>{
 const source=selection('file',()=>assert.fail('copy must not fetch')).instance,target=selection('auto',()=>assert.fail('copy must not fetch')).instance;
 let changes=0;target.onChange=()=>changes++;
 source.selectedFile={name:'B.geojson'};source.saved={key:boundsKey(bounds),data:{id:'b'.repeat(32)}};
 target.download.href='/api/osm/sources/'+'a'.repeat(32)+'/download';target.copySelectionFrom(source);
 assert.match(target.download.href,/b{32}/);assert.equal(changes,1);
 target.copySelectionFrom(source);assert.equal(changes,1);
 source.select.value='auto';source.selectedFile=null;source.saved=null;target.copySelectionFrom(source);
 assert.equal(target.download.hidden,true);
});
const plan={regions:[{id:'europe/germany',name:'Germany',bytes:2*1024**3,cached:false},{id:'europe/france',name:'France',bytes:1024,cached:true,dataDate:'2026-10-20'}],downloadBytes:2*1024**3,maximumFileBytes:8*1024**3,maximumCacheBytes:16*1024**3,largeFirstDownload:true,coverage:'complete'};

test('source controls render safely before world bounds have been initialized',()=>{
 const {instance,status}=selection('auto',()=>assert.fail('render must not fetch'));
 instance.bounds=()=>{throw Error('Bounds not initialized');};
 assert.doesNotThrow(()=>instance.refresh());assert.match(status.textContent,/Check regional download/);
});
test('region plan uses raw bounds, caches by bounds AND mode, and shows large initial download/cache limits',async()=>{
 let calls=0,expected=bounds;const {instance,status,select}=selection('auto',async(url,options)=>{calls++;assert.equal(url,'/api/osm/regions/plan');assert.deepEqual(JSON.parse(options.body),expected);return Response.json(plan);});
 assert.equal((await instance.ensure()).sourceMode,'auto');await instance.ensure();assert.equal(calls,1);assert.match(status.textContent,/Germany.*France/);assert.match(regionPlanText(plan),/2 GiB/);assert.match(status.textContent,/8 GiB.*16 GiB/);assert.doesNotMatch(status.textContent,/64 MiB/);
 select.value='global';assert.equal((await instance.ensure()).sourceMode,'global');assert.equal(calls,2);
 expected={...bounds,north:bounds.north+.001};instance.bounds=()=>expected;await instance.ensure();assert.equal(calls,3);
});
test('explicit global planning failure blocks; auto preserves its regional/online/saved fallback policy and cancellation never triggers fallback',async()=>{
 const {instance,status,select}=selection('global',async()=>Response.json({error:'No supported Geofabrik region'},{status:400}));await assert.rejects(instance.ensure(),/No supported/);
 select.value='auto';assert.equal((await instance.ensure()).sourceMode,'auto');assert.match(status.textContent,/compatible saved snapshot/);assert.match(status.textContent,/No supported/);
 const controller=new AbortController();controller.abort();await assert.rejects(instance.ensure(controller.signal),{name:'AbortError'});
});
test('JSON uploads send text with exact selected bounds and preserve response metadata',async()=>{
 let payload;const file={name:'saved.json',size:12,text:async()=>' {"elements":[]} '};
 const result=await uploadOsmSource(file,bounds,{fetchImpl:async(url,options)=>{assert.equal(url,'/api/osm/sources');payload=JSON.parse(options.body);return {ok:true,json:async()=>({id:'saved',bounds,format:'json',features:0})};}});
 assert.deepEqual(payload,{name:'saved.json',bounds,json:' {"elements":[]} '});assert.equal(result.id,'saved');assert.deepEqual(result.bounds,bounds);
});
test('PBF uploads only the FileReader DataURL base64 body',async()=>{
 let payload;await uploadOsmSource({name:'other-region.osm.pbf',size:10},bounds,{readDataUrl:async()=>'data:application/octet-stream;base64,AAECAw==',fetchImpl:async(_,options)=>{payload=JSON.parse(options.body);return {ok:true,json:async()=>({id:'pbf',bounds})};}});
 assert.equal(payload.pbfBase64,'AAECAw==');assert.equal(payload.json,undefined);
});
test('limits reject before reading or posting; single map has a separate JSON limit',async()=>{
 let calls=0;const options={fetchImpl:async()=>{calls++;},readDataUrl:async()=>{calls++;}};
 await assert.rejects(uploadOsmSource({name:'big.json',size:JSON_SOURCE_LIMIT+1},bounds,options),/64/);
 await assert.rejects(uploadOsmSource({name:'big.pbf',size:PBF_SOURCE_LIMIT+1},bounds,options),/64/);
 await assert.rejects(uploadOsmSource({name:'map.json',size:16_000_001},bounds,{...options,maxJsonBytes:16_000_000}),/16/);
 assert.equal(calls,0);
});
test('failed upstream and malformed upload responses never become empty successful sources',async()=>{
 const file={name:'source.json',size:15,text:async()=>'{"elements":[]}'};
 await assert.rejects(uploadOsmSource(file,bounds,{fetchImpl:async()=>({ok:false,status:502,json:async()=>({error:'first area failed'})})}),/first area failed/);
 await assert.rejects(uploadOsmSource(file,bounds,{fetchImpl:async()=>({ok:true,json:async()=>({features:0})})}),/source ID/i);
 const controller=new AbortController();controller.abort();await assert.rejects(uploadOsmSource(file,bounds,{signal:controller.signal}),{name:'AbortError'});
});
