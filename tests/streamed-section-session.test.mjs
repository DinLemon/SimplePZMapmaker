import test from 'node:test';
import assert from 'node:assert/strict';
import {blank} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {StreamedSectionSession} from '../src/SimplePZMapmaker.Web/wwwroot/streamed-section-session.js';
const id=n=>String(n).padStart(32,'0');
const manifest=()=>({id:id(1),revision:3,sections:[{id:id(2),name:'West',worldX:100,worldY:100,width:256,height:256},{id:id(3),name:'East',worldX:101,worldY:100,width:256,height:256}]});
test('section switch saves only dirty active section before requesting the next raster',async()=>{
 let p=blank(),calls=[];const m=manifest(),snapshot=(memberId,revision=3)=>({revision,member:{id:memberId,project:{...blank(),worldX:memberId===id(2)?100:101}},shared:{buildings:[],linearFeatures:[]},sections:m.sections});
 const session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async(url,data)=>{calls.push([url,data]);if(data)return{...m,revision:4};return snapshot(url.includes(id(3))?id(3):id(2),calls.some(c=>c[1])?4:3);}});
 await session.open(id(1),m,id(2));p.name='Authored west';session.markChanged(p);await session.open(id(1),m,id(3));assert.equal(calls.length,3);assert(calls[1][0].endsWith('/sections/'+id(2)));assert.equal(calls[1][1].project.name,'Authored west');assert(calls[2][0].endsWith('/sections/'+id(3)));assert.equal(session.activeId,id(3));assert.equal(session.dirty,false);assert.equal(p.worldX,101);
});
test('failed or stale section save leaves active edits and prevents switching',async()=>{
 let p=blank();const m=manifest(),session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async(url,data)=>{if(data)throw Error('Revision conflict');return {revision:3,member:{id:id(2),project:blank()},shared:{},sections:m.sections};}});
 await session.open(id(1),m,id(2));p.name='Keep unsaved';session.markChanged(p);await assert.rejects(session.open(id(1),m,id(3)),/Revision conflict/);assert.equal(session.activeId,id(2));assert.equal(p.name,'Keep unsaved');assert.equal(session.dirty,true);
});
test('late cancelled section loads cannot replace the newer section',async()=>{
 let p=blank(),release;const m=manifest(),session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async url=>{if(url.endsWith(id(2)))return new Promise(r=>release=r);return {revision:3,member:{id:id(3),project:{...blank(),worldX:101}},shared:{},sections:m.sections};}});
 const old=session.open(id(1),m,id(2));await new Promise(r=>setTimeout(r,0));await session.open(id(1),m,id(3));release({revision:3,member:{id:id(2),project:blank()},shared:{},sections:m.sections});await old;assert.equal(p.worldX,101);assert.equal(session.activeId,id(3));
});
test('edits made while a neighbour downloads prevent its replacement of the current section',async()=>{
 let p=blank(),release;const m=manifest(),session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async url=>url.endsWith(id(3))?new Promise(r=>release=r):{revision:3,member:{id:id(2),project:blank()},shared:{},sections:m.sections}});
 await session.open(id(1),m,id(2));const next=session.open(id(1),m,id(3));await new Promise(r=>setTimeout(r,0));p.name='Painted during download';session.markChanged();release({revision:3,member:{id:id(3),project:blank()},shared:{},sections:m.sections});await assert.rejects(next,/edited.*loading|changed.*loading/i);assert.equal(session.activeId,id(2));assert.equal(p.name,'Painted during download');assert.equal(session.dirty,true);
});
test('unchanged flushes and undo to the saved baseline do not leave a section dirty',async()=>{
 let p=blank();const m=manifest(),session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async()=>({revision:3,member:{id:id(2),project:blank()},shared:{},sections:m.sections})});
 await session.open(id(1),m,id(2));session.markChanged();assert.equal(session.dirty,false);const name=p.name;p.name='changed';session.markChanged();assert.equal(session.dirty,true);p.name=name;session.markChanged();assert.equal(session.dirty,false);
});
test('a save returning a foreign workspace cannot replace the current revision or saved baseline',async()=>{
 let p=blank();const m=manifest(),session=new StreamedSectionSession({getProject:()=>p,applySnapshot:s=>{p=s.member.project;},request:async(url,data)=>data?{...m,id:id(9),revision:4}:{revision:3,member:{id:id(2),project:blank()},shared:{},sections:m.sections}});
 await session.open(id(1),m,id(2));p.name='Keep local';session.markChanged();await assert.rejects(session.save(),/workspace|invalid/i);assert.equal(session.manifest.revision,3);assert.equal(session.dirty,true);
});
test('a cancelled apply waiting on previous recovery cannot overwrite a newer section',async()=>{
 let p=blank(),release,started;const entered=new Promise(r=>started=r),wait=new Promise(r=>release=r),m=manifest();const session=new StreamedSectionSession({getProject:()=>p,request:async url=>({revision:3,member:{id:url.endsWith(id(2))?id(2):id(3),project:{...blank(),name:url.endsWith(id(2))?'West':'East'}},shared:{},sections:m.sections}),applySnapshot:async(snapshot,{assertCurrent})=>{if(snapshot.member.id===id(2)){started();await wait;}assertCurrent();p=snapshot.member.project;}});
 const old=session.open(id(1),m,id(2));await entered;const next=session.open(id(1),m,id(3));release();await old.catch(e=>assert.equal(e.name,'AbortError'));await next;assert.equal(p.name,'East');assert.equal(session.activeId,id(3));
});
