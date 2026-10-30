import {test} from 'node:test';
import assert from 'node:assert/strict';
import {validateWorkspace,completeWorldAvailability,WorldWorkspaces} from '../src/SimplePZMapmaker.Web/wwwroot/world-workspaces.js';
test('expanded section indexes keep lazy editing beyond the ordinary 128-section allowance',()=>{
 const id=n=>n.toString(16).padStart(32,'0');
 const m={format:'SimplePZWorldSections',version:1,id:id(9999),revision:1,name:'Expanded',sections:Array.from({length:129},(_,i)=>({id:id(i+1),bytes:100,worldX:100+i%20,worldY:100+Math.floor(i/20),width:256,height:256}))};
 assert.equal(validateWorkspace(m),m);
 const expanded={...m,sections:Array.from({length:65536},(_,i)=>({...m.sections[0],id:id(i+1)}))};
 assert.equal(validateWorkspace(expanded),expanded);
 assert.throws(()=>validateWorkspace({...expanded,sections:[...expanded.sections,{...m.sections[0],id:id(65537)}]}));
});
import {changeSummary} from '../src/SimplePZMapmaker.Web/wwwroot/agent-review.js';
import {blank,mapAuthoringVersion} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {maximumProjectBytes} from '../src/SimplePZMapmaker.Web/wwwroot/project-files.js';
test('section index validation requires bounded distinct saved references and a revision',()=>{const id='a'.repeat(32),s={id,bytes:100,worldX:100,worldY:100,width:256,height:256};const m={format:'SimplePZWorldSections',version:1,id,revision:1,name:'Sections',sections:[s]};assert.equal(validateWorkspace(m),m);assert.throws(()=>validateWorkspace({...m,revision:0}));assert.throws(()=>validateWorkspace({...m,sections:[s,s]}));assert.doesNotThrow(()=>validateWorkspace({...m,sections:[{...s,bytes:86_729_784}]}));assert.doesNotThrow(()=>validateWorkspace({...m,sections:[{...s,bytes:maximumProjectBytes}]}));assert.throws(()=>validateWorkspace({...m,sections:[{...s,bytes:maximumProjectBytes+1}]}));});
test('agent review lists gameplay zone changes and infrastructure promotes format11',()=>{const before=blank(),after=structuredClone(before);after.gameZones=[{id:'a'.repeat(32),name:'Farm area',type:'Farm',x:20,y:20,width:32,height:32,z:0}];const summary=changeSummary(before,after);assert.equal(summary.changedObjects,1);assert.match(summary.changes[0],/Farm area/);assert.equal(mapAuthoringVersion(after),9);after.linearFeatures=[{level:1}];assert.equal(mapAuthoringVersion(after),11);});

const sized=(metadataBytes,...bytes)=>({metadataBytes,sections:bytes.map(bytes=>({bytes}))});
test('complete-world loading counts shared plans and every section against the compatibility budget',()=>{
 assert.equal(completeWorldAvailability(sized(1,maximumProjectBytes-1)).allowed,true);
 const over=completeWorldAvailability(sized(2,maximumProjectBytes-1));assert.equal(over.allowed,false);assert.equal(over.bytes,maximumProjectBytes+1);assert.match(over.message,/128 MiB.*Open only this section.*Export saved world/);
 const moscow=completeWorldAvailability(sized(44_289_593,560_946_825));assert.equal(moscow.allowed,false);assert.equal(moscow.bytes,605_236_418);assert.match(moscow.message,/577\.2 MiB/);
});
test('complete-world loading cannot assume that missing or malformed size information fits',()=>{
 for(const manifest of [null,{},sized(undefined,10),sized(1),sized(1,NaN),sized(-1,10),sized(1,0),sized(1,1.5),sized(1,Number.MAX_SAFE_INTEGER)])assert.equal(completeWorldAvailability(manifest).allowed,false);
});
test('oversized complete-world action preserves edits and makes no save or world request',async()=>{
 const calls=[],ws=Object.create(WorldWorkspaces.prototype);ws.manifest=sized(100,maximumProjectBytes);
 ws.workshop={assertCanReplace:()=>calls.push('replace check'),acceptWorld:()=>calls.push('replace')};ws.saveActive=async()=>calls.push('save');ws.request=async()=>calls.push('request');
 await assert.rejects(ws.loadComplete(new AbortController().signal),/128 MiB/);assert.deepEqual(calls,[]);
});
test('complete-world action rechecks the size after saving an active section',async()=>{
 const calls=[],ws=Object.create(WorldWorkspaces.prototype);ws.manifest=sized(1,10);
 ws.workshop={assertCanReplace:()=>calls.push('replace check'),acceptWorld:()=>calls.push('replace')};ws.saveActive=async()=>{calls.push('save');ws.manifest=sized(2,maximumProjectBytes);};ws.request=async()=>calls.push('request');
 await assert.rejects(ws.loadComplete(new AbortController().signal),/128 MiB/);assert.deepEqual(calls,['replace check','save']);
});
test('small complete-world action still opens the saved world',async()=>{
 const calls=[],world={name:'Small'},ws=Object.create(WorldWorkspaces.prototype);ws.manifest=sized(1,10);
 ws.workshop={assertCanReplace:()=>calls.push('replace check'),acceptWorld:value=>{assert.equal(value,world);calls.push('replace');}};ws.saveActive=async()=>calls.push('save');ws.base=()=>'/workspace';ws.request=async url=>{assert.equal(url,'/workspace/world');calls.push('request');return world;};ws.session={detach:()=>calls.push('detach')};ws.update=()=>calls.push('update');ws.state=()=>{};
 await ws.loadComplete(new AbortController().signal);assert.deepEqual(calls,['replace check','save','request','replace','detach','update']);
});
test('load-all refreshes the saved index even when the clean active session has an older revision',async()=>{
 const ws=Object.create(WorldWorkspaces.prototype),id='b'.repeat(32),memberId='c'.repeat(32),m={format:'SimplePZWorldSections',version:1,id,revision:4,name:'World',metadataBytes:1,sections:[{id:memberId,bytes:100,worldX:100,worldY:100,width:256,height:256}]},project=blank(),calls=[];
 ws.manifest={...m,revision:3};ws.workspaceId=id;ws.generation=1;ws.workshop={current:()=>project,flush:()=>{},assertCanReplace:()=>{}};ws.session={};ws.saveActive=async()=>{};ws.request=async url=>{assert.equal(url,`/api/world/workspaces/${id}`);return m;};ws.update=()=>{};ws.createSession=()=>({loadAll:async(_id,manifest)=>{calls.push(manifest.revision);throw Error('stop before adoption');}});
 await assert.rejects(ws.loadAll(),/stop before adoption/);assert.deepEqual(calls,[4]);
});
