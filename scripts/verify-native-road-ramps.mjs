import assert from 'node:assert/strict';
import {randomUUID,createHash} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48812';
assert.equal(new URL(base).hostname,'127.0.0.1');assert.notEqual(new URL(base).port,'48779');
const out=new URL('../work/knoxmap-research/road-ramps/',import.meta.url);await mkdir(out,{recursive:true});
const id=()=>randomUUID().replaceAll('-','');
async function api(path,body){const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:body===undefined?{}:{'Content-Type':'application/json',Origin:base},body:body===undefined?undefined:JSON.stringify(body)});const data=await response.json();if(!response.ok)throw Error(`${path}: ${response.status} ${data.error}`);return data;}
const blank=await api('/api/generate',{kind:'blank',size:256,seed:42,name:'Native ramp verification'}),origin=25600;
let world={format:'SimplePZWorld',version:6,id:id(),name:'Native cardinal road ramps',maps:[],linearFeatures:[],buildings:[],fences:[],fenceStyles:[],gateStyles:[],decorations:[]};
for(let y=0;y<2;y++)for(let x=0;x<2;x++){
 const project=structuredClone(blank);Object.assign(project,{worldX:100+x,worldY:100+y,worldSection:true,version:6});project.spawns=x===0&&y===0?[{x:20,y:20}]:[];world.maps.push({id:id(),project});
}
function ground(x,y,value){const p=world.maps.find(m=>m.project.worldX===100+Math.floor(x/256)&&m.project.worldY===100+Math.floor(y/256)).project;p.terrain[y%256*256+x%256]=value;}
for(let y=80;y<120;y++)for(let x=279;x<283;x++)ground(x,y,2);
ground(281,100,3);
const sourceTerrain=world.maps.map(m=>JSON.stringify(m.project.terrain)),requests=[];
for(const [name,points,level] of [
 ['east across seam',[[240,100],[321,100]],2],['west',[[180,200],[99,200]],2],
 ['north',[[100,350],[100,309]],1],['south',[[200,350],[200,391]],1]
]){
 const feature={id:id(),kind:'bridge',width:4,level,access:'ramps',guardRails:true,supportSpacing:4,interpolation:'polyline',points:points.map(([x,y])=>({x:origin+x,y:origin+y})),sourcePath:[],sourceTags:{name},underlay:[],decorationIds:[],generatedDecorations:[],assetSources:{},deckArtwork:[]};
 const before=JSON.stringify(world);const preview=await api('/api/world/linear/preview',{world,feature});assert.equal(JSON.stringify(world),before);
 assert.ok(preview.tiles>0&&preview.holes===0);if(name==='east across seam')assert.equal(preview.sections.length,2);
 world=(await api('/api/world/linear/put',{world,feature})).world;requests.push({name,tiles:preview.tiles,sections:preview.sections.length});
}
assert.deepEqual(world.maps.map(m=>JSON.stringify(m.project.terrain)),sourceTerrain,'Approaches rewrote source ground');
const blocked={id:id(),kind:'bridge',width:1,level:1,access:'none',guardRails:true,interpolation:'polyline',points:[{x:origin+255,y:origin+100},{x:origin+260,y:origin+100}],sourcePath:[],sourceTags:{},underlay:[],decorationIds:[],generatedDecorations:[],assetSources:{},deckArtwork:[]};
await assert.rejects(api('/api/world/linear/put',{world,feature:blocked}),/400.*ramp intersects/i);
const storage=id(),manifest=await api('/api/world/workspaces/'+storage,{world});
const job=await api(`/api/world/workspaces/${storage}/export/jobs?expectedRevision=${manifest.revision}`,{});
let state;for(let i=0;i<240;i++){state=await api('/api/export/jobs/'+job.id);if(state.status==='completed')break;assert.ok(['queued','running'].includes(state.status),state.error);await new Promise(resolve=>setTimeout(resolve,250));}
assert.equal(state.status,'completed');assert.equal(state.revision,String(manifest.revision));
const archive=await fetch(base+state.downloadUrl);assert.ok(archive.ok);const bytes=Buffer.from(await archive.arrayBuffer());await writeFile(new URL('NativeRoadRamps-B42.zip',out),bytes);
await writeFile(new URL('NativeRoadRamps.pzworld.json',out),JSON.stringify(world));
await writeFile(new URL('api-proof.json',out),JSON.stringify({status:'passed',storage,revision:manifest.revision,job:job.id,bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex'),requests,sourceGroundUnchanged:true,structuralCollisionRejected:true,streamedExport:true,nativeClientTraversal:'not tested'},null,2));
console.log('PASS native ramp API: four directions, +1/+2 floors, shared 255/256 seam, terrain preserved, streamed ZIP.');
