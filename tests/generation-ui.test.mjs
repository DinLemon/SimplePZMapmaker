import test from 'node:test';
import assert from 'node:assert/strict';
import {parametricSettings} from '../src/SimplePZMapmaker.Web/wwwroot/parametric-buildings.js';
import {ensembleSettings} from '../src/SimplePZMapmaker.Web/wwwroot/street-ensembles.js';
const building={name:'Compound',entrances:2,floors:3,apartmentsPerFloor:2,layout:'slab',facade:'brick',seed:42};
test('parametric controls send exact per entrance unit count and native floor range',()=>{assert.deepEqual(parametricSettings({...building,entrances:'4',floors:'32',apartmentsPerFloor:'2',seed:'-12'}),{...building,entrances:4,floors:32,apartmentsPerFloor:2,seed:-12});});
test('parametric controls reject unsupported native room capacity before request',()=>{for(const change of [{entrances:4,apartmentsPerFloor:3},{floors:33},{floors:0},{entrances:1.5},{seed:Infinity}])assert.throws(()=>parametricSettings({...building,...change}));});
test('ensemble controls preserve bounded rectangles and feature toggles',()=>{const input={x:'20',y:'30',width:'100',height:'80',preset:'garden',seed:'7',entrances:'2',floors:'3',apartmentsPerFloor:'2',fences:true,parking:false,greenery:true,lights:true};assert.deepEqual(ensembleSettings(input),{...input,x:20,y:30,width:100,height:80,seed:7,entrances:2,floors:3,apartmentsPerFloor:2});for(const change of [{x:-1},{width:257},{height:39},{y:.5}])assert.throws(()=>ensembleSettings({...input,...change}));});
