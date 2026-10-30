import {environmentPresets} from './appearance.js';
import {terrainLayer} from './layer-edit-guards.js';
import {buildingVisible} from './terrain-raster.js';
import {sharedLocalDecorations} from './world-protection.js';

// Coordinates remain relative to the active section. No giant MapProject, raster
// or compatibility-world JSON is constructed for a resident workspace.
export function residentSections({project,workspace}={}){
 if(!project||!workspace?.loadedSections?.size)return [];
 return [...workspace.loadedSections.values()].map(({member})=>{
  const p=member.id===workspace.memberId?project:member.project;
  return {id:member.id,project:p,x:(p.worldX-project.worldX)*256,y:(p.worldY-project.worldY)*256,width:p.width,height:p.height};
 });
}
export function residentBounds(sections){
 if(!sections.length)return null;
 let x=Infinity,y=Infinity,right=-Infinity,bottom=-Infinity;
 for(const s of sections){x=Math.min(x,s.x);y=Math.min(y,s.y);right=Math.max(right,s.x+s.width);bottom=Math.max(bottom,s.y+s.height);}
 return {x,y,width:right-x,height:bottom-y};
}
export function residentSectionAt(sections,p){return sections.find(s=>p.x>=s.x&&p.y>=s.y&&p.x<s.x+s.width&&p.y<s.y+s.height);}
export function residentFit(b,viewport,isometric=false){
 const width=isometric?b.width+b.height:b.width,height=isometric?(b.width+b.height)/2:b.height;
 const zoom=Math.max(.0001,Math.min(Math.max(1,viewport.width-72)/width,Math.max(1,viewport.height-90)/height)),cx=b.x+b.width/2,cy=b.y+b.height/2;
 return {zoom,pan:{x:viewport.width/2-(isometric?cx-cy:cx)*zoom,y:viewport.height/2-(isometric?(cx+cy)/2:cy)*zoom}};
}
export function rebaseSectionPan(pan,zoom,previous,next,isometric=false){
 const dx=(next.worldX-previous.worldX)*256,dy=(next.worldY-previous.worldY)*256;
 return {x:pan.x+(isometric?dx-dy:dx)*zoom,y:pan.y+(isometric?(dx+dy)/2:dy)*zoom};
}
const colors=Object.fromEntries(environmentPresets.map(p=>[p.id,p.colors.map(c=>[1,3,5].map(i=>parseInt(c.slice(i,i+2),16)))]));
export class ResidentWorldView{
 constructor(){this.rasters=new Map();this.owner=null;this.bytes=0;this.budget=16*1024*1024;}
 clear(){for(const r of this.rasters.values()){r.canvas.width=0;r.canvas.height=0;}this.rasters.clear();this.bytes=0;}
 invalidate(id){for(const [key,r] of this.rasters)if(r.id===id){r.canvas.width=0;this.rasters.delete(key);this.bytes-=r.bytes;}}
 raster(section,layers,zoom){
  const p=section.project,resolution=Math.max(8,Math.min(256,Math.ceil(Math.max(p.width,p.height)*zoom))),cacheKey=section.id+':'+resolution,key=[p.surfaceTheme,JSON.stringify(p.surfaceRegions??[]),...['terrain','roads','paths'].map(id=>layers.isVisible(id))].join('|');
  let r=this.rasters.get(cacheKey);
  if(r&&r.project===p&&r.key===key)return r.canvas;
  if(r){this.bytes-=r.bytes;r.canvas.width=0;this.rasters.delete(cacheKey);}
  const stride=Math.max(1,Math.ceil(Math.max(p.width,p.height)/resolution)),w=Math.ceil(p.width/stride),h=Math.ceil(p.height/stride),bytes=w*h*4;
  while(this.bytes+bytes>this.budget&&this.rasters.size){const [id,old]=this.rasters.entries().next().value;old.canvas.width=0;this.rasters.delete(id);this.bytes-=old.bytes;}
  const canvas=document.createElement('canvas');canvas.width=w;canvas.height=h;const ctx=canvas.getContext('2d'),image=ctx.createImageData(w,h);
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
   const px=x*stride,py=y*stride,t=p.terrain[py*p.width+px],i=(y*w+x)*4;
   let theme=p.surfaceTheme;for(const region of p.surfaceRegions??[])if(px>=region.x&&py>=region.y&&px<region.x+region.width&&py<region.y+region.height)theme=region.theme;
   const rgb=(colors[theme]??colors.legacy)[t]??colors.legacy[0];image.data.set([...rgb,layers.isVisible(terrainLayer(t))?255:0],i);
  }
  ctx.putImageData(image,0,0);r={id:section.id,project:p,key,canvas,bytes};this.rasters.set(cacheKey,r);this.bytes+=bytes;return canvas;
 }
 draw(ctx,context,{screen,zoom,viewport,layers,drawBuilding,minimap=false}={}){
  const owner=context.workspace?.loadedSections;if(owner!==this.owner){this.clear();this.owner=owner;}
  const sections=residentSections(context),visible=[];
  for(const s of sections){
   if(!buildingVisible(s,screen,viewport.width,viewport.height,context.transform?.isometric))continue;
   if(!minimap&&s.id===context.memberId){visible.push(s.id);continue;}
   visible.push(s.id);const p=s.project,shared=context.workspace.loadedSections.get(s.id)?.shared;
   ctx.save();const a=screen(s.x,s.y),b=screen(s.x+1,s.y),c=screen(s.x,s.y+1);
   ctx.transform(b.x-a.x,b.y-a.y,c.x-a.x,c.y-a.y,a.x,a.y);
   ctx.imageSmoothingEnabled=false;ctx.drawImage(this.raster(s,layers,zoom),0,0,s.width,s.height);
   ctx.beginPath();ctx.rect(0,0,s.width,s.height);ctx.clip();
   if(layers.isVisible('zombies'))for(let y=0;y<p.height/8;y++)for(let x=0;x<p.width/8;x++){const z=p.zombies[y*(p.width/8)+x];if(z){ctx.fillStyle=`rgba(171,53,32,${z/255*.7})`;ctx.fillRect(x*8,y*8,8,8);}}
   if(layers.isVisible('paths')){ctx.lineWidth=1/zoom;ctx.strokeStyle='#b9c8a1';const lines=[...(p.fences??[]),...(shared?.fences??[]).map(f=>({...f,points:f.points.map(v=>({x:v.x-p.worldX*256,y:v.y-p.worldY*256}))}))];for(const f of lines){ctx.beginPath();f.points.forEach((v,i)=>i?ctx.lineTo(v.x,v.y):ctx.moveTo(v.x,v.y));ctx.stroke();}}
   if(layers.isVisible('decorations')){ctx.fillStyle='#d5b575';for(const d of [...(p.decorations??[]),...sharedLocalDecorations(shared,p)])for(const part of d.parts??[])ctx.fillRect(d.x+part.dx,d.y+part.dy,Math.max(1,2/zoom),Math.max(1,2/zoom));}
   if(layers.isVisible('models')){ctx.fillStyle='#9fc7d6';for(const m of p.staticModels??[])ctx.fillRect(m.x,m.y,Math.max(1,2/zoom),Math.max(1,2/zoom));}
   ctx.restore();
   ctx.save();ctx.beginPath();for(const [i,[x,y]] of [[s.x,s.y],[s.x+s.width,s.y],[s.x+s.width,s.y+s.height],[s.x,s.y+s.height]].entries()){const q=screen(x,y);i?ctx.lineTo(q.x,q.y):ctx.moveTo(q.x,q.y);}ctx.closePath();ctx.clip();
   const buildings=[...(p.buildings??[]).map(b=>({...b,x:b.x+s.x,y:b.y+s.y})),...(shared?.buildings??[]).map(b=>({...b,x:b.x-context.project.worldX*256,y:b.y-context.project.worldY*256}))];
   if(layers.isVisible('buildings'))for(const b of buildings){if(!buildingVisible(b,screen,viewport.width,viewport.height,context.transform?.isometric))continue;if(drawBuilding&&!minimap)drawBuilding(b);else{const q=screen(b.x,b.y);ctx.fillStyle='#d5c8a4';ctx.fillRect(q.x,q.y,Math.max(1,b.width*zoom),Math.max(1,b.height*zoom));}}
   ctx.restore();
   if(!minimap&&layers.isVisible('spawns'))for(const spawn of p.spawns??[]){const q=screen(s.x+spawn.x,s.y+spawn.y);ctx.beginPath();ctx.arc(q.x,q.y,4,0,Math.PI*2);ctx.strokeStyle='#edb16b';ctx.stroke();}
  }
  return {sections,visible};
 }
}
