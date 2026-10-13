export function changeSummary(before,after,world=false){
 const count=p=>(p.buildings?.length||0)+(world?p.maps.reduce((n,m)=>n+m.project.buildings.length,0):0);
 const objects=value=>{const projects=world?value.maps.map(m=>m.project):[value];return [...(world?value.buildings||[]:[]),...projects.flatMap(p=>[...p.buildings,...p.staticModels||[],...p.decorations||[]]),...projects.flatMap(p=>p.buildings.flatMap(b=>b.design?.floors?.flatMap(f=>f.staticModels||[])||[]))];};
 const oldObjects=new Map(objects(before).map(o=>[o.id,o])),newObjects=new Map(objects(after).map(o=>[o.id,o]));
 const changes=[];for(const id of new Set([...oldObjects.keys(),...newObjects.keys()])){const old=oldObjects.get(id),next=newObjects.get(id);if(JSON.stringify(old)!==JSON.stringify(next))changes.push((!old?'Added: ':!next?'Removed: ':'Changed: ')+(next?.name||old?.name||next?.modelId||old?.modelId||next?.asset||old?.asset||id));}
 const result={buildingsBefore:count(before),buildingsAfter:count(after),sectionsBefore:before.maps?.length||1,sectionsAfter:after.maps?.length||1,terrainChanged:0,changedSections:0,changes:changes.slice(0,12),changedObjects:changes.length};
 if(world){const previous=new Map(before.maps.map(m=>[m.id,m]));for(const m of after.maps){const old=previous.get(m.id);if(!old||old.project.worldX!==m.project.worldX||old.project.worldY!==m.project.worldY||JSON.stringify(old.project.buildings)!==JSON.stringify(m.project.buildings))result.changedSections++;if(old)for(let i=0;i<m.project.terrain.length;i++)if(old.project.terrain[i]!==m.project.terrain[i])result.terrainChanged++;}}
 else for(let i=0;i<after.terrain.length;i++)if(before.terrain[i]!==after.terrain[i])result.terrainChanged++;
 return result;
}
export function drawAgentReview(canvas,before,after,world=false){
 const ctx=canvas.getContext('2d');canvas.width=640;canvas.height=240;ctx.fillStyle='#162119';ctx.fillRect(0,0,640,240);
 function draw(value,offset,label){
  ctx.fillStyle='#d8d3b5';ctx.font='14px Segoe UI';ctx.fillText(label,offset+12,20);const maps=world?value.maps.map(m=>m.project):[value];
  const minX=world?Math.min(...maps.map(p=>p.worldX*256)):0,minY=world?Math.min(...maps.map(p=>p.worldY*256)):0,maxX=world?Math.max(...maps.map(p=>(p.worldX*256+p.width))):value.width,maxY=world?Math.max(...maps.map(p=>p.worldY*256+p.height)):value.height;
  const scale=Math.min(290/Math.max(1,maxX-minX),190/Math.max(1,maxY-minY));
  for(const p of maps){const x=world?p.worldX*256-minX:0,y=world?p.worldY*256-minY:0;ctx.fillStyle='#6d8053';ctx.fillRect(offset+12+x*scale,34+y*scale,p.width*scale,p.height*scale);
   // Bounded preview reads samples directly; never allocate a world-size image.
   const step=Math.max(1,Math.ceil(Math.max(p.width,p.height)/100));for(let yy=0;yy<p.height;yy+=step)for(let xx=0;xx<p.width;xx+=step){const t=p.terrain[yy*p.width+xx];if(t===2||t===3){ctx.fillStyle=t===2?'#ada78e':'#568ead';ctx.fillRect(offset+12+(x+xx)*scale,34+(y+yy)*scale,Math.max(1,step*scale),Math.max(1,step*scale));}}
   ctx.fillStyle='#eac48b';for(const b of p.buildings)ctx.fillRect(offset+12+(x+b.x)*scale,34+(y+b.y)*scale,Math.max(1,b.width*scale),Math.max(1,b.height*scale));
   ctx.fillStyle='#9bc5e2';for(const model of p.staticModels||[])ctx.fillRect(offset+12+(x+model.x)*scale,34+(y+model.y)*scale,3,3);for(const prop of p.decorations||[])ctx.fillRect(offset+12+(x+prop.x)*scale,34+(y+prop.y)*scale,2,2);
  }
  if(world){ctx.fillStyle='#e7a876';for(const b of value.buildings||[])ctx.fillRect(offset+12+(b.x-minX)*scale,34+(b.y-minY)*scale,Math.max(1,b.width*scale),Math.max(1,b.height*scale));}
 }
 draw(before,0,'Current project');draw(after,320,'Proposed version');
}
