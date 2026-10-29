import test from 'node:test';
import assert from 'node:assert/strict';
import {MapNativePreview,nativeChunkPlan} from '../src/SimplePZMapmaker.Web/wwwroot/map-native-preview.js';
async function fixture(run){
 const old={document:globalThis.document,Image:globalThis.Image};globalThis.document={addEventListener(){},removeEventListener(){}};
 globalThis.Image=class{naturalWidth=8;naturalHeight=8;async decode(){}};
 const preview=new MapNativePreview({canvas:{dataset:{}},requestDraw(){}}),project={width:32,height:32};
 const view={project,revision:'v',isometric:true,level:0,zoom:64,pan:{x:64,y:64},viewport:{width:128,height:128}};
 preview.view=view;preview.session='snapshot';preview.projectIdentity=project;preview.identity='v|map||||0';preview.plan=nativeChunkPlan({width:32,height:32,...view});
 const sprite={x:0,y:0,width:128,height:64,offsetX:0,offsetY:192,frameWidth:128,frameHeight:256};
 for(const tile of ['near','far','overhang'])preview.images.meta.set(tile,{page:tile,pageWidth:8,pageHeight:8,sprite});
 const scene={sprites:[{x:0,y:0,z:0,tile:'near'},{x:12,y:0,z:0,tile:'far'}],models:[]};
 preview.cache.set(preview.plan[0].key,scene);preview.images.budget=8*8*4;
 try{await run(preview,view,scene);}finally{preview.session=null;preview.close();Object.assign(globalThis,old);}
}
test('offscreen sprites in a visible chunk consume no atlas memory or missing-image count',()=>fixture(async preview=>{
 await preview.loadImages();assert.deepEqual([...preview.images.pinned],['near']);
 assert.equal(preview.images.pages.has('far'),false);let painted=0;
 preview.draw({drawImage(){painted++;}});assert.equal(painted,1);assert.equal(preview.canvas.dataset.nativeMissing,'0');
}));
test('panning inside the same chunk loads the newly visible atlas and releases the previous one',()=>fixture(async(preview,view)=>{
 preview.update(view);await preview.imageQueue;
 const plan=preview.plan.map(c=>c.key);preview.update({...view,pan:{x:-704,y:-320}});await preview.imageQueue;
 assert.deepEqual(preview.plan.map(c=>c.key),plan);assert.deepEqual([...preview.images.pinned],['far']);
 assert.ok(preview.images.pages.has('far'));assert.equal(preview.images.pages.has('near'),false);
 assert.ok(preview.images.bytes<=preview.images.budget);
}));
test('an offscreen anchor retains a tall overhanging sprite that enters the viewport',()=>fixture(async(preview,_view,scene)=>{
 const info=preview.images.meta.get('overhang');info.sprite={...info.sprite,height:800,offsetY:-384};
 scene.sprites=[{x:3,y:3,z:0,tile:'overhang'}];await preview.loadImages();
 assert.deepEqual([...preview.images.pinned],['overhang']);let painted=0;preview.draw({drawImage(){painted++;}});assert.equal(painted,1);
}));
test('panning while a previous atlas decodes pins only the final viewport',()=>fixture(async(preview,view)=>{
 let release,started;const entered=new Promise(resolve=>started=resolve);
 globalThis.Image=class{naturalWidth=8;naturalHeight=8;async decode(){if(this.src.endsWith('/near')){started();await new Promise(resolve=>release=resolve);}}};
 preview.update(view);await entered;preview.update({...view,pan:{x:-704,y:-320}});release();await preview.imageQueue;
 assert.deepEqual([...preview.images.pinned],['far']);assert.ok(preview.images.pages.has('far'));assert.ok(preview.images.bytes<=preview.images.budget);
 preview.draw({drawImage(){}});assert.equal(preview.canvas.dataset.nativeMissing,'0');
}));
test('catalogue refresh during decoding cannot publish stale images into the new scene',()=>fixture(async(preview,view,scene)=>{
 let release,started;const entered=new Promise(resolve=>started=resolve),metadata=new Map(preview.images.meta);
 globalThis.Image=class{naturalWidth=8;naturalHeight=8;async decode(){if(this.src.endsWith('/near')){started();await new Promise(resolve=>release=resolve);}}};
 preview.update(view);await entered;const obsolete=preview.imageQueue;
 preview.listener();clearTimeout(preview.timer);preview.images.meta=new Map(metadata);preview.session='fresh';
 preview.plan=nativeChunkPlan({width:32,height:32,...view});preview.cache.set(preview.plan[0].key,scene);
 preview.update({...view,pan:{x:-704,y:-320}});await preview.imageQueue;release();assert.equal(await obsolete,false);
 assert.deepEqual([...preview.images.pinned],['far']);assert.equal(preview.images.pages.has('near'),false);assert.ok(preview.images.pages.has('far'));
}));
