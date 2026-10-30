import test from 'node:test';
import assert from 'node:assert/strict';
import {blank} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {StreamedSectionSession} from '../src/SimplePZMapmaker.Web/wwwroot/streamed-section-session.js';
const id=n=>String(n).padStart(32,'0');
function fixture(requestOverride){
 let project=blank(),revision=3;const calls=[],manifest={id:id(1),name:'Large world',revision,metadataBytes:1,sections:[2,3,4].map(n=>({id:id(n),name:`Section ${n}`,bytes:60*1024*1024,worldX:100+n,worldY:100,width:256,height:256}))};
 const request=async(url,data,signal)=>{calls.push({url,data});if(requestOverride)await requestOverride(url,data,signal);if(data){revision++;return {...manifest,revision};}if(!url.includes('/sections/'))return {...manifest,revision};const memberId=url.split('/sections/')[1].split('?')[0];return {revision,member:{id:memberId,project:{...blank(),name:memberId,worldX:100+Number(memberId)}},shared:{buildings:[],linearFeatures:[]}};};
 const session=new StreamedSectionSession({getProject:()=>project,applySnapshot:s=>{project=s.member.project;},request});
 return {session,manifest,calls,get project(){return project;},setRevision:r=>{revision=r;}};
}
test('all section rasters load above the compatibility budget and switch without downloading them again',async()=>{
 const f=fixture(),progress=[];await f.session.loadAll(id(1),f.manifest,undefined,(done,total)=>progress.push([done,total]));
 assert.equal(f.session.loadedSections.size,3);assert.deepEqual(progress,[[1,3],[2,3],[3,3]]);
 await f.session.open(id(1),f.manifest,id(2));f.project.name='Edited west';f.session.markChanged();await f.session.open(id(1),f.manifest,id(3));await f.session.open(id(1),f.manifest,id(2));
 assert.equal(f.project.name,'Edited west');assert.equal(f.session.loadedSections.get(id(2)).member.project.name,'Edited west');
 assert.equal(f.calls.filter(c=>c.url.includes('/sections/')&&!c.data).length,3);assert.equal(f.calls.filter(c=>c.data).length,1);assert.equal(f.session.manifest.revision,4);
});
test('a cancelled all-section load retains the current section and publishes no partial cache',async()=>{
 const abort=new AbortController(),f=fixture(url=>{if(url.includes(id(3)))abort.abort();});
 await f.session.open(id(1),f.manifest,id(2));const before=f.project;
 await assert.rejects(f.session.loadAll(id(1),f.manifest,abort.signal),e=>e.name==='AbortError');assert.equal(f.project,before);assert.equal(f.session.activeId,id(2));assert.equal(f.session.loadedSections.size,0);
});
test('all-section loading refuses mixed revisions and retains the previous project',async()=>{
 const f=fixture(url=>{if(url.includes(id(3)))f.setRevision(4);}),before=f.project;
 await assert.rejects(f.session.loadAll(id(1),f.manifest),/revision|workspace changed/i);assert.equal(f.project,before);assert.equal(f.session.loadedSections.size,0);
});
test('cached switching checks the saved revision and preserves unsaved changes on conflict',async()=>{
 const f=fixture();await f.session.loadAll(id(1),f.manifest);await f.session.open(id(1),f.manifest,id(2));f.project.name='Unsaved';f.session.markChanged();f.setRevision(9);
 // A real section POST rejects the stale revision; model that behaviour explicitly.
 const request=f.session.request;f.session.request=async(url,data,...rest)=>{if(data)throw Error('Revision conflict');return request(url,data,...rest);};
 await assert.rejects(f.session.open(id(1),f.manifest,id(3)),/Revision conflict/);assert.equal(f.project.name,'Unsaved');assert.equal(f.session.dirty,true);assert.equal(f.session.activeId,id(2));
});
test('clean cached switching rejects an external revision instead of opening stale geometry',async()=>{
 const f=fixture();await f.session.loadAll(id(1),f.manifest);await f.session.open(id(1),f.manifest,id(2));f.setRevision(9);
 await assert.rejects(f.session.open(id(1),f.manifest,id(3)),/workspace changed|revision/i);assert.equal(f.session.activeId,id(2));
});
test('edits during all-section loading remain current and prevent cache publication',async()=>{
 const f=fixture(url=>{if(url.includes(id(3)))f.project.name='Edited during loading';});
 await assert.rejects(f.session.loadAll(id(1),f.manifest),/edited.*loading/i);assert.equal(f.project.name,'Edited during loading');assert.equal(f.session.loadedSections.size,0);
});

test('editor context exposes every resident section while keeping section writes isolated',async()=>{
 const f=fixture();await f.session.loadAll(id(1),f.manifest);await f.session.open(id(1),f.manifest,id(2));
 const context=f.session.context();assert.equal(context.loadedSections.size,3);
 assert.equal(context.loadedSections.get(id(3)).member.project.worldX,103);
 assert.equal(context.memberId,id(2));assert.equal(context.world,undefined);
});
