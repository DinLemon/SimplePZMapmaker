import {cell,refreshEdges,objectCells,stairSpace} from './building.js';
export const facadePalettes={wood:{name:'Деревянная обшивка',tileset:'walls_exterior_house_01',base:48},brick:{name:'Красный кирпич',tileset:'walls_exterior_house_01',base:16},plaster:{name:'Светлая обшивка',tileset:'walls_exterior_house_01',base:32},grey:{name:'Серая кладка',tileset:'walls_exterior_house_01',base:0},stone:{name:'Каменная кладка',tileset:'walls_exterior_house_02',base:0},lightBrick:{name:'Светлый кирпич',tileset:'walls_exterior_house_02',base:48}};
export function applyFacade(b,id){const palette=facadePalettes[id];if(!palette)throw Error('Выберите оформление фасада.');for(const f of b.design.floors)for(const e of f.edges){const a=cell(b,f,e.x,e.y),other=e.edge==='n'?cell(b,f,e.x,e.y-1):cell(b,f,e.x-1,e.y);if((a==='.'||other==='.')&&f.rooms[(a==='.'?other:a)]!=='balcony'&&e.kind!=='open'){e.wallTile=`${palette.tileset}_${palette.base+(e.kind==='door'?10:e.kind==='window'?8:0)+(e.edge==='n'?1:0)}`;e.edited=true;}}}
// Geometry expansion translates every floor and all authored coordinates. No regeneration occurs.
export function addBuildingWing(b,index,{side,depth,width,kind='room'}){
 if(!b.design?.floors[index]||!['north','south','east','west'].includes(side)||!['room','balcony'].includes(kind)||![depth,width].every(Number.isInteger)||depth<1||depth>12||width<2)throw Error('Задайте сторону, ширину от 2 клеток и глубину 1–12.');
 const horizontal=['north','south'].includes(side),span=horizontal?b.width:b.height,oldW=b.width,oldH=b.height,newW=oldW+(horizontal?0:depth),newH=oldH+(horizontal?depth:0),dx=side==='west'?depth:0,dy=side==='north'?depth:0;
 if(width>span||newW>64||newH>64||(b.x??0)<dx||(b.y??0)<dy)throw Error('Пристройка выходит за доступный размер здания или границу карты.');
 const original=b.design.floors[index],code=[...'123456789abcdefghijklmnopqrstuvwxyz'].find(c=>!Object.hasOwn(original.rooms,c));if(!code||Object.keys(original.rooms).length>=35)throw Error('На этаже может быть до 35 помещений.');
 const starts=Array.from({length:span-width+1},(_,i)=>i).sort((a,c)=>Math.abs(a-(span-width)/2)-Math.abs(c-(span-width)/2));
 const contact=t=>side==='north'?{x:t,y:0}:side==='south'?{x:t,y:oldH-1}:side==='west'?{x:0,y:t}:{x:oldW-1,y:t};
 const start=starts.find(s=>Array.from({length:width},(_,i)=>contact(s+i)).every(p=>cell(b,original,p.x,p.y)!=='.'));if(start===undefined)throw Error('На этой стороне нет сплошной стены нужной ширины. Выберите другую сторону или уменьшите ширину.');
 const occupied=new Set(original.objects.flatMap(objectCells).map(p=>`${p.x},${p.y}`));
 const doorCandidates=Array.from({length:width},(_,i)=>contact(start+i)).sort((a,c)=>Math.abs((horizontal?a.x:a.y)-(start+(width-1)/2))-Math.abs((horizontal?c.x:c.y)-(start+(width-1)/2)));
 const door=doorCandidates.find(p=>!occupied.has(`${p.x},${p.y}`)&&!stairSpace(b,index,p.x,p.y)&&!(original.staticModels||[]).some(m=>m.x===p.x&&m.y===p.y));if(!door)throw Error('Стена пристройки перекрыта мебелью, моделью или лестницей.');
 for(const f of b.design.floors){const cells=Array(newW*newH).fill('.');for(let y=0;y<oldH;y++)for(let x=0;x<oldW;x++)cells[(y+dy)*newW+x+dx]=f.cells[y*oldW+x];f.cells=cells.join('');f.floorTiles=Object.fromEntries(Object.entries(f.floorTiles||{}).map(([i,tile])=>[(Math.floor(+i/oldW)+dy)*newW+(+i%oldW)+dx,tile]));for(const e of f.edges){e.x+=dx;e.y+=dy;}for(const item of [...f.objects,...f.staticModels||[]]){item.x+=dx;item.y+=dy;}}
 b.width=newW;b.height=newH;b.x=(b.x??0)-dx;b.y=(b.y??0)-dy;b.design.shape='custom';b.modified=true;b.levelsOrigin='manual';
 const f=b.design.floors[index],cells=f.cells.split('');f.rooms[code]=kind==='balcony'?'balcony':'livingroom';
 const x0=horizontal?start:side==='west'?0:oldW,y0=horizontal?(side==='north'?0:oldH):start;
 for(let y=y0;y<y0+(horizontal?depth:width);y++)for(let x=x0;x<x0+(horizontal?width:depth);x++)cells[y*newW+x]=code;f.cells=cells.join('');
 for(const e of f.edges)e.edited=true;refreshEdges(b,index);
 for(const e of f.edges){const a=cell(b,f,e.x,e.y),other=e.edge==='n'?cell(b,f,e.x,e.y-1):cell(b,f,e.x-1,e.y);if(kind==='balcony'&&(a===code&&other==='.'||other===code&&a==='.')){e.kind='wall';e.wallTile=null;e.openingTile=null;e.edited=true;}}
 const boundary={x:door.x+dx+(side==='east'?1:0),y:door.y+dy+(side==='south'?1:0),edge:horizontal?'n':'w'};
 const e=f.edges.find(e=>e.x===boundary.x&&e.y===boundary.y&&e.edge===boundary.edge);if(e){e.kind='door';e.wallTile=null;e.openingTile=null;e.edited=true;}
 return b;
}
export function installArchitectureControls(workshop){
 const section=document.createElement('details');section.id='be-architecture';section.open=true;
 section.innerHTML='<summary>Фасад, балкон и пристройка</summary><label>Фасад<select id="be-facade"></select></label><button id="be-facade-apply" type="button">Применить фасад</button><label>Сторона<select id="be-wing-side"><option value="south">Юг</option><option value="north">Север</option><option value="east">Восток</option><option value="west">Запад</option></select></label><label>Ширина вдоль стены<input id="be-wing-width" type="number" min="2" max="64" value="4"></label><label>Глубина<input id="be-wing-depth" type="number" min="1" max="12" value="2"></label><button id="be-balcony-add" type="button">Добавить балкон / террасу</button><button id="be-annex-add" type="button">Добавить жилую пристройку</button><small>Только выбранный этаж. Существующие комнаты, ресурсы и модели сохраняются. Крыша — в разделе оформления выше.</small>';
 document.getElementById('be-shape-apply').after(section);const get=id=>document.getElementById(id);
 for(const [id,p] of Object.entries(facadePalettes))get('be-facade').add(new Option(p.name,id));
 get('be-facade-apply').onclick=()=>workshop.change(()=>applyFacade(workshop.draft,get('be-facade').value));
 for(const [id,kind] of [['be-balcony-add','balcony'],['be-annex-add','room']])get(id).onclick=()=>workshop.change(()=>addBuildingWing(workshop.draft,workshop.level,{side:get('be-wing-side').value,width:+get('be-wing-width').value,depth:+get('be-wing-depth').value,kind}));
}
