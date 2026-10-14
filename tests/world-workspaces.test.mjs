import {test} from 'node:test';
import assert from 'node:assert/strict';
import {validateWorkspace} from '../src/SimplePZMapmaker.Web/wwwroot/world-workspaces.js';
import {changeSummary} from '../src/SimplePZMapmaker.Web/wwwroot/agent-review.js';
import {blank,mapAuthoringVersion} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
test('section index validation requires bounded distinct saved references and a revision',()=>{const id='a'.repeat(32),s={id,bytes:100,worldX:100,worldY:100,width:256,height:256};const m={format:'SimplePZWorldSections',version:1,id,revision:1,name:'Sections',sections:[s]};assert.equal(validateWorkspace(m),m);assert.throws(()=>validateWorkspace({...m,revision:0}));assert.throws(()=>validateWorkspace({...m,sections:[s,s]}));assert.throws(()=>validateWorkspace({...m,sections:[{...s,bytes:49*1024*1024}]}));});
test('agent review lists gameplay zone changes and infrastructure promotes format11',()=>{const before=blank(),after=structuredClone(before);after.gameZones=[{id:'a'.repeat(32),name:'Farm area',type:'Farm',x:20,y:20,width:32,height:32,z:0}];const summary=changeSummary(before,after);assert.equal(summary.changedObjects,1);assert.match(summary.changes[0],/Farm area/);assert.equal(mapAuthoringVersion(after),9);after.linearFeatures=[{level:1}];assert.equal(mapAuthoringVersion(after),11);});
