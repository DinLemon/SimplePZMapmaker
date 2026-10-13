import {cell,objectCells,objectParts,stairSpace,floorLevel} from './building.js';
import {SpriteImages} from './resources.js';

const $=id=>document.getElementById(id);
export function isoProject(x,y,z,t){return {x:t.x+64*(x-y)*t.scale,y:t.y+(32*(x+y)-192*z)*t.scale};}
export function isoHit(x,y,z,t){const a=(x-t.x)/(64*t.scale),b=((y-t.y)/t.scale+192*z)/32,fx=(a+b)/2+.5,fy=(b-a)/2+.5;return {x:Math.floor(fx),y:Math.floor(fy),fx,fy};}
export function nearestEdge(p){let x=p.x,y=p.y,edge='n';const distances=[p.fy-y,p.fx-x,1-(p.fy-y),1-(p.fx-x)],nearest=distances.indexOf(Math.min(...distances));if(nearest===1)edge='w';if(nearest===2)y++;if(nearest===3){x++;edge='w';}return {x,y,edge};}
export function zoomAt(t,p,scale){scale=Math.max(.035,Math.min(3,scale));const ratio=scale/t.scale;return {x:p.x-(p.x-t.x)*ratio,y:p.y-(p.y-t.y)*ratio,scale};}
const renderParts=o=>o.kind==='stairs'?Array.from({length:3},(_,i)=>({dx:o.direction==='w'?i+1:0,dy:o.direction==='n'?i+1:0,tile:'carpentry_02_'+((o.direction==='n'?98:90)-i)})):objectParts(o);
// Check the complete footprint before mutating. Draft connectivity is checked on apply.
export function placementCheck(b,level,o){
 const f=b.design.floors[level],cells=objectCells(o),fail=reason=>({ok:false,reason,cells});
 if(!cells.length||cells.some(p=>cell(b,f,p.x,p.y)==='.'))return fail('Предмет выходит за контур пола.');
 if(o.kind==='stairs'){
  const upper=b.design.floors[level+1];if(!upper)return fail('Лестнице нужен следующий этаж.');
  if(cells.some(p=>cell(b,upper,p.x,p.y)==='.'))return fail('Лестнице нужны площадки на обоих этажах.');
  for(const floor of [f,upper])if(floor.objects.some(item=>objectCells(item).some(p=>cells.some(q=>p.x===q.x&&p.y===q.y))))return fail('Площадка лестницы занята предметом.');
  if(cells.slice(1).some(p=>[f,upper].some(floor=>floor.edges.some(e=>e.x===p.x&&e.y===p.y&&e.edge===o.direction&&e.kind!=='open'))))return fail('Лестница пересекает стену или проём.');
 }else{
  if(cells.some(p=>stairSpace(b,level,p.x,p.y)))return fail('Предмет перекрывает лестницу или площадку.');
  if(f.objects.some(item=>objectCells(item).some(p=>cells.some(q=>p.x===q.x&&p.y===q.y))))return fail('Клетка занята: сначала уберите прежний предмет.');
  const occupied=new Set(cells.map(p=>`${p.x},${p.y}`));
  if(f.edges.some(e=>e.kind!=='open'&&occupied.has(`${e.x},${e.y}`)&&occupied.has(e.edge==='n'?`${e.x},${e.y-1}`:`${e.x-1},${e.y}`)))return fail('Цельный предмет пересекает стену или проём.');
  if(f.edges.some(e=>['door','open'].includes(e.kind)&&(occupied.has(`${e.x},${e.y}`)||occupied.has(e.edge==='n'?`${e.x},${e.y-1}`:`${e.x-1},${e.y}`))))return fail('Предмет перекрывает дверь или проход.');
 }
 return {ok:true,reason:'Можно разместить',cells};
}

