import {kinds,occupied,occupiedIndices,containsPoint,validateDesign,combineAttribution,minLevel,usesAssets,usesNativeModelBehavior,validateParts,tileId} from './building.js';
import {validateDependencies,validateModel,decodeTerrainEdits,encodeTerrainEdits,markTerrainEdit} from './resource-data.js';
export function blank(size=256){return {version:4,worldSection:false,terrainEdits:'',name:'Новая карта',width:size,height:size,seed:42,worldX:100,worldY:100,terrain:Array(size*size).fill(0),zombies:Array(size*size/64).fill(0),buildings:[],buildingLibrary:[],decorations:[],staticModels:[],requiredMods:[],assetSources:{},surfaceTiles:{},surfaceTheme:'legacy',surfaceRegions:[],spawns:[{x:size/2,y:size/2}],attribution:[],bounds:null,metresPerTile:null,importSummary:null};}
export function geographicBase(project,size,keep){if(keep)return project;const result=blank(size);result.surfaceTheme=project.surfaceTheme??'legacy';result.buildingLibrary=structuredClone(project.buildingLibrary||[]);result.attribution=combineAttribution(project.attribution,...result.buildingLibrary.map(b=>b.attribution||[]));return result;}
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
  if(terrain===3&&(history?.pending?history.blocksWater(i):p.buildings.some(b=>containsPoint(b,xx-b.x,yy-b.y))||p.spawns.some(s=>s.x===xx&&s.y===yy)||(p.staticModels||[]).some(m=>m.x===xx&&m.y===yy)||(p.decorations||[]).some(d=>d.parts.some(part=>d.x+part.dx===xx&&d.y+part.dy===yy))))continue;
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
 if(!p||![1,2,3,4,5,6,7].includes(p.version)||!Number.isInteger(p.width)||!Number.isInteger(p.height)||p.width<256||p.height<256||p.width>2048||p.height>2048||p.width%256||p.height%256)fail('Неподдерживаемый размер или версия проекта.');
 decodeTerrainEdits(p);if(p.version<6&&(p.terrainEdits?.length||p.worldSection)||p.worldSection!==undefined&&typeof p.worldSection!=='boolean')fail('Ручная земля и участки мира требуют формат6.');
 validateDependencies(p.requiredMods??[],p.assetSources??{});const models=p.staticModels??[];if(!Array.isArray(models)||models.length>10000)fail('Повреждён слой моделей.');const modelIds=new Set();for(const m of models){validateModel(m,p.width,p.height);if(modelIds.has(m.id)||p.terrain[m.y*p.width+m.x]===3||p.buildings.some(b=>containsPoint(b,m.x-b.x,m.y-b.y))||p.spawns.some(s=>s.x===m.x&&s.y===m.y))fail('Модель пересекается с водой, зданием или появлением.');modelIds.add(m.id);}if(p.version<6&&((p.requiredMods?.length||0)||Object.keys(p.assetSources||{}).length||models.length||[...p.buildings,...(p.buildingLibrary||[])].some(b=>b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length))))fail('Моды и модели требуют формат проекта версии6.');
 if(typeof p.name!=='string'||!p.name.trim()||p.name.length>80||/[\x00-\x1f\x7f]/.test(p.name))fail('Проверьте название карты.');
 if(!Number.isInteger(p.seed)||p.seed< -2147483648||p.seed>2147483647)fail('Seed должен быть целым числом от −2147483648 до 2147483647.');
 if(p.bounds!==null&&p.bounds!==undefined){const b=p.bounds;if(![b.south,b.north,b.west,b.east].every(Number.isFinite)||b.south< -85||b.north>85||b.west< -180||b.east>180||b.south>=b.north||b.west>=b.east||b.north-b.south>.08||b.east-b.west>.12)fail('Географические границы проекта повреждены или слишком велики.');}
 if(p.metresPerTile!==undefined&&p.metresPerTile!==null&&(!Number.isFinite(p.metresPerTile)||p.metresPerTile<=0||p.metresPerTile>100000))fail('Масштаб геоданных повреждён.');
 if(p.importSummary!==undefined&&p.importSummary!==null){const s=p.importSummary;if(!s||![s.sourceFeatures,s.sourceBuildings,s.placedBuildings].every(Number.isInteger)||s.sourceFeatures<0||s.sourceFeatures>30000||s.sourceBuildings<0||s.sourceBuildings>30000||s.placedBuildings<0||s.placedBuildings>4000||!s.skipReasons||typeof s.skipReasons!=='object'||Array.isArray(s.skipReasons)||Object.keys(s.skipReasons).length>20||Object.entries(s.skipReasons).some(([k,v])=>k.length>100||!Number.isInteger(v)||v<0||v>30000))fail('Отчёт импорта повреждён.');}
 if(!Number.isInteger(p.worldX)||!Number.isInteger(p.worldY)||p.worldX<0||p.worldY<0||p.worldX>1000||p.worldY>1000)fail('Координаты мира должны быть от 0 до 1000.');
 if(!Array.isArray(p.terrain)||p.terrain.length!==p.width*p.height||p.terrain.some(t=>!Number.isInteger(t)||t<0||t>6))fail('Слой земли повреждён.');
 if(!Array.isArray(p.zombies)||p.zombies.length!==p.width*p.height/64||p.zombies.some(v=>!Number.isInteger(v)||v<0||v>255))fail('Слой зомби повреждён.');
 if(!Array.isArray(p.buildings)||p.buildings.length>4000||!Array.isArray(p.spawns)||p.spawns.length<(p.worldSection?0:1)||p.spawns.length>100||!Array.isArray(p.attribution)||p.attribution.length>30||p.attribution.some(a=>typeof a!=='string'||a.length>1000))fail('Объекты проекта повреждены.');
 const library=p.buildingLibrary??[];if(!Array.isArray(library)||library.length>64)fail('Библиотека зданий повреждена.');for(const b of library)validateDesign(b);
 if(p.version===1&&(library.length||p.buildings.some(b=>b?.design)))fail('Этажи и шаблоны требуют формат проекта версии 2.');
 if(p.version<3&&[...p.buildings,...library].some(b=>minLevel(b)!==0))fail('Подвалы требуют формат проекта версии 3.');
 const regions=p.surfaceRegions??[];if(!Array.isArray(regions)||regions.length>128||regions.some(r=>!r||![r.x,r.y,r.width,r.height].every(Number.isInteger)||r.x<0||r.y<0||r.width<1||r.height<1||r.x+r.width>p.width||r.y+r.height>p.height||!['legacy','temperate','rural','urban','dry'].includes(r.theme))||p.version<5&&regions.length)fail('Участки оформления повреждены или требуют формат5.');
 const theme=p.surfaceTheme??'legacy',surface=p.surfaceTiles??{},decor=p.decorations??[];if(!['legacy','temperate','rural','urban','dry'].includes(theme)||typeof surface!=='object'||!surface||Array.isArray(surface)||Object.keys(surface).length>100000||Object.entries(surface).some(([i,id])=>!/^\d+$/.test(i)||+i>=p.terrain.length||!tileId(id))||!Array.isArray(decor)||decor.length>10000)fail('Поверхности или декор повреждены.');
 if(p.version<4&&(theme!=='legacy'||Object.keys(surface).length||decor.length||[...p.buildings,...library].some(usesAssets)))fail('Оформление и ресурсы требуют формат проекта версии 4.');
 const decorationIds=new Set(),decorationCells=new Set();for(const d of decor){if(!d||typeof d.id!=='string'||!d.id||d.id.length>100||decorationIds.has(d.id)||![d.x,d.y].every(Number.isInteger))fail('Декор повреждён.');decorationIds.add(d.id);validateParts(d.asset,d.parts);for(const part of d.parts){const x=d.x+part.dx,y=d.y+part.dy,index=y*p.width+x;if(x<0||y<0||x>=p.width||y>=p.height||p.terrain[index]===3||p.buildings.some(b=>containsPoint(b,x-b.x,y-b.y))||p.spawns.some(s=>s.x===x&&s.y===y)||decorationCells.has(index))fail('Декор пересекается с водой или объектами.');decorationCells.add(index);}}
 if(p.importSummary?.notes!==undefined&&(!Array.isArray(p.importSummary.notes)||p.importSummary.notes.length>20||p.importSummary.notes.some(s=>typeof s!=='string'||s.length>1000)))fail('Примечания импорта повреждены.');
 if([...p.buildings,...library].reduce((sum,b)=>sum+(b?.design?.floors?.reduce((n,f)=>n+(f?.cells?.length||0),0)||0),0)>20_000_000)fail('Слишком много клеток в этажах.');
 const ids=new Set();for(const b of p.buildings){validateDesign(b);if(!b||typeof b.id!=='string'||!b.id||ids.has(b.id)||typeof b.name!=='string'||b.name.length>100||b.sourceId?.length>100||b.sourceKind?.length>100||!['inferred','osm-levels','osm-height','manual'].includes(b.levelsOrigin??'inferred')||!canPlace(p,b))fail('Здание повреждено, пересекается с другим или стоит на воде.');ids.add(b.id);}
 for(const s of p.spawns)if(!s||!Number.isInteger(s.x)||!Number.isInteger(s.y)||s.x<0||s.y<0||s.x>=p.width||s.y>=p.height||p.terrain[s.y*p.width+s.x]===3||p.buildings.some(b=>containsPoint(b,s.x-b.x,s.y-b.y)))fail('Точка появления должна стоять на земле снаружи здания.');
 return p;
}
const meta=p=>structuredClone({version:p.version,worldSection:p.worldSection??false,terrainEdits:p.terrainEdits??'',buildings:p.buildings,buildingLibrary:p.buildingLibrary??[],decorations:p.decorations??[],staticModels:p.staticModels??[],requiredMods:p.requiredMods??[],assetSources:p.assetSources??{},seed:p.seed,bounds:p.bounds??null,metresPerTile:p.metresPerTile??null,importSummary:p.importSummary??null,surfaceTiles:p.surfaceTiles??{},surfaceTheme:p.surfaceTheme??'legacy',surfaceRegions:p.surfaceRegions??[],spawns:p.spawns,attribution:p.attribution,name:p.name,worldX:p.worldX,worldY:p.worldY});
function objectsDiff(before,after){const a=new Map(before.map(b=>[b.id,b])),b=new Map(after.map(b=>[b.id,b]));return [...new Set([...a.keys(),...b.keys()])].filter(id=>JSON.stringify(a.get(id))!==JSON.stringify(b.get(id))).map(id=>({id,before:a.get(id)??null,after:b.get(id)??null}));}
function slimMeta(p){return {version:p.version,worldSection:p.worldSection,terrainEdits:p.terrainEdits,name:p.name,worldX:p.worldX,worldY:p.worldY,seed:p.seed,bounds:p.bounds,metresPerTile:p.metresPerTile,importSummary:p.importSummary,requiredMods:p.requiredMods,assetSources:p.assetSources,spawns:p.spawns,attribution:p.attribution,surfaceTheme:p.surfaceTheme,surfaceRegions:p.surfaceRegions??[],buildingOrder:p.buildings.map(b=>b.id),libraryOrder:p.buildingLibrary.map(b=>b.id),decorationOrder:p.decorations.map(b=>b.id),modelOrder:p.staticModels.map(m=>m.id)};}
export class History{
 constructor(){this.past=[];this.future=[];this.pending=null;}
 checkpoint(p,next){validateProject(next);if(p.width!==next.width||p.height!==next.height)throw Error('Для карты другого размера откройте новый проект.');this.commit(p);this.begin(p);for(const layer of ['terrain','zombies'])for(let start=0;start<p[layer].length;start+=4096){if(p[layer].slice(start,start+4096).some((value,i)=>value!==next[layer][start+i]))this.touch(layer,start);}Object.assign(p,structuredClone(next));this.commit(p);}
 begin(p){if(!this.pending)this.pending={before:meta(p),changes:new Map(),project:p};}
 editTerrain(index){if(!this.pending)return;const p=this.pending.project;if(index<0||index>=p.width*p.height)throw Error('Правка земли выходит за карту.');this.pending.terrainEdits??=decodeTerrainEdits(p);this.pending.terrainEdits[index>>3]|=1<<(index&7);p.version=Math.max(6,p.version);}
 touch(layer,index){
  if(!this.pending)return;const start=Math.floor(index/4096)*4096,key=start+(layer==='zombies'?2**24:0);
  if(!this.pending.changes.has(key))this.pending.changes.set(key,{layer,start,old:Uint8Array.from(this.pending.project[layer].slice(start,start+4096))});
 }
 blocksWater(index){
  if(!this.pending)return false;const p=this.pending.project;
  if(this.extraBlockedWater?.(index))return true;
  if(!this.pending.waterMask){const mask=new Uint8Array(p.terrain.length);for(const b of p.buildings)for(const i of occupiedIndices(b))mask[(b.y+Math.floor(i/(b.width+1)))*p.width+b.x+i%(b.width+1)]=1;for(const d of p.decorations||[])for(const part of d.parts)mask[(d.y+part.dy)*p.width+d.x+part.dx]=1;for(const m of p.staticModels||[])mask[m.y*p.width+m.x]=1;for(const s of p.spawns)mask[s.y*p.width+s.x]=1;this.pending.waterMask=mask;}
  return this.pending.waterMask[index]===1;
 }
 get retainedBytes(){return [...this.past,...this.future].reduce((sum,item)=>sum+item.bytes,0);}
 commit(p){if(!this.pending)return;if((p.staticModels||[]).some(m=>(m.profile||'pickup')!=='pickup')||[...p.buildings,...(p.buildingLibrary||[])].some(usesNativeModelBehavior))p.version=Math.max(7,p.version);const pending=this.pending;this.pending=null;if(pending.terrainEdits)p.terrainEdits=encodeTerrainEdits(pending.terrainEdits);const after=meta(p);const item={before:slimMeta(pending.before),after:slimMeta(after),buildings:objectsDiff(pending.before.buildings,after.buildings),library:objectsDiff(pending.before.buildingLibrary,after.buildingLibrary),decorations:objectsDiff(pending.before.decorations,after.decorations),models:objectsDiff(pending.before.staticModels,after.staticModels),surface:[...new Set([...Object.keys(pending.before.surfaceTiles),...Object.keys(after.surfaceTiles)])].filter(i=>pending.before.surfaceTiles[i]!==after.surfaceTiles[i]).map(i=>({i,before:pending.before.surfaceTiles[i]??null,after:after.surfaceTiles[i]??null})),changes:[]};
  for(const c of pending.changes.values()){const value=Uint8Array.from(p[c.layer].slice(c.start,c.start+c.old.length));if(value.some((v,i)=>v!==c.old[i]))item.changes.push({...c,value});}
  if(!item.changes.length&&!item.buildings.length&&!item.library.length&&!item.decorations.length&&!item.models.length&&!item.surface.length&&JSON.stringify(item.before)===JSON.stringify(item.after))return;
  item.bytes=item.changes.reduce((sum,c)=>sum+c.old.byteLength+c.value.byteLength,0)+(JSON.stringify(item.before).length+JSON.stringify(item.after).length+JSON.stringify(item.buildings).length+JSON.stringify(item.library).length+JSON.stringify(item.decorations).length+JSON.stringify(item.models).length+JSON.stringify(item.surface).length)*2;
  this.past.push(item);this.future=[];while(this.past.length>1&&(this.past.length>30||this.retainedBytes>64*1024*1024))this.past.shift();
 }
 apply(p,item,reverse){for(const c of item.changes){const bytes=reverse?c.old:c.value;for(let i=0;i<bytes.length;i++)p[c.layer][c.start+i]=bytes[i];}const m=structuredClone(reverse?item.before:item.after);for(const [key,diffs,order] of [['buildings',item.buildings,m.buildingOrder],['buildingLibrary',item.library,m.libraryOrder],['decorations',item.decorations,m.decorationOrder],['staticModels',item.models,m.modelOrder]]){const values=new Map((p[key]??[]).map(b=>[b.id,b]));for(const change of diffs){const value=reverse?change.before:change.after;if(value===null)values.delete(change.id);else values.set(change.id,structuredClone(value));}p[key]=order.map(id=>values.get(id));}p.surfaceTiles??={};for(const c of item.surface){const value=reverse?c.before:c.after;if(value===null)delete p.surfaceTiles[c.i];else p.surfaceTiles[c.i]=value;}delete m.buildingOrder;delete m.libraryOrder;delete m.decorationOrder;delete m.modelOrder;Object.assign(p,m);}
 undo(p){this.commit(p);const item=this.past.pop();if(!item)return false;this.apply(p,item,true);this.future.push(item);return true;}
 redo(p){this.commit(p);const item=this.future.pop();if(!item)return false;this.apply(p,item,false);this.past.push(item);return true;}
}
