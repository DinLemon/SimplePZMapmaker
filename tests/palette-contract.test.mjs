import {test} from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,geographicBase,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {validatePalettes} from '../src/SimplePZMapmaker.Web/wwwroot/palette-data.js';
const palette=()=>({id:'a'.repeat(32),name:'Workshop references',resources:[{role:'floor',id:'floors_interior_tilesandwood_01_0',sourceId:'sample-mod',requiredMods:['sample-mod']}]});
test('unused mod palette survives save, geographic reimport and undo without activating a mod',()=>{
 const p=blank(),h=new History();h.begin(p);p.resourcePalettes=[palette()];p.version=8;h.commit(p);
 assert.equal(validateProject(p).version,8);assert.deepEqual(p.requiredMods,[]);
 const saved=JSON.parse(JSON.stringify(p));assert.deepEqual(validateProject(saved).resourcePalettes,p.resourcePalettes);
 const imported=geographicBase(p,256,false);assert.deepEqual(imported.resourcePalettes,p.resourcePalettes);imported.resourcePalettes[0].name='Changed';assert.equal(p.resourcePalettes[0].name,'Workshop references');
 h.undo(p);assert.deepEqual(p.resourcePalettes,[]);h.redo(p);assert.equal(p.resourcePalettes[0].resources[0].sourceId,'sample-mod');
});
test('palette bounds and malformed provider references rejected before persistence',()=>{
 for(const change of [p=>p.resources[0].sourceId='../mod',p=>p.resources[0].requiredMods=['mod','mod'],p=>p.name='bad\nname',p=>p.resources.push({...p.resources[0]}),p=>p.resources[0].role='script']){const p=palette();change(p);assert.throws(()=>validatePalettes([p]));}
 assert.throws(()=>validatePalettes([palette(),palette()]));assert.throws(()=>validatePalettes(Array.from({length:17},palette)));
 const p=blank();p.resourcePalettes=[palette()];assert.throws(()=>validateProject(p),/format 8/);
});
