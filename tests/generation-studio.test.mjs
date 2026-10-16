import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {GenerationReview,studioRequest,footprintOrigin,defaultRules,generationControl} from '../src/SimplePZMapmaker.Web/wwwroot/generation-data.js';
import {settlementFields} from '../src/SimplePZMapmaker.Web/wwwroot/generation-studio.js';

test('generation protections and accepted settings survive undo redo and JSON reload',()=>{
 const p=blank(),h=new History();
 h.begin(p);p.generation={lockedBuildingIds:['saved-building'],protectedAreas:[{id:'yard',name:'Keep yard',x:12,y:20,width:30,height:40}],generatedBuildingIds:[],rules:null,lastSeed:123,lastVariant:2,lastMode:'fill'};h.commit(p);
 assert.equal(h.past.length,1,'Protecting an area must create a history operation');
 h.undo(p);assert.equal(p.generation??null,null);h.redo(p);
 assert.equal(p.generation.lastSeed,123);assert.equal(p.generation.protectedAreas[0].name,'Keep yard');
 const loaded=JSON.parse(JSON.stringify(p));validateProject(loaded);assert.deepEqual(loaded.generation,p.generation);
});
test('variant comparison remains bounded and rejects changed source projects',()=>{
 const p=blank(),review=new GenerationReview();
 review.add(p,{proposalId:'A'});review.add(p,{proposalId:'B'});review.selected=0;assert.equal(review.current(p).proposalId,'A');
 review.add(p,{proposalId:'C'});assert.deepEqual(review.variants.map(v=>v.proposalId),['B','C']);
 p.generation={lockedBuildingIds:['new-pin']};assert.throws(()=>review.current(p),/changed/);
 review.add(p,{proposalId:'D'});assert.deepEqual(review.variants.map(v=>v.proposalId),['D']);
});
test('selected-area requests clone original and distinguish real outlines from inferred building data',()=>{
 const p=blank(),rules=defaultRules(),request=studioRequest(p,{x:10,y:20,width:80,height:90,seed:-5,variant:4,mode:'fill',rules});
 request.project.name='Proposal';assert.equal(p.name,'New map');assert.deepEqual(request.area,{x:10,y:20,width:80,height:90});
 assert.equal(footprintOrigin({id:'source',sourceId:'way/1',footprintOrigin:'source-grid'}),'osm-source');
 assert.equal(footprintOrigin({id:'source',sourceId:'way/1'}),'source-approximated');
 assert.equal(footprintOrigin({id:'settlement-1',modified:false}),'generated');
 assert.equal(footprintOrigin({id:'settlement-1',modified:true}),'authored');
 for(const change of [{width:300},{seed:2.2},{variant:-1},{rules:{...rules,minimumFloors:6,maximumFloors:2}}])assert.throws(()=>studioRequest(p,{x:10,y:20,width:80,height:90,seed:1,variant:0,mode:'fill',rules,...change}));
});
test('damaged generation protection rectangles and duplicate ids are rejected on load',()=>{
 for(const generation of [
  {lockedBuildingIds:['same','same'],protectedAreas:[]},
  {lockedBuildingIds:[],protectedAreas:[{x:250,y:2,width:30,height:20}]},
  {lockedBuildingIds:[],protectedAreas:[],lastVariant:10001},
 ])assert.throws(()=>validateProject({...blank(),generation}));
});
test('legacy partial generation settings normalize without losing accepted values',()=>{
 const p={...blank(),generation:{lastSeed:17,rules:{era:'soviet'}}};
 validateProject(p);const g=generationControl(p);assert.deepEqual(g.lockedBuildingIds,[]);assert.deepEqual(g.protectedAreas,[]);assert.deepEqual(g.generatedBuildingIds,[]);assert.equal(g.lastSeed,17);assert.equal(g.rules.era,'soviet');assert.equal(g.rules.setback,3);assert.equal(g.rules.preset,'legacy');
});
test('edited source footprint is not labelled as surveyed source geometry',()=>{
 assert.equal(footprintOrigin({id:'source',sourceId:'way/1',footprintOrigin:'manual-source-edit',sourceOutline:[[{longitude:1,latitude:1}]]}),'source-edited');
});
test('only new map creation offers legacy random town generation',()=>{
 assert(settlementFields('new-rules').includes('value="legacy"'));
 assert(!settlementFields('studio-rules',false,true).includes('value="legacy"'));
});
