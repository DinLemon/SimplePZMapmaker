import {cell,objectCells,refreshEdges,validateDesign,normalizeBuilding,minLevel,roomNames} from './building.js';
import {placementCheck,nearestEdge} from './visual-workshop.js';
import {modelCells} from './resource-data.js';

const clone=value=>structuredClone(value);
const surfaceTools=new Set(['room','footprint','erase-floor','floor']);
const edgeTools=new Set(['wall','door','window','open']);
const footprint=b=>JSON.stringify([b.x,b.y,b.width,b.height,b.design?.minLevel??0,b.design?.floors.map(f=>f.cells.replace(/[0-9a-z]/g,'#'))]);

// Synchronous, detached draft transaction. Access/connectivity and catalog checks
// still belong to Apply/preview; structural and complete-footprint checks run here.
// A callback may replace the candidate (module/shape controls), but never the source.
export function transactBuildingEdit(original,mutate){
 const candidate=normalizeBuilding(clone(original)),replacement=mutate(candidate);
 if(replacement?.then)throw Error('Building edit transactions must be synchronous. Resolve native resources before editing.');
 const result=normalizeBuilding(replacement===undefined||replacement===candidate?candidate:clone(replacement));
 result.modified=true;result.levelsOrigin='manual';
 if((original.sourceOutline?.length||original.footprintOrigin==='source-grid')&&footprint(original)!==footprint(result))result.footprintOrigin='manual-source-edit';
 validateDesign(result,false);
 return result;
}

function editPoint(point){
 if(!point||![point.x,point.y].every(Number.isInteger)||![point.fx??point.x+.5,point.fy??point.y+.5].every(Number.isFinite))throw Error('Choose a finite building tile.');
 return {...point,fx:point.fx??point.x+.5,fy:point.fy??point.y+.5};
}

// Mutates a transaction candidate. Existing native callers also use this helper
// to keep an absolute building anchor stable while extending north or west.
export function expandBuildingFootprint(b,point){
 const p=editPoint(point),dx=Math.max(0,-p.x),dy=Math.max(0,-p.y),width=Math.max(b.width+dx,p.x+dx+1),height=Math.max(b.height+dy,p.y+dy+1);
 if(width>64||height>64)throw Error('A building can be at most 64 × 64 tiles.');
 if(width===b.width&&height===b.height)return p;
 for(const f of b.design.floors){
  const cells=Array(width*height).fill('.');
  for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)cells[(y+dy)*width+x+dx]=f.cells[y*b.width+x];
  f.cells=cells.join('');
  f.floorTiles=Object.fromEntries(Object.entries(f.floorTiles??{}).map(([i,id])=>[(Math.floor(+i/b.width)+dy)*width+(+i%b.width)+dx,id]));
  for(const edge of f.edges){edge.x+=dx;edge.y+=dy;}
  for(const item of [...f.objects,...f.staticModels??[]]){item.x+=dx;item.y+=dy;}
 }
 b.x-=dx;b.y-=dy;b.width=width;b.height=height;
 return {...p,x:p.x+dx,y:p.y+dy,fx:p.fx+dx,fy:p.fy+dy};
}

function roomCode(f,{room='livingroom',roomCode:code}){
 if(code!==undefined){if(!Object.hasOwn(f.rooms,code)||!Object.hasOwn(roomNames,f.rooms[code]))throw Error('Choose a room type.');return code;}
 if(!Object.hasOwn(roomNames,room))throw Error('Choose a room type.');
 const existing=Object.keys(f.rooms).find(k=>f.rooms[k]===room);if(existing!==undefined)return existing;
 code=[...'0123456789abcdefghijklmnopqrstuvwxyz'].find(k=>!Object.hasOwn(f.rooms,k));
 if(!code||Object.keys(f.rooms).length>=35)throw Error('A floor can contain at most 35 rooms.');
 f.rooms[code]=room;return code;
}

function paintSurface(b,index,p,end,operation){
 const f=b.design.floors[index],{tool,brush={}}=operation;
 if([p,end].some(q=>q.x<0||q.y<0||q.x>=b.width||q.y>=b.height))throw Error('Select a tile within the building floor.');
 if(tool==='floor'&&!brush.id)throw Error('Choose a floor resource in the palette.');
 const chars=f.cells.split('');let code;
 for(let y=Math.min(p.y,end.y);y<=Math.max(p.y,end.y);y++)for(let x=Math.min(p.x,end.x);x<=Math.max(p.x,end.x);x++){
  const i=y*b.width+x;
  if(tool==='floor'||tool==='room')if(chars[i]==='.')throw Error(tool==='floor'?'Add a floor tile before applying its surface.':'Use Add floor to expand the footprint.');
  if(tool==='floor'){(f.floorTiles??={})[i]=brush.id;continue;}
  if(tool==='erase-floor'){
   if(f.objects.some(o=>objectCells(o).some(c=>c.x===x&&c.y===y))||(f.staticModels??[]).some(m=>modelCells(m).some(c=>c.x===x&&c.y===y)))throw Error('Remove the object, model, or staircase before removing its floor.');
   chars[i]='.';if(f.floorTiles)delete f.floorTiles[i];
  }else chars[i]=code??=roomCode(f,operation);
 }
 f.cells=chars.join('');
 if(tool!=='floor'){b.design.shape='custom';refreshEdges(b,index);}
}

