import {mountProjectDoctor} from './project-doctor.js';
import {mountBuildingExchange} from './building-exchange.js';
import {mountAutomappingControls} from './automapping.js';
import {drawGenerationMap} from './generation-studio.js';

/** Menu entry points for shared authoring services; the host owns snapshots and history. */
export function installAuthoringTools({getProject,getSnapshot,getBuilding,importDraft,applyProject,api,focus,selectArea}){
 const link=document.createElement('link');link.rel='stylesheet';link.href='authoring-tools.css';document.head.append(link);
 function dialog(id,label){
  const button=document.createElement('button');button.type='button';button.id=id;button.textContent=label;
  document.querySelector('.topbar nav').append(button);
  const dialog=document.createElement('dialog');dialog.className='authoring-dialog';dialog.id=id+'-dialog';
  const close=document.createElement('button');close.type='button';close.className='authoring-close';close.textContent='Close';close.onclick=()=>dialog.close();
  const host=document.createElement('div');dialog.append(close,host);document.body.append(dialog);button.onclick=()=>dialog.showModal();
  return {button,dialog,host};
 }
 const doctor=dialog('open-project-doctor','Project Doctor');
 mountProjectDoctor(doctor.host,{getSnapshot,navigate:point=>{doctor.dialog.close();focus?.(point);}});
 const exchange=dialog('open-building-exchange','BuildingEd / TileZed exchange');
 const exchangeController=mountBuildingExchange({container:exchange.host,getBuilding,onImport:async(b,context)=>{await importDraft(b,context);exchange.dialog.close();},request:api});
 exchange.button.onclick=()=>{exchangeController.refresh();exchange.dialog.showModal();};
 const draftExchange=document.createElement('button');draftExchange.id='be-exchange';draftExchange.type='button';draftExchange.textContent='BuildingEd / TileZed formats';draftExchange.onclick=exchange.button.onclick;document.getElementById('be-template-export').after(draftExchange);
 const streets=dialog('open-automapping','Street automapping');
 const bounds=document.createElement('fieldset'),legend=document.createElement('legend');legend.textContent='Selected area';bounds.append(legend);
 const fields={};for(const key of ['x','y','width','height']){const label=document.createElement('label'),input=document.createElement('input');label.textContent=key;input.type='number';input.id='auto-area-'+key;input.min=key==='x'||key==='y'?'0':'1';input.value=key==='x'||key==='y'?'0':'128';fields[key]=input;label.append(input);bounds.append(label);}
 const selection=()=>Object.fromEntries(Object.entries(fields).map(([k,v])=>[k,+v.value]));
 streets.host.append(bounds);
 if(selectArea){const draw=document.createElement('button');draw.type='button';draw.textContent='Draw area on map';draw.onclick=()=>{streets.dialog.close();selectArea(area=>{for(const [k,v] of Object.entries(area))if(fields[k])fields[k].value=v;streets.dialog.showModal();});};streets.host.append(draw);}
 const canvas=document.createElement('canvas');canvas.setAttribute('aria-label','Street automapping preview');streets.host.append(canvas);
 const controls=mountAutomappingControls(streets.host,{getProject,applyProject,getSelection:selection,onPreview:plan=>{
  const p=getProject(),area=plan?.options??selection();drawGenerationMap(canvas,p,area);
  if(!plan)return;const ctx=canvas.getContext('2d'),s=Math.min(456/area.width,336/area.height),ox=(480-area.width*s)/2,oy=(360-area.height*s)/2;ctx.fillStyle='#ecd693';
  for(const d of plan.decorations)for(const part of d.parts)ctx.fillRect(ox+(d.x+part.dx-area.x)*s,oy+(d.y+part.dy-area.y)*s,Math.max(1,s),Math.max(1,s));
 }});
 streets.host.querySelector('#automapping-controls').open=true;
 for(const input of Object.values(fields))input.addEventListener('input',()=>controls.refresh());
 return {doctor,exchange,streets};
}
