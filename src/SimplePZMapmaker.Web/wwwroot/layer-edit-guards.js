const equal=(a,b)=>JSON.stringify(a??null)===JSON.stringify(b??null);
export const terrainLayer=value=>value===2?'roads':value===6?'paths':'terrain';
export function assertLayerChanges(before,after,changes,layers){
 const guard=id=>layers.guardEdit(id,()=>{});
 for(const [layer,keys] of Object.entries({buildings:['buildings','buildingLibrary'],decorations:['decorations'],models:['staticModels'],spawns:['spawns'],zones:['gameZones','roomRegionRules'],zombies:['zombieDensityRegions','zombieDensityState'],terrain:['surfaceTheme','surfaceRegions','surfaceTiles'],source:['sourceFeatures','sites']}))if(keys.some(k=>!equal(before[k],after[k])))guard(layer);
 const contents=(project,key)=>(project.buildings??[]).flatMap(b=>(b.design?.floors??[]).flatMap((f,z)=>(f[key]??[]).map(item=>({building:b.id,z,item}))));
 if(!equal(contents(before,'staticModels'),contents(after,'staticModels')))guard('models');
 if(!equal(contents(before,'objects'),contents(after,'objects')))guard('decorations');
 if(['fences','fenceStyles','gateStyles'].some(key=>!equal(before[key],after[key])))guard('paths');
 if(!equal(before.linearFeatures,after.linearFeatures)){guard('paths');if([...(before.linearFeatures??[]),...(after.linearFeatures??[])].some(f=>f.kind==='road'||f.kind==='bridge'))guard('roads');}
 const raster=(layer,start,old)=>{const now=after[layer];if(!now)return;for(let i=0;i<old.length;i++)if(old[i]!==now[start+i]){if(layer==='zombies'){guard('zombies');return;}guard(terrainLayer(old[i]));guard(terrainLayer(now[start+i]));}};
 if(changes)for(const change of changes.values())raster(change.layer,change.start,change.old);
 else for(const layer of ['terrain','zombies'])if(before[layer]&&before[layer]!==after[layer])raster(layer,0,before[layer]);
}
export function nativeLayer(node,project,shared=[]){
 if(node.type==='model')return 'models';
 if(node.layer===0)return terrainLayer(project.terrain?.[node.y*project.width+node.x]);
 if([...(project.buildings??[]),...shared].some(b=>node.x>=b.x&&node.y>=b.y&&node.x<=b.x+b.width&&node.y<=b.y+b.height))return 'buildings';
 if((project.decorations??[]).some(d=>d.parts.some(p=>d.x+p.dx===node.x&&d.y+p.dy===node.y)))return 'decorations';
 return node.z!==0?'paths':terrainLayer(project.terrain?.[node.y*project.width+node.x]);
}
export function nativeLayerClassifier(project,shared=[]){
 const fenceTiles=new Set((project.fenceStyles??[]).flatMap(s=>[s.w,s.n,s.nw,s.post,s.wAlt,s.nAlt,s.gateSpaceW,s.gateSpaceN]).filter(Boolean).concat((project.gateStyles??[]).flatMap(s=>[...s.closedW,...s.closedN,...s.openW,...s.openN])));
 const owner=new Uint8Array(project.width*project.height),write=(x,y,code)=>{if(x>=0&&y>=0&&x<project.width&&y<project.height)owner[y*project.width+x]=code;};
 for(const b of [...(project.buildings??[]),...shared]){
  if(!b.design){for(let y=Math.max(0,b.y);y<Math.min(project.height,b.y+b.height);y++)for(let x=Math.max(0,b.x);x<Math.min(project.width,b.x+b.width);x++)write(x,y,1);continue;}
  for(const floor of b.design.floors){for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)if(floor.cells[y*b.width+x]!=='.')write(b.x+x,b.y+y,1);for(const edge of floor.edges)write(b.x+edge.x,b.y+edge.y,1);}
 }
 for(const d of project.decorations??[])for(const part of d.parts)write(d.x+part.dx,d.y+part.dy,2);
 return node=>node.type==='model'?'models':fenceTiles.has(node.tile)?'paths':node.layer===0?terrainLayer(project.terrain[node.y*project.width+node.x]):owner[node.y*project.width+node.x]===1?'buildings':owner[node.y*project.width+node.x]===2?'decorations':node.z!==0?'paths':terrainLayer(project.terrain[node.y*project.width+node.x]);
}
