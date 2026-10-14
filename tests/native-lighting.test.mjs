import test from 'node:test';
import assert from 'node:assert/strict';
import {lightingArea,lightingPoint,validateLightingResult,sameLightingScene,NativeLightingPanel} from '../src/SimplePZMapmaker.Web/wwwroot/native-lighting.js';

const area={x:40,y:50,size:16,level:-1};
const response=()=>({...area,revision:'7',cells:[[0,0,-1,.5,.25,.1,1,...Array(8).fill(-16777216)]],warnings:[],provenance:{engine:'Project Zomboid LightingJNI',gameSession:false,protocol:2,counter:12}});
test('lighting viewport crop correctly inverts signed native floors and independent pan/zoom',()=>{
 for(const level of [-32,-17,-16,-1,0,15,16,31])for(const zoom of [.08,1,32]){
  const pan={x:132,y:277},center=lightingPoint(100.5,90.5,level,{zoom,pan});
  const context={project:{width:256,height:256},level,transform:{zoom,pan,viewport:{width:center.x*2,height:center.y*2}}};
  assert.deepEqual(lightingArea(context,32),{x:84,y:74,size:32,level});
 }
});
test('lighting crop is clipped inside the map and rejects fractional coordinates and unsupported floors',()=>{
 const context={project:{width:256,height:512},level:0,transform:{zoom:1,pan:{x:0,y:0},viewport:{width:600,height:400}}};
 assert.deepEqual(lightingArea(context,64,300,-40),{x:192,y:0,size:64,level:0});
 assert.throws(()=>lightingArea(context,65));assert.throws(()=>lightingArea(context,7));assert.throws(()=>lightingArea(context,32,1.5,2));
 assert.throws(()=>lightingArea({...context,level:-33},32));assert.throws(()=>lightingArea({...context,level:32},32));assert.throws(()=>lightingArea({...context,transform:{...context.transform,zoom:0}},32));
});
test('native lighting accepts only the requested immutable scene and authentic solver provenance',()=>{
 const valid=response();assert.equal(validateLightingResult(valid,area,'7'),valid);
 for(const mutate of [r=>r.revision='8',r=>r.x++,r=>r.level=0,r=>r.provenance.engine='Preview tint',r=>r.provenance.gameSession=true,r=>r.provenance.protocol=1,r=>r.provenance.counter=0,r=>r.provenance.counter=undefined,r=>r.provenance.counter=NaN,r=>r.provenance.counter=1.5,r=>r.warnings=[{}],r=>r.warnings=['x'.repeat(1001)]]){const r=response();mutate(r);assert.throws(()=>validateLightingResult(r,area,'7'));}
});
test('native lighting rejects duplicate outside nonfinite and invalid-color samples',()=>{
 for(const mutate of [r=>r.cells.push([...r.cells[0]]),r=>r.cells[0][0]=16,r=>r.cells[0][1]=-1,r=>r.cells[0][2]=0,r=>r.cells[0][3]=1.1,r=>r.cells[0][4]=-1,r=>r.cells[0][6]=-1,r=>r.cells[0][6]=5,r=>r.cells[0][7]=NaN,r=>r.cells[0][7]=.5,r=>r.cells[0][7]=2147483648,r=>r.cells[0].pop(),r=>r.cells=Array(257).fill(r.cells[0])]){const r=response();mutate(r);assert.throws(()=>validateLightingResult(r,area,'7'));}
 const empty=response();empty.cells=[];assert.equal(validateLightingResult(empty,area,'7'),empty);
});
test('lighting snapshots invalidate on map replacement section world revision or signed floor changes',()=>{
 const a={revision:'7',memberId:'section',project:{},world:{},level:-1};assert.ok(sameLightingScene(a,{...a}));
 for(const changed of [{revision:'8'},{memberId:'other'},{project:{}},{world:{}},{level:0}])assert.equal(sameLightingScene(a,{...a,...changed}),false);
});
test('moving the lighting crop to the visible area cancels and invalidates the previous result',()=>{
 const fields={size:{value:'16'},x:{value:'40'},y:{value:'50'}},panel=Object.create(NativeLightingPanel.prototype);let aborted=0,redrawn=0;
 Object.assign(panel,{result:response(),controller:{abort(){aborted++;}},getContext:()=>({project:{width:256,height:256},level:-1,transform:{zoom:1,pan:{x:0,y:0},viewport:{width:256,height:256}}}),find:name=>fields[name],requestDraw(){redrawn++;},state(){},message(text){throw Error(text);}});
 panel.center();assert.equal(aborted,1);assert.equal(panel.result,null);assert.ok(redrawn>0);
});
