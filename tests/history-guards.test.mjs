import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';

test('rejected history commit restores metadata optional fields and both raster layers exactly',()=>{
 const p=blank(),h=new History();h.begin(p);p.name='Accepted';h.commit(p);h.undo(p);
 const before=structuredClone(p),past=h.past.slice(),future=h.future.slice();
 h.beforeCommit=(metadata,candidate,changes)=>{assert.equal(metadata.name,before.name);assert.equal(candidate.name,'Rejected');assert.equal(changes.size,2);throw Error('Layer is locked');};
 h.begin(p);h.touch('terrain',4100);h.touch('zombies',2);p.terrain[4100]=3;p.zombies[2]=188;h.editTerrain(4100);
 p.name='Rejected';p.surfaceTiles={12:'floor_test_0'};p.generation={changed:true};p.zombieDensityRegions=[];p.zombieDensityState={baseline:[],applied:{}};p.roomRegionRules=[];
 assert.throws(()=>h.commit(p),/locked/);assert.deepEqual(p,before);assert.equal(h.pending,null);assert.deepEqual(h.past,past);assert.deepEqual(h.future,future);
});
test('checkpoint guard rejects before pending project or history mutation',()=>{
 const p=blank(),next=blank(),h=new History();h.begin(p);p.name='Pending';const before=structuredClone(p),pending=h.pending;next.name='Candidate';
 h.beforeCheckpoint=(current,candidate)=>{assert.equal(current,p);assert.equal(candidate,next);throw Error('Buildings are locked');};
 assert.throws(()=>h.checkpoint(p,next),/locked/);assert.deepEqual(p,before);assert.equal(h.pending,pending);assert.equal(h.past.length,0);assert.equal(h.future.length,0);
});
test('checkpoint replacement removes legacy optional authoring metadata exactly',()=>{
 const p=blank(),h=new History();p.generation={mode:'evidence',terrain:'preserve',roads:'preserve',buildings:'preserve',environment:'preserve',seed:42};p.sourceFeatures=[];p.sites=[];
 const before=structuredClone(p),next=blank();h.checkpoint(p,next);for(const key of ['generation','sourceFeatures','sites'])assert.equal(Object.hasOwn(p,key),false,key);h.undo(p);assert.deepEqual(p,before);h.redo(p);for(const key of ['generation','sourceFeatures','sites'])assert.equal(Object.hasOwn(p,key),false,key);
});
test('undo redo guard rejection keeps project and both history stacks intact',()=>{
 const p=blank(),h=new History();h.begin(p);h.touch('terrain',3);p.terrain[3]=2;p.name='Road';h.commit(p);const current=structuredClone(p),entry=h.past[0];
 h.beforeApply=(project,item,reverse)=>{assert.equal(project,p);assert.equal(item,entry);assert.equal(reverse,true);throw Error('Roads are locked');};
 assert.throws(()=>h.undo(p),/locked/);assert.deepEqual(p,current);assert.equal(h.past.length,1);assert.equal(h.past[0],entry);assert.equal(h.future.length,0);
 h.beforeApply=null;assert.equal(h.undo(p),true);const undone=structuredClone(p);h.beforeApply=(project,item,reverse)=>{assert.equal(project,p);assert.equal(item,entry);assert.equal(reverse,false);throw Error('Roads are hidden');};
 assert.throws(()=>h.redo(p),/hidden/);assert.deepEqual(p,undone);assert.equal(h.past.length,0);assert.equal(h.future.length,1);assert.equal(h.future[0],entry);
});
