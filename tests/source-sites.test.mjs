import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {LatestSourceAudit,siteContains} from '../src/SimplePZMapmaker.Web/wwwroot/source-data.js';
const evidence=()=>({id:'e',sourceId:'osm-node-1',kind:'poi',geometryKind:'point',name:'Clinic',tags:{amenity:'clinic'},sourceGeometry:[[{longitude:32,latitude:54}]],geometry:[[{x:40,y:50}]],origin:'source',modified:false});

test('complex geographic source and site retain all holes through validation and save',()=>{
 const square=(x,y,size)=>[{x,y},{x:x+size,y},{x:x+size,y:y+size},{x,y:y+size},{x,y}];
 const geometry=[square(10,10,220),...Array.from({length:33},(_,i)=>square(20+i%6*30,20+Math.floor(i/6)*30,8))];const e={...evidence(),kind:'site',geometryKind:'polygon',geometry,sourceGeometry:geometry.map(r=>r.map(({x,y})=>({longitude:32+x/100000,latitude:54+y/100000})))};
 const p={...blank(),sourceFeatures:[e],sites:[{id:'s',name:'Park',origin:'source',locked:false,boundary:geometry,buildingIds:[],decorationIds:[],staticModelIds:[],linearFeatureIds:[]}]};
 const restored=JSON.parse(JSON.stringify(p));validateProject(restored);assert.equal(restored.sourceFeatures[0].sourceGeometry.length,34);assert.equal(restored.sites[0].boundary.length,34);assert(!siteContains(restored.sites[0],24,24));assert(siteContains(restored.sites[0],15,15));
 const excessive=structuredClone(p);excessive.sourceFeatures[0].geometry=Array.from({length:1025},()=>square(10,10,2));excessive.sourceFeatures[0].sourceGeometry=excessive.sourceFeatures[0].geometry.map(r=>r.map(({x,y})=>({longitude:32+x/100000,latitude:54+y/100000})));assert.throws(()=>validateProject(excessive));
});
test('source evidence and sites survive grouped undo, redo and JSON save without changing legacy defaults',()=>{
 const p=blank(),h=new History(),before=structuredClone(p);h.begin(p);p.sourceFeatures=[evidence()];p.sites=[{id:'s',name:'Yard',origin:'authored',boundary:[[{x:30,y:40},{x:80,y:40},{x:80,y:90},{x:30,y:90}]],buildingIds:[],decorationIds:[],staticModelIds:[],linearFeatureIds:[],locked:false}];h.commit(p);assert.equal(h.past.length,1);h.undo(p);assert.deepEqual(p,before);h.redo(p);validateProject(JSON.parse(JSON.stringify(p)));assert.equal(p.sourceFeatures[0].tags.amenity,'clinic');assert.equal(p.sites[0].name,'Yard');
});
test('source and site validation rejects broken geometry and dangling group members',()=>{
 for(const alter of [p=>p.sourceFeatures[0].geometry[0][0].x=NaN,p=>p.sourceFeatures[0].sourceGeometry=[],p=>p.sourceFeatures.push(evidence())]){const p={...blank(),sourceFeatures:[evidence()]};alter(p);assert.throws(()=>validateProject(p));}
 assert.throws(()=>validateProject({...blank(),sites:[{id:'s',name:'x',origin:'authored',boundary:[[{x:1,y:1},{x:2,y:1},{x:2,y:2}]],buildingIds:['missing']}]}));
});
test('site hole excludes enclosed objects',()=>{const s={boundary:[[{x:0,y:0},{x:10,y:0},{x:10,y:10},{x:0,y:10}],[{x:4,y:4},{x:6,y:4},{x:6,y:6},{x:4,y:6}]]};assert(siteContains(s,2,2));assert(!siteContains(s,5,5));});
test('late source audit results never replace results from a newer map revision',async()=>{
 const audit=new LatestSourceAudit(),old=audit.begin();const next=audit.begin();assert(old.signal.aborted);assert.equal(audit.accept(old,'old'),false);assert.equal(audit.accept(next,'new'),true);assert.equal(audit.value,'new');audit.invalidate();assert.equal(audit.value,null);assert.equal(audit.accept(next,'late'),false);
});
test('deleting a grouped member detaches it in the same undo transaction',()=>{
 const p=blank(),h=new History();p.decorations=[{id:'d',x:25,y:25,asset:'vegetation_ornamental_01_0',parts:[{dx:0,dy:0,tile:'vegetation_ornamental_01_0'}]}];p.sites=[{id:'s',name:'Yard',origin:'source',boundary:[[{x:20,y:20},{x:30,y:20},{x:30,y:30},{x:20,y:30}]],buildingIds:[],decorationIds:['d'],staticModelIds:[],linearFeatureIds:[],locked:false}];
 h.begin(p);p.decorations=[];h.commit(p);assert.deepEqual(p.sites[0].decorationIds,[]);validateProject(p);h.undo(p);assert.deepEqual(p.sites[0].decorationIds,['d']);assert.equal(p.decorations[0].id,'d');
});
