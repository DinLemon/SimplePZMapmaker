import {guideTours} from './guide-content.js';

export const guideStorageKey='simplepzmap.guide.v1';
const tourFor=id=>guideTours.find(t=>t.id===id);

/** Guide progress only: never store project/source data or resume editor actions. */
export class GuideProgress {
 constructor(storage){
  this.storage=storage;this.active=null;this.language='en';this.dismissed=false;this.tours=Object.create(null);
  try{
   const raw=storage?.getItem(guideStorageKey);if(!raw||raw.length>12000)return;
   const saved=JSON.parse(raw);if(saved?.version!==1)return;
   if(['en','ru'].includes(saved.language))this.language=saved.language;
   this.dismissed=saved.dismissed===true;
   for(const tour of guideTours){
    const entry=Object.hasOwn(saved.tours??{},tour.id)?saved.tours[tour.id]:null;
    if(entry&&Number.isInteger(entry.step)&&entry.step>=0&&entry.step<tour.steps.length)
     this.tours[tour.id]={step:entry.step,done:entry.done===true};
   }
  }catch{/* Private browsing / corrupt progress must not prevent editing. */}
 }
 get step(){return this.index(this.active);}
 index(id){return this.tours[id]?.step??0;}
 visited(id){return Object.hasOwn(this.tours,id);}
 completed(id){return this.tours[id]?.done===true;}
 save(){try{this.storage?.setItem(guideStorageKey,JSON.stringify({version:1,language:this.language,dismissed:this.dismissed,tours:this.tours}));}catch{}}
 setLanguage(language){if(!['en','ru'].includes(language))return;this.language=language;this.save();}
 dismiss(){this.dismissed=true;this.save();}
 start(id,restart=false){
  if(!tourFor(id))throw Error('Unknown guide task.');
  this.active=id;if(restart||!this.visited(id))this.tours[id]={step:0,done:false};this.dismiss();
 }
 move(offset){
  const tour=tourFor(this.active);if(!tour||!Number.isFinite(offset))return;
  this.tours[this.active].step=Math.max(0,Math.min(tour.steps.length-1,this.step+Math.trunc(offset)));this.save();
 }
 pause(){this.active=null;this.save();}
 finish(){if(this.active){this.tours[this.active].done=true;this.pause();}}
}

/** Prefer a side of the target; otherwise minimize overlap, clamped to viewport. */
export function placeGuideCard(target,card,viewport){
 const pad=8,gap=12,w=card.width,h=card.height,vw=viewport.width,vh=viewport.height;
 const clamp=(value,low,high)=>Math.max(low,Math.min(value,Math.max(low,high)));
 const fit=([left,top])=>({left:clamp(left,pad,vw-w-pad),top:clamp(top,pad,vh-h-pad)});
 if(!target)return fit([vw-w-16,vh-h-16]);
 const {left:x,top:y,width:tw,height:th}=target;
 const positions=[[x+tw+gap,y],[x-w-gap,y],[x,y+th+gap],[x,y-h-gap],[vw-w-16,vh-h-16]].map(fit);
 const overlap=p=>Math.max(0,Math.min(x+tw,p.left+w)-Math.max(x,p.left))*Math.max(0,Math.min(y+th,p.top+h)-Math.max(y,p.top));
 return positions.reduce((best,p)=>overlap(p)<overlap(best)?p:best);
}
