import assert from 'node:assert/strict';
import test from 'node:test';
import {validateLinearFeatures,linearSummary,junctionKinds} from '../src/SimplePZMapmaker.Web/wwwroot/linear-editing.js';
const feature=(level=1)=>({id:'deck',kind:'bridge',width:4,level,access:'stairs',guardRails:true,supportSpacing:level>0?12:0,groupId:'crossing',interpolation:'polyline',points:[{x:40,y:60},{x:80,y:60}],sourcePath:[],sourceTags:{},underlay:[],decorationIds:[],generatedDecorations:[],assetSources:{}});
const validate=f=>validateLinearFeatures({width:256,height:256,linearFeatures:[f]});
test('signed infrastructure floors accept native endpoints and reject invalid floor/access/resources',()=>{for(const z of [-32,-1,1,31])assert.equal(validate(feature(z))[0].level,z);for(const z of [-33,32,1.5,NaN])assert.throws(()=>validate(feature(z)));for(const patch of [{access:'ramp'},{level:0},{kind:'river'},{supportSpacing:1},{supportSpacing:65},{groupId:'  '},{assetSources:{carpentry_02_98:'game'}},{underlay:[{index:1}]}])assert.throws(()=>validate({...feature(),...patch}));assert.throws(()=>validate({...feature(-1),supportSpacing:12}));});
test('junction tool names and feature summary retain floor evidence',()=>{assert.deepEqual(junctionKinds,['crossroads','t-junction','roundabout','overpass','underpass','stacked-crossing']);assert.match(linearSummary(feature(-2)),/z=-2/);});
