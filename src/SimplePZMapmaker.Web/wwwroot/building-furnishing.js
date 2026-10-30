import {floorLevel,normalizeBuilding,validateDesign} from './building.js';

export async function refurnishFloor(workshop){
 if(!workshop.draft||workshop.saving||workshop.visual.allLevels)return false;
 const original=JSON.stringify(workshop.draft),level=workshop.level;
 workshop.saving=true;workshop.lock(true);let result;
 try{
  result=normalizeBuilding((await workshop.api('/api/building/furnish',{building:structuredClone(workshop.draft),level:floorLevel(workshop.draft,level)})).building);validateDesign(result);
  if(JSON.stringify(workshop.draft)!==original||workshop.level!==level)throw Error('The floor changed while furnishing. Current edits are retained.');
 }catch(e){workshop.message(e.message,true);return false;}finally{workshop.saving=false;workshop.lock(false);}
 const applied=workshop.change(()=>{workshop.draft=result;});
 if(applied)workshop.message('Furniture arranged for this floor’s room types. Review the draft; Undo restores the previous furniture.');return !!applied;
}
export function installFurnishingControls(workshop){
 const box=document.createElement('details');box.id='be-furnishing';
 box.innerHTML='<summary>Automatic furniture</summary><p class="hint">Replace ordinary furniture on this floor using its room types. Keep walls, stairs, individually placed tile groups and 3D models. Review the draft before applying; Undo restores the old furniture.</p><button type="button" id="be-refurnish">Refurnish this floor</button>';
 document.getElementById('be-auto-stairs').after(box);box.querySelector('button').onclick=()=>refurnishFloor(workshop);
}
