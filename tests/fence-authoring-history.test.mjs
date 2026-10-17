import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,History,mapAuthoringVersion,validateProject} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
const fenced=()=>({...blank(),version:12,fenceStyles:[{id:'iron',w:'fencing_01_0',n:'fencing_01_1'}],gateStyles:[{id:'gate',width:2,closedW:['fixtures_doors_fences_01_0','fixtures_doors_fences_01_1'],closedN:['fixtures_doors_fences_01_2','fixtures_doors_fences_01_3']}],fences:[{id:'fence-1',style:'iron',points:[{x:10,y:10},{x:14,y:10},{x:14,y:16}],gates:[{at:1,style:'gate'}],sourceTags:{barrier:'fence'},sourcePath:[{longitude:45.91,latitude:55.55}]}]});
test('fences styles and gate metadata undo together while density ownership survives',()=>{
 const p=blank(),h=new History();p.version=9;p.zombieDensityRegions=[];p.zombieDensityState={baseline:p.zombies.slice(),applied:{},manualOverrides:[]};p.roomRegionRules=[];const before=structuredClone(p),next=fenced();next.zombieDensityRegions=[];next.zombieDensityState=structuredClone(p.zombieDensityState);next.roomRegionRules=[];
 h.checkpoint(p,next);assert.equal(mapAuthoringVersion(p),12);assert.equal(validateProject(p),p);const after=structuredClone(p);h.undo(p);assert.deepEqual(p,before);for(const key of ['fences','fenceStyles','gateStyles'])assert.equal(Object.hasOwn(p,key),false);h.redo(p);assert.deepEqual(p,after);
 const without=structuredClone(before);h.checkpoint(p,without);for(const key of ['fences','fenceStyles','gateStyles'])assert.equal(Object.hasOwn(p,key),false);h.undo(p);assert.deepEqual(p,after);
});
test('fence validation rejects diagonal or repeated vertices unknown styles gate corners and damaged evidence',()=>{
 for(const mutate of [p=>p.version=11,p=>p.fences[0].points[1].y=11,p=>p.fences[0].points[1]={x:10,y:10},p=>p.fences[0].style='missing',p=>p.fences.push(structuredClone(p.fences[0])),p=>p.fences[0].points[0].x=Infinity,p=>p.fenceStyles[0].altEvery=2,p=>p.gateStyles[0].closedN.pop(),p=>p.fences[0].gates[0].at=3,p=>p.fences[0].gates.push({at:2,style:'gate'}),p=>p.fences[0].gates[0].open=true,p=>p.fences[0].sourcePath[0].longitude=181,p=>p.fences[0].sourceTags={broken:'x'.repeat(301)},p=>p.fences=null]){const p=fenced();mutate(p);assert.throws(()=>validateProject(p));}
});
test('fence empty defaults preserve legacy maps and section projected negative vertices remain valid',()=>{
 assert.equal(validateProject(blank()).version,4);const p=fenced();p.fences[0].points=[{x:-4,y:10},{x:14,y:10}];p.fences[0].gates=[];assert.equal(validateProject(p),p);
});
