import test from 'node:test';
import assert from 'node:assert/strict';
import {GeometryDraftSession,snapDraftPoint,validateDraftDocument,canApplyDraft} from '../src/SimplePZMapmaker.Web/wwwroot/geographic-authoring-data.js';
import {installGeographicAuthoringTools} from '../src/SimplePZMapmaker.Web/wwwroot/geographic-authoring-tools.js';

test('path snapping chooses closest vertex or segment projection before tile snapping',()=>{
 const paths=[{points:[{x:2,y:2},{x:10,y:2}]}];
 assert.deepEqual(snapDraftPoint({x:5.3,y:2.3},{paths,tolerance:.5}),{x:5.3,y:2});
 assert.deepEqual(snapDraftPoint({x:2.2,y:2.1},{paths,tolerance:.5}),{x:2,y:2});
 assert.deepEqual(snapDraftPoint({x:4.4,y:8.8},{paths,tolerance:.5}),{x:4,y:9});
});
test('polygon drafting closes exact vertices and rejects crossing rings',()=>{
 const s=new GeometryDraftSession();s.start('polygon',{memberId:'north',level:0});for(const p of [{x:2,y:2},{x:8,y:2},{x:8,y:7}])s.addPoint(p);
 const d=s.finish();assert.equal(d.kind,'polygon');assert.equal(d.memberId,'north');assert.deepEqual(d.points,[{x:2,y:2},{x:8,y:2},{x:8,y:7}]);assert.equal(s.getDrafts().length,1);
 s.start('polygon');for(const p of [{x:0,y:0},{x:4,y:4},{x:0,y:4},{x:4,y:0}])s.addPoint(p);assert.throws(()=>s.finish(),/cross|intersect/i);
});
test('small building rectangle records 2 by 2 exactly and never widens to core minimum',()=>{
 const s=new GeometryDraftSession();s.start('smallBuilding');s.addPoint({x:12,y:15});s.addPoint({x:10,y:13});const d=s.finish();
 assert.deepEqual(d.rectangle,{x:10,y:13,width:2,height:2});
 assert.equal(canApplyDraft(d,{smallBuilding:{minWidth:4,minHeight:4,maxWidth:40,maxHeight:40}}).supported,false);
 assert.equal(canApplyDraft(d,{smallBuilding:{minWidth:2,minHeight:2,maxWidth:40,maxHeight:40}}).supported,true);
});
test('draft handle edits retain stable identity and cancellation restores original geometry',()=>{
 const s=new GeometryDraftSession();s.start('polyline');s.addPoint({x:1,y:1});s.addPoint({x:9,y:1});const d=s.finish();
 s.edit(d.id);s.movePoint(1,{x:9,y:6});s.cancel();assert.deepEqual(s.getDrafts()[0].points,[{x:1,y:1},{x:9,y:1}]);
 s.edit(d.id);s.movePoint(1,{x:9,y:6});const edited=s.finish();assert.equal(edited.id,d.id);assert.equal(s.getDrafts().length,1);assert.equal(edited.points[1].y,6);
});
test('unsupported fence drafts survive portable versioned JSON and block lossy saves',()=>{
 const s=new GeometryDraftSession();s.start('fence',{memberId:'a',level:1});s.addPoint({x:1,y:1});s.addPoint({x:4,y:1});const d=s.finish();
 assert.equal(canApplyDraft(d,{}).supported,false);assert.throws(()=>s.assertNoPendingDrafts(),/draft/i);
 const json=JSON.parse(s.exportDrafts());assert.equal(json.format,'SimplePZGeometryDrafts');assert.equal(json.version,1);assert.equal(json.requiredFeatures[0],'geometry-drafts-v1');
 assert.equal(validateDraftDocument(json).drafts[0].kind,'fence');const restored=new GeometryDraftSession();restored.importDrafts(json);assert.deepEqual(restored.getDrafts(),s.getDrafts());
 assert.throws(()=>validateDraftDocument({...json,version:2}),/version|format/i);
});
test('failed apply retains draft and successful acknowledgement is explicit',async()=>{
 const s=new GeometryDraftSession();s.start('polyline');s.addPoint({x:0,y:0});s.addPoint({x:3,y:0});const d=s.finish();
 await assert.rejects(()=>s.apply(d.id,{polyline:{version:1}},async()=>{throw Error('Core rejected');}),/Core rejected/);assert.equal(s.getDrafts().length,1);
 await s.apply(d.id,{polyline:{version:1}},async shape=>({applied:shape.points.length===2}));assert.equal(s.getDrafts().length,0);s.assertNoPendingDrafts();
});
test('editing layer locks and context switching cannot bypass draft restrictions',()=>{
 let allowed=true;const s=new GeometryDraftSession({canEdit:()=>allowed});s.start('polygon',{memberId:'a'});s.addPoint({x:0,y:0});allowed=false;
 assert.throws(()=>s.addPoint({x:3,y:0}),/locked|hidden/i);s.cancel();assert.equal(s.active,null);assert.throws(()=>s.start('polygon'),/locked|hidden/i);
});

