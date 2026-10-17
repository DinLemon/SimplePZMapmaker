import test from 'node:test';
import assert from 'node:assert/strict';
import {WorldWorkspaces} from '../src/SimplePZMapmaker.Web/wwwroot/world-workspaces.js';
import {StreamedSectionSession} from '../src/SimplePZMapmaker.Web/wwwroot/streamed-section-session.js';
import {blank} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {WorldWorkshop} from '../src/SimplePZMapmaker.Web/wwwroot/world-workshop.js';
const id=n=>String(n).repeat(32),a=id('a'),b=id('b'),sectionA=id('1'),sectionB=id('2');
function manifest(world,revision=1){return {format:'SimplePZWorldSections',version:1,id:world,name:'World '+world[0],revision,sections:[{id:sectionA,name:'West',worldX:100,worldY:100,width:256,height:256,bytes:100},{id:sectionB,name:'East',worldX:101,worldY:100,width:256,height:256,bytes:100}]};}
function harness(request){
 const ws=Object.create(WorldWorkspaces.prototype),nodes={section:{value:sectionA},world:{value:a}},project=blank();
 let current=project;Object.assign(ws,{manifest:manifest(a),workspaceId:a,generation:0,loading:false,request,node:key=>nodes[key]??(nodes[key]={}),update(){},state(text){this.status=text;}});
 ws.workshop={current:()=>current,finish(){},flush(){},assertCanReplace(){},assertCanSaveOrExport(){},world:{maps:[]},queue:Promise.resolve(),session:{constructor:class {constructor(){this.world={maps:[]};}}},revision:0,thumbs:new Map(),adopt:p=>{current=p;},update(){},fit(){},editLabel(){},message(){},canvas:{closest:()=>({close(){}})}};
 ws.session=new StreamedSectionSession({request:(...args)=>ws.request(...args),getProject:()=>current,applySnapshot:async snapshot=>{current=snapshot.member.project;}});
 return {ws,get project(){return current;}};
}
test('browsing another manifest preserves active workspace section and shared context',async()=>{
 const {ws}=harness(async()=>manifest(b));Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a),shared:{buildings:[{id:'shared'}]}});
 await ws.select(b,new AbortController().signal);assert.equal(ws.workspaceId,b);assert.equal(ws.memberId,sectionA);assert.equal(ws.context().id,a);assert.equal(ws.shared.buildings[0].id,'shared');
});
test('opening an unloaded neighbour saves the dirty active section before reading only the target',async()=>{
 const calls=[],h=harness(async(url,data)=>{calls.push([url,data!==null]);return data?manifest(a,2):{member:{id:sectionB,project:{...blank(),worldX:101}},revision:2,shared:{},sections:manifest(a,2).sections};}),{ws}=h;
 Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a)});ws.session.markChanged();ws.node('section').value=sectionB;
 await ws.open(new AbortController().signal);assert.deepEqual(calls.map(([url,post])=>[url.split('/').at(-1),post]),[[sectionA,true],[sectionB,false]]);assert.equal(ws.memberId,sectionB);assert.equal(ws.manifest.revision,2);
});
test('save conflicts preserve local terrain and current active section while exposing reload',async()=>{
 const {ws,project}=harness(async()=>{throw Error('Revision conflict');});Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a)});project.terrain[0]=2;ws.session.markChanged();ws.node('section').value=sectionB;
 await assert.rejects(()=>ws.open(new AbortController().signal),/conflict/i);assert.equal(project.terrain[0],2);assert.equal(ws.memberId,sectionA);assert.equal(ws.session.dirty,true);
});
test('saving active section while browsing another workspace never posts to the browsed workspace',async()=>{
 const calls=[],{ws}=harness(async(url,data)=>{calls.push(url);return manifest(a,2);});Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a)});ws.workspaceId=b;ws.manifest=manifest(b);ws.session.markChanged();
 await ws.saveSection(new AbortController().signal);assert.deepEqual(calls,[`/api/world/workspaces/${a}/sections/${sectionA}`]);assert.equal(ws.manifest.id,b);assert.equal(ws.context().revision,2);
});
test('isolated current edits mark the section dirty instead of adding its raster to the world',()=>{
 const {ws,project}=harness(async()=>manifest(a,2));Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a)});
 const w=Object.create(WorldWorkshop.prototype);Object.assign(w,{workspaces:ws,editedId:null,session:{world:{maps:[]}},editLabel(){},autosave(){this.queued=true;}});
 project.terrain[0]=2;w.currentChanged(project);assert.equal(ws.session.dirty,true);assert.equal(w.queued,true);assert.equal(w.world.maps.length,0);
});
test('manifest overview displays loaded active raster and unloaded neighbours without fabricated terrain',()=>{
 const {ws,project}=harness(async()=>manifest(a));Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a)});
 const w=Object.create(WorldWorkshop.prototype);Object.assign(w,{workspaces:ws,current:()=>project,session:{world:{maps:[]}}});
 const view=w.displayWorld;assert.equal(view.maps.length,2);assert.equal(view.maps[0].project,project);assert.equal(view.maps[1].project.terrain,null);assert.equal(w.world.maps.length,0);
});
test('explicit reload discards local edits only after reading the latest saved section',async()=>{
 const calls=[],{ws,project}=harness(async(url,data)=>{calls.push([url,data]);return url.endsWith(sectionA)?{revision:4,member:{id:sectionA,project:{...blank(),name:'Saved copy'}},shared:{},sections:manifest(a,4).sections}:manifest(a,4);});
 Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a),dirty:true});project.name='Local discarded';await ws.reloadActive(new AbortController().signal);assert.equal(ws.workshop.current().name,'Saved copy');assert.equal(ws.session.dirty,false);assert.equal(ws.context().revision,4);assert.equal(calls.length,2);assert(calls.every(([,data])=>data===null));
});
test('failed explicit reload retains both the current map and its dirty workspace ownership',async()=>{
 const {ws,project}=harness(async url=>{if(url.endsWith(sectionA))throw Error('Unavailable raster');return manifest(a,4);});Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a),dirty:true});project.name='Keep this copy';const previous=ws.session;
 await assert.rejects(ws.reloadActive(new AbortController().signal),/Unavailable/);assert.equal(ws.session,previous);assert.equal(ws.workshop.current(),project);assert.equal(ws.session.dirty,true);
});
test('saved revision preview validates a historical section against the current workspace without replacing its index',async()=>{
 const calls=[],historical={...blank(),name:'Previous saved copy'},h=harness(async(url,data)=>{calls.push([url,data]);if(url.includes('?version=1'))return {revision:2,member:{id:sectionA,project:historical},shared:{},sections:manifest(a,2).sections};if(url.endsWith('/validate'))return {valid:true,revision:4};return manifest(a,4);}),{ws}=h;
 Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a,4)});ws.node('revision').value='1';const before=h.project;await ws.previewRevision(new AbortController().signal);assert.equal(h.project,before);assert.equal(ws.context().revision,4);assert.equal(ws.revisionPreview.project.name,'Previous saved copy');assert.equal(calls.at(-1)[1].expectedRevision,4);
});
test('restoring a reviewed saved revision uses one history callback and retains the current workspace revision',async()=>{
 const calls=[],historical={...blank(),name:'Previous saved copy'},{ws}=harness(async(url,data)=>{if(url.includes('?version=1'))return {revision:2,member:{id:sectionA,project:historical},shared:{},sections:manifest(a,2).sections};if(url.endsWith('/validate'))return {valid:true,revision:4};return manifest(a,4);});
 Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a,4)});ws.node('revision').value='1';ws.workshop.restoreSection=async p=>{calls.push(p);ws.workshop.adopt(p);};await ws.previewRevision(new AbortController().signal);await ws.restoreRevision(new AbortController().signal);assert.equal(calls.length,1);assert.equal(ws.workshop.current().name,'Previous saved copy');assert.equal(ws.context().revision,4);assert.equal(ws.session.dirty,true);assert.equal(ws.revisionPreview,null);
});
test('saved revision review cannot overwrite edits made after preview',async()=>{
 const {ws}=harness(async(url,data)=>url.includes('?version=1')?{revision:2,member:{id:sectionA,project:blank()},shared:{},sections:manifest(a,2).sections}:url.endsWith('/validate')?{valid:true,revision:4}:manifest(a,4));Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a,4)});ws.node('revision').value='1';ws.workshop.restoreSection=async()=>{throw Error('Should not apply');};await ws.previewRevision(new AbortController().signal);ws.workshop.current().name='New edit';await assert.rejects(ws.restoreRevision(new AbortController().signal),/changed|preview/i);assert.equal(ws.workshop.current().name,'New edit');
});
test('saved revision preview rejects historical layout changes and current revision conflicts',async()=>{
 let moved=true;const {ws}=harness(async(url,data)=>url.includes('?version=1')?{revision:2,member:{id:sectionA,project:{...blank(),worldX:moved?101:100}},shared:{},sections:manifest(a,2).sections}:manifest(a,moved?4:5));Object.assign(ws.session,{workspaceId:a,activeId:sectionA,manifest:manifest(a,4)});ws.node('revision').value='1';await assert.rejects(ws.previewRevision(new AbortController().signal),/origin|layout|frame/i);moved=false;await assert.rejects(ws.previewRevision(new AbortController().signal),/revision|workspace changed/i);assert.equal(ws.context().revision,4);
});
