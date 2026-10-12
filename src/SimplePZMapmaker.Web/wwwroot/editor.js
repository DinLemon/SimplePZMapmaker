import {kinds,occupied,occupiedIndices,containsPoint,validateDesign,combineAttribution} from './building.js';
export function blank(size=256){return {version:2,name:'Новая карта',width:size,height:size,seed:42,worldX:100,worldY:100,terrain:Array(size*size).fill(0),zombies:Array(size*size/64).fill(0),buildings:[],buildingLibrary:[],spawns:[{x:size/2,y:size/2}],attribution:[],bounds:null};}
export function geographicBase(project,size,keep){if(keep)return project;const result=blank(size);result.buildingLibrary=structuredClone(project.buildingLibrary||[]);result.attribution=combineAttribution(project.attribution,...result.buildingLibrary.map(b=>b.attribution||[]));return result;}
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
  if(terrain===3&&(history?.pending?history.blocksWater(i):p.buildings.some(b=>containsPoint(b,xx-b.x,yy-b.y))||p.spawns.some(s=>s.x===xx&&s.y===yy)))continue;
  if(p.terrain[i]===terrain)continue;history?.touch('terrain',i,p.terrain[i]);p.terrain[i]=terrain;
 }
}
export function line(p,x0,y0,x1,y1,terrain,radius=1,history){
 const n=Math.max(Math.abs(x1-x0),Math.abs(y1-y0));for(let i=0;i<=n;i++){const t=n?i/n:0;paint(p,Math.round(x0+(x1-x0)*t),Math.round(y0+(y1-y0)*t),terrain,radius,history);}
}
export function canPlace(p,b){
 if(!b||!Object.hasOwn(kinds,b.kind)||![b.x,b.y,b.width,b.height].every(Number.isInteger)||b.width<6||b.height<6||b.width>64||b.height>64||b.x<1||b.y<1||b.x+b.width>=p.width||b.y+b.height>=p.height||Math.floor(b.x/256)!==Math.floor((b.x+b.width)/256)||Math.floor(b.y/256)!==Math.floor((b.y+b.height)/256))return false;
 const points=occupiedIndices(b),global=new Set([...points].map(i=>(b.y+Math.floor(i/(b.width+1)))*p.width+b.x+i%(b.width+1)));
 if(p.buildings.some(a=>a!==b&&b.x<a.x+a.width+1&&b.x+b.width+1>a.x&&b.y<a.y+a.height+1&&b.y+b.height+1>a.y&&[...occupiedIndices(a)].some(i=>global.has((a.y+Math.floor(i/(a.width+1)))*p.width+a.x+i%(a.width+1)))))return false;
 if(p.spawns.some(s=>global.has(s.y*p.width+s.x)))return false;
 for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)if(occupied(b,x,y)&&p.terrain[(b.y+y)*p.width+b.x+x]===3)return false;
 return true;
}
export function validateProject(p){
 const fail=m=>{throw Error(m);};
 if(!p||![1,2].includes(p.version)||!Number.isInteger(p.width)||!Number.isInteger(p.height)||p.width<256||p.height<256||p.width>2048||p.height>2048||p.width%256||p.height%256)fail('Неподдерживаемый размер или версия проекта.');
 if(typeof p.name!=='string'||!p.name.trim()||p.name.length>80||/[\x00-\x1f\x7f]/.test(p.name))fail('Проверьте название карты.');
 if(!Number.isInteger(p.seed)||p.seed< -2147483648||p.seed>2147483647)fail('Seed должен быть целым числом от −2147483648 до 2147483647.');
 if(p.bounds!==null&&p.bounds!==undefined){const b=p.bounds;if(![b.south,b.north,b.west,b.east].every(Number.isFinite)||b.south< -85||b.north>85||b.west< -180||b.east>180||b.south>=b.north||b.west>=b.east||b.north-b.south>.08||b.east-b.west>.12)fail('Географические границы проекта повреждены или слишком велики.');}
 if(p.metresPerTile!==undefined&&p.metresPerTile!==null&&(!Number.isFinite(p.metresPerTile)||p.metresPerTile<=0||p.metresPerTile>100000))fail('Масштаб геоданных повреждён.');
 if(p.importSummary!==undefined&&p.importSummary!==null){const s=p.importSummary;if(!s||![s.sourceFeatures,s.sourceBuildings,s.placedBuildings].every(Number.isInteger)||s.sourceFeatures<0||s.sourceFeatures>30000||s.sourceBuildings<0||s.sourceBuildings>30000||s.placedBuildings<0||s.placedBuildings>4000||!s.skipReasons||typeof s.skipReasons!=='object'||Array.isArray(s.skipReasons)||Object.keys(s.skipReasons).length>20||Object.entries(s.skipReasons).some(([k,v])=>k.length>100||!Number.isInteger(v)||v<0||v>30000))fail('Отчёт импорта повреждён.');}
 if(!Number.isInteger(p.worldX)||!Number.isInteger(p.worldY)||p.worldX<0||p.worldY<0||p.worldX>1000||p.worldY>1000)fail('Координаты мира должны быть от 0 до 1000.');
 if(!Array.isArray(p.terrain)||p.terrain.length!==p.width*p.height||p.terrain.some(t=>!Number.isInteger(t)||t<0||t>6))fail('Слой земли повреждён.');
 if(!Array.isArray(p.zombies)||p.zombies.length!==p.width*p.height/64||p.zombies.some(v=>!Number.isInteger(v)||v<0||v>255))fail('Слой зомби повреждён.');
 if(!Array.isArray(p.buildings)||p.buildings.length>4000||!Array.isArray(p.spawns)||p.spawns.length<1||p.spawns.length>100||!Array.isArray(p.attribution)||p.attribution.length>30||p.attribution.some(a=>typeof a!=='string'||a.length>1000))fail('Объекты проекта повреждены.');
 const library=p.buildingLibrary??[];if(!Array.isArray(library)||library.length>64)fail('Библиотека зданий повреждена.');for(const b of library)validateDesign(b);
 if(p.version===1&&(library.length||p.buildings.some(b=>b?.design)))fail('Этажи и шаблоны требуют формат проекта версии 2.');
 if(p.importSummary?.notes!==undefined&&(!Array.isArray(p.importSummary.notes)||p.importSummary.notes.length>20||p.importSummary.notes.some(s=>typeof s!=='string'||s.length>1000)))fail('Примечания импорта повреждены.');
 if([...p.buildings,...library].reduce((sum,b)=>sum+(b?.design?.floors?.reduce((n,f)=>n+(f?.cells?.length||0),0)||0),0)>20_000_000)fail('Слишком много клеток в этажах.');
 const ids=new Set();for(const b of p.buildings){validateDesign(b);if(!b||typeof b.id!=='string'||!b.id||ids.has(b.id)||typeof b.name!=='string'||b.name.length>100||b.sourceId?.length>100||b.sourceKind?.length>100||!['inferred','osm-levels','osm-height','manual'].includes(b.levelsOrigin??'inferred')||!canPlace(p,b))fail('Здание повреждено, пересекается с другим или стоит на воде.');ids.add(b.id);}
 for(const s of p.spawns)if(!s||!Number.isInteger(s.x)||!Number.isInteger(s.y)||s.x<0||s.y<0||s.x>=p.width||s.y>=p.height||p.terrain[s.y*p.width+s.x]===3||p.buildings.some(b=>containsPoint(b,s.x-b.x,s.y-b.y)))fail('Точка появления должна стоять на земле снаружи здания.');
 return p;
}
const meta=p=>structuredClone({version:p.version,buildings:p.buildings,buildingLibrary:p.buildingLibrary??[],spawns:p.spawns,attribution:p.attribution,name:p.name,worldX:p.worldX,worldY:p.worldY});
function objectsDiff(before,after){const a=new Map(before.map(b=>[b.id,b])),b=new Map(after.map(b=>[b.id,b]));return [...new Set([...a.keys(),...b.keys()])].filter(id=>JSON.stringify(a.get(id))!==JSON.stringify(b.get(id))).map(id=>({id,before:a.get(id)??null,after:b.get(id)??null}));}
function slimMeta(p){return {version:p.version,name:p.name,worldX:p.worldX,worldY:p.worldY,spawns:p.spawns,attribution:p.attribution,buildingOrder:p.buildings.map(b=>b.id),libraryOrder:p.buildingLibrary.map(b=>b.id)};}
export class History{
 constructor(){this.past=[];this.future=[];this.pending=null;}
 begin(p){if(!this.pending)this.pending={before:meta(p),changes:new Map(),project:p};}
 touch(layer,index){
  if(!this.pending)return;const start=Math.floor(index/4096)*4096,key=start+(layer==='zombies'?2**24:0);
  if(!this.pending.changes.has(key))this.pending.changes.set(key,{layer,start,old:Uint8Array.from(this.pending.project[layer].slice(start,start+4096))});
 }
 blocksWater(index){
  if(!this.pending)return false;const p=this.pending.project;
  if(!this.pending.waterMask){const mask=new Uint8Array(p.terrain.length);for(const b of p.buildings)for(const i of occupiedIndices(b))mask[(b.y+Math.floor(i/(b.width+1)))*p.width+b.x+i%(b.width+1)]=1;for(const s of p.spawns)mask[s.y*p.width+s.x]=1;this.pending.waterMask=mask;}
  return this.pending.waterMask[index]===1;
 }
 get retainedBytes(){return [...this.past,...this.future].reduce((sum,item)=>sum+item.bytes,0);}
 commit(p){if(!this.pending)return;const pending=this.pending;this.pending=null;const after=meta(p);const item={before:slimMeta(pending.before),after:slimMeta(after),buildings:objectsDiff(pending.before.buildings,after.buildings),library:objectsDiff(pending.before.buildingLibrary,after.buildingLibrary),changes:[]};
  for(const c of pending.changes.values()){const value=Uint8Array.from(p[c.layer].slice(c.start,c.start+c.old.length));if(value.some((v,i)=>v!==c.old[i]))item.changes.push({...c,value});}
  if(!item.changes.length&&!item.buildings.length&&!item.library.length&&JSON.stringify(item.before)===JSON.stringify(item.after))return;
  item.bytes=item.changes.reduce((sum,c)=>sum+c.old.byteLength+c.value.byteLength,0)+(JSON.stringify(item.before).length+JSON.stringify(item.after).length+JSON.stringify(item.buildings).length+JSON.stringify(item.library).length)*2;
  this.past.push(item);this.future=[];while(this.past.length>30||this.retainedBytes>64*1024*1024)this.past.shift();
 }
 apply(p,item,reverse){for(const c of item.changes){const bytes=reverse?c.old:c.value;for(let i=0;i<bytes.length;i++)p[c.layer][c.start+i]=bytes[i];}const m=structuredClone(reverse?item.before:item.after);for(const [key,diffs,order] of [['buildings',item.buildings,m.buildingOrder],['buildingLibrary',item.library,m.libraryOrder]]){const values=new Map((p[key]??[]).map(b=>[b.id,b]));for(const change of diffs){const value=reverse?change.before:change.after;if(value===null)values.delete(change.id);else values.set(change.id,structuredClone(value));}p[key]=order.map(id=>values.get(id));}delete m.buildingOrder;delete m.libraryOrder;Object.assign(p,m);}
 undo(p){this.commit(p);const item=this.past.pop();if(!item)return false;this.apply(p,item,true);this.future.push(item);return true;}
 redo(p){this.commit(p);const item=this.future.pop();if(!item)return false;this.apply(p,item,false);this.past.push(item);return true;}
}
