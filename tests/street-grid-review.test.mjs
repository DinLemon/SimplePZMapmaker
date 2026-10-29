import test from 'node:test';
import assert from 'node:assert/strict';
import {reviewStreetGrid,applyStreetGridReview,streetGridReviewText} from '../src/SimplePZMapmaker.Web/wwwroot/street-grid-review.js';

const request=()=>({bounds:{west:30,east:30.01,south:50,north:50.01},rotationDegrees:0,preserveEdits:false,sourceMode:'auto',warp:null});
const result=()=>({sourceId:'a'.repeat(32),source:'saved fixture',alignment:{rotationDegrees:11.22,strength:.8,roadMetres:1700,districtCount:4,reason:'Aligns the strongest district.'}});
test('alignment review applies only the explicit suggested angle and keeps a reusable saved source',()=>{
 const input=request(),before=structuredClone(input),review=reviewStreetGrid(input,result());
 const applied=applyStreetGridReview(input,review);
 assert.deepEqual(input,before);assert.equal(applied.rotationDegrees,11.22);assert.equal(applied.sourceId,'a'.repeat(32));
 assert.match(streetGridReviewText(review),/80%/);assert.match(streetGridReviewText(review),/4 districts/);
});
test('alignment cannot apply to a different source, area, rotation or preserved/deformed world',()=>{
 const input=request(),review=reviewStreetGrid(input,result());
 for(const changed of [{sourceMode:'overpass'},{rotationDegrees:15},{bounds:{...input.bounds,east:30.02}},{preserveEdits:true},{warp:{version:1}}])
  assert.throws(()=>applyStreetGridReview({...input,...changed},review),/changed|preserv|deform/i);
});
test('weak evidence is reported and cannot silently replace the manual angle',()=>{
 const response=result();response.alignment.rotationDegrees=null;response.alignment.strength=.1;response.alignment.reason='No consistent grid.';
 const review=reviewStreetGrid(request(),response);
 assert.match(streetGridReviewText(review),/No consistent grid/);assert.throws(()=>applyStreetGridReview(request(),review),/consistent/i);
});
test('untrusted analysis values cannot supply a ready review',()=>{
 for(const patch of [{rotationDegrees:90},{rotationDegrees:NaN},{strength:2},{roadMetres:-1},{districtCount:NaN}]){
  const response=result();Object.assign(response.alignment,patch);assert.throws(()=>reviewStreetGrid(request(),response),/invalid/i);
 }
 const response=result();response.sourceId='../bad';assert.throws(()=>reviewStreetGrid(request(),response),/source/i);
});
