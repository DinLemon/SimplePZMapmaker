import test from 'node:test';
import assert from 'node:assert/strict';
import {validateDesign} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
function twoCores(){return {id:'two-cores',kind:'apartments',width:12,height:6,design:{material:'wood',roof:'flat',shape:'custom',variant:42,floors:Array.from({length:2},(_,z)=>({cells:'11111..22222'.repeat(6),rooms:{1:'hall',2:'hall'},edges:[{x:2,y:0,edge:'n',kind:z===0?'door':'window'},{x:9,y:0,edge:'n',kind:z===0?'door':'window'}],objects:z===0?[2,9].map(x=>({x,y:0,kind:'stairs',direction:'n'})):[]}))}};}
test('separate entrances and staircases support independent apartment sections',()=>assert.doesNotThrow(()=>validateDesign(twoCores())));
test('each upper section needs vertical access from its own entrance',()=>{const b=twoCores();b.design.floors[0].objects.pop();assert.throws(()=>validateDesign(b));});
test('a disconnected ground section without outside entry cannot be applied',()=>{const b=twoCores();b.design.floors[0].edges[1].kind='wall';assert.throws(()=>validateDesign(b));});
