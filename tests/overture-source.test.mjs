import test from 'node:test';
import assert from 'node:assert/strict';
import {readOvertureFile,overtureReport} from '../src/SimplePZMapmaker.Web/wwwroot/overture-source.js';
test('Overture file adapter reads only bounded explicit building GeoJSON',async()=>{
 const json=JSON.stringify({type:'FeatureCollection',features:[]});
 assert.equal(await readOvertureFile({size:json.length,text:async()=>json}),json);
 await assert.rejects(readOvertureFile({size:64_000_001,text:async()=>{throw Error('must not read');}}),/64 MB/);
 await assert.rejects(readOvertureFile({size:2,text:async()=>'{}'}),/FeatureCollection/);
 await assert.rejects(readOvertureFile(null),/Choose/);
});
test('Overture reports supplied counts and does not claim photographs or verified floor plans',()=>{
 assert.match(overtureReport({candidates:10,added:4,existing:3,tooSmall:2,outside:1}),/4 added.*3 already.*2.*small.*1 outside/i);
 assert.match(overtureReport({candidates:0,added:0,existing:0,tooSmall:0,outside:0}),/roofprints.*not verified/i);
 assert.throws(()=>overtureReport({candidates:1,added:-1,existing:0,tooSmall:0,outside:0}),/invalid/i);
});
