import test from 'node:test';
import assert from 'node:assert/strict';

test('shell tools round-trip through native modes without masquerading as a terrain brush',async()=>{
 const {nativeToolFor,shellToolForNative,overviewToolFor,toolContext}=await import('../src/SimplePZMapmaker.Web/wwwroot/editor-shell.js');
 for(const [shell,native] of [['select','select'],['building','building'],['world-asset','object'],['static-model','model'],['erase-decor','erase-object'],['zones','zone-area'],['native-wall','wall']]){
  assert.equal(nativeToolFor(shell),native);assert.equal(shellToolForNative(native),shell);
 }
 assert.equal(nativeToolFor('brush'),null);assert.equal(overviewToolFor('native-wall'),'select');
 assert.equal(toolContext('native-wall').category,'building');assert.equal(toolContext('world-asset').category,'objects');assert.equal(toolContext('zones').category,'zones');
});

test('context keeps building placement, object palette and zone authoring mutually exclusive',async()=>{
 const {toolContext}=await import('../src/SimplePZMapmaker.Web/wwwroot/editor-shell.js');
 assert.deepEqual(toolContext('brush'),{category:'terrain',terrain:true,building:false,objects:false,zones:false,paths:false,density:false});
 assert.equal(toolContext('building').building,true);assert.equal(toolContext('select').building,false);
 assert.equal(toolContext('zombies').density,true);assert.equal(toolContext('road').paths,true);
});
