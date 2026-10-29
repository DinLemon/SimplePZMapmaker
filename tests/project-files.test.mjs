import test from 'node:test';
import assert from 'node:assert/strict';
import {maximumProjectBytes,readProjectFile} from '../src/SimplePZMapmaker.Web/wwwroot/project-files.js';

test('a geographic map above the old 48 MB cap can be opened',async()=>{
 assert.deepEqual(await readProjectFile({size:64_000_000,text:async()=>'{"width":2048}'}),{width:2048});
});
test('project file budget rejects oversized input before reading it',async()=>{
 let read=false;
 await assert.rejects(readProjectFile({size:maximumProjectBytes+1,text:async()=>{read=true;return '{}';}}),/128 MB/);
 assert.equal(read,false);
 assert.deepEqual(await readProjectFile({size:maximumProjectBytes,text:async()=>'{}'}),{});
});
test('a cancelled project read cannot replace the current map',async()=>{
 const controller=new AbortController();
 await assert.rejects(readProjectFile({size:1,text:async()=>{controller.abort();return '{}';}},controller.signal),{name:'AbortError'});
});
