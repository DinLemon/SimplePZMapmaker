import {blank,paint,line,canPlace,History,validateProject,boundsFromSearchResult,geographicBase} from './editor.js';
import {kinds,containsPoint,cell,shapeCells,validateDesign,combineAttribution,minLevel,aboveLevels,visibleFloors,containsVisiblePoint,usesAssets,normalizeBuilding} from './building.js';
import {BuildingWorkshop} from './workshop.js';
import {ResourcePicker,GamePreview} from './resources.js';

const $=id=>document.getElementById(id);
const colors=['#8b9768','#526b4b','#787975','#507b85','#c6b58d','#8e765e','#a5a395'];
const terrainNames=['трава','лес','асфальт','вода','песок','земля','тротуар'];
const catalog=Object.fromEntries(Object.entries(kinds).map(([kind,name])=>[kind,{width:kind==='house'?12:kind==='garage'?10:20,height:kind==='house'?10:kind==='garage'?8:16,name}]));
const recoveryKey='simple-pz-mapmaker:project:v1';
const recoveryClientKey='simple-pz-mapmaker:recovery-client:v1';
let recoveryId=crypto.randomUUID().replaceAll('-','');
try{const existing=localStorage.getItem(recoveryClientKey);if(/^[a-f0-9]{32}$/.test(existing||''))recoveryId=existing;else localStorage.setItem(recoveryClientKey,recoveryId);}catch{}
let autosaveRevision=0,recoveryQueue=Promise.resolve();
let project=blank(),history=new History(),tool='brush',terrain=0,buildingKind='house';
let selectedId=null,selectedSpawn=-1,gesture=null,cursor=null,isometric=false,showGrid=true;
let selectedPlaceBounds=null;
let zoom=1,pan={x:0,y:0},space=false,busy=false,controller=null,saveTimer,toastTimer,frame=0,dirty=true;
const canvas=$('map-canvas'),ctx=canvas.getContext('2d'),mini=$('minimap'),mctx=mini.getContext('2d');
const raster=document.createElement('canvas'),rctx=raster.getContext('2d');
let viewport={width:800,height:600};
let worldAsset=null,surfaceAsset=null,standaloneId=null;
const gamePreview=new GamePreview();
const resources=new ResourcePicker({message:toast,pickWorld:a=>{worldAsset=a;setTool('world-asset');$('world-resource-name').textContent=a.name+' · '+a.id;},pickSurface:a=>{surfaceAsset=a;setTool('surface-asset');$('world-resource-name').textContent=a.id;}});
function saveTemplate(b){project.buildingLibrary??=[];if(project.buildingLibrary.length>=64)throw Error('В библиотеке может быть до 64 шаблонов.');b.attribution=combineAttribution(b.attribution||[],project.attribution);history.begin(project);b.id=crypto.randomUUID();b.sourceId=null;b.sourceKind=null;b.modified=false;project.buildingLibrary.push(b);history.commit(project);changed();}
const workshop=new BuildingWorkshop({api,download,resources,preview:gamePreview,apply:b=>{
 if(b.id===standaloneId){saveTemplate(b);standaloneId=null;toast('Свой шаблон сохранён. Выберите его в библиотеке, чтобы поставить на карту.');return;}
 const old=project.buildings.find(a=>a.id===b.id);if(!old)throw Error('Исходное здание больше не существует.');
 b.modified=true;if(aboveLevels(b)!==aboveLevels(old))b.levelsOrigin='manual';validateDesign(b);if(!canPlace({...project,buildings:project.buildings.filter(a=>a!==old)},b))throw Error('Новый контур пересекается с водой, другим зданием, точкой появления или границей ячейки.');
 history.begin(project);Object.assign(old,b);history.commit(project);selectedId=b.id;changed();
},template:saveTemplate,attribution:()=>project.attribution});
function updateLibrary(){const selected=$('building-preset').value;$('building-preset').replaceChildren();for(const [value,name] of Object.entries(kinds))$('building-preset').add(new Option(name,value));for(const b of project.buildingLibrary||[])$('building-preset').add(new Option('Шаблон: '+b.name,'template:'+b.id));$('building-preset').value=[...$('building-preset').options].some(o=>o.value===selected)?selected:buildingKind;}
function listBuildings(){const q=$('building-list-search').value.toLocaleLowerCase('ru-RU');$('building-list').replaceChildren();const items=project.buildings.filter(b=>(b.name+' '+kinds[b.kind]).toLocaleLowerCase('ru-RU').includes(q)).slice(0,100);for(const b of items){const button=document.createElement('button');button.textContent=`${b.name} · ${kinds[b.kind]} · ${aboveLevels(b)} этаж(а) / ${-minLevel(b)} подвал(а)`;button.onclick=()=>{selectedId=b.id;$('building-list-dialog').close();update();$('edit-building').click();};$('building-list').append(button);}if(!items.length){const p=document.createElement('p');p.textContent=project.buildings.length?'Уточните название.':'На карте пока нет зданий. Создайте свой шаблон.';$('building-list').append(p);}}
$('open-building-editor').onclick=()=>{if(busy)return;if(selectedId){$('edit-building').click();return;}$('building-list-search').value='';listBuildings();$('building-list-dialog').showModal();};$('building-list-search').oninput=listBuildings;
$('new-building-template').onclick=()=>{ $('building-list-dialog').close();task('Подготавливаем новый шаблон…',async signal=>{const b=await api('/api/building/design',{building:{id:crypto.randomUUID(),name:'Свой дом',kind:buildingKind,x:8,y:8,width:catalog[buildingKind].width,height:catalog[buildingKind].height},levels:+$('placement-levels').value,basements:+$('placement-basements').value,seed:project.seed},signal);standaloneId=b.id;workshop.open(b,true);});};
$('open-resources').onclick=()=>resources.open();$('pick-world-resource').onclick=()=>resources.open('world',a=>{worldAsset=a;setTool('world-asset');$('world-resource-name').textContent=a.name+' · '+a.id;});$('pick-surface-resource').onclick=()=>resources.open('surface',a=>{surfaceAsset=a;setTool('surface-asset');$('world-resource-name').textContent=a.id;});
$('erase-world-resource').onclick=()=>setTool('erase-decor');
$('apply-visual-style').onclick=()=>task('Подбираем оформление застройки и местности…',async signal=>{const result=await api('/api/styles/apply',{project,surfaceTheme:$('surface-theme').value},signal);history.begin(project);project.surfaceTheme=result.project.surfaceTheme;project.buildings=result.project.buildings;history.commit(project);changed();toast(`Оформлено зданий: ${result.changed}. Ручные здания сохранены.`);});
$('preview-region').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId),center=b?{x:b.x+Math.floor(b.width/2),y:b.y+Math.floor(b.height/2)}:cursor||{x:Math.floor(project.width/2),y:Math.floor(project.height/2)},cellX=Math.min(project.width/256-1,Math.max(0,Math.floor(center.x/256))),cellY=Math.min(project.height/256-1,Math.max(0,Math.floor(center.y/256))),x=Math.min(208,Math.max(0,Math.floor(center.x-cellX*256)-24)),y=Math.min(208,Math.max(0,Math.floor(center.y-cellY*256)-24));gamePreview.open('Участок карты · 48 × 48',level=>api('/api/preview/cell',{project,cellX,cellY,x,y,level}),-16,15,$('map-level').value==='all'?'0':$('map-level').value);};

