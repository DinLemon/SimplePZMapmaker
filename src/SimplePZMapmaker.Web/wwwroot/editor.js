import {validateLinearFeatures} from './linear-editing.js';
import {usesExtendedZ} from './z-levels.js';
import {validateGameZones} from './gameplay-data.js';
import {validatePalettes} from './palette-data.js';
import {kinds,occupied,occupiedIndices,containsPoint,validateDesign,combineAttribution,minLevel,usesAssets,usesNativeModelBehavior,usesInteractiveAuthoring,validateParts,tileId} from './building.js';
import {validateDependencies,validateModel,validateModelProject,modelCells,blockingModel,interactiveModel,decodeTerrainEdits,encodeTerrainEdits,markTerrainEdit} from './resource-data.js';
export function blank(size=256){return {version:4,worldSection:false,terrainEdits:'',name:'New map',width:size,height:size,seed:42,worldX:100,worldY:100,terrain:Array(size*size).fill(0),zombies:Array(size*size/64).fill(0),buildings:[],buildingLibrary:[],resourcePalettes:[],linearFeatures:[],gameZones:[],decorations:[],staticModels:[],requiredMods:[],assetSources:{},surfaceTiles:{},surfaceTheme:'legacy',surfaceRegions:[],spawns:[{x:size/2,y:size/2}],attribution:[],bounds:null,metresPerTile:null,importSummary:null};}
export const usesInfrastructure=p=>(p.linearFeatures??[]).some(f=>(f.level??0)!==0||(f.access??'none')!=='none'||f.guardRails||(f.supportSpacing??0)!==0||f.groupId!=null);
export function mapAuthoringVersion(p){const buildings=[...(p.buildings??[]),...(p.buildingLibrary??[])];return Math.max(p.version??1,usesInfrastructure(p)?11:buildings.some(usesExtendedZ)?10:(p.linearFeatures?.length||p.gameZones?.length||(p.staticModels??[]).some(interactiveModel)||buildings.some(usesInteractiveAuthoring))?9:p.resourcePalettes?.length?8:1);}
export function geographicBase(project,size,keep){if(keep)return project;const result=blank(size);result.surfaceTheme=project.surfaceTheme??'legacy';result.buildingLibrary=structuredClone(project.buildingLibrary||[]);result.resourcePalettes=structuredClone(project.resourcePalettes||[]);result.version=mapAuthoringVersion(result);result.attribution=combineAttribution(project.attribution,...result.buildingLibrary.map(b=>b.attribution||[]));return result;}
export function boundsFromSearchResult(item){
 if(!Array.isArray(item?.boundingbox)||item.boundingbox.length!==4)return null;
 const [south,north,west,east]=item.boundingbox.map(Number);
 if(![south,north,west,east].every(Number.isFinite)||south< -85||north>85||west< -180||east>180||south>=north||west>=east||north-south>.08||east-west>.12)return null;
 return {south,north,west,east};
}
export function paint(p,x,y,terrain,radius=1,history){
 for(let yy=Math.max(0,y-radius+1);yy<=Math.min(p.height-1,y+radius-1);yy++)for(let xx=Math.max(0,x-radius+1);xx<=Math.min(p.width-1,x+radius-1);xx++){
  if((xx-x)**2+(yy-y)**2>radius**2)continue;
  const i=yy*p.width+xx;
  if(terrain===3&&(history?.pending?history.blocksWater(i):p.buildings.some(b=>containsPoint(b,xx-b.x,yy-b.y))||p.spawns.some(s=>s.x===xx&&s.y===yy)||(p.staticModels||[]).some(m=>modelCells(m).some(c=>c.x===xx&&c.y===yy))||(p.decorations||[]).some(d=>d.parts.some(part=>d.x+part.dx===xx&&d.y+part.dy===yy))))continue;
  if(p.surfaceTiles)delete p.surfaceTiles[i];if(history?.pending)history.editTerrain(i);else markTerrainEdit(p,i);if(p.terrain[i]===terrain)continue;history?.touch('terrain',i,p.terrain[i]);p.terrain[i]=terrain;
 }
}
export function line(p,x0,y0,x1,y1,terrain,radius=1,history){
 const n=Math.max(Math.abs(x1-x0),Math.abs(y1-y0));for(let i=0;i<=n;i++){const t=n?i/n:0;paint(p,Math.round(x0+(x1-x0)*t),Math.round(y0+(y1-y0)*t),terrain,radius,history);}
}
export function canPlace(p,b){
 if(!b||!Object.hasOwn(kinds,b.kind)||![b.x,b.y,b.width,b.height].every(Number.isInteger)||b.width<6||b.height<6||b.width>64||b.height>64||b.x<1||b.y<1||b.x+b.width>=p.width||b.y+b.height>=p.height)return false;
 const points=occupiedIndices(b),global=new Set([...points].map(i=>(b.y+Math.floor(i/(b.width+1)))*p.width+b.x+i%(b.width+1)));
 if(p.buildings.some(a=>a!==b&&b.x<a.x+a.width+1&&b.x+b.width+1>a.x&&b.y<a.y+a.height+1&&b.y+b.height+1>a.y&&[...occupiedIndices(a)].some(i=>global.has((a.y+Math.floor(i/(a.width+1)))*p.width+a.x+i%(a.width+1)))))return false;
 if((p.staticModels||[]).some(m=>global.has(m.y*p.width+m.x)))return false;
 if((p.decorations||[]).some(d=>d.parts.some(part=>global.has((d.y+part.dy)*p.width+d.x+part.dx))))return false;
 if(p.spawns.some(s=>global.has(s.y*p.width+s.x)))return false;
 for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)if(occupied(b,x,y)&&p.terrain[(b.y+y)*p.width+b.x+x]===3)return false;
 return true;
}
export function validateProject(p){
 const fail=m=>{throw Error(m);};
 if(!p||![1,2,3,4,5,6,7,8,9,10,11].includes(p.version)||!Number.isInteger(p.width)||!Number.isInteger(p.height)||p.width<256||p.height<256||p.width>2048||p.height>2048||p.width%256||p.height%256)fail('Unsupported project size or version.');
 validateGameZones(p);validateLinearFeatures(p);validateModelProject(p);
 validatePalettes(p.resourcePalettes===undefined?[]:p.resourcePalettes);if(p.resourcePalettes?.length&&p.version<8)fail('Resource palettes require project format 8.');
 decodeTerrainEdits(p);if(p.version<6&&(p.terrainEdits?.length||p.worldSection)||p.worldSection!==undefined&&typeof p.worldSection!=='boolean')fail('Manual terrain edits and world sections require format 6.');
 validateDependencies(p.requiredMods??[],p.assetSources??{});const models=p.staticModels??[];if(!Array.isArray(models)||models.length>10000)fail('The model layer is damaged.');const modelIds=new Set();for(const m of models){validateModel(m,p.width,p.height);if(modelIds.has(m.id)||modelCells(m).some(c=>p.terrain[c.y*p.width+c.x]===3||p.buildings.some(b=>containsPoint(b,c.x-b.x,c.y-b.y))||p.spawns.some(s=>s.x===c.x&&s.y===c.y)))fail('The model overlaps water, a building, or a spawn point.');modelIds.add(m.id);}if(p.version<6&&((p.requiredMods?.length||0)||Object.keys(p.assetSources||{}).length||models.length||[...p.buildings,...(p.buildingLibrary||[])].some(b=>b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length))))fail('Mods and models require project format version 6.');
 if(typeof p.name!=='string'||!p.name.trim()||p.name.length>80||/[\x00-\x1f\x7f]/.test(p.name))fail('Check the map name.');
 if(!Number.isInteger(p.seed)||p.seed< -2147483648||p.seed>2147483647)fail('Seed must be an integer between −2147483648 and 2147483647.');
 if(p.bounds!==null&&p.bounds!==undefined){const b=p.bounds;if(![b.south,b.north,b.west,b.east].every(Number.isFinite)||b.south< -85||b.north>85||b.west< -180||b.east>180||b.south>=b.north||b.west>=b.east||b.north-b.south>.08||b.east-b.west>.12)fail('The geographic boundaries of the project are damaged or too large.');}
 if(p.metresPerTile!==undefined&&p.metresPerTile!==null&&(!Number.isFinite(p.metresPerTile)||p.metresPerTile<=0||p.metresPerTile>100000))fail('The geodata scale is damaged.');
 if(p.importSummary!==undefined&&p.importSummary!==null){const s=p.importSummary;if(!s||![s.sourceFeatures,s.sourceBuildings,s.placedBuildings].every(Number.isInteger)||s.sourceFeatures<0||s.sourceFeatures>30000||s.sourceBuildings<0||s.sourceBuildings>30000||s.placedBuildings<0||s.placedBuildings>4000||!s.skipReasons||typeof s.skipReasons!=='object'||Array.isArray(s.skipReasons)||Object.keys(s.skipReasons).length>20||Object.entries(s.skipReasons).some(([k,v])=>k.length>100||!Number.isInteger(v)||v<0||v>30000))fail('The import report is corrupted.');}
 if(!Number.isInteger(p.worldX)||!Number.isInteger(p.worldY)||p.worldX<0||p.worldY<0||p.worldX>1000||p.worldY>1000)fail('World coordinates must be from 0 to 1000.');
 if(!Array.isArray(p.terrain)||p.terrain.length!==p.width*p.height||p.terrain.some(t=>!Number.isInteger(t)||t<0||t>6))fail('The ground layer is damaged.');
 if(!Array.isArray(p.zombies)||p.zombies.length!==p.width*p.height/64||p.zombies.some(v=>!Number.isInteger(v)||v<0||v>255))fail('The zombie layer is damaged.');
 if(!Array.isArray(p.buildings)||p.buildings.length>4000||!Array.isArray(p.spawns)||p.spawns.length<(p.worldSection?0:1)||p.spawns.length>100||!Array.isArray(p.attribution)||p.attribution.length>30||p.attribution.some(a=>typeof a!=='string'||a.length>1000))fail('Project objects are damaged.');
 const library=p.buildingLibrary??[];if(!Array.isArray(library)||library.length>64)fail('The building library is damaged.');for(const b of library)validateDesign(b);
 if(p.version===1&&(library.length||p.buildings.some(b=>b?.design)))fail('Floors and templates require project format version 2.');
 if(p.version<3&&[...p.buildings,...library].some(b=>minLevel(b)!==0))fail('Basements require project format version 3.');
 const regions=p.surfaceRegions??[];if(!Array.isArray(regions)||regions.length>128||regions.some(r=>!r||![r.x,r.y,r.width,r.height].every(Number.isInteger)||r.x<0||r.y<0||r.width<1||r.height<1||r.x+r.width>p.width||r.y+r.height>p.height||!['legacy','temperate','rural','urban','dry'].includes(r.theme))||p.version<5&&regions.length)fail('Design areas are damaged or require format5.');
 const theme=p.surfaceTheme??'legacy',surface=p.surfaceTiles??{},decor=p.decorations??[];if(!['legacy','temperate','rural','urban','dry'].includes(theme)||typeof surface!=='object'||!surface||Array.isArray(surface)||Object.keys(surface).length>100000||Object.entries(surface).some(([i,id])=>!/^\d+$/.test(i)||+i>=p.terrain.length||!tileId(id))||!Array.isArray(decor)||decor.length>10000)fail('Surfaces or decor are damaged.');
 if(p.version<4&&(theme!=='legacy'||Object.keys(surface).length||decor.length||[...p.buildings,...library].some(usesAssets)))fail('Appearance and resources require project format version 4.');
 const decorationIds=new Set(),decorationCells=new Set();for(const d of decor){if(!d||typeof d.id!=='string'||!d.id||d.id.length>100||decorationIds.has(d.id)||![d.x,d.y].every(Number.isInteger))fail('Decor is damaged.');decorationIds.add(d.id);validateParts(d.asset,d.parts);for(const part of d.parts){const x=d.x+part.dx,y=d.y+part.dy,index=y*p.width+x;if(x<0||y<0||x>=p.width||y>=p.height||p.terrain[index]===3||p.buildings.some(b=>containsPoint(b,x-b.x,y-b.y))||p.spawns.some(s=>s.x===x&&s.y===y)||decorationCells.has(index))fail('The decor intersects with water or objects.');decorationCells.add(index);}}
 const blockingCells=new Set();for(const m of models.filter(blockingModel))for(const c of modelCells(m)){const index=c.y*p.width+c.x;if(blockingCells.has(index)||decorationCells.has(index))fail('A blocking model footprint overlaps another blocker or decoration.');blockingCells.add(index);}for(const m of models.filter(m=>m.door)){const x=m.x-(m.door.direction==='w'?1:0),y=m.y-(m.door.direction==='n'?1:0);if(x<0||y<0||blockingCells.has(m.y*p.width+m.x)||blockingCells.has(y*p.width+x)||decorationCells.has(m.y*p.width+m.x)||decorationCells.has(y*p.width+x)||p.terrain[y*p.width+x]===3||p.buildings.some(b=>containsPoint(b,x-b.x,y-b.y)))fail('A model door needs a clear approach on both sides of its edge.');}
 if(p.importSummary?.notes!==undefined&&(!Array.isArray(p.importSummary.notes)||p.importSummary.notes.length>20||p.importSummary.notes.some(s=>typeof s!=='string'||s.length>1000)))fail('Import notes are corrupted.');
 if([...p.buildings,...library].reduce((sum,b)=>sum+(b?.design?.floors?.reduce((n,f)=>n+(f?.cells?.length||0),0)||0),0)>20_000_000)fail('There are too many tiles on the floors.');
 const ids=new Set();for(const b of p.buildings){validateDesign(b);if(!b||typeof b.id!=='string'||!b.id||ids.has(b.id)||typeof b.name!=='string'||b.name.length>100||b.sourceId?.length>100||b.sourceKind?.length>100||!['inferred','osm-levels','osm-height','manual'].includes(b.levelsOrigin??'inferred')||!canPlace(p,b))fail('The building is damaged, intersects with another, or sits on water.');ids.add(b.id);}
 for(const s of p.spawns)if(!s||!Number.isInteger(s.x)||!Number.isInteger(s.y)||s.x<0||s.y<0||s.x>=p.width||s.y>=p.height||p.terrain[s.y*p.width+s.x]===3||p.buildings.some(b=>containsPoint(b,s.x-b.x,s.y-b.y)))fail('The spawn point should be on the ground outside the building.');
 return p;
}
const meta=p=>structuredClone({version:p.version,worldSection:p.worldSection??false,terrainEdits:p.terrainEdits??'',buildings:p.buildings,buildingLibrary:p.buildingLibrary??[],resourcePalettes:p.resourcePalettes??[],linearFeatures:p.linearFeatures??[],gameZones:p.gameZones??[],decorations:p.decorations??[],staticModels:p.staticModels??[],requiredMods:p.requiredMods??[],assetSources:p.assetSources??{},seed:p.seed,bounds:p.bounds??null,metresPerTile:p.metresPerTile??null,importSummary:p.importSummary??null,surfaceTiles:p.surfaceTiles??{},surfaceTheme:p.surfaceTheme??'legacy',surfaceRegions:p.surfaceRegions??[],spawns:p.spawns,attribution:p.attribution,name:p.name,worldX:p.worldX,worldY:p.worldY});
function objectsDiff(before,after){const a=new Map(before.map(b=>[b.id,b])),b=new Map(after.map(b=>[b.id,b]));return [...new Set([...a.keys(),...b.keys()])].filter(id=>JSON.stringify(a.get(id))!==JSON.stringify(b.get(id))).map(id=>({id,before:a.get(id)??null,after:b.get(id)??null}));}
function slimMeta(p){return {version:p.version,worldSection:p.worldSection,terrainEdits:p.terrainEdits,name:p.name,worldX:p.worldX,worldY:p.worldY,seed:p.seed,resourcePalettes:p.resourcePalettes??[],linearFeatures:p.linearFeatures??[],gameZones:p.gameZones??[],bounds:p.bounds,metresPerTile:p.metresPerTile,importSummary:p.importSummary,requiredMods:p.requiredMods,assetSources:p.assetSources,spawns:p.spawns,attribution:p.attribution,surfaceTheme:p.surfaceTheme,surfaceRegions:p.surfaceRegions??[],buildingOrder:p.buildings.map(b=>b.id),libraryOrder:p.buildingLibrary.map(b=>b.id),decorationOrder:p.decorations.map(b=>b.id),modelOrder:p.staticModels.map(m=>m.id)};}
export class History{
 constructor(){this.past=[];this.future=[];this.pending=null;}
 checkpoint(p,next){validateProject(next);if(p.width!==next.width||p.height!==next.height)throw Error('Open a new project to use a different map size.');this.commit(p);this.begin(p);for(const layer of ['terrain','zombies'])for(let start=0;start<p[layer].length;start+=4096){if(p[layer].slice(start,start+4096).some((value,i)=>value!==next[layer][start+i]))this.touch(layer,start);}Object.assign(p,structuredClone(next));this.commit(p);}
 begin(p){if(!this.pending)this.pending={before:meta(p),changes:new Map(),project:p};}
 editTerrain(index){if(!this.pending)return;const p=this.pending.project;if(index<0||index>=p.width*p.height)throw Error('Terrain editing goes beyond the map.');this.pending.terrainEdits??=decodeTerrainEdits(p);this.pending.terrainEdits[index>>3]|=1<<(index&7);p.version=Math.max(6,p.version);}
 touch(layer,index){
  if(!this.pending)return;const start=Math.floor(index/4096)*4096,key=start+(layer==='zombies'?2**24:0);
  if(!this.pending.changes.has(key))this.pending.changes.set(key,{layer,start,old:Uint8Array.from(this.pending.project[layer].slice(start,start+4096))});
 }
 blocksWater(index){
  if(!this.pending)return false;const p=this.pending.project;
  if(this.extraBlockedWater?.(index))return true;
  if(!this.pending.waterMask){const mask=new Uint8Array(p.terrain.length);for(const b of p.buildings)for(const i of occupiedIndices(b))mask[(b.y+Math.floor(i/(b.width+1)))*p.width+b.x+i%(b.width+1)]=1;for(const d of p.decorations||[])for(const part of d.parts)mask[(d.y+part.dy)*p.width+d.x+part.dx]=1;for(const m of p.staticModels||[])for(const c of modelCells(m))mask[c.y*p.width+c.x]=1;for(const s of p.spawns)mask[s.y*p.width+s.x]=1;this.pending.waterMask=mask;}
  return this.pending.waterMask[index]===1;
 }
 get retainedBytes(){return [...this.past,...this.future].reduce((sum,item)=>sum+item.bytes,0);}
 commit(p){if(!this.pending)return;p.version=mapAuthoringVersion(p);if((p.staticModels||[]).some(m=>(m.profile||'pickup')!=='pickup')||[...p.buildings,...(p.buildingLibrary||[])].some(usesNativeModelBehavior))p.version=Math.max(7,p.version);const pending=this.pending;this.pending=null;if(pending.terrainEdits)p.terrainEdits=encodeTerrainEdits(pending.terrainEdits);const after=meta(p);const item={before:slimMeta(pending.before),after:slimMeta(after),buildings:objectsDiff(pending.before.buildings,after.buildings),library:objectsDiff(pending.before.buildingLibrary,after.buildingLibrary),decorations:objectsDiff(pending.before.decorations,after.decorations),models:objectsDiff(pending.before.staticModels,after.staticModels),surface:[...new Set([...Object.keys(pending.before.surfaceTiles),...Object.keys(after.surfaceTiles)])].filter(i=>pending.before.surfaceTiles[i]!==after.surfaceTiles[i]).map(i=>({i,before:pending.before.surfaceTiles[i]??null,after:after.surfaceTiles[i]??null})),changes:[]};
  for(const c of pending.changes.values()){const value=Uint8Array.from(p[c.layer].slice(c.start,c.start+c.old.length));if(value.some((v,i)=>v!==c.old[i]))item.changes.push({...c,value});}
  if(!item.changes.length&&!item.buildings.length&&!item.library.length&&!item.decorations.length&&!item.models.length&&!item.surface.length&&JSON.stringify(item.before)===JSON.stringify(item.after))return;
  item.bytes=item.changes.reduce((sum,c)=>sum+c.old.byteLength+c.value.byteLength,0)+(JSON.stringify(item.before).length+JSON.stringify(item.after).length+JSON.stringify(item.buildings).length+JSON.stringify(item.library).length+JSON.stringify(item.decorations).length+JSON.stringify(item.models).length+JSON.stringify(item.surface).length)*2;
  this.past.push(item);this.future=[];while(this.past.length>1&&(this.past.length>30||this.retainedBytes>64*1024*1024))this.past.shift();
 }
 apply(p,item,reverse){for(const c of item.changes){const bytes=reverse?c.old:c.value;for(let i=0;i<bytes.length;i++)p[c.layer][c.start+i]=bytes[i];}const m=structuredClone(reverse?item.before:item.after);for(const [key,diffs,order] of [['buildings',item.buildings,m.buildingOrder],['buildingLibrary',item.library,m.libraryOrder],['decorations',item.decorations,m.decorationOrder],['staticModels',item.models,m.modelOrder]]){const values=new Map((p[key]??[]).map(b=>[b.id,b]));for(const change of diffs){const value=reverse?change.before:change.after;if(value===null)values.delete(change.id);else values.set(change.id,structuredClone(value));}p[key]=order.map(id=>values.get(id));}p.surfaceTiles??={};for(const c of item.surface){const value=reverse?c.before:c.after;if(value===null)delete p.surfaceTiles[c.i];else p.surfaceTiles[c.i]=value;}delete m.buildingOrder;delete m.libraryOrder;delete m.decorationOrder;delete m.modelOrder;Object.assign(p,m);}
 undo(p){this.commit(p);const item=this.past.pop();if(!item)return false;this.apply(p,item,true);this.future.push(item);return true;}
 redo(p){this.commit(p);const item=this.future.pop();if(!item)return false;this.apply(p,item,false);this.past.push(item);return true;}
}
