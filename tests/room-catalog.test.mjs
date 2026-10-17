import test from 'node:test';
import assert from 'node:assert/strict';
import {validateDesign,refreshEdges,roomNames} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';

test('the editor accepts Build 42 room names and keeps its own labels',()=>{
 assert.equal(roomNames.livingroom,'Living room');assert.ok(Object.keys(roomNames).length>500);
 const b={id:'bakery',name:'Bakery',kind:'shop',x:10,y:10,width:4,height:3,design:{material:'wood',roof:'flat',shape:'rect',variant:1,floors:[{cells:'111111111111',rooms:{'1':'bakery'},edges:[],objects:[]}]}};
 refreshEdges(b,0);validateDesign(b);
 b.design.floors[0].rooms['1']='no-such-room';assert.throws(()=>validateDesign(b));
});
