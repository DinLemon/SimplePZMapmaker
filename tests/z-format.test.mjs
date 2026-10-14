import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,mapAuthoringVersion} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {buildingTemplateVersion,buildingTemplateFromDocument,worldAuthoringVersion} from '../src/SimplePZMapmaker.Web/wwwroot/world-geography.js';
import {autoStairs} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import {validateTemplateBytes,templateByteLimit} from '../src/SimplePZMapmaker.Web/wwwroot/z-levels.js';
const openBuilding=()=>{const b={id:'a'.repeat(32),name:'Open roof',kind:'house',x:40,y:40,width:12,height:10,design:{minLevel:0,material:'brick',roof:'none',shape:'rect',variant:42,floors:[{cells:'1'.repeat(120),rooms:{1:'livingroom'},objects:[],edges:[],staticModels:[]}]}};autoStairs(b);return b;};
test('extended authored levels and open roof templates advance map10/template7/world4',()=>{
 const p=blank();assert.equal(mapAuthoringVersion(p),4);const b=openBuilding();p.buildingLibrary=[b];assert.equal(mapAuthoringVersion(p),10);assert.equal(buildingTemplateVersion(b),7);assert.equal(worldAuthoringVersion({version:1,maps:[],buildings:[b]}),4);assert.equal(worldAuthoringVersion({version:1,maps:[{project:{version:10}}]}),4);
 assert.equal(buildingTemplateFromDocument({format:'SimplePZBuilding',version:7,building:b}).design.roof,'none');assert.throws(()=>buildingTemplateFromDocument({format:'SimplePZBuilding',version:6,building:b}),/format 7/);
});
test('new vertical authoring remains one undoable metadata change without promoting legacy blank maps',()=>{
 const p=blank(),h=new History();h.begin(p);p.buildingLibrary.push(openBuilding());h.commit(p);assert.equal(p.version,10);h.undo(p);assert.equal(p.version,4);assert.deepEqual(p.buildingLibrary,[]);h.redo(p);assert.equal(p.version,10);assert.equal(p.buildingLibrary[0].design.roof,'none');
});
test('large valid signed templates fit a common bounded import budget before parsing',()=>{
 validateTemplateBytes(2_597_319);validateTemplateBytes(templateByteLimit);for(const value of [templateByteLimit+1,-1,NaN,Infinity,'100'])assert.throws(()=>validateTemplateBytes(value),/16 MB/);
});
