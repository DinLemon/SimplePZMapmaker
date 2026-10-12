import objectCatalog from './building-objects.json' with {type:'json'};
export const objectParts=o=>o.kind==='stairs'?Array.from({length:5},(_,i)=>({dx:o.direction==='w'?i:0,dy:o.direction==='n'?i:0})):objectCatalog[o.kind][o.direction];
export const objectCells=o=>objectParts(o).map(p=>({x:o.x+p.dx,y:o.y+p.dy}));
export function combineAttribution(...lists){if(lists.some(a=>!Array.isArray(a)||a.some(s=>typeof s!=='string'||s.length>1000)))throw Error('Источник шаблона повреждён.');const result=[...new Set(lists.flat())];if(result.length>30)throw Error('Слишком много источников географии.');return result;}
export const kinds={house:'Частный дом',apartments:'Многоквартирный дом',school:'Школа',shop:'Магазин',warehouse:'Склад',industrial:'Промышленный корпус',clinic:'Больница',hotel:'Гостиница',garage:'Гараж'};
export const roomNames={hall:'Коридор',livingroom:'Гостиная',kitchen:'Кухня',bedroom:'Спальня',bathroom:'Санузел',classroom:'Класс',office:'Кабинет',conveniencestore:'Торговый зал',warehouse:'Склад',medical:'Палата',hotelroom:'Номер',garage:'Гараж',storage:'Кладовая',balcony:'Балкон'};
export const shapeNames={rect:'Прямоугольник',l:'Г-образная',t:'Т-образная',courtyard:'Внутренний двор',annex:'Пристройка',custom:'Свободный контур'};
export const cell=(b,f,x,y)=>x<0||y<0||x>=b.width||y>=b.height?'.':f.cells[y*b.width+x];
export function shapeCells(w,h,shape='rect'){let result='';for(let y=0;y<h;y++)for(let x=0;x<w;x++)result+=(shape==='l'?x<Math.floor(w/2)||y<Math.floor(h/2):shape==='t'?y<Math.floor(h/3)||Math.abs(x-Math.floor(w/2))<=Math.floor(w/6):shape==='courtyard'?x<3||y<3||x>=w-3||y>=h-3:shape==='annex'?x<Math.floor(w*2/3)||y<Math.floor(h/2):true)?'1':'.';return result;}
export const occupied=(b,x,y)=>b.design?b.design.floors.some(f=>cell(b,f,x,y)!=='.'):x>=0&&y>=0&&x<b.width&&y<b.height;
export const containsPoint=(b,x,y)=>x>=0&&y>=0&&x<=b.width&&y<=b.height&&(b.design?occupied(b,x,y)||occupied(b,x-1,y)||occupied(b,x,y-1):true);
export function occupiedIndices(b,perimeter=true){const result=new Set();for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)if(occupied(b,x,y)){result.add(y*(b.width+1)+x);if(perimeter){result.add(y*(b.width+1)+x+1);result.add((y+1)*(b.width+1)+x);}}if(!b.design&&perimeter)for(let y=0;y<=b.height;y++)for(let x=0;x<=b.width;x++)result.add(y*(b.width+1)+x);return result;}
export const stairSpace=(b,z,x,y)=>b.design.floors.filter((_,i)=>i===z||i===z-1).flatMap(f=>f.objects).some(o=>o.kind==='stairs'&&(o.direction==='w'?y===o.y&&x>=o.x&&x<=o.x+4:x===o.x&&y>=o.y&&y<=o.y+4));
export function refreshEdges(b,z){const f=b.design.floors[z],edges=[],doors=new Set();let entrance=false;
 const regions=Array(f.cells.length).fill(-1);let region=0;for(let i=0;i<regions.length;i++)if(f.cells[i]!=='.'&&regions[i]<0){const queue=[i];regions[i]=region;for(let j=0;j<queue.length;j++){const index=queue[j],x=index%b.width,y=Math.floor(index/b.width);for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){const nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=b.width||ny>=b.height)continue;const next=ny*b.width+nx;if(regions[next]<0&&f.cells[next]===f.cells[i]){regions[next]=region;queue.push(next);}}}region++;}
 for(let y=0;y<=b.height;y++)for(let x=0;x<=b.width;x++)for(const edge of ['n','w']){const a=cell(b,f,x,y),other=edge==='n'?cell(b,f,x,y-1):cell(b,f,x-1,y);if(a===other)continue;const outer=a==='.'||other==='.';let kind='wall';
  if(!outer){const first=regions[y*b.width+x],second=edge==='n'?regions[(y-1)*b.width+x]:regions[y*b.width+x-1],pair=[first,second].sort((a,b)=>a-b).join(',');if(!doors.has(pair)){kind='door';doors.add(pair);}}else if(z===0&&!entrance&&a!=='.'&&edge==='n'){kind='door';entrance=true;}else if((edge==='n'?x:y)%5===3)kind='window';
  edges.push({x,y,edge,kind,edited:false});}
 for(const e of f.edges.filter(e=>e.edited)){if(cell(b,f,e.x,e.y)==='.'&&(e.edge==='n'?cell(b,f,e.x,e.y-1):cell(b,f,e.x-1,e.y))==='.')continue;const index=edges.findIndex(a=>a.x===e.x&&a.y===e.y&&a.edge===e.edge);if(index>=0)edges[index]=e;else edges.push(e);}
 f.edges=edges;
}
export function autoStairs(b){
 for(const f of b.design.floors)f.objects=f.objects.filter(o=>o.kind!=='stairs');
 for(let z=0;z<b.design.floors.length-1;z++){
  const lower=b.design.floors[z],upper=b.design.floors[z+1];let found=null;
  for(const direction of ['n','w']){for(let y=1;y<b.height&&!found;y++)for(let x=1;x<b.width&&!found;x++)if(Array.from({length:5},(_,i)=>[x+(direction==='w'?i:0),y+(direction==='n'?i:0)]).every(([xx,yy])=>cell(b,lower,xx,yy)!=='.'&&cell(b,upper,xx,yy)!=='.'))found={x,y,direction,kind:'stairs'};if(found)break;}
  if(!found)throw Error(`Между этажами ${z+1} и ${z+2} нет места для лестницы и площадок.`);
  for(const f of [lower,upper]){f.rooms['0']='hall';const cells=f.cells.split('');for(let i=0;i<5;i++)cells[(found.y+(found.direction==='n'?i:0))*b.width+found.x+(found.direction==='w'?i:0)]='0';f.cells=cells.join('');}
  lower.objects.push(found);
 }
 for(let z=0;z<b.design.floors.length;z++){const f=b.design.floors[z];f.objects=f.objects.filter(o=>o.kind==='stairs'||objectCells(o).every(p=>!stairSpace(b,z,p.x,p.y)));refreshEdges(b,z);}
}
export function validateDesign(b){
 const fail=text=>{throw Error(text);};if(!b||!Object.hasOwn(kinds,b.kind)||![b.width,b.height].every(v=>Number.isInteger(v)&&v>=6&&v<=64))fail('Размер здания: 6–64 клетки.');if(b.attribution!==undefined&&(!Array.isArray(b.attribution)||b.attribution.length>30||b.attribution.some(s=>typeof s!=='string'||s.length>1000)))fail('Источник шаблона повреждён.');if(!b.design)return b;
 const d=b.design;if(!['wood','brick','plaster'].includes(d.material)||!['flat','gabled','shed','hipped'].includes(d.roof)||!Object.hasOwn(shapeNames,d.shape)||!Number.isInteger(d.variant)||d.variant< -2147483648||d.variant>2147483647||!Array.isArray(d.floors)||d.floors.length<1||d.floors.length>16)fail('Проверьте материал, крышу и этажность.');
 for(const f of d.floors)if(!f||typeof f.cells!=='string'||f.cells.length!==b.width*b.height||!Array.isArray(f.objects)||f.objects.length>512||!Array.isArray(f.edges)||f.edges.length>2*(b.width+1)*(b.height+1)||f.edges.some(e=>!e)||!f.rooms)fail('Слой этажа повреждён.');
 for(let z=0;z<d.floors.length;z++){const f=d.floors[z];if(!f.rooms||Array.isArray(f.rooms)||Object.keys(f.rooms).length<1||Object.keys(f.rooms).length>35||Object.entries(f.rooms).some(([key,value])=>!/^[0-9a-z]$/.test(key)||!Object.hasOwn(roomNames,value))||[...f.cells].some(c=>c!=='.'&&!f.rooms[c])||!Array.isArray(f.edges)||f.edges.length>2*(b.width+1)*(b.height+1)||f.objects.length>512)fail(`Этаж ${z+1}: проверьте комнаты и объекты.`);
  const keys=new Set();for(const e of f.edges){if(!e||![e.x,e.y].every(Number.isInteger)||e.x<0||e.y<0||e.x>b.width||e.y>b.height||!['n','w'].includes(e.edge)||!['wall','door','window','open'].includes(e.kind))fail(`Этаж ${z+1}: стена повреждена.`);const key=`${e.x},${e.y},${e.edge}`;if(keys.has(key)||cell(b,f,e.x,e.y)==='.'&&(e.edge==='n'?cell(b,f,e.x,e.y-1):cell(b,f,e.x-1,e.y))==='.')fail('Стена стоит вне контура или повторяется.');keys.add(key);}
  const positions=new Set();for(const o of f.objects){if(!o||![o.x,o.y].every(Number.isInteger)||!['stairs','bed','storage','counter','seat','sink'].includes(o.kind)||!['n','w'].includes(o.direction)||cell(b,f,o.x,o.y)==='.'||positions.has(o.y*b.width+o.x))fail('Объект стоит вне помещения или пересекается.');positions.add(o.y*b.width+o.x);
   if(o.kind==='stairs'){if(z===d.floors.length-1)fail('Лестница должна вести на следующий этаж.');for(let i=0;i<5;i++){const x=o.x+(o.direction==='w'?i:0),y=o.y+(o.direction==='n'?i:0);if(cell(b,f,x,y)==='.'||cell(b,d.floors[z+1],x,y)==='.')fail('Лестнице нужны три ступени и две площадки.');}}else if(stairSpace(b,z,o.x,o.y))fail('Мебель перекрывает лестницу.');}
  const furnitureCells=new Set();for(const o of f.objects.filter(o=>o.kind!=='stairs')){const parts=objectCells(o),positions=new Set(parts.map(p=>`${p.x},${p.y}`));for(const p of parts){const index=p.y*b.width+p.x;if(cell(b,f,p.x,p.y)==='.'||stairSpace(b,z,p.x,p.y)||furnitureCells.has(index))fail('Мебель пересекается, перекрывает лестницу или выходит за контур.');furnitureCells.add(index);for(const [dx,dy,edge] of [[1,0,'w'],[0,1,'n']])if(positions.has(`${p.x+dx},${p.y+dy}`)&&f.edges.some(e=>e.x===p.x+dx&&e.y===p.y+dy&&e.edge===edge&&e.kind!=='open'))fail('Мебель пересекает стену или проём.');}if(f.edges.some(e=>['door','open'].includes(e.kind)&&(positions.has(`${e.x},${e.y}`)||positions.has(e.edge==='n'?`${e.x},${e.y-1}`:`${e.x-1},${e.y}`))))fail('Мебель перекрывает дверь или проход.');}
  if(z<d.floors.length-1&&!f.objects.some(o=>o.kind==='stairs'))fail(`Этаж ${z+1}: добавьте лестницу.`);
  const stairCells=new Set();for(const stair of f.objects.filter(o=>o.kind==='stairs'))for(let i=0;i<5;i++){const x=stair.x+(stair.direction==='w'?i:0),y=stair.y+(stair.direction==='n'?i:0),index=y*b.width+x;if(stairCells.has(index))fail('Лестницы пересекаются.');stairCells.add(index);if(i>0&&[f,d.floors[z+1]].some(level=>level.edges.some(e=>e.x===x&&e.y===y&&e.edge===stair.direction&&e.kind!=='open')))fail('Лестница пересекает стену или проём.');}
  const start=f.cells.search(/[0-9a-z]/);if(start<0)fail('Этаж не может быть пустым.');const walls=new Set(f.edges.filter(e=>['wall','window'].includes(e.kind)).map(e=>`${e.x},${e.y},${e.edge}`)),seen=new Set([start]),queue=[start];
  for(let j=0;j<queue.length;j++){const i=queue[j],x=i%b.width,y=Math.floor(i/b.width);for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){const nx=x+dx,ny=y+dy;if(cell(b,f,nx,ny)==='.')continue;const edge=dx?`${Math.max(x,nx)},${y},w`:`${x},${Math.max(y,ny)},n`,next=ny*b.width+nx;if(!walls.has(edge)&&!seen.has(next)){seen.add(next);queue.push(next);}}}
  if(seen.size!==[...f.cells].filter(c=>c!=='.').length)fail(`Этаж ${z+1}: есть изолированная комната. Добавьте проход.`);
  if(z===0&&!f.edges.some(e=>['door','open'].includes(e.kind)&&(cell(b,f,e.x,e.y)==='.'||(e.edge==='n'?cell(b,f,e.x,e.y-1):cell(b,f,e.x-1,e.y))==='.')))fail('На первом этаже нужен наружный вход.');
 }return b;
}
export class DraftHistory{
 constructor(){this.past=[];this.future=[];}
 save(b){const json=JSON.stringify(b);if(this.past.at(-1)!==json)this.past.push(json);this.future=[];while(this.past.length>30||this.past.reduce((n,s)=>n+s.length*2,0)>16*1024*1024)this.past.shift();}
 undo(b){if(!this.past.length)return b;this.future.push(JSON.stringify(b));return JSON.parse(this.past.pop());}
 redo(b){if(!this.future.length)return b;this.past.push(JSON.stringify(b));return JSON.parse(this.future.pop());}
}
