import {cell,objectCells,objectParts,floorLevel,validateDesign} from './building.js';
import {modelCells,blockingModel} from './resource-data.js';

// Diagnostics remain separate from drafts. Nothing is painted into saved terrain or resource layers.
export function buildingDiagnostics(b){
 if(!b?.design)return [];const issues=[],add=(index,points,message)=>{const cells=points.filter(p=>Number.isInteger(p.x)&&Number.isInteger(p.y)&&p.x>=0&&p.y>=0&&p.x<b.width&&p.y<b.height);issues.push({level:floorLevel(b,index),floor:index,cells,message});};
 for(let index=0;index<b.design.floors.length;index++){
  const f=b.design.floors[index],occupied=new Map();
  for(const object of f.objects){const points=objectCells(object);if(points.some(p=>cell(b,f,p.x,p.y)==='.'))add(index,points,'Object footprint extends beyond floor.');
   if(object.kind==='stairs'){const upper=b.design.floors[index+1];if(!upper||cell(b,upper,points[0].x,points[0].y)==='.'||cell(b,f,points.at(-1).x,points.at(-1).y)==='.')add(index,points,'Stairs need a lower landing and an upper anchor.');for(const p of points.slice(1))if(f.edges.some(e=>e.x===p.x&&e.y===p.y&&e.edge===object.direction&&e.kind!=='open'))add(index,points,'Wall or opening crosses the stair run.');}
   else{for(const p of points){const key=p.x+','+p.y;if(occupied.has(key))add(index,[p],'Furniture footprints overlap.');occupied.set(key,object);}if(f.edges.some(e=>['door','open'].includes(e.kind)&&points.some(p=>p.x===e.x&&p.y===e.y||e.edge==='n'&&p.x===e.x&&p.y===e.y-1||e.edge==='w'&&p.x===e.x-1&&p.y===e.y)))add(index,points,'Furniture blocks an opening.');}
  }
  for(const model of (f.staticModels||[]).filter(blockingModel))for(const p of modelCells(model)){if(occupied.has(p.x+','+p.y))add(index,[p],'Blocking model overlaps furniture.');if(f.edges.some(e=>['door','open'].includes(e.kind)&&(e.x===p.x&&e.y===p.y||e.edge==='n'&&e.x===p.x&&e.y-1===p.y||e.edge==='w'&&e.x-1===p.x&&e.y===p.y)))add(index,[p],'Blocking model occupies a door or passage.');occupied.set(p.x+','+p.y,model);}
 }
 // Reachability flood-fill uses explicit walls, signed levels and native blocking model profiles.
 const labels=[],components=[],links=[],roots=new Set(),ground=-(b.design.minLevel||0),edgeKey=(x,y,edge)=>`${x},${y},${edge}`;
 for(let index=0;index<b.design.floors.length;index++){
  const f=b.design.floors[index],map=new Int32Array(b.width*b.height).fill(-1),walls=new Set(f.edges.filter(e=>['wall','window'].includes(e.kind)).map(e=>edgeKey(e.x,e.y,e.edge))),blocked=new Set((f.staticModels||[]).filter(blockingModel).flatMap(modelCells).map(m=>m.y*b.width+m.x));for(const o of f.objects.filter(o=>o.kind!=='stairs'))for(const part of objectParts(o))if(((part.flags??(o.kind==='asset'?0:1))&1)!==0)blocked.add((o.y+part.dy)*b.width+o.x+part.dx);for(const m of f.staticModels||[])if(m.profile==='door'&&m.door?.locked&&!m.door.open)walls.add(edgeKey(m.x,m.y,m.door.direction));for(const o of f.objects.filter(o=>o.kind==='stairs'))for(const p of objectCells(o).slice(1,4))blocked.add(p.y*b.width+p.x);const below=b.design.floors[index-1];for(const o of below?.objects?.filter(o=>o.kind==='stairs')||[])for(const p of objectCells(o).slice(1,4))blocked.add(p.y*b.width+p.x);labels.push(map);
  for(let start=0;start<map.length;start++){if(f.cells[start]==='.'||map[start]>=0||blocked.has(start))continue;const component=components.length,queue=[start],cells=[];components.push({floor:index,cells});links.push([]);map[start]=component;
   for(let q=0;q<queue.length;q++){const current=queue[q],x=current%b.width,y=Math.floor(current/b.width);cells.push({x,y});for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){const nx=x+dx,ny=y+dy;if(cell(b,f,nx,ny)==='.')continue;const next=ny*b.width+nx;if(map[next]>=0||blocked.has(next)||walls.has(edgeKey(dx?Math.max(x,nx):x,dy?Math.max(y,ny):y,dx?'w':'n')))continue;map[next]=component;queue.push(next);}}
  }
 }
 const g=b.design.floors[ground];for(const e of g?.edges.filter(e=>['door','open'].includes(e.kind))||[]){const nx=e.x-(e.edge==='w'?1:0),ny=e.y-(e.edge==='n'?1:0),a=cell(b,g,e.x,e.y),other=cell(b,g,nx,ny);if((a==='.')===(other==='.'))continue;if((g.staticModels||[]).some(m=>m.profile==='door'&&m.door?.locked&&!m.door.open&&m.x===e.x&&m.y===e.y&&m.door.direction===e.edge))continue;const label=labels[ground][(a==='.'?ny:e.y)*b.width+(a==='.'?nx:e.x)];if(label>=0)roots.add(label);}
 for(let index=0;index<b.design.floors.length-1;index++)for(const stair of b.design.floors[index].objects.filter(o=>o.kind==='stairs')){const first=stair.y*b.width+stair.x,last=(stair.y+(stair.direction==='n'?4:0))*b.width+stair.x+(stair.direction==='w'?4:0),lower=labels[index][last],upper=labels[index+1][first];if(lower>=0&&upper>=0){links[lower].push(upper);links[upper].push(lower);}}
 const seen=new Set(roots),queue=[...roots];for(let q=0;q<queue.length;q++)for(const next of links[queue[q]])if(!seen.has(next)){seen.add(next);queue.push(next);}
 for(let component=0;component<components.length;component++)if(!seen.has(component)){const value=components[component];add(value.floor,value.cells,roots.size?'Area has no route to an exterior ground-level entrance.':'Add an exterior entrance on ground level (z=0).');}
 try{validateDesign(b);}catch(e){if(!issues.length)add(ground,[],e.message);}
 return issues;
}
