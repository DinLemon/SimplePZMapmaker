import {test} from 'node:test';
import assert from 'node:assert/strict';
import {environmentIndices} from '../src/SimplePZMapmaker.Web/wwwroot/appearance.js';
import {blank,History,validateProject,geographicBase,boundsFromSearchResult} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';

test('overlapping appearance regions use the newest preset without changing geography',()=>{
 const p=blank();p.version=5;p.surfaceTheme='temperate';const geography=structuredClone(p.terrain);
 p.surfaceRegions=[{x:2,y:3,width:5,height:4,theme:'rural'},{x:4,y:4,width:2,height:2,theme:'dry'}];
 validateProject(p);const indices=environmentIndices(p);
 assert.equal(indices[0],1);assert.equal(indices[3*256+2],2);assert.equal(indices[4*256+4],4);assert.equal(indices[6*256+6],2);assert.equal(indices[7*256+6],1);assert.deepEqual(p.terrain,geography);
});

test('regional styling history restores format version and overlapping regions exactly',()=>{
 const p=blank(),history=new History();history.begin(p);p.version=5;p.surfaceRegions.push({x:0,y:0,width:256,height:256,theme:'dry'});history.commit(p);
 history.begin(p);p.surfaceRegions.push({x:12,y:13,width:14,height:15,theme:'rural'});history.commit(p);
 const applied=structuredClone(p);history.undo(p);assert.equal(p.surfaceRegions.length,1);history.undo(p);assert.equal(p.version,4);assert.deepEqual(p.surfaceRegions,[]);
 history.redo(p);history.redo(p);assert.deepEqual(p,applied);validateProject(p);
});

test('region bounds, preset identifiers and version are validated before project adoption',()=>{
 for(const region of [{x:-1,y:0,width:1,height:1,theme:'dry'},{x:255,y:0,width:2,height:1,theme:'dry'},{x:0,y:0,width:0,height:1,theme:'dry'},{x:0,y:0,width:1,height:1,theme:'missing'},{x:0,y:0,width:1.5,height:1,theme:'dry'}]){
  const p=blank();p.version=5;p.surfaceRegions=[region];assert.throws(()=>validateProject(p));
 }
 const older=blank();older.surfaceRegions=[{x:0,y:0,width:1,height:1,theme:'dry'}];assert.throws(()=>validateProject(older));
 const boundary=blank();boundary.version=5;boundary.surfaceRegions=[{x:255,y:255,width:1,height:1,theme:'dry'}];validateProject(boundary);
});

test('reimport retains appearance areas only when preserving edits and large cities retain centre selection',()=>{
 const p=blank();p.version=5;p.surfaceRegions=[{x:1,y:2,width:3,height:4,theme:'rural'}];p.surfaceTheme='dry';
 assert.equal(geographicBase(p,256,true),p);const fresh=geographicBase(p,256,false);assert.deepEqual(fresh.surfaceRegions,[]);assert.equal(fresh.surfaceTheme,'dry');
 assert.equal(boundsFromSearchResult({boundingbox:['54.68','54.87','31.86','32.22'],lat:'54.7827',lon:'32.0453'}),null);
 assert.deepEqual(boundsFromSearchResult({boundingbox:['54.77','54.79','32.03','32.05']}),{south:54.77,north:54.79,west:32.03,east:32.05});
});
