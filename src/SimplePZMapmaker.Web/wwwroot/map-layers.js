export const mapLayerDefinitions=Object.freeze([
 {id:'terrain',label:'Terrain'},{id:'roads',label:'Roads'},{id:'paths',label:'Paths and fences'},
 {id:'buildings',label:'Buildings'},{id:'decorations',label:'Objects'},{id:'models',label:'Static models'},
 {id:'spawns',label:'Spawn points'},{id:'zones',label:'Game zones'},{id:'zombies',label:'Zombie density',visible:false},
 {id:'source',label:'Source boundaries',visible:false},{id:'sections',label:'Sections and seams'}
]);
const annotationLayers=['source','sections'];
export class MapLayerState {
 constructor(){this.order=mapLayerDefinitions.map(l=>l.id);this.layers=Object.fromEntries(mapLayerDefinitions.map(l=>[l.id,{visible:l.visible!==false,locked:false}]));}
 layer(id){if(!Object.hasOwn(this.layers,id))throw Error(`Unknown map layer: ${id}.`);return this.layers[id];}
 setVisible(id,value){this.layer(id).visible=!!value;}
 setLocked(id,value){this.layer(id).locked=!!value;}
 setOrder(ids){if(!Array.isArray(ids)||new Set(ids).size!==ids.length)throw Error('Layer order contains duplicates.');ids.forEach(id=>this.layer(id));this.order=[...ids,...this.order.filter(id=>!ids.includes(id))];}
 isVisible(id){return this.layer(id).visible;}
 isLocked(id){return this.layer(id).locked;}
 canEdit(id){return this.isVisible(id)&&!this.isLocked(id);}
 guardEdit(id,action){const l=this.layer(id);if(l.locked)throw Error(`${id} layer is locked. Unlock it before editing.`);if(!l.visible)throw Error(`${id} layer is hidden. Show it before editing.`);return action();}
 getState(){return structuredClone({order:this.order,layers:this.layers});}
}
export function projectPoint(point,transform={}){
 const {zoom=1,pan={x:0,y:0},isometric=false}=transform,{x,y,z=0}=point;
 return isometric?{x:pan.x+(x-y)*zoom,y:pan.y+((x+y)/2-z*3)*zoom}:{x:pan.x+x*zoom,y:pan.y+y*zoom};
}
export function sectionOverlays({project,world,memberId,manifest,sections,loadedSections,workspace}={}){
 if(!project)return [];const loaded=new Set([...(world?.maps??[]).map(m=>m.id),...(loadedSections??workspace?.loadedSections??new Map()).keys()]);if(memberId)loaded.add(memberId);
 const standalone=!sections&&!manifest?.sections&&!world?.maps,rows=sections??manifest?.sections??world?.maps?.map(m=>({id:m.id,...m.project}))??[{id:memberId??'current',...project}],ox=(project.worldX??0)*256,oy=(project.worldY??0)*256;
 return rows.filter(s=>[s.worldX,s.worldY,s.width,s.height].every(Number.isFinite)).map(s=>({id:s.id,x:s.worldX*256-ox,y:s.worldY*256-oy,width:s.width,height:s.height,active:s.id===memberId||standalone,loaded:loaded.has(s.id)||standalone,label:`${s.name||'Section'} · ${loaded.has(s.id)||standalone?'loaded':'unloaded'}`}));
}
export function sourceOverlays(project){
 const rows=[];
 for(const f of project?.sourceFeatures??[])for(const ring of f.geometry??[])if(ring.length&&ring.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)))rows.push({id:f.id,label:f.name||f.sourceId||'Source',kind:f.geometryKind,points:ring.map(p=>({x:p.x,y:p.y}))});
 for(const s of project?.sites??[])for(const ring of s.boundary??[])if(ring.length&&ring.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)))rows.push({id:s.id,label:s.name||'Source site',kind:'polygon',points:ring.map(p=>({x:p.x,y:p.y}))});
 return rows;
}
function trace(ctx,points,transform,closed=false){if(!points.length)return;const first=projectPoint(points[0],transform);ctx.beginPath();ctx.moveTo(first.x,first.y);for(const p of points.slice(1)){const s=projectPoint(p,transform);ctx.lineTo(s.x,s.y);}if(closed)ctx.closePath();}
export function installMapLayers({host,getProject=()=>null,getContext=()=>({project:getProject()}),requestDraw=()=>{},onStateChange=()=>{}}={}){
 const state=new MapLayerState(),box=host?document.createElement('details'):null;let status;
 const changed=()=>{render();requestDraw();onStateChange(state.getState());};
 function render(){if(!box)return;const wasOpen=box.open;box.replaceChildren();box.className='map-layers-workbench';box.open=wasOpen;const summary=document.createElement('summary');summary.textContent='Map layers';box.append(summary);
  const explanation=document.createElement('p');explanation.className='map-layer-hint';explanation.textContent='View controls keep all saved geometry. Hidden or locked layers cannot be edited. Up and Down change annotation order only; physical sprites retain their depth order.';box.append(explanation);
  for(const [index,id] of state.order.entries()){
   const def=mapLayerDefinitions.find(l=>l.id===id),row=document.createElement('div');row.className='map-layer-row';row.dataset.layer=id;
   const label=document.createElement('label'),visible=document.createElement('input');visible.type='checkbox';visible.checked=state.isVisible(id);visible.setAttribute('aria-label',`Show ${def.label}`);visible.onchange=()=>{state.setVisible(id,visible.checked);changed();};label.append(visible,document.createTextNode(def.label));
   const lock=document.createElement('button');lock.type='button';lock.textContent=state.isLocked(id)?'Unlock':'Lock';lock.title=`${lock.textContent} ${def.label}`;lock.setAttribute('aria-label',lock.title);lock.setAttribute('aria-pressed',String(state.isLocked(id)));lock.onclick=()=>{state.setLocked(id,!state.isLocked(id));changed();};row.append(label,lock);
   if(annotationLayers.includes(id)){const overlays=state.order.filter(layer=>annotationLayers.includes(layer)),position=overlays.indexOf(id);for(const [step,text] of [[-1,'Up'],[1,'Down']]){const move=document.createElement('button');move.type='button';move.textContent=text;move.className='map-layer-order';move.disabled=position+step<0||position+step>=overlays.length;move.setAttribute('aria-label',`Move ${def.label} ${text.toLowerCase()}`);move.onclick=()=>{const order=[...state.order],other=order.indexOf(overlays[position+step]);[order[index],order[other]]=[order[other],order[index]];state.setOrder(order);changed();};row.append(move);}}box.append(row);
  }
  status=document.createElement('p');status.className='map-layer-hint';status.setAttribute('role','status');const sections=sectionOverlays(getContext());status.textContent=sections.length?`${sections.filter(s=>s.loaded).length} loaded / ${sections.length} sections. Dashed outlines mark unloaded neighbours.`:'Open a map to inspect its layers.';box.append(status);
 }
 function draw(ctx,context=getContext()){
  const transform=context.transform??{};ctx.save();
  for(const id of state.order.filter(layer=>annotationLayers.includes(layer)&&state.isVisible(layer))){
  if(id==='sections')for(const section of sectionOverlays(context)){
   const {x,y,width:w,height:h}=section;trace(ctx,[{x,y},{x:x+w,y},{x:x+w,y:y+h},{x,y:y+h}],transform,true);
   ctx.lineWidth=section.active?2:1;ctx.strokeStyle=section.active?'#edc57e':section.loaded?'#91ba9d':'#ed987b';ctx.setLineDash(section.loaded?[]:[7,5]);if(!section.loaded){ctx.fillStyle='#ed987b0a';ctx.fill();}ctx.stroke();ctx.setLineDash([]);
   const p=projectPoint({x,y},transform),view=transform.viewport,labelWidth=Math.min(w,h)*(transform.zoom??1)-10;
   if((section.active||labelWidth>=150)&&labelWidth>=40&&(!view||p.x> -200&&p.y> -30&&p.x<view.width&&p.y<view.height)){
    ctx.fillStyle=ctx.strokeStyle;ctx.font='11px system-ui';let label=section.label;const measure=text=>ctx.measureText?.(text)?.width??text.length*6;
    if(measure(label)>labelWidth){while(label.length&&measure(label+'…')>labelWidth)label=label.slice(0,-1);label+='…';}ctx.fillText(label,p.x+5,p.y+15);
   }
  }
  if(id==='source'){ctx.strokeStyle='#6bc8e3';ctx.lineWidth=1.5;ctx.setLineDash([3,3]);for(const row of sourceOverlays(context.project??getProject())){trace(ctx,row.points,transform,row.kind==='polygon');if(row.kind==='point'){const p=projectPoint(row.points[0],transform);ctx.arc(p.x,p.y,4,0,Math.PI*2);}ctx.stroke();}}}
  ctx.restore();
 }
 if(box){host.append(box);render();}
 return {isVisible:id=>state.isVisible(id),isLocked:id=>state.isLocked(id),canEdit:id=>state.canEdit(id),guardEdit:(id,action)=>state.guardEdit(id,action),setVisible:(id,value)=>{state.setVisible(id,value);changed();},setLocked:(id,value)=>{state.setLocked(id,value);changed();},setOrder:ids=>{state.setOrder(ids);changed();},getState:()=>state.getState(),draw,refresh:render,destroy:()=>box?.remove()};
}
