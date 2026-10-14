import test from 'node:test';
import assert from 'node:assert/strict';
import {isBuildingModule,modulePlacementSettings} from '../src/SimplePZMapmaker.Web/wwwroot/building-modules.js';
test('module roles use the existing building template metadata',()=>{for(const role of ['entrance','section','annex','stairs'])assert.equal(isBuildingModule({sourceKind:'module:'+role}),true);for(const sourceKind of ['house','module:unknown','prefixmodule:annex',null])assert.equal(isBuildingModule({sourceKind}),false);assert.equal(isBuildingModule(null),false);});
test('module placement retains signed basement and upper-floor coordinates',()=>{for(const level of [-32,-1,0,31]){const result=modulePlacementSettings({x:'8',y:'4',level:String(level),quarterTurns:'3',mirror:true,autoConnect:true,expand:true});assert.deepEqual(result,{x:8,y:4,level,quarterTurns:3,mirror:true,autoConnect:true,expand:true});}});
test('module coordinates reject fractions and signed range overflow',()=>{const valid={x:0,y:0,level:0,quarterTurns:0};for(const [key,value] of [['x',-1],['x',64],['x',.5],['y',NaN],['level',-33],['level',32],['quarterTurns',4]])assert.throws(()=>modulePlacementSettings({...valid,[key]:value}));});
