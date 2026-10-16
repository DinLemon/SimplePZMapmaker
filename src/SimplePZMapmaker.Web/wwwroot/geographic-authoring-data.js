// Authoring geometry is deliberately separate from native project DTOs. A core
// adapter must acknowledge an applied draft before it leaves this collection.
export const draftLayers=Object.freeze({polygon:'zones',polyline:'paths',fence:'paths',smallBuilding:'buildings'});
const copy=value=>structuredClone(value),kinds=Object.keys(draftLayers),maxPoints=4096,maxDrafts=256;
const same=(a,b)=>a.x===b.x&&a.y===b.y;
function point(p){if(!p||![p.x,p.y].every(n=>Number.isFinite(n)&&Math.abs(n)<=2_000_000))throw Error('Draft coordinates must be finite map tiles.');return {x:p.x,y:p.y};}
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
function projection(p,a,b){const dx=b.x-a.x,dy=b.y-a.y,t=Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/(dx*dx+dy*dy||1)));return {x:a.x+t*dx,y:a.y+t*dy};}
export function snapDraftPoint(value,{paths=[],tolerance=.65,grid=1}={}){
 const p=point(value);if(!Number.isFinite(tolerance)||tolerance<0||!Number.isFinite(grid)||grid<=0)throw Error('Invalid draft snap settings.');
 // Prefer a nearby existing vertex so adjoining paths share exactly one node.
 let best=null,bestDistance=tolerance;
 for(const path of paths)for(const candidate of path.points??[]){const d=distance(p,candidate);if(d<=bestDistance){best=point(candidate);bestDistance=d;}}
 if(best)return best;
 for(const path of paths){const points=path.points??[];for(let i=1;i<points.length;i++){const candidate=projection(p,points[i-1],points[i]),d=distance(p,candidate);if(d<=bestDistance){best=candidate;bestDistance=d;}}}
 return best??{x:Math.round(p.x/grid)*grid,y:Math.round(p.y/grid)*grid};
}
const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
function onSegment(a,b,p){return Math.abs(cross(a,b,p))<1e-9&&p.x>=Math.min(a.x,b.x)-1e-9&&p.x<=Math.max(a.x,b.x)+1e-9&&p.y>=Math.min(a.y,b.y)-1e-9&&p.y<=Math.max(a.y,b.y)+1e-9;}
function intersects(a,b,c,d){return cross(a,b,c)*cross(a,b,d)<0&&cross(c,d,a)*cross(c,d,b)<0||onSegment(a,b,c)||onSegment(a,b,d)||onSegment(c,d,a)||onSegment(c,d,b);}
function validateShape(d){
 if(!d||!kinds.includes(d.kind)||typeof d.id!=='string'||!d.id||d.id.length>100||!Number.isInteger(d.level)||d.level< -32||d.level>31||d.memberId!==null&&(typeof d.memberId!=='string'||d.memberId.length>100)||!Array.isArray(d.points)||d.points.length>maxPoints)throw Error('Invalid geometry draft.');
 if(d.frame&&(!['worldX','worldY','width','height'].every(k=>Number.isInteger(d.frame[k])&&Math.abs(d.frame[k])<=2_000_000)||d.frame.width<1||d.frame.height<1))throw Error('Invalid draft map coordinate frame.');
 d.points.forEach(point);const min=d.kind==='polygon'?3:2;
 if(d.points.length<min)throw Error(`Add at least ${min} vertices before finishing.`);
 for(let i=1;i<d.points.length;i++)if(same(d.points[i-1],d.points[i]))throw Error('Consecutive draft vertices must be different.');
 if(d.kind==='polygon'){
  const p=d.points,n=p.length;
  for(let i=0;i<n;i++)for(let j=i+1;j<n;j++)if(j!==i+1&&!(i===0&&j===n-1)&&intersects(p[i],p[(i+1)%n],p[j],p[(j+1)%n]))throw Error('Polygon edges intersect. Move or remove the crossing vertex.');
  const area=p.reduce((sum,a,i)=>sum+a.x*p[(i+1)%n].y-p[(i+1)%n].x*a.y,0);if(Math.abs(area)<1e-9)throw Error('Polygon needs a nonzero area.');
 }
 if(d.kind==='smallBuilding'){
  if(d.points.length!==2)throw Error('A building rectangle needs exactly two opposite corners.');
  const [a,b]=d.points,rectangle={x:Math.min(a.x,b.x),y:Math.min(a.y,b.y),width:Math.abs(b.x-a.x),height:Math.abs(b.y-a.y)};
  if(!Object.values(rectangle).every(Number.isInteger)||rectangle.width<1||rectangle.height<1)throw Error('Building corners must enclose whole tiles.');
  if(d.rectangle&&Object.keys(rectangle).some(key=>d.rectangle[key]!==rectangle[key]))throw Error('Draft building rectangle disagrees with its corners.');d.rectangle=rectangle;
 }
 return d;
}
export function canApplyDraft(d,capabilities={}){
 validateShape(copy(d));const cap=capabilities?.[d.kind];
 if(d.kind==='smallBuilding'){
  if(!cap||!['minWidth','minHeight','maxWidth','maxHeight'].every(k=>Number.isInteger(cap[k])&&cap[k]>0))return {supported:false,reason:'Building apply needs the core footprint range. Keep or download this exact draft.'};
  const r=d.rectangle??validateShape(copy(d)).rectangle;
  if(r.width<cap.minWidth||r.height<cap.minHeight||r.width>cap.maxWidth||r.height>cap.maxHeight)return {supported:false,reason:`Core supports ${cap.minWidth}–${cap.maxWidth} × ${cap.minHeight}–${cap.maxHeight} tiles; this ${r.width} × ${r.height} footprint stays exact.`};
  return {supported:true,reason:'Exact footprint supported by the connected building adapter.'};
 }
 return cap?.version===1?{supported:true,reason:'The connected core adapter supports this geometry.'}:{supported:false,reason:`${d.kind==='fence'?'Fence edges and gates':'This geometry'} need a connected core adapter. Keep or download the draft.`};
}
export function validateDraftDocument(document){
 if(!document||document.format!=='SimplePZGeometryDrafts'||document.version!==1||!Array.isArray(document.requiredFeatures)||document.requiredFeatures.length!==1||document.requiredFeatures[0]!=='geometry-drafts-v1'||!Array.isArray(document.drafts)||document.drafts.length>maxDrafts)throw Error('Unsupported geometry draft format or version.');
 const result=copy(document),ids=new Set();for(const d of result.drafts){validateShape(d);if(ids.has(d.id))throw Error('Duplicate geometry draft identity.');ids.add(d.id);}return result;
}
export class GeometryDraftSession {
 constructor({canEdit=()=>true}={}){this.canEdit=canEdit;this.drafts=[];this.active=null;this.applying=false;}
 assertEditable(kind){if(this.applying)throw Error('Wait for the current draft apply to finish.');if(!this.canEdit(draftLayers[kind]))throw Error('The target layer is locked or hidden.');}
 start(kind,{memberId=null,level=0,frame}={}){if(!kinds.includes(kind))throw Error('Unknown geometry draft tool.');this.assertEditable(kind);if(this.active)throw Error('Finish or cancel the current draft first.');if(!Number.isInteger(level)||level< -32||level>31)throw Error('Select one floor from -32 through 31.');this.active={id:globalThis.crypto.randomUUID(),kind,memberId,level,points:[],...(frame?{frame:copy(frame)}:{})};return copy(this.active);}
 addPoint(value){if(!this.active)throw Error('Choose a drawing tool first.');this.assertEditable(this.active.kind);if(this.active.points.length>=maxPoints)throw Error('A draft can contain up to 4096 vertices.');if(this.active.kind==='smallBuilding'&&this.active.points.length===2)this.active.points[1]=point(value);else this.active.points.push(point(value));return copy(this.active);}
 movePoint(index,value){if(!this.active||!Number.isInteger(index)||!this.active.points[index])throw Error('Choose a draft vertex.');this.assertEditable(this.active.kind);this.active.points[index]=point(value);delete this.active.rectangle;return copy(this.active);}
 removeLastPoint(){if(!this.active)return;this.assertEditable(this.active.kind);this.active.points.pop();delete this.active.rectangle;}
 edit(id){const d=this.drafts.find(d=>d.id===id);if(!d)throw Error('Draft not found.');this.assertEditable(d.kind);if(this.active)throw Error('Finish or cancel the current draft first.');this.active=copy(d);return copy(d);}
 finish(){if(!this.active)throw Error('No geometry is being drawn.');this.assertEditable(this.active.kind);const d=copy(this.active);if(d.kind==='polygon'&&d.points.length>1&&same(d.points[0],d.points.at(-1)))d.points.pop();validateShape(d);const index=this.drafts.findIndex(x=>x.id===d.id);if(index<0){if(this.drafts.length>=maxDrafts)throw Error('Download or apply drafts before adding more.');this.drafts.push(d);}else this.drafts[index]=d;this.active=null;return copy(d);}
 cancel(){if(this.applying)throw Error('Wait for draft apply to finish.');this.active=null;}
 remove(id){const d=this.drafts.find(d=>d.id===id);if(d)this.assertEditable(d.kind);this.drafts=this.drafts.filter(d=>d.id!==id);if(this.active?.id===id)this.active=null;}
 getDrafts(){return copy(this.drafts);}
 assertNoPendingDrafts(){if(this.active||this.drafts.length||this.applying)throw Error('Geometry drafts are not part of map/native export. Apply or download and explicitly clear the drafts before saving or exporting.');}
 exportDrafts(){if(this.active)throw Error('Finish or cancel the active draft before downloading.');return JSON.stringify({format:'SimplePZGeometryDrafts',version:1,requiredFeatures:['geometry-drafts-v1'],drafts:this.getDrafts()},null,2);}
 importDrafts(document){if(this.active||this.applying)throw Error('Finish or cancel the current draft first.');const incoming=validateDraftDocument(document);const ids=new Set(this.drafts.map(d=>d.id));if(incoming.drafts.some(d=>ids.has(d.id)))throw Error('Duplicate geometry draft identity.');if(this.drafts.length+incoming.drafts.length>maxDrafts)throw Error('Too many geometry drafts.');this.drafts.push(...incoming.drafts);}
 async apply(id,capabilities,adapter){const d=this.drafts.find(d=>d.id===id);if(!d)throw Error('Draft not found.');if(this.active)throw Error('Finish or cancel the active draft first.');this.assertEditable(d.kind);const availability=canApplyDraft(d,capabilities);if(!availability.supported)throw Error(availability.reason);if(typeof adapter!=='function')throw Error('No connected core adapter for this draft.');this.applying=true;try{const result=await adapter(copy(d));if(result?.applied!==true)throw Error('Core did not acknowledge applying the draft. The draft is retained.');this.drafts=this.drafts.filter(x=>x.id!==id);return result;}finally{this.applying=false;}}
}
