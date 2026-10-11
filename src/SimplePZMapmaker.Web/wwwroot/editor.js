export function blank(size=256){return {version:1,name:'Новая карта',width:size,height:size,seed:42,worldX:100,worldY:100,terrain:Array(size*size).fill(0),zombies:Array(size*size/64).fill(0),buildings:[],spawns:[{x:size/2,y:size/2}],attribution:[],bounds:null};}
export function paint(p,x,y,terrain,radius=1,history){
 for(let yy=Math.max(0,y-radius+1);yy<=Math.min(p.height-1,y+radius-1);yy++)for(let xx=Math.max(0,x-radius+1);xx<=Math.min(p.width-1,x+radius-1);xx++){
  if((xx-x)**2+(yy-y)**2>radius**2)continue;
  if(terrain===3&&(p.buildings.some(b=>xx>=b.x&&xx<=b.x+b.width&&yy>=b.y&&yy<=b.y+b.height)||p.spawns.some(s=>s.x===xx&&s.y===yy)))continue;
  const i=yy*p.width+xx;history?.touch('terrain',i,p.terrain[i]);p.terrain[i]=terrain;
 }
}
export function line(p,x0,y0,x1,y1,terrain,radius=1,history){
 const n=Math.max(Math.abs(x1-x0),Math.abs(y1-y0));for(let i=0;i<=n;i++){const t=n?i/n:0;paint(p,Math.round(x0+(x1-x0)*t),Math.round(y0+(y1-y0)*t),terrain,radius,history);}
}
export function canPlace(p,b){
 if(!b||!['house','shop','warehouse'].includes(b.kind)||![b.x,b.y,b.width,b.height].every(Number.isInteger)||b.width<6||b.height<6||b.width>64||b.height>64||b.x<1||b.y<1||b.x+b.width>=p.width-1||b.y+b.height>=p.height-1||Math.floor(b.x/256)!==Math.floor((b.x+b.width)/256)||Math.floor(b.y/256)!==Math.floor((b.y+b.height)/256))return false;
 if(p.buildings.some(a=>a!==b&&b.x<a.x+a.width+1&&b.x+b.width+1>a.x&&b.y<a.y+a.height+1&&b.y+b.height+1>a.y))return false;
 if(p.spawns.some(s=>s.x>=b.x&&s.x<=b.x+b.width&&s.y>=b.y&&s.y<=b.y+b.height))return false;
 for(let y=b.y;y<b.y+b.height;y++)for(let x=b.x;x<b.x+b.width;x++)if(p.terrain[y*p.width+x]===3)return false;
 return true;
}
export function validateProject(p){
 const fail=m=>{throw Error(m);};
 if(!p||p.version!==1||!Number.isInteger(p.width)||!Number.isInteger(p.height)||p.width<256||p.height<256||p.width>1024||p.height>1024||p.width%256||p.height%256)fail('Неподдерживаемый размер или версия проекта.');
 if(typeof p.name!=='string'||!p.name.trim()||p.name.length>80||/[\x00-\x1f\x7f]/.test(p.name))fail('Проверьте название карты.');
 if(!Number.isInteger(p.worldX)||!Number.isInteger(p.worldY)||p.worldX<0||p.worldY<0||p.worldX>1000||p.worldY>1000)fail('Координаты мира должны быть от 0 до 1000.');
 if(!Array.isArray(p.terrain)||p.terrain.length!==p.width*p.height||p.terrain.some(t=>!Number.isInteger(t)||t<0||t>6))fail('Слой земли повреждён.');
 if(!Array.isArray(p.zombies)||p.zombies.length!==p.width*p.height/64||p.zombies.some(v=>!Number.isInteger(v)||v<0||v>255))fail('Слой зомби повреждён.');
 if(!Array.isArray(p.buildings)||p.buildings.length>4000||!Array.isArray(p.spawns)||p.spawns.length<1||p.spawns.length>100||!Array.isArray(p.attribution)||p.attribution.length>30||p.attribution.some(a=>typeof a!=='string'||a.length>1000))fail('Объекты проекта повреждены.');
 const ids=new Set();for(const b of p.buildings){if(!b||typeof b.id!=='string'||!b.id||ids.has(b.id)||typeof b.name!=='string'||b.name.length>100||!canPlace(p,b))fail('Здание повреждено, пересекается с другим или стоит на воде.');ids.add(b.id);}
 for(const s of p.spawns)if(!s||!Number.isInteger(s.x)||!Number.isInteger(s.y)||s.x<0||s.y<0||s.x>=p.width||s.y>=p.height||p.terrain[s.y*p.width+s.x]===3||p.buildings.some(b=>s.x>=b.x&&s.x<=b.x+b.width&&s.y>=b.y&&s.y<=b.y+b.height))fail('Точка появления должна стоять на земле снаружи здания.');
 return p;
}
const meta=p=>structuredClone({buildings:p.buildings,spawns:p.spawns,name:p.name,worldX:p.worldX,worldY:p.worldY});
export class History{
 constructor(){this.past=[];this.future=[];this.pending=null;}
 begin(p){if(!this.pending)this.pending={before:meta(p),changes:new Map()};}
 touch(layer,index,old){if(this.pending&&!this.pending.changes.has(`${layer}:${index}`))this.pending.changes.set(`${layer}:${index}`,{layer,index,old});}
 commit(p){if(!this.pending)return;const item=this.pending;this.pending=null;item.after=meta(p);item.changes=[...item.changes.values()].map(c=>({...c,value:p[c.layer][c.index]})).filter(c=>c.old!==c.value);
  if(!item.changes.length&&JSON.stringify(item.before)===JSON.stringify(item.after))return;
  this.past.push(item);if(this.past.length>30)this.past.shift();this.future=[];
 }
 apply(p,item,reverse){for(const c of item.changes)p[c.layer][c.index]=reverse?c.old:c.value;Object.assign(p,structuredClone(reverse?item.before:item.after));}
 undo(p){const item=this.past.pop();if(!item)return false;this.apply(p,item,true);this.future.push(item);return true;}
 redo(p){const item=this.future.pop();if(!item)return false;this.apply(p,item,false);this.past.push(item);return true;}
}