// Common mutable primitive for one tile or a rectangular surface operation.
// Use editBuilding for standalone edits, or enclose a batch in transactBuildingEdit.
// requireResource=false retains the workshop's authored style/material brushes.
export function applyBuildingEdit(b,operation){
 const {tool,level,brush={},requireResource=false}=operation,index=level-minLevel(b),f=b.design?.floors[index];
 if(!Number.isInteger(level)||!f)throw Error('This building has no floor at the selected level.');
 let p=editPoint(operation.point),end=operation.end?editPoint(operation.end):p;
 if(surfaceTools.has(tool)){
  if(tool==='footprint'&&operation.expandFootprint){
   // Extend north/west first, translate the area, then extend south/east.
   const oldX=b.x,oldY=b.y;
   expandBuildingFootprint(b,{x:Math.min(p.x,end.x),y:Math.min(p.y,end.y)});
   const dx=oldX-b.x,dy=oldY-b.y;
   p={...p,x:p.x+dx,y:p.y+dy,fx:p.fx+dx,fy:p.fy+dy};end={...end,x:end.x+dx,y:end.y+dy,fx:end.fx+dx,fy:end.fy+dy};
   expandBuildingFootprint(b,{x:Math.max(p.x,end.x),y:Math.max(p.y,end.y)});
  }
  paintSurface(b,index,p,end,operation);
 }else if(edgeTools.has(tool)){
  const e=nearestEdge(p);
  if(cell(b,f,e.x,e.y)==='.'&&cell(b,f,e.x-(e.edge==='w'?1:0),e.y-(e.edge==='n'?1:0))==='.')throw Error('Place walls and openings on an existing floor edge.');
  if(tool!=='open'&&requireResource&&!brush[e.edge])throw Error('Choose a matching wall or opening resource.');
  const old=f.edges.find(edge=>edge.x===e.x&&edge.y===e.y&&edge.edge===e.edge),next={...e,kind:tool,edited:true};
  // Converting a solid wall or a different opening must discard its old frame.
  if(old?.kind===tool&&tool!=='open'){if(old.wallTile)next.wallTile=old.wallTile;if(tool!=='wall'&&old.openingTile)next.openingTile=old.openingTile;}
  if(old?.overlayTile&&tool!=='open'){next.overlayTile=old.overlayTile;if(old.overlayLevelOffset)next.overlayLevelOffset=old.overlayLevelOffset;}
  if(tool!=='open'&&brush[e.edge])next[tool==='wall'?'wallTile':'openingTile']=brush[e.edge];
  if(old)f.edges[f.edges.indexOf(old)]=next;else f.edges.push(next);
 }else if(['object','stairs','furniture'].includes(tool)){
  if(tool==='object'&&!brush.parts?.length)throw Error('Choose an item in the palette.');
  const o=tool==='object'?{x:p.x,y:p.y,kind:'asset',direction:brush.facing==='W'?'w':'n',style:'',asset:brush.id,parts:clone(brush.parts)}:{x:p.x,y:p.y,kind:tool==='stairs'?'stairs':brush.kind,direction:brush.direction??'n',style:tool==='stairs'?'':b.design.style||''};
  const check=placementCheck(b,index,o);if(!check.ok)throw Error(check.reason);f.objects.push(o);
 }else if(tool==='model'){
  if(!brush.modelId)throw Error('Choose a registered model in the palette.');
  (f.staticModels??=[]).push({...clone(brush),id:crypto.randomUUID().replaceAll('-',''),x:p.x,y:p.y,z:level});
 }else if(tool==='erase-object'){
  const object=f.objects.findLast(o=>objectCells(o).some(c=>c.x===p.x&&c.y===p.y)),model=f.staticModels?.findLast(m=>modelCells(m).some(c=>c.x===p.x&&c.y===p.y));
  if(model)f.staticModels=f.staticModels.filter(m=>m!==model);else if(object)f.objects=f.objects.filter(o=>o!==object);else throw Error('There is no object at this tile.');
 }else throw Error('Choose a supported building edit tool.');
 return b;
}

export function editBuilding(original,operation){return transactBuildingEdit(original,candidate=>applyBuildingEdit(candidate,operation));}
