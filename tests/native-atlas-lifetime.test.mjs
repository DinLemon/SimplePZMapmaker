import test from 'node:test';
import assert from 'node:assert/strict';
import {MapNativePreview} from '../src/SimplePZMapmaker.Web/wwwroot/map-native-preview.js';

const tick=()=>new Promise(resolve=>setTimeout(resolve,1));
async function settled(preview){for(let i=0;i<1000;i++){await tick();if(!preview.pending.size&&!preview.imagePending)return;}throw Error('Preview did not settle');}
async function fixture(run){
 const saved={document:globalThis.document,Image:globalThis.Image,fetch:globalThis.fetch};
 globalThis.document={addEventListener(){},removeEventListener(){}};
 globalThis.Image=class{naturalWidth=8;naturalHeight=8;async decode(){await tick();}};
 globalThis.fetch=async(_url,options)=>({ok:true,json:async()=>JSON.parse(options.body).map(id=>({id,page:id,pageWidth:8,pageHeight:8,sprite:{}}))});
 const messages=[],preview=new MapNativePreview({canvas:{dataset:{}},requestDraw(){},status:m=>messages.push(m)});
 preview.invalidate();preview.session='test';preview.images.budget=3*8*8*4;
 preview.view={isometric:true,level:0,project:{width:512,height:512}};
 const scenes={a:{sprites:[{tile:'fence'},{tile:'ground'}],models:[]},b:{sprites:[{tile:'pipe'}],models:[]}};
 preview.plan=Object.keys(scenes).map(key=>({key,x:key==='a'?0:32,y:0,size:32,stride:1}));
 preview.request=async url=>{await tick();return structuredClone(scenes[new URL('http://local'+url).searchParams.get('x')==='0'?'a':'b']);};
 try{await run(preview,messages);}finally{preview.session=null;preview.close();Object.assign(globalThis,saved);}
}
test('concurrent native chunks retain every visible atlas under cache pressure',async()=>fixture(async preview=>{
 preview.schedule();await settled(preview);
 assert.equal(preview.canvas.dataset.nativeState,'ready');assert.equal(preview.cache.size,2);
 await preview.images.load(['unrelated-page']);
 assert.deepEqual([...preview.images.pinned].sort(),['fence','ground','pipe']);
 for(const tile of ['fence','ground','pipe'])assert.ok(preview.images.pages.has(tile),tile+' was evicted while visible');
 assert.ok(preview.images.bytes<=preview.images.budget);
}));
test('returning to a cached native chunk reloads its evicted atlas and releases old pins',async()=>fixture(async preview=>{
 preview.schedule();await settled(preview);
 preview.plan=preview.plan.filter(c=>c.key==='b');await preview.loadImages();
 assert.deepEqual([...preview.images.pinned],['pipe']);
 await preview.images.load(['offscreen-1','offscreen-2','offscreen-3']);
 assert.ok(!preview.images.pages.has('fence'));
 preview.plan=[{key:'a'}];await preview.loadImages();
 assert.deepEqual([...preview.images.pinned].sort(),['fence','ground']);
 assert.ok(preview.images.pages.has('fence')&&preview.images.pages.has('ground'));
}));
test('an oversized visible atlas union reports an error instead of a ready incomplete view',async()=>fixture(async(preview,messages)=>{
 preview.images.budget=2*8*8*4;preview.schedule();await settled(preview);
 assert.equal(preview.canvas.dataset.nativeState,'error');assert.ok(messages.some(m=>/smaller area|memory budget/i.test(m)));
 assert.ok(preview.images.bytes<=preview.images.budget);
}));
test('a catalog refresh creates a live owner after clearing old atlas metadata',async()=>fixture(async preview=>{
 preview.schedule();await settled(preview);const previous=preview.imageOwner;
 preview.view=null;preview.listener();
 assert.notEqual(preview.imageOwner,previous);assert.equal(preview.imageOwner,preview.images.sceneOwner);
 preview.plan=[{key:'new'}];preview.cache.set('new',{sprites:[{tile:'updated-fence'}],models:[]});
 assert.equal(await preview.loadImages(),true);
 assert.deepEqual([...preview.images.pinned],['updated-fence']);assert.ok(preview.images.pages.has('updated-fence'));
}));