function toast(message,error=false){$('toast').textContent=message;$('toast').classList.toggle('error',error);$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,error?10000:5500);}
function status(message){$('status').textContent=message;}
async function api(path,data,signal){
 const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data),signal});
 if(!response.ok){let message=`Ошибка запроса (${response.status}).`;try{message=(await response.json()).error||message;}catch{}throw Error(message);}
 return path==='/api/export'?response.blob():response.json();
}
async function task(message,run){
 if(busy){toast('Дождитесь завершения текущей операции.');return;}
 finish();document.activeElement?.blur();history.commit(project);
 busy=true;controller=new AbortController();$('busy-message').textContent=message;status(message);
 for(const element of document.querySelectorAll('.topbar,main,footer,dialog:not(#operation-dialog)'))element.inert=true;
 $('operation-dialog').showModal();
 try{await run(controller.signal);status('Готово');}catch(e){if(e.name==='AbortError'){toast('Операция отменена.');status('Отменено');}else{toast(e.message,true);status('Проверьте данные');}}
 finally{busy=false;controller=null;$('operation-dialog').close();for(const element of document.querySelectorAll('.topbar,main,footer,dialog'))element.inert=false;update();}
}
function download(blob,name){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),30000);}
function autosave(){
 const revision=++autosaveRevision;$('save-state').textContent='Есть изменения';clearTimeout(saveTimer);
 saveTimer=setTimeout(()=>{recoveryQueue=recoveryQueue.catch(()=>{}).then(async()=>{
  if(revision!==autosaveRevision)return;
  try{await api('/api/recovery/'+recoveryId,project);if(revision===autosaveRevision)$('save-state').textContent='Автосохранено в папке проекта';try{localStorage.removeItem(recoveryKey);}catch{}}
  catch{if(revision===autosaveRevision)$('save-state').textContent='Автосохранение недоступно — сохраните файл';}
 });},650);
}
function update(){
 $('project-name').value=project.name;$('inspector-title').textContent=project.name;$('size-stat').textContent=`${project.width} × ${project.height}`;
 $('map-summary').textContent=`${project.width} × ${project.height} / ${project.width*project.height/65536} яч.`;
 $('building-count').textContent=project.buildings.length;$('spawn-count').textContent=project.spawns.length;$('seed-stat').textContent=project.seed;
 $('world-x').value=project.worldX;$('world-y').value=project.worldY;$('undo').disabled=!history.past.length;$('redo').disabled=!history.future.length;
 $('attribution').textContent=project.attribution.length?project.attribution.join(' · '):'Геоданные не используются';
 $('attribution').title=project.attribution.join('\n');
 $('geography-panel').hidden=!project.metresPerTile;
 if(project.metresPerTile){$('scale-stat').textContent=project.metresPerTile.toLocaleString('ru-RU',{maximumFractionDigits:2})+' м / клетка';const report=project.importSummary;$('import-summary').textContent=report?`При импорте размещено ${report.placedBuildings} из ${report.sourceBuildings} зданий. `+Object.entries(report.skipReasons).map(([k,v])=>`${k}: ${v}`).join('; ')+' '+(report.notes||[]).join(' '):'Географическая основа карты';}
 $('zoom-value').textContent=Math.round(zoom*100)+'%';
 $('surface-theme').value=project.surfaceTheme??'legacy';
 const b=project.buildings.find(b=>b.id===selectedId);$('selection-panel').hidden=!b;
 if(b){$('building-name').value=b.name;$('building-x').value=b.x;$('building-y').value=b.y;$('building-width').value=b.width;$('building-height').value=b.height;$('building-details').textContent=`${kinds[b.kind]} · ${aboveLevels(b)} этаж(а) · ${-minLevel(b)} подвал(а) · ${b.levelsOrigin==='osm-levels'?'этажность OSM':b.levelsOrigin==='osm-height'?'оценка по высоте OSM':b.levelsOrigin==='manual'?'задано вручную':'этажность предположена'}${b.modified?' · ручные правки':''}`;}
 updateLibrary();
 const names={brush:`Кисть · ${terrainNames[terrain]}`,rectangle:`Область · ${terrainNames[terrain]}`,road:'Дорога · проведите линию',building:`Здание · ${catalog[buildingKind].name}`,spawn:'Точка появления',select:'Выбор объектов','world-asset':'Игровой декор · нажмите на земле','surface-asset':'Игровое покрытие · нажмите на земле','erase-decor':'Убрать игровой декор',pan:'Перемещение карты',zombies:`Зомби · ${$('zombie-density').value}/255`};
 $('map-tool-label').textContent=isometric?'Изометрический обзор · рисование в виде сверху':names[tool];
 const help={brush:'Рисуйте левой кнопкой. Колесо меняет масштаб. Средняя кнопка или Space перемещает карту.',rectangle:'Протяните прямоугольную область от одного угла к другому.',road:'Нажмите в начале дороги и отпустите в конце. Размер кисти задаёт ширину дороги.',building:'Нажмите на свободной земле. Красный контур означает пересечение, воду или границу ячейки.',spawn:'Нажмите на суше снаружи здания. Выбрать и удалить точку можно инструментом «Выбор» и клавишей Delete.',select:'Нажмите на здание, чтобы изменить положение и размер. Delete удаляет выбранный объект.',pan:'Перетащите карту мышью. Колесо меняет масштаб.',zombies:'Рисуйте плотность зомби. Один квадрат плотности соответствует чанку 8 × 8.'};
 $('tool-help').textContent=help[tool];requestDraw();
}
function changed(){dirty=true;autosave();update();}
function adopt(p){validateProject(p);p.version=4;p.surfaceTheme??='legacy';p.surfaceTiles??={};p.decorations??=[];for(const b of [...p.buildings,...(p.buildingLibrary||[])])normalizeBuilding(b);project=p;history=new History();selectedId=null;selectedSpawn=-1;gesture=null;dirty=true;fit();changed();}
function setTool(value){if(gesture)finish();tool=value;document.querySelectorAll('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));if(value==='zombies')$('show-zombies').checked=true;update();}
function fit(){
 const w=viewport.width,h=viewport.height;
 if(isometric){zoom=Math.min((w-90)/(project.width+project.height),(h-90)/((project.width+project.height)/2));pan={x:w/2,y:(h-(project.width+project.height)*zoom/2)/2};}
 else{zoom=Math.min((w-72)/project.width,(h-90)/project.height);pan={x:(w-project.width*zoom)/2,y:(h-project.height*zoom)/2};}
 zoom=Math.max(.08,zoom);update();
}
function gridPoint(event){const rect=canvas.getBoundingClientRect();return inverse(event.clientX-rect.left,event.clientY-rect.top);}
function inverse(x,y){x-=pan.x;y-=pan.y;return isometric?{x:Math.floor((x+2*y)/(2*zoom)),y:Math.floor((2*y-x)/(2*zoom))}:{x:Math.floor(x/zoom),y:Math.floor(y/zoom)};}
function screen(x,y){return isometric?{x:pan.x+(x-y)*zoom,y:pan.y+(x+y)*zoom/2}:{x:pan.x+x*zoom,y:pan.y+y*zoom};}
function within(p){return p.x>=0&&p.y>=0&&p.x<project.width&&p.y<project.height;}
function scaleAt(factor,x=viewport.width/2,y=viewport.height/2){
 const old=zoom;zoom=Math.max(.08,Math.min(20,zoom*factor));pan.x=x-(x-pan.x)*zoom/old;pan.y=y-(y-pan.y)*zoom/old;update();
}
function requestDraw(){if(!frame)frame=requestAnimationFrame(()=>{frame=0;draw();});}
function rebuildRaster(){
 if(!dirty)return;raster.width=project.width;raster.height=project.height;
 const image=rctx.createImageData(project.width,project.height);const rgb=colors.map(c=>[parseInt(c.slice(1,3),16),parseInt(c.slice(3,5),16),parseInt(c.slice(5,7),16)]);
 for(let i=0;i<project.terrain.length;i++){const c=rgb[project.terrain[i]],shade=project.terrain[i]===1?((i*31+project.seed)>>>0)%7-3:0;image.data[i*4]=c[0]+shade;image.data[i*4+1]=c[1]+shade;image.data[i*4+2]=c[2]+shade;image.data[i*4+3]=255;}
 rctx.putImageData(image,0,0);dirty=false;
}
function polygon(points,fill,stroke){ctx.beginPath();for(let i=0;i<points.length;i++){const p=points[i];i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y);}ctx.closePath();if(fill){ctx.fillStyle=fill;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.stroke();}}
function draw(){
 rebuildRaster();const dpr=window.devicePixelRatio||1;ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,viewport.width,viewport.height);ctx.imageSmoothingEnabled=false;
 ctx.save();if(isometric)ctx.transform(zoom,zoom/2,-zoom,zoom/2,pan.x,pan.y);else ctx.transform(zoom,0,0,zoom,pan.x,pan.y);
 ctx.shadowColor='#0007';ctx.shadowBlur=12;ctx.drawImage(raster,0,0);ctx.shadowBlur=0;
 if($('show-zombies').checked){const n=project.width/8;for(let y=0;y<project.height/8;y++)for(let x=0;x<n;x++){const z=project.zombies[y*n+x];if(z){ctx.fillStyle=`rgba(171,53,32,${z/255*.7})`;ctx.fillRect(x*8,y*8,8,8);}}}
 if(showGrid){
  ctx.lineWidth=1/zoom;ctx.strokeStyle='#d5ad6999';ctx.setLineDash([5/zoom,5/zoom]);ctx.beginPath();for(let x=0;x<=project.width;x+=256){ctx.moveTo(x,0);ctx.lineTo(x,project.height);}for(let y=0;y<=project.height;y+=256){ctx.moveTo(0,y);ctx.lineTo(project.width,y);}ctx.stroke();ctx.setLineDash([]);
  if(zoom>=5&&!isometric){ctx.strokeStyle='#25302322';ctx.lineWidth=.5/zoom;ctx.beginPath();const a=inverse(0,0),b=inverse(viewport.width,viewport.height);for(let x=Math.max(0,a.x);x<=Math.min(project.width,b.x+1);x++){ctx.moveTo(x,Math.max(0,a.y));ctx.lineTo(x,Math.min(project.height,b.y+1));}for(let y=Math.max(0,a.y);y<=Math.min(project.height,b.y+1);y++){ctx.moveTo(Math.max(0,a.x),y);ctx.lineTo(Math.min(project.width,b.x+1),y);}ctx.stroke();}
 }
 ctx.restore();
 if($('show-buildings').checked){
  const buildings=isometric?[...project.buildings].sort((a,b)=>(a.x+a.y)-(b.x+b.y)):project.buildings;
  for(const b of buildings){const fill=b.kind==='house'?'#d5c8a4':b.kind==='shop'?'#ba9465':'#a3aaa0';
   if(b.design){drawDesignedBuilding(b);continue;}if($('map-level').value!=='all'&&+$('map-level').value!==0)continue;
   if(isometric){const points=[[b.x,b.y],[b.x+b.width,b.y],[b.x+b.width,b.y+b.height],[b.x,b.y+b.height]].map(([x,y])=>screen(x,y)),lift=Math.max(3,zoom*7);const roof=points.map(p=>({x:p.x,y:p.y-lift}));polygon([roof[1],roof[2],points[2],points[1]],'#8b8169');polygon([roof[2],roof[3],points[3],points[2]],'#a49776');polygon(roof,fill,b.id===selectedId?'#f5ce84':'#4b4e3d');}
   else{const point=screen(b.x,b.y);ctx.fillStyle=fill;ctx.fillRect(point.x,point.y,b.width*zoom,b.height*zoom);ctx.strokeStyle=b.id===selectedId?'#fff0be':'#555644';ctx.lineWidth=b.id===selectedId?2:1;ctx.strokeRect(point.x,point.y,b.width*zoom,b.height*zoom);
    if(zoom>2&&b.kind==='house'){ctx.strokeStyle='#706f5a';ctx.beginPath();ctx.moveTo(point.x+b.width/2*zoom,point.y);ctx.lineTo(point.x+b.width/2*zoom,point.y+b.height*zoom);ctx.moveTo(point.x+b.width/2*zoom,point.y+b.height/2*zoom);ctx.lineTo(point.x+b.width*zoom,point.y+b.height/2*zoom);ctx.stroke();}
    if(zoom>1){ctx.fillStyle='#425443';ctx.fillRect(point.x+3*zoom,point.y-1,zoom*1.5,Math.max(2,zoom));}
   }
  }
 }
 if(['all','0'].includes($('map-level').value))for(const d of project.decorations){for(const part of d.parts){const p=screen(d.x+part.dx,d.y+part.dy);ctx.fillStyle='#d5b575';ctx.strokeStyle='#28382d';ctx.lineWidth=1;ctx.fillRect(p.x,p.y,Math.max(3,zoom),Math.max(3,zoom));ctx.strokeRect(p.x,p.y,Math.max(3,zoom),Math.max(3,zoom));}}
 if($('show-spawns').checked&&['all','0'].includes($('map-level').value))for(let i=0;i<project.spawns.length;i++){const s=project.spawns[i],p=screen(s.x+.5,s.y+.5);ctx.beginPath();ctx.arc(p.x,p.y,selectedSpawn===i?8:6,0,Math.PI*2);ctx.fillStyle='#2b3528';ctx.fill();ctx.lineWidth=2;ctx.strokeStyle='#edb16b';ctx.stroke();ctx.beginPath();ctx.moveTo(p.x-3,p.y);ctx.lineTo(p.x+3,p.y);ctx.moveTo(p.x,p.y-3);ctx.lineTo(p.x,p.y+3);ctx.lineWidth=1;ctx.stroke();}
 if(cursor&&within(cursor)&&!isometric&&!busy){
  const p=screen(cursor.x,cursor.y);ctx.lineWidth=1;
  if(tool==='building'){const b=ghost(cursor);const q=screen(b.x,b.y);const valid=canPlace(project,b);ctx.fillStyle=valid?'#ead8a755':'#bd654c66';ctx.strokeStyle=valid?'#e8c690':'#ef9d7b';ctx.fillRect(q.x,q.y,b.width*zoom,b.height*zoom);ctx.strokeRect(q.x,q.y,b.width*zoom,b.height*zoom);}
  else if(tool==='brush'||tool==='zombies'){ctx.beginPath();ctx.arc(p.x,p.y,+$('brush-size').value*zoom,0,Math.PI*2);ctx.strokeStyle='#f4e6be';ctx.stroke();}
  if(gesture?.start&&(tool==='rectangle'||tool==='road')){const a=screen(gesture.start.x,gesture.start.y);ctx.strokeStyle='#f5cf86';ctx.lineWidth=tool==='road'?Math.max(2,(+$('brush-size').value*2-1)*zoom):1;ctx.globalAlpha=.6;if(tool==='road'){ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(p.x,p.y);ctx.stroke();}else ctx.strokeRect(Math.min(a.x,p.x),Math.min(a.y,p.y),Math.abs(a.x-p.x),Math.abs(a.y-p.y));ctx.globalAlpha=1;}
 }
 mctx.clearRect(0,0,150,150);mctx.drawImage(raster,0,0,150,150);mctx.fillStyle='#ddd1ae';for(const b of project.buildings)mctx.fillRect(b.x/project.width*150,b.y/project.height*150,Math.max(2,b.width/project.width*150),Math.max(2,b.height/project.height*150));
 mctx.strokeStyle='#f4d293';mctx.lineWidth=1;if(!isometric){const a=inverse(0,0),b=inverse(viewport.width,viewport.height);mctx.strokeRect(a.x/project.width*150,a.y/project.height*150,(b.x-a.x)/project.width*150,(b.y-a.y)/project.height*150);}
}
function drawDesignedBuilding(b){
 const floors=visibleFloors(b,$('map-level').value,isometric);
 floors.forEach(({f,z})=>{const lift=isometric?(z+1)*Math.max(2,zoom*5):0,fill={wood:'#c7b58e',brick:'#b69179',plaster:'#bdc3ae'}[b.design.material];
  for(let y=0;y<b.height;y++)for(let x=0;x<b.width;){if(cell(b,f,x,y)==='.'){x++;continue;}const start=x;while(x<b.width&&cell(b,f,x,y)!=='.')x++;const w=x-start;
   if(isometric){const pts=[[b.x+start,b.y+y],[b.x+x,b.y+y],[b.x+x,b.y+y+1],[b.x+start,b.y+y+1]].map(([xx,yy])=>screen(xx,yy)),roof=pts.map(p=>({x:p.x,y:p.y-lift}));
    if(cell(b,f,x,y)==='.')polygon([roof[1],roof[2],pts[2],pts[1]],'#756c59');if(Array.from({length:w},(_,i)=>cell(b,f,start+i,y+1)).some(c=>c==='.'))polygon([roof[2],roof[3],pts[3],pts[2]],'#8c7e64');polygon(roof,fill,b.id===selectedId?'#f7dca1':null);
   }else{const p=screen(b.x+start,b.y+y);ctx.fillStyle=fill;ctx.fillRect(p.x,p.y,w*zoom,zoom);if(b.id===selectedId){ctx.strokeStyle='#f7dca1';ctx.lineWidth=1;ctx.strokeRect(p.x,p.y,w*zoom,zoom);}}
  }
  if(!isometric&&zoom>2){for(const e of f.edges){const p=screen(b.x+e.x,b.y+e.y);ctx.strokeStyle={wall:'#4e4a3d',door:'#e9b477',window:'#a1cbdb',open:'#87997d'}[e.kind];ctx.lineWidth=e.kind==='wall'?Math.max(1,zoom*.18):Math.max(1,zoom*.25);ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x+(e.edge==='n'?zoom:0),p.y+(e.edge==='w'?zoom:0));ctx.stroke();}}
 });
}
function ghost(point){const template=project.buildingLibrary?.find(b=>'template:'+b.id===$('building-preset').value);const c=template?structuredClone(template):{...catalog[buildingKind],kind:buildingKind};const b={...c,id:'preview',x:Math.floor(point.x-c.width/2),y:Math.floor(point.y-c.height/2)};if(!template){b.design={material:'wood',roof:'flat',shape:$('placement-shape').value,variant:project.seed,floors:[{cells:shapeCells(b.width,b.height,$('placement-shape').value),rooms:{'1':'livingroom'},edges:[],objects:[]}]};}return b;}
function paintAt(point,last){
 const size=+$('brush-size').value;
 if(tool==='zombies'){
  const radius=Math.max(0,Math.floor(size/8));const cx=Math.floor(point.x/8),cy=Math.floor(point.y/8),n=project.width/8;
  for(let y=Math.max(0,cy-radius);y<=Math.min(project.height/8-1,cy+radius);y++)for(let x=Math.max(0,cx-radius);x<=Math.min(n-1,cx+radius);x++){const i=y*n+x;history.touch('zombies',i,project.zombies[i]);project.zombies[i]=+$('zombie-density').value;}
 }else if(last)line(project,last.x,last.y,point.x,point.y,terrain,size,history);else paint(project,point.x,point.y,terrain,size,history);
 dirty=true;requestDraw();
}
canvas.addEventListener('contextmenu',e=>e.preventDefault());
canvas.addEventListener('pointerdown',e=>{
 if(busy)return;canvas.focus();const p=gridPoint(e);cursor=p;
 if(e.button===1||e.button===2||space||tool==='pan'){gesture={pan:true,last:{x:e.clientX,y:e.clientY}};canvas.setPointerCapture(e.pointerId);return;}
 if(isometric){toast('Для рисования переключитесь на вид сверху.');return;}if(!within(p))return;
 history.begin(project);
 if(tool==='building'){const draft=ghost(p);history.commit(project);task('Создаём здание…',async signal=>{const isTemplate=$('building-preset').value.startsWith('template:');const b=isTemplate?draft:await api('/api/building/design',{building:draft,levels:+$('placement-levels').value,basements:+$('placement-basements').value,seed:(project.seed+project.buildings.length)|0,shape:$('placement-shape').value},signal);validateDesign(b);if(!canPlace(project,b))throw Error('Здесь нельзя поставить здание: вода, пересечение или граница ячейки.');history.begin(project);b.id=crypto.randomUUID();b.modified=true;b.sourceId=null;b.levelsOrigin='manual';project.buildings.push(b);selectedId=b.id;selectedSpawn=-1;history.commit(project);changed();});return;}
 if(tool==='world-asset'||tool==='surface-asset'||tool==='erase-decor'){try{if(!['all','0'].includes($('map-level').value))throw Error('Декор и покрытия местности размещаются на наземном уровне.');if(tool==='world-asset'){if(!worldAsset)throw Error('Выберите игровой предмет.');const d={id:crypto.randomUUID(),x:p.x,y:p.y,asset:worldAsset.id,parts:structuredClone(worldAsset.parts)};validateProject({...project,decorations:[...project.decorations,d]});project.decorations.push(d);}else if(tool==='erase-decor'){project.decorations=project.decorations.filter(d=>!d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y));}else{if(!surfaceAsset)throw Error('Выберите покрытие из игры.');if(project.buildings.some(b=>containsPoint(b,p.x-b.x,p.y-b.y)))throw Error('Пол здания редактируется в мастерской.');const newTerrain=surfaceAsset.water?3:surfaceAsset.id.startsWith('blends_street_')?2:surfaceAsset.id.startsWith('blends_natural_')?0:6,index=p.y*project.width+p.x;if(newTerrain===3&&(project.spawns.some(s=>s.x===p.x&&s.y===p.y)||project.decorations.some(d=>d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y))))throw Error('Вода пересечётся с объектом или точкой появления.');history.touch('terrain',index);project.terrain[index]=newTerrain;project.surfaceTiles[index]=surfaceAsset.id;}history.commit(project);changed();}catch(e){history.commit(project);toast(e.message,true);}return;}
 if(tool==='spawn'){if(project.terrain[p.y*project.width+p.x]===3||project.buildings.some(b=>containsPoint(b,p.x-b.x,p.y-b.y))||project.decorations.some(d=>d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y))){toast('Поставьте точку на суше снаружи здания и декора.',true);}else if(project.spawns.length>=100){toast('Максимум 100 точек появления.',true);}else if(!project.spawns.some(s=>s.x===p.x&&s.y===p.y)){project.spawns.push(p);}history.commit(project);changed();return;}
 if(tool==='select'){selectedId=project.buildings.find(b=>containsVisiblePoint(b,p.x-b.x,p.y-b.y,$('map-level').value))?.id||null;selectedSpawn=selectedId||!['all','0'].includes($('map-level').value)?-1:project.spawns.findIndex(s=>Math.abs(s.x-p.x)<4&&Math.abs(s.y-p.y)<4);history.commit(project);update();return;}
 gesture={start:p,last:p};canvas.setPointerCapture(e.pointerId);if(tool==='brush'||tool==='zombies')paintAt(p);
});
canvas.addEventListener('pointermove',e=>{
 const p=gridPoint(e);cursor=p;$('coordinates').textContent=`X ${p.x} · Y ${p.y}`;
 if(gesture?.pan){pan.x+=e.clientX-gesture.last.x;pan.y+=e.clientY-gesture.last.y;gesture.last={x:e.clientX,y:e.clientY};requestDraw();return;}
 if(gesture&&within(p)&&(tool==='brush'||tool==='zombies')){paintAt(p,gesture.last);gesture.last=p;}requestDraw();
});
function finish(){
 if(!gesture)return;
 if(!gesture.pan&&cursor){const end={x:Math.max(0,Math.min(project.width-1,cursor.x)),y:Math.max(0,Math.min(project.height-1,cursor.y))};
  if(tool==='road')line(project,gesture.start.x,gesture.start.y,end.x,end.y,2,+$('brush-size').value,history);
  if(tool==='rectangle')for(let y=Math.min(gesture.start.y,end.y);y<=Math.max(gesture.start.y,end.y);y++)for(let x=Math.min(gesture.start.x,end.x);x<=Math.max(gesture.start.x,end.x);x++)paint(project,x,y,terrain,1,history);
  history.commit(project);changed();
 }
 gesture=null;requestDraw();
}
canvas.addEventListener('pointerup',finish);canvas.addEventListener('pointercancel',finish);canvas.addEventListener('lostpointercapture',finish);canvas.addEventListener('pointerleave',()=>{if(!gesture){cursor=null;requestDraw();}});
canvas.addEventListener('wheel',e=>{e.preventDefault();const r=canvas.getBoundingClientRect();scaleAt(e.deltaY<0?1.15:1/1.15,e.clientX-r.left,e.clientY-r.top);},{passive:false});
new ResizeObserver(entries=>{const r=entries[0].contentRect;viewport={width:r.width,height:r.height};const dpr=window.devicePixelRatio||1;canvas.width=Math.round(r.width*dpr);canvas.height=Math.round(r.height*dpr);fit();}).observe($('canvas-wrap'));
mini.addEventListener('pointerdown',e=>{if(isometric)return;const r=mini.getBoundingClientRect();pan.x=viewport.width/2-(e.clientX-r.left)/r.width*project.width*zoom;pan.y=viewport.height/2-(e.clientY-r.top)/r.height*project.height*zoom;requestDraw();});
document.querySelectorAll('[data-tool]').forEach(b=>b.onclick=()=>setTool(b.dataset.tool));
document.querySelectorAll('[data-terrain]').forEach(b=>b.onclick=()=>{terrain=+b.dataset.terrain;document.querySelectorAll('[data-terrain]').forEach(a=>a.classList.toggle('active',a===b));if(!['brush','rectangle'].includes(tool))setTool('brush');update();});
document.querySelectorAll('[data-building]').forEach(b=>b.onclick=()=>{buildingKind=b.dataset.building;$('building-preset').value=buildingKind;document.querySelectorAll('[data-building]').forEach(a=>a.classList.toggle('active',a===b));setTool('building');});
$('building-preset').onchange=()=>{if(kinds[$('building-preset').value])buildingKind=$('building-preset').value;setTool('building');};
$('import-building-template').onclick=()=>$('building-template-file').click();
$('building-template-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Читаем шаблон здания…',async signal=>{if(file.size>4_000_000)throw Error('Шаблон превышает 4 МБ.');const value=JSON.parse(await file.text());if(value.format!=='SimplePZBuilding'||![1,2,3].includes(value.version))throw Error('Это не шаблон SimplePZMapmaker.');const b=value.building;if(value.version<3&&usesAssets(b))throw Error('Оформление требует формат шаблона версии 3.');if(value.version===1&&minLevel(b)!==0)throw Error('Подвалы требуют формат шаблона версии 2.');b.attribution=combineAttribution(value.attribution??[],b.attribution??[]);validateDesign(b);await api('/api/building/validate',b,signal);project.buildingLibrary??=[];if(project.buildingLibrary.length>=64)throw Error('Библиотека заполнена.');const source=combineAttribution(project.attribution,b.attribution);if(source.length>30)throw Error('Слишком много источников географии.');history.begin(project);b.id=crypto.randomUUID();b.sourceId=null;b.sourceKind=null;b.modified=false;project.attribution=source;project.buildingLibrary.push(b);history.commit(project);changed();toast('Шаблон добавлен в библиотеку.');}).finally(()=>e.target.value='');};
$('edit-building').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId);if(!b)return;if(b.design)workshop.open(b);else task('Подготавливаем план здания…',async signal=>workshop.open(await api('/api/building/design',{building:structuredClone(b),levels:1,seed:project.seed,shape:'rect'},signal)));};
$('regenerate-building').onclick=()=>regenerate(selectedId);$('regenerate-buildings').onclick=()=>regenerate(null);
function regenerate(id){if(id===null&&!project.buildings.length)return;task('Создаём новые варианты зданий…',async signal=>{const result=await api('/api/buildings/regenerate',{project,id},signal);validateProject(result.project);history.begin(project);project.buildings=result.project.buildings;history.commit(project);changed();toast(`Обновлено: ${result.changed}; сохранено без изменений: ${result.skipped}.`);});}
for(const [id,out,suffix] of [['brush-size','brush-value',''],['zombie-density','zombie-value',''],['new-forest','new-forest-value','%'],['new-buildings','new-buildings-value','%']])$(id).oninput=()=>{$(out).textContent=$(id).value+suffix;update();};
for(const id of ['show-buildings','show-spawns','show-zombies'])$(id).onchange=requestDraw;
$('map-level').onchange=requestDraw;
$('toggle-grid').onclick=()=>{showGrid=!showGrid;$('toggle-grid').classList.toggle('active',showGrid);requestDraw();};
$('view-top').onclick=()=>{isometric=false;$('view-top').classList.add('active');$('view-iso').classList.remove('active');fit();};
$('view-iso').onclick=()=>{isometric=true;$('view-iso').classList.add('active');$('view-top').classList.remove('active');fit();};
$('fit-map').onclick=fit;$('zoom-in').onclick=()=>scaleAt(1.25);$('zoom-out').onclick=()=>scaleAt(.8);
$('undo').onclick=()=>{finish();if(history.undo(project)){selectedId=null;selectedSpawn=-1;changed();}};$('redo').onclick=()=>{if(history.redo(project)){selectedId=null;selectedSpawn=-1;changed();}};
$('project-name').onfocus=()=>history.begin(project);$('project-name').onchange=()=>{const value=$('project-name').value.trim();if(!value||/[\x00-\x1f\x7f]/.test(value)){toast('Введите название карты.',true);update();return;}project.name=value;history.commit(project);changed();};
$('project-name').onblur=()=>history.commit(project);
for(const [id,key] of [['world-x','worldX'],['world-y','worldY']])$(id).onchange=()=>{const value=+$(id).value;if(!Number.isInteger(value)||value<0||value>1000){toast('Координата ячейки: целое число от 0 до 1000.',true);update();return;}history.begin(project);project[key]=value;history.commit(project);changed();};
$('apply-building').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId);if(!b)return;const updated={...b,name:$('building-name').value,x:+$('building-x').value,y:+$('building-y').value,width:+$('building-width').value,height:+$('building-height').value};
 if(b.design&&(updated.width!==b.width||updated.height!==b.height)){toast('Размер этажей меняется в мастерской здания.',true);return;}
 if(!canPlace({...project,buildings:project.buildings.filter(a=>a!==b)},updated)){toast('Здание пересекается с объектами, водой или границей ячейки.',true);return;}history.begin(project);Object.assign(b,updated,{modified:true});history.commit(project);changed();};