export class VisualWorkshop{
 constructor(workshop){
  this.workshop=workshop;this.images=new SpriteImages();this.scene=null;this.transform={x:0,y:0,scale:.5};this.revision=0;this.timer=null;this.abort=null;this.hover=null;this.pan=null;this.drag=null;this.fitPending=true;this.mode='visual';this.allLevels=false;this.brushRevision=0;
  const canvas=this.canvas=$('be-visual-canvas');
  new ResizeObserver(()=>{if(this.fitPending){this.fit();this.fitPending=!this.scene;}this.draw();}).observe(canvas);
  $('be-view-visual').onclick=()=>this.setMode('visual');$('be-view-plan').onclick=()=>this.setMode('plan');
  $('be-visual-fit').onclick=()=>this.fit();
  for(const [id,factor] of [['be-visual-zoom-in',1.25],['be-visual-zoom-out',.8]])$(id).onclick=()=>{const r=canvas.getBoundingClientRect();this.transform=zoomAt(this.transform,{x:r.width/2,y:r.height/2},this.transform.scale*factor);this.fitPending=false;this.draw();};
  $('be-all-levels').onchange=()=>{this.allLevels=$('be-all-levels').checked;this.hover=null;this.fitPending=true;this.workshop.draw();this.schedule();this.setMode(this.mode);this.readonly();};
  canvas.addEventListener('wheel',e=>{e.preventDefault();const r=canvas.getBoundingClientRect();this.transform=zoomAt(this.transform,{x:e.clientX-r.left,y:e.clientY-r.top},this.transform.scale*Math.exp(-e.deltaY*.0015));this.fitPending=false;this.draw();},{passive:false});
  canvas.addEventListener('contextmenu',e=>e.preventDefault());
  canvas.addEventListener('pointerdown',e=>{
   if(!this.workshop.draft||this.workshop.saving)return;canvas.focus();
   if(e.button===1||e.button===2||e.altKey){e.preventDefault();this.pan={x:e.clientX,y:e.clientY,origin:{...this.transform}};canvas.setPointerCapture(e.pointerId);return;}
   if(e.button!==0||this.allLevels)return;const p=this.point(e);if(!this.inBounds(p))return;
   if(['room','footprint','erase','floor-tile'].includes(this.workshop.tool)){this.drag={start:p,end:p};canvas.setPointerCapture(e.pointerId);this.draw();}
   else this.workshop.change(()=>this.workshop.place(p));
  });
  canvas.addEventListener('pointermove',e=>{if(this.pan){this.transform={...this.pan.origin,x:this.pan.origin.x+e.clientX-this.pan.x,y:this.pan.origin.y+e.clientY-this.pan.y};this.fitPending=false;}else{this.hover=this.point(e);if(this.drag)this.drag.end=this.hover;}this.draw();});
  canvas.addEventListener('pointerup',()=>{this.pan=null;if(this.drag){const {start,end}=this.drag;this.drag=null;this.workshop.paintArea(start,end);}this.draw();});
  canvas.addEventListener('pointercancel',()=>{this.pan=null;this.drag=null;this.draw();});canvas.addEventListener('pointerleave',()=>{if(!this.drag&&!this.pan){this.hover=null;this.draw();}});
  $('building-dialog').addEventListener('close',()=>{clearTimeout(this.timer);this.abort?.abort();this.revision++;this.brushRevision++;this.scene=null;this.images.endScene();});
 }
 get z(){return floorLevel(this.workshop.draft,this.workshop.level);}
 point(e){const r=this.canvas.getBoundingClientRect();return isoHit(e.clientX-r.left,e.clientY-r.top,this.z,this.transform);}
 inBounds(p){const b=this.workshop.draft;return p.x>=0&&p.y>=0&&p.x<b.width&&p.y<b.height;}
 open(){this.allLevels=false;$('be-all-levels').checked=false;this.scene=null;this.hover=null;this.fitPending=true;this.setMode('visual');this.brush();this.schedule();}
 setMode(mode){this.mode=mode;$('be-visual-wrap').hidden=mode!=='visual';$('be-plan-wrap').hidden=mode!=='plan';for(const name of ['visual','plan']){$('be-view-'+name).classList.toggle('active',mode===name);$('be-view-'+name).setAttribute('aria-pressed',String(mode===name));}$('be-all-levels').disabled=mode==='plan';if(mode==='plan'&&this.allLevels){this.allLevels=false;$('be-all-levels').checked=false;this.schedule();}$('be-room-options').hidden=mode!=='plan';this.readonly();this.workshop.draw();this.draw();}
 readonly(){for(const element of document.querySelectorAll('#be-architecture input,#be-architecture select,#be-architecture button'))element.disabled=this.allLevels||this.workshop.saving;for(const id of ['be-name','be-kind','be-style','be-width','be-height','be-resize','be-shape','be-shape-apply','be-material','be-roof','be-add-floor','be-remove-floor','be-add-basement','be-remove-basement','be-auto-stairs','be-room','be-room-type','be-new-room','be-delete-room','be-object','be-direction','be-floor-all'])$(id).disabled=this.allLevels||this.workshop.saving;for(const button of document.querySelectorAll('[data-be-tool]'))button.disabled=this.allLevels||this.workshop.saving;$('be-direction').disabled||=['asset-object','static-model'].includes(this.workshop.tool);$('be-model-resource').disabled=this.allLevels||this.workshop.saving;$('be-undo').disabled=this.allLevels||this.workshop.saving||!this.workshop.history.past.length;$('be-redo').disabled=this.allLevels||this.workshop.saving||!this.workshop.history.future.length;}
 schedule(){
  if(!this.workshop.draft||!$('building-dialog').open)return;
  clearTimeout(this.timer);this.abort?.abort();this.revision++;this.scene=null;this.canvas.dataset.state='loading';this.state('Собираем текущий черновик…');this.draw();
  this.timer=setTimeout(()=>this.reload(this.revision),190);
 }
 async reload(revision){
  const abort=this.abort=new AbortController(),owner=this.images.beginScene(),b=structuredClone(this.workshop.draft),level=this.allLevels?null:this.z;
  try{
   const response=await fetch('/api/preview/building/draft',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({building:b,level}),signal:abort.signal});
   if(!response.ok){let error;try{error=(await response.json()).error;}catch{}throw Error(error||'Игровой вид черновика недоступен.');}
   const scene=await response.json();if(revision!==this.revision)return;
   if(!await this.images.load(scene.sprites.map(s=>s.tile),owner)||revision!==this.revision)return;
   this.scene={...scene,sprites:scene.sprites.map(s=>({...s,x:s.x-2,y:s.y-2})).sort((a,b)=>a.z-b.z||(a.x+a.y)-(b.x+b.y)||a.x-b.x)};
   const missing=new Set(scene.sprites.filter(s=>!this.images.meta.get(s.tile)?.sprite).map(s=>s.tile));
   this.canvas.dataset.state='ready';this.state(`${this.allLevels?'Здание целиком · редактирование отключено':`Этаж z=${level} · крыша скрыта`} · ${scene.sprites.length.toLocaleString('ru-RU')} спрайтов${missing.size?` · без изображения: ${missing.size}`:''}`,!!missing.size);
   if(this.fitPending)this.fit();this.draw();for(const f of b.design.floors)for(const model of f.staticModels||[])this.workshop.modelSprites.load(model).then(()=>{if(revision===this.revision)this.draw();});
  }catch(e){if(revision!==this.revision||e.name==='AbortError')return;this.scene=null;this.images.endScene();this.canvas.dataset.state='error';this.state('Черновик не отображён: '+e.message,true);this.draw();}
 }
 state(text,error=false){$('be-visual-state').textContent=text;$('be-visual-state').classList.toggle('error',error);}
 fit(){
  if(!this.workshop.draft)return;const r=this.canvas.getBoundingClientRect();if(!r.width||!r.height)return;
  const b=this.workshop.draft,z=this.z,points=[[0,0],[b.width,0],[0,b.height],[b.width,b.height]].map(([x,y])=>isoProject(x,y,z,{x:0,y:0,scale:1}));
  let left=Math.min(...points.map(p=>p.x))-64,right=Math.max(...points.map(p=>p.x))+64,top=Math.min(...points.map(p=>p.y))-200,bottom=Math.max(...points.map(p=>p.y))+48;
  for(const s of this.scene?.sprites||[]){const p=isoProject(s.x,s.y,s.z,{x:0,y:0,scale:1}),a=this.images.meta.get(s.tile)?.sprite;if(!a)continue;const x=p.x+a.offsetX-a.frameWidth/2,y=p.y+a.offsetY-a.frameHeight+32;left=Math.min(left,x);right=Math.max(right,x+a.width);top=Math.min(top,y);bottom=Math.max(bottom,y+a.height);}
  const scale=Math.max(.035,Math.min(1.5,(r.width-36)/(right-left),(r.height-36)/(bottom-top)));this.transform={scale,x:(r.width-(right-left)*scale)/2-left*scale,y:(r.height-(bottom-top)*scale)/2-top*scale};this.fitPending=false;this.draw();
 }
 changedLevel(){this.hover=null;this.fitPending=true;}
 async brush(){
  const w=this.workshop,revision=++this.brushRevision,o=this.objectAt({x:0,y:0}),ids=o?renderParts(o).map(p=>p.tile).filter(Boolean):w.tool==='floor-tile'?[w.floorTile].filter(Boolean):['wall','door','window'].includes(w.tool)?[w[w.tool+'Pair']?.n,w[w.tool+'Pair']?.w].filter(Boolean):[];
  $('be-direction').disabled=['asset-object','static-model'].includes(w.tool)||this.allLevels||w.saving;
  const label=({room:'Назначение комнаты',footprint:'Контур пола',erase:'Удаление пола',open:'Свободный проход',wall:'Стена из игры',door:'Дверь из игры',window:'Окно из игры','floor-tile':'Пол из игры','erase-object':'Удаление предмета',stairs:'Лестница из игры',object:'Мебель из игры','static-model':'3D предмет мира'})[w.tool]||w.asset?.name||w.tool;
  $('be-brush-name').textContent=w.tool==='static-model'?(w.staticModel?.modelId||'Выберите модель'):w.tool==='asset-object'?(w.asset?.name||'Выберите предмет'):w.tool==='floor-tile'?(w.floorTile||'Выберите пол'):['wall','door','window'].includes(w.tool)?(w[w.tool+'Pair']?.id||label):label;
  $('be-brush-status').textContent=w.tool==='static-model'?(['obstacle','destructible','container'].includes(w.staticModel?.profile)?'Статичный объект с коллизией на одну клетку. Проверьте проходы и лестницы.':'Статичный предмет без коллизии. Поворот и масштаб задаются в каталоге.'):w.tool==='asset-object'?`Цельный предмет · ${w.asset?.parts.length||0} клеток · направление ${w.asset?.facing||'из ресурса'}; для другого направления выберите его вариант`:'Стены и проёмы: наведите на нужную грань клетки. N/W выбирается по грани.';
  const canvas=$('be-brush-canvas'),ctx=canvas.getContext('2d');ctx.clearRect(0,0,canvas.width,canvas.height);
  try{if(ids.length)await this.images.load(ids);if(revision!==this.brushRevision)return;
   if(o){const parts=renderParts(o).filter(p=>p.tile);let left=Infinity,top=Infinity,right=-Infinity,bottom=-Infinity;for(const part of parts){const a=this.images.meta.get(part.tile)?.sprite;if(!a)continue;const p=isoProject(part.dx,part.dy,0,{x:0,y:0,scale:1}),x=p.x+a.offsetX-a.frameWidth/2,y=p.y+a.offsetY-a.frameHeight+32;left=Math.min(left,x);top=Math.min(top,y);right=Math.max(right,x+a.width);bottom=Math.max(bottom,y+a.height);}if(Number.isFinite(left)){const scale=Math.min((canvas.width-12)/(right-left),(canvas.height-12)/(bottom-top));for(const p of parts){const a=isoProject(p.dx,p.dy,0,{x:(canvas.width-(right-left)*scale)/2-left*scale,y:(canvas.height-(bottom-top)*scale)/2-top*scale,scale});this.images.draw(ctx,p.tile,a.x,a.y,scale);}}}
   else{const id=ids.find(id=>this.images.meta.get(id)?.sprite);if(id)this.images.thumbnail(canvas,this.images.meta.get(id));}
   this.draw();
  }catch(e){if(revision===this.brushRevision)$('be-brush-status').textContent='Изображение кисти недоступно: '+e.message;}
 }
 objectAt(p){const w=this.workshop;if(w.tool==='asset-object'&&w.asset)return {x:p.x,y:p.y,kind:'asset',direction:'n',asset:w.asset.id,parts:w.asset.parts};if(['object','stairs'].includes(w.tool))return {x:p.x,y:p.y,kind:w.tool==='stairs'?'stairs':$('be-object').value,direction:$('be-direction').value,style:w.tool==='stairs'?'':w.draft?.design.style||''};return null;}
 diamond(ctx,x,y,z,fill,stroke){const p=[[x-.5,y-.5],[x+.5,y-.5],[x+.5,y+.5],[x-.5,y+.5]].map(([a,b])=>isoProject(a,b,z,this.transform));ctx.beginPath();p.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.closePath();if(fill){ctx.fillStyle=fill;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.stroke();}}
 draw(){
  const w=this.workshop,canvas=this.canvas;if(!w.draft||!$('building-dialog').open||this.mode!=='visual')return;const r=canvas.getBoundingClientRect();if(!r.width||!r.height)return;const dpr=window.devicePixelRatio||1;canvas.width=Math.round(r.width*dpr);canvas.height=Math.round(r.height*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.fillStyle='#111b18';ctx.fillRect(0,0,r.width,r.height);ctx.imageSmoothingEnabled=this.transform.scale<.45;
  Object.assign(canvas.dataset,{originX:String(this.transform.x),originY:String(this.transform.y),scale:String(this.transform.scale),level:String(this.z)});
  $('be-visual-zoom').textContent=Math.round(this.transform.scale*100)+'%';
  ctx.lineWidth=1;const b=w.draft;for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)this.diamond(ctx,x,y,this.z,cell(b,w.floor,x,y)==='.'?'#14231d':'#21342b','#8baf8730');
  for(const s of this.scene?.sprites||[]){const p=isoProject(s.x,s.y,s.z,this.transform);this.images.draw(ctx,s.tile,p.x,p.y,this.transform.scale);}
  for(let i=0;i<b.design.floors.length;i++){const z=floorLevel(b,i);if(!this.allLevels&&z!==this.z)continue;for(const model of b.design.floors[i].staticModels||[]){const p=isoProject(model.x,model.y,z,this.transform);w.modelSprites.draw(ctx,model,p.x,p.y,Math.max(14,100*this.transform.scale));}}
  if(this.allLevels){delete canvas.dataset.hoverCell;return;}
  if(this.drag){const {start,end}=this.drag;for(let y=Math.max(0,Math.min(start.y,end.y));y<=Math.min(b.height-1,Math.max(start.y,end.y));y++)for(let x=Math.max(0,Math.min(start.x,end.x));x<=Math.min(b.width-1,Math.max(start.x,end.x));x++)this.diamond(ctx,x,y,this.z,'#ecd39250','#ecd392');}
  if(!this.hover||!this.inBounds(this.hover)){delete canvas.dataset.hoverCell;return;}
  const p=this.hover,o=this.objectAt(p);let ok=true,reason='';canvas.dataset.hoverCell=`${p.x},${p.y}`;
  if(w.tool==='static-model'&&w.staticModel){ok=cell(b,w.floor,p.x,p.y)!=='.';reason=ok?(['obstacle','destructible','container'].includes(w.staticModel.profile)?'3D объект · коллизия одной клетки':'3D предмет · без коллизии'):'Нужна клетка пола';this.diamond(ctx,p.x,p.y,this.z,ok?'#a0c7ee70':'#df836970','#c5d9eb');const anchor=isoProject(p.x,p.y,this.z,this.transform);ctx.globalAlpha=.65;w.modelSprites.draw(ctx,w.staticModel,anchor.x,anchor.y,Math.max(14,100*this.transform.scale));ctx.globalAlpha=1;}
  else if(o){const check=placementCheck(b,w.level,o);ok=check.ok;reason=check.reason;for(const c of check.cells)this.diamond(ctx,c.x,c.y,this.z,ok?'#a0d6aa70':'#df836970',ok?'#c5ebbd':'#ffaf8b');ctx.globalAlpha=.65;for(const part of renderParts(o).filter(p=>p.tile)){const a=isoProject(o.x+part.dx,o.y+part.dy,this.z,this.transform);this.images.draw(ctx,part.tile,a.x,a.y,this.transform.scale);}ctx.globalAlpha=1;
  }else if(['wall','door','window','open'].includes(w.tool)){
   const edge=nearestEdge(p),other=edge.edge==='n'?cell(b,w.floor,edge.x,edge.y-1):cell(b,w.floor,edge.x-1,edge.y);ok=cell(b,w.floor,edge.x,edge.y)!=='.'||other!=='.';reason=ok?`Грань ${edge.edge.toUpperCase()}`:'Грань вне пола';
   const a=isoProject(edge.x-.5,edge.y-.5,this.z,this.transform),c=isoProject(edge.x-.5+(edge.edge==='n'?1:0),edge.y-.5+(edge.edge==='w'?1:0),this.z,this.transform);ctx.strokeStyle=ok?'#f5d29b':'#e8a38c';ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(c.x,c.y);ctx.stroke();const tile=w[w.tool+'Pair']?.[edge.edge];if(tile){ctx.globalAlpha=.65;const anchor=isoProject(edge.x,edge.y,this.z,this.transform);this.images.draw(ctx,tile,anchor.x,anchor.y,this.transform.scale);ctx.globalAlpha=1;}
  }else{ok=w.tool!=='floor-tile'||cell(b,w.floor,p.x,p.y)!=='.';this.diamond(ctx,p.x,p.y,this.z,ok?'#d5c49360':'#df836970','#efdaa6');if(w.tool==='floor-tile'&&w.floorTile){const a=isoProject(p.x,p.y,this.z,this.transform);ctx.globalAlpha=.7;this.images.draw(ctx,w.floorTile,a.x,a.y,this.transform.scale);ctx.globalAlpha=1;}}
  canvas.dataset.hoverValid=String(ok);$('be-hover-state').textContent=`Клетка ${p.x}, ${p.y} · ${reason||'Нажмите или протяните область'}`;
 }
}