test('pointer drawing and draggable handles update draft geometry without mutating the project',()=>{
 const project={width:256,height:256,linearFeatures:[]},before=structuredClone(project),context={project,memberId:'a',level:0,transform:{zoom:12}},tools=installGeographicAuthoringTools({getContext:()=>context});
 tools.start('polyline');tools.pointerDown({x:2,y:3});tools.pointerUp({x:2,y:3});tools.pointerDown({x:8,y:3});tools.pointerUp({x:8,y:3});tools.finish();
 tools.edit(tools.getDrafts()[0].id);tools.pointerDown({x:8,y:3});tools.pointerMove({x:8,y:7});tools.pointerUp({x:8,y:7});tools.finish();
 assert.deepEqual(tools.getDrafts()[0].points,[{x:2,y:3},{x:8,y:7}]);assert.deepEqual(project,before);
});
test('unsupported UI drafts remain after apply and section changes block cross-section commits',async()=>{
 const context={project:{width:256,height:256},memberId:'a',level:0},tools=installGeographicAuthoringTools({getContext:()=>context,getCapabilities:()=>({polyline:{version:1}}),onDrawFinished:async()=>({applied:true})});
 tools.start('polyline');tools.pointerDown({x:2,y:3});tools.pointerDown({x:8,y:3});tools.finish();const id=tools.getDrafts()[0].id;
 context.memberId='b';await assert.rejects(()=>tools.apply(id),/section/i);assert.equal(tools.getDrafts().length,1);
 context.memberId='a';await tools.apply(id);assert.equal(tools.getDrafts().length,0);
});
test('polygon rejects adjacent overlapping edges and repeated nonclosing vertices',()=>{
 const s=new GeometryDraftSession();s.start('polygon');for(const p of [{x:0,y:0},{x:4,y:0},{x:2,y:0},{x:2,y:4}])s.addPoint(p);assert.throws(()=>s.finish(),/overlap|intersect/i);
});
test('draft apply requires explicit success and portable JSON rejects unknown required features',async()=>{
 const s=new GeometryDraftSession();s.start('polyline');s.addPoint({x:1,y:1});s.addPoint({x:3,y:1});const d=s.finish();
 await assert.rejects(()=>s.apply(d.id,{polyline:{version:1}},async()=>({})),/acknowledge/i);assert.equal(s.getDrafts().length,1);
 const doc=JSON.parse(s.exportDrafts());doc.requiredFeatures.push('future-gates-v2');assert.throws(()=>validateDraftDocument(doc),/version|format/i);
});
test('portable standalone drafts retain their map frame and cannot apply at a different world origin',async()=>{
 const context={project:{width:256,height:256,worldX:100,worldY:200},memberId:null,level:0},tools=installGeographicAuthoringTools({getContext:()=>context,getCapabilities:()=>({polyline:{version:1}}),onDrawFinished:async()=>({applied:true})});
 tools.start('polyline');tools.pointerDown({x:2,y:3});tools.pointerDown({x:8,y:3});tools.finish();
 const doc=JSON.parse(tools.exportDrafts());assert.deepEqual(doc.drafts[0].frame,{worldX:100,worldY:200,width:256,height:256});
 context.project.worldX=101;await assert.rejects(()=>tools.apply(doc.drafts[0].id),/frame|origin/i);assert.equal(tools.getDrafts().length,1);
});