function deleteSelection(){if(busy)return;history.begin(project);if(selectedId){project.buildings=project.buildings.filter(b=>b.id!==selectedId);selectedId=null;}else if(selectedSpawn>=0){if(project.spawns.length===1){toast('Нужна хотя бы одна точка появления.',true);history.commit(project);return;}project.spawns.splice(selectedSpawn,1);selectedSpawn=-1;}history.commit(project);changed();}
$('delete-building').onclick=deleteSelection;
document.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>$(b.dataset.close).close());
$('new-map').onclick=()=>{if(busy)return;$('new-dialog').showModal();};
$('new-form').onsubmit=e=>{e.preventDefault();task('Создаём карту…',async signal=>{const p=await api('/api/generate',{size:+$('new-size').value,seed:+$('new-seed').value,kind:$('new-kind').value,name:$('new-name').value,surfaceTheme:$('new-surface-theme').value,forestDensity:+$('new-forest').value,buildingDensity:+$('new-buildings').value,zombieDensity:+$('zombie-density').value},signal);adopt(p);$('new-dialog').close();toast('Карта создана. Можно рисовать.');});};
$('save-project').onclick=()=>{try{validateProject(project);download(new Blob([JSON.stringify(project)],{type:'application/json'}),project.name.replace(/[<>:"/\\|?*]/g,'_')+'.pzmap.json');$('save-state').textContent='Проект сохранён в файл';toast('Проект сохранён. Открывайте этот файл для продолжения.');}catch(e){toast(e.message,true);}};
$('load-project').onclick=()=>{if(!busy)$('project-file').click();};
$('project-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Открываем проект…',async signal=>{if(file.size>48_000_000)throw Error('Проект превышает 48 МБ.');const text=await file.text();signal.throwIfAborted();adopt(JSON.parse(text));toast('Проект открыт.');}).finally(()=>e.target.value='');};
$('open-help').onclick=()=>$('help-dialog').showModal();
$('open-import').onclick=()=>{if(!busy)$('import-dialog').showModal();};
function bounds(){if($('use-place-bounds').checked&&selectedPlaceBounds)return {...selectedPlaceBounds};const lat=+$('import-lat').value,lon=+$('import-lon').value,extent=+$('import-extent').value;const dy=extent/111320/2,dx=dy/Math.cos(lat*Math.PI/180);if(!Number.isFinite(dx)||Math.abs(lat)>84||Math.abs(lon)>179)throw Error('Проверьте координаты выбранного участка.');return {south:lat-dy,north:lat+dy,west:lon-dx,east:lon+dx};}
function preview(){try{const b=bounds();$('osm-preview').src=`https://www.openstreetmap.org/export/embed.html?bbox=${b.west},${b.south},${b.east},${b.north}&layer=mapnik`;$('osm-preview').parentElement.classList.add('loaded');}catch(e){toast(e.message,true);}}
$('refresh-preview').onclick=preview;for(const id of ['import-lat','import-lon','import-extent'])$(id).onchange=()=>{$('osm-preview').parentElement.classList.remove('loaded');$('osm-preview').removeAttribute('src');};
$('use-place-bounds').onchange=()=>{for(const id of ['import-lat','import-lon','import-extent'])$(id).disabled=$('use-place-bounds').checked;preview();};
$('search-form').onsubmit=e=>{e.preventDefault();task('Ищем место в OpenStreetMap…',async signal=>{const items=await api('/api/search',{query:$('place-query').value},signal);$('place-results').replaceChildren();if(!items.length)toast('Место не найдено. Уточните город или адрес.');for(const item of items){const b=document.createElement('button');b.type='button';b.textContent=item.display_name;b.onclick=()=>{
  selectedPlaceBounds=boundsFromSearchResult(item);$('use-place-bounds').disabled=!selectedPlaceBounds;$('use-place-bounds').checked=!!selectedPlaceBounds;
  $('import-lat').value=item.lat;$('import-lon').value=item.lon;
  if(selectedPlaceBounds){const metres=(selectedPlaceBounds.north-selectedPlaceBounds.south)*111320;$('import-size').value=metres>2000?'2048':metres>1000?'1024':'512';}
  else toast('Место слишком большое для полного импорта. Выберите участок вокруг центра.');
  for(const id of ['import-lat','import-lon','import-extent'])$(id).disabled=!!selectedPlaceBounds;
  preview();$('place-results').replaceChildren();
 };$('place-results').append(b);}});};
$('load-pilna').onclick=()=>task('Открываем карту Пильны…',async signal=>{const response=await fetch('/api/examples/pilna',{signal});if(!response.ok)throw Error('Пример Пильны не найден в этой сборке.');adopt(await response.json());toast('Пильна открыта. Можно редактировать и собрать мод.');});
$('import-osm-button').onclick=()=>task('Загружаем участок OSM. Это может занять до 100 секунд…',async signal=>{const keep=$('preserve-building-edits').checked&&Boolean(project.buildings.some(b=>b.modified)||project.decorations.length||Object.keys(project.surfaceTiles).length);if(keep&&+$('import-size').value!==project.width)throw Error('Для сохранения ручных зданий оставьте прежний размер карты.');const result=await api('/api/import/osm',{project:geographicBase(project,+$('import-size').value,keep),bounds:bounds(),preserveEdits:keep},signal);result.project.name=$('place-query').value.trim().slice(0,80)||'Карта из OSM';result.project.worldX=project.worldX;result.project.worldY=project.worldY;adopt(result.project);$('import-dialog').close();toast(`Импортировано объектов: ${result.importedFeatures}. ${result.warnings.join(' ')}`);});
$('import-file-button').onclick=()=>{if(!busy)$('geodata-file').click();};
$('geodata-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Читаем геоданные…',async signal=>{if(file.size>16_000_000)throw Error('Файл превышает 16 МБ.');const keep=$('preserve-building-edits').checked&&Boolean(project.buildings.some(b=>b.modified)||project.decorations.length||Object.keys(project.surfaceTiles).length);if(keep&&+$('import-size').value!==project.width)throw Error('Для сохранения ручных зданий оставьте прежний размер карты.');const result=await api('/api/import/geojson',{project:geographicBase(project,+$('import-size').value,keep),json:await file.text(),bounds:keep?project.bounds:null,preserveEdits:keep},signal);result.project.name=file.name.replace(/\.(geo)?json$/i,'').slice(0,80)||'Импорт';result.project.worldX=project.worldX;result.project.worldY=project.worldY;adopt(result.project);$('import-dialog').close();toast(`Импортировано объектов: ${result.importedFeatures}. ${result.warnings.join(' ')}`);}).finally(()=>e.target.value='');};
$('export-map').onclick=()=>{try{validateProject(project);$('export-summary').textContent=`${project.width/256} × ${project.height/256} ячеек; ${project.buildings.length} зданий; начало: ${project.worldX}, ${project.worldY}.`;$('export-dialog').showModal();}catch(e){toast(e.message,true);}};
$('download-mod').onclick=()=>task('Собираем карту для Build 42…',async signal=>{download(await api('/api/export',project,signal),'SimplePZMap.zip');$('export-dialog').close();toast('ZIP собран. Инструкция и исходный проект находятся внутри.');});
$('cancel-operation').onclick=()=>controller?.abort();
$('operation-dialog').addEventListener('cancel',e=>{e.preventDefault();controller?.abort();});
document.addEventListener('keydown',e=>{if(['INPUT','SELECT','TEXTAREA'].includes(e.target.tagName)||document.querySelector('dialog[open]'))return;
 if(e.code==='Space'){space=true;e.preventDefault();return;}if(busy)return;
 if(e.ctrlKey||e.metaKey){if(e.key.toLowerCase()==='z'){e.preventDefault();$(e.shiftKey?'redo':'undo').click();}else if(e.key.toLowerCase()==='s'){e.preventDefault();$('save-project').click();}else if(e.key.toLowerCase()==='o'){e.preventDefault();$('load-project').click();}return;}
 const keys={b:'brush',a:'rectangle',r:'road',h:'building',p:'spawn',v:'select',z:'zombies'};if(keys[e.key.toLowerCase()])setTool(keys[e.key.toLowerCase()]);if(e.key==='Delete')deleteSelection();
});document.addEventListener('keyup',e=>{if(e.code==='Space')space=false;});window.addEventListener('blur',()=>{space=false;finish();});
async function start(){
 let recovered=false;try{const response=await fetch('/api/recovery/'+recoveryId);if(response.ok){adopt(await response.json());recovered=true;status('Восстановлено автосохранение');}else{const text=localStorage.getItem(recoveryKey);if(text){adopt(JSON.parse(text));recovered=true;status('Переносим автосохранение в папку проекта');}}}catch{toast('Автосохранение недоступно. Откройте ранее сохранённый файл.',true);}
 if(!recovered)await task('Готовим первую карту…',async signal=>adopt(await api('/api/generate',{size:256,seed:42,kind:'town',name:'Тихий город'},signal)));
 try{const environment=await fetch('/api/environment').then(r=>r.json());$('game-status').textContent=environment.gameFound?'Project Zomboid найден':'Игра не найдена: экспорт доступен';}catch{$('game-status').textContent='Не удалось проверить игру';}
 update();
}
start();
