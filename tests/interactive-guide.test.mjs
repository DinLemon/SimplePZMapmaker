import test from 'node:test';
import assert from 'node:assert/strict';
import {guideTours,guideCopy} from '../src/SimplePZMapmaker.Web/wwwroot/guide-content.js';
import {GuideProgress,guideStorageKey,placeGuideCard} from '../src/SimplePZMapmaker.Web/wwwroot/guide-state.js';

const memory=initial=>({value:initial,getItem(){return this.value;},setItem(_key,value){this.value=value;}});

test('all task routes have stable unique steps and complete Russian and English copy',()=>{
 assert.equal(guideTours.length,6);
 assert.equal(new Set(guideTours.map(t=>t.id)).size,6);
 for(const language of ['en','ru']){
  assert.ok(guideCopy[language].pause);
  for(const tour of guideTours){
   assert.ok(tour.title[language]);assert.ok(tour.description[language]);
   assert.ok(tour.steps.length>=4);assert.equal(new Set(tour.steps.map(s=>s.id)).size,tour.steps.length);
   for(const step of tour.steps){assert.ok(step.title[language]);assert.ok(step.body[language]);assert.ok(step.target);assert.ok(step.hint[language]);}
  }
 }
});

test('pause and reload resume each task independently; completion and restart are explicit',()=>{
 const storage=memory(null),first=new GuideProgress(storage);
 first.start('first-map');first.move(1);first.move(1);first.pause();
 const next=new GuideProgress(storage);assert.equal(next.active,null);assert.equal(next.index('first-map'),2);
 next.start('world');next.move(1);next.start('first-map');assert.equal(next.step,2);
 assert.equal(next.completed('first-map'),false);
 next.finish();assert.equal(next.completed('first-map'),true);assert.equal(next.active,null);
 next.start('first-map',true);assert.equal(next.step,0);assert.equal(next.completed('first-map'),false);
 assert.equal(next.index('world'),1);
 assert.equal(guideStorageKey,'simplepzmap.guide.v1');
 assert.deepEqual(Object.keys(JSON.parse(storage.value)).sort(),['dismissed','language','tours','version']);
});

test('storage is bounded, rejects invalid versions, hostile IDs, indices and languages',()=>{
 for(const data of ['{',null,'x'.repeat(20000),JSON.stringify({version:999,tours:{world:{step:3}}})]){
  const progress=new GuideProgress(memory(data));assert.equal(progress.index('world'),0);
 }
 const progress=new GuideProgress(memory(JSON.stringify({version:1,language:'<script>',dismissed:'false',tours:{world:{step:999,done:'yes'},'first-map':{step:-2,done:false},'__proto__':{step:3,done:true},unknown:{step:3,done:true}}})));
 assert.equal(progress.index('world'),0);assert.equal(progress.completed('world'),false);assert.equal(progress.index('first-map'),0);
 assert.equal(progress.language,'en');assert.equal(progress.dismissed,false);
 assert.throws(()=>progress.start('__proto__'));
 progress.start('first-map');progress.move(-100);assert.equal(progress.step,0);progress.move(1000);assert.equal(progress.step,guideTours[0].steps.length-1);
});

test('blocked localStorage keeps navigation and dismissal functional in memory',()=>{
 const progress=new GuideProgress({getItem(){throw Error('blocked');},setItem(){throw Error('quota');}});
 progress.start('world');progress.move(1);progress.setLanguage('ru');progress.dismiss();
 assert.equal(progress.step,1);assert.equal(progress.language,'ru');assert.equal(progress.dismissed,true);
 progress.pause();assert.equal(progress.active,null);
});

test('card placement stays within narrow, short and wide viewports and avoids small targets',()=>{
 for(const [width,height] of [[360,640],[1440,1100],[320,240]]){
  const card={width:Math.min(350,width-16),height:Math.min(290,height-16)};
  const target={left:width-80,top:40,width:60,height:25};
  const p=placeGuideCard(target,card,{width,height});
  assert.ok(p.left>=8&&p.top>=8);assert.ok(p.left+card.width<=width-8);assert.ok(p.top+card.height<=height-8);
 }
 const p=placeGuideCard({left:30,top:100,width:120,height:40},{width:350,height:290},{width:1440,height:900});
 assert.ok(p.left>=162);
});
