import {nativeSpriteOrder} from './native-sprite-order.js';
import {minZ,maxZ} from './z-levels.js';
import {ModResources} from './mod-resources.js';
export const categories={floors:'Floors and surfaces',walls:'Walls and facades',doors:'Doors',windows:'Windows',roofs:'Roofs',furniture:'Furniture',appliances:'Household appliances',lighting:'Lighting fixtures',vegetation:'Vegetation',fences:'Fences',decoration:'Street decor',stairs:'Stairs',other:'Other resources'};
const $=id=>document.getElementById(id);
// Shared by embedded, modal and native palettes. Raw search remains the API default.
export function resourceSearchUrl({role='object',q='',category='',source='',facing='',offset=0,limit=36,rawTiles=false}={}){
 const query=new URLSearchParams({q,category,source,offset:String(offset),limit:String(limit),wholeObjects:String(['object','world','browse'].includes(role)&&!rawTiles)});
 if(facing)query.set('facing',facing);return '/api/assets/search?'+query;
}
// Mount once next to the palette's existing filters. Native editor owns its paging
// reset: call onChange, set offset=0 and rerun search with controls.options(role).
export function createResourcePaletteControls({onChange=()=>{}}={}){
 const element=document.createElement('fieldset'),legend=document.createElement('legend');legend.textContent='Object palette';
 const rawLabel=document.createElement('label'),raw=document.createElement('input');raw.type='checkbox';rawLabel.append(raw,document.createTextNode('Advanced: raw tiles'));rawLabel.title='Browse individual sprites. Object tools still validate and assemble the full footprint.';
 const faceLabel=document.createElement('label'),facing=document.createElement('select');faceLabel.append(document.createTextNode('Orientation '),facing);facing.setAttribute('aria-label','Object orientation');
 for(const [value,text] of [['','All orientations'],['N','North'],['E','East'],['S','South'],['W','West']])facing.add(new Option(text,value));
 element.append(legend,rawLabel,faceLabel);raw.onchange=facing.onchange=()=>{facing.disabled=raw.checked;onChange();};
 return {element,options:()=>({rawTiles:raw.checked,facing:facing.value}),setRole(role){element.hidden=!['object','world','browse'].includes(role);facing.disabled=raw.checked;}};
}
async function json(url,body){const r=await fetch(url,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(!r.ok)throw Error((await r.json()).error||'Failed to read game resources.');return r.json();}
export function resourceAllowed(a,role){if(!a.page)return false;if(role==='browse')return true;if(['object','world'].includes(role))return a.placeable;if(role==='surface')return a.floor;if(role==='floor')return a.floor&&!a.water;return a.roles?.includes(role)||false;}
export async function resolveResource(a,role){if(['object','world'].includes(role))return {...a,...await json('/api/assets/placement/'+encodeURIComponent(a.id))};if(['wall','door','window','roof'].includes(role))return {...a,...await json('/api/assets/pair/'+encodeURIComponent(a.id)+'?role='+role)};const full=await json('/api/assets/item/'+encodeURIComponent(a.id));return {...a,properties:full.properties};}
export class SpriteImages{
 constructor(){this.meta=new Map();this.pages=new Map();this.pending=new Map();this.bytes=0;this.budget=192*1024*1024;this.active=0;this.waiters=[];this.revision=0;this.sceneOwner=null;this.pinned=new Set();}
 clear(){this.revision++;this.meta.clear();this.pages.clear();this.pending.clear();this.bytes=0;this.endScene();}
 beginScene(){this.sceneOwner={};this.pinned.clear();return this.sceneOwner;}
 endScene(){this.sceneOwner=null;this.pinned.clear();this.trim();}
 trim(){for(const [key,img] of this.pages){if(this.bytes<=this.budget)break;if(this.pinned.has(key))continue;this.pages.delete(key);this.bytes-=img.naturalWidth*img.naturalHeight*4;}}
 async page(key){if(this.pages.has(key)){const img=this.pages.get(key);this.pages.delete(key);this.pages.set(key,img);return img;}if(this.pending.has(key))return this.pending.get(key);const revision=this.revision;const task=(async()=>{
  if(this.active>=2)await new Promise(resolve=>this.waiters.push(resolve));else this.active++;
  try{if(revision!==this.revision)return;const img=new Image();img.src='/api/assets/page/'+key;await img.decode();if(revision!==this.revision)return;const bytes=img.naturalWidth*img.naturalHeight*4;if(bytes>this.budget)throw Error('Atlas exceeds preview memory budget.');this.pages.set(key,img);this.bytes+=bytes;this.trim();return img;
  }finally{const next=this.waiters.shift();if(next)next();else this.active--;}
 })();this.pending.set(key,task);try{return await task;}finally{if(this.pending.get(key)===task)this.pending.delete(key);}}
 async describe(ids){const revision=this.revision;const unique=[...new Set(ids)],missing=unique.filter(id=>!this.meta.has(id));for(let i=0;i<missing.length;i+=1500){const batch=missing.slice(i,i+1500),result=await json('/api/assets/describe',batch);if(revision!==this.revision)return false;for(const id of batch)this.meta.set(id,null);for(const a of result)this.meta.set(a.id,a);}return revision===this.revision;}
 async load(ids,owner=null){const revision=this.revision,unique=[...new Set(ids)];if(!await this.describe(unique))return false;const infos=new Map(unique.map(id=>this.meta.get(id)).filter(a=>a?.page).map(a=>[a.page,a]));if(owner){if(owner!==this.sceneOwner)return false;const needed=[...infos.values()].reduce((n,a)=>n+a.pageWidth*a.pageHeight*4,0);if(!Number.isFinite(needed)||needed>this.budget)throw Error('These resources require more than 192MB of atlases. Choose one floor or a smaller area.');this.pinned=new Set(infos.keys());}
  await Promise.all([...infos.keys()].map(key=>this.page(key)));return revision===this.revision&&(!owner||owner===this.sceneOwner);
 }
 draw(ctx,id,x,y,scale){const a=this.meta.get(id),s=a?.sprite,image=a&&this.pages.get(a.page);if(!s||!(image instanceof HTMLImageElement))return false;ctx.drawImage(image,s.x,s.y,s.width,s.height,x+(s.offsetX-s.frameWidth/2)*scale,y+(s.offsetY-s.frameHeight+32)*scale,s.width*scale,s.height*scale);return true;}
 async thumbnail(canvas,a){
  const revision=this.revision;try{if(!a.sprite||!a.page)throw Error('Missing image');this.meta.set(a.id,a);
   const parts=a.wholeObject?a.parts:[{dx:0,dy:0,tile:a.id}];if(!parts?.length)throw Error('Missing object parts');
   if(!await this.load(parts.map(p=>p.tile))||!canvas.isConnected||revision!==this.revision)return;
   const sprites=parts.map(p=>{const info=this.meta.get(p.tile),s=info?.sprite,img=this.pages.get(info?.page);if(!s||!img)throw Error('Missing part image');return {s,img,x:(p.dx-p.dy)*64+s.offsetX-s.frameWidth/2,y:(p.dx+p.dy)*32+s.offsetY-s.frameHeight+32,depth:p.dx+p.dy,dx:p.dx};}).sort((a,b)=>a.depth-b.depth||a.dx-b.dx);
   const minX=Math.min(...sprites.map(p=>p.x)),minY=Math.min(...sprites.map(p=>p.y)),maxX=Math.max(...sprites.map(p=>p.x+p.s.width)),maxY=Math.max(...sprites.map(p=>p.y+p.s.height));
   const scale=Math.min((canvas.width-8)/(maxX-minX),(canvas.height-8)/(maxY-minY)),ox=(canvas.width-(maxX-minX)*scale)/2,oy=(canvas.height-(maxY-minY)*scale)/2,ctx=canvas.getContext('2d');ctx.clearRect(0,0,canvas.width,canvas.height);
   for(const {s,img,x,y} of sprites)ctx.drawImage(img,s.x,s.y,s.width,s.height,ox+(x-minX)*scale,oy+(y-minY)*scale,s.width*scale,s.height*scale);
  }catch{if(canvas.isConnected&&revision===this.revision)canvas.setAttribute('aria-label','Image not available');}
 }
}
export const spriteImages=new SpriteImages();
function sourceBadge(a){const badge=document.createElement('small');badge.className='resource-source-badge';badge.textContent=a.modId?'Mod: '+a.modId:'Project Zomboid';return badge;}
export function createResourceCard(a,{role='object',images=spriteImages,onChoose=()=>{},className='asset-card',selected=false,width=100,height=94}={}){
 const button=document.createElement('button');button.type='button';button.className=className;button.dataset.assetId=a.id;button.dataset.nativeAsset=a.id;button.dataset.role=role;button.classList.toggle('active',selected);button.disabled=!resourceAllowed(a,role);
 button.title=[a.name,a.id,a.group,a.facing?`Direction ${a.facing}`:'',a.wholeObject?`Assembled object · ${a.parts.length} tiles`:a.grid?`Raw sprite part ${a.grid}`:'Raw tile',a.modId?`Mod: ${a.modId}`:'Project Zomboid'].filter(Boolean).join('\n');
 const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;canvas.setAttribute('role','img');canvas.setAttribute('aria-label',a.wholeObject?`${a.name}, ${a.parts.length} assembled tiles`:a.name||a.id);
 const label=document.createElement('strong');label.textContent=[a.group,a.name].filter(Boolean).join(' · ')||a.id;
 const detail=document.createElement('small');detail.textContent=[a.facing,a.wholeObject?`${a.parts.length} tiles`:a.grid?'Raw part':'',a.id,!a.page?'no picture':''].filter(Boolean).join(' · ');
 button.append(canvas,label,detail,sourceBadge(a));button.onclick=()=>onChoose(a);images.thumbnail(canvas,a);return button;
}
function sourceFilter(id){const select=document.createElement('select');select.id=id;select.setAttribute('aria-label','Resource source');select.append(new Option('Game and mods',''),new Option('Game only','game'),new Option('All active mods','mod'));return select;}
async function updateSources(select){const previous=select.value,state=await json('/api/assets/mods');select.replaceChildren(new Option('Game and mods',''),new Option('Game only','game'),new Option('All active mods','mod'));for(const mod of state.mods.filter(m=>state.active.includes(m.id)))select.add(new Option(mod.name,mod.id));select.value=[...select.options].some(o=>o.value===previous)?previous:'';}
export class ResourcePicker{
 constructor({pickWorld,pickSurface,message}){this.pickWorld=pickWorld;this.pickSurface=pickSurface;this.message=message;this.role='browse';this.callback=null;this.offset=0;this.revision=0;this.timer=null;
  this.provider=sourceFilter('asset-provider');$('asset-role').before(this.provider);this.provider.onchange=()=>{this.offset=0;this.search();};this.mods=new ModResources({changed:async()=>{spriteImages.clear();this.offset=0;await this.source();await this.search();}});
  this.paletteControls=createResourcePaletteControls({onChange:()=>{this.offset=0;this.search();}});$('asset-grid').before(this.paletteControls.element);
  for(const [value,name] of Object.entries(categories))$('asset-category').add(new Option(name,value));
  $('asset-close').onclick=()=>$('asset-dialog').close();$('asset-search').oninput=()=>{clearTimeout(this.timer);this.timer=setTimeout(()=>{this.offset=0;this.search();},220);};$('asset-category').onchange=()=>{this.offset=0;this.search();};$('asset-prev').onclick=()=>{this.offset=Math.max(0,this.offset-60);this.search();};$('asset-next').onclick=()=>{this.offset+=60;this.search();};
  $('asset-role').onchange=()=>{this.role=$('asset-role').value;this.callback=this.role==='world'?pickWorld:this.role==='surface'?pickSurface:null;this.offset=0;this.search();};
  $('asset-refresh').onclick=()=>this.loadInstallation();$('asset-source').addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();this.loadInstallation();}});$('asset-detect').onclick=()=>this.findInstallations();$('asset-installations').onchange=()=>{$('asset-source').value=$('asset-installations').value;};
 }
 setInstallationBusy(value){this.installationBusy=value;for(const id of ['asset-refresh','asset-detect','asset-installations'])$(id).disabled=value;}
 async loadInstallation(){if(this.installationBusy)return;this.setInstallationBusy(true);try{this.status('Reading the selected game folder…');await json('/api/assets/refresh',{gamePath:$('asset-source').value.trim()});spriteImages.clear();document.dispatchEvent(new CustomEvent('assets-catalog-changed'));const state=await this.source();if($('game-status'))$('game-status').textContent=state.ready?'Game resources connected':'Select the game folder';await this.search();}catch(e){this.status(e.message,true);}finally{this.setInstallationBusy(false);}}
 async findInstallations(){if(this.installationBusy)return;this.setInstallationBusy(true);let load=false;try{this.status('Reading registered Steam libraries…');const result=await json('/api/assets/installations');$('asset-installations').replaceChildren(...result.installations.map(i=>new Option(i.gamePath+' · '+i.source,i.gamePath)));$('asset-detection').hidden=!result.installations.length;if(!result.installations.length)this.status('No Build 42 installation found in registered Steam libraries. Paste the game folder path above and choose Load folder.');else{const current=$('asset-source').value;const selected=result.installations.find(i=>i.gamePath.toLowerCase()===current.toLowerCase())??result.installations[0];$('asset-installations').value=selected.gamePath;$('asset-source').value=selected.gamePath;load=result.installations.length===1;this.status(load?'Installation found. Loading resources…':result.installations.length+' installations found. Choose one and select Load folder.');}}catch(e){this.status(e.message,true);}finally{this.setInstallationBusy(false);}if(load)await this.loadInstallation();}
 async open(role='browse',callback=null){this.role=role;this.callback=callback;this.offset=0;$('asset-role').value=['browse','world','surface'].includes(role)?role:'browse';$('asset-role').disabled=!!callback;$('asset-category').value=({floor:'floors',surface:'floors',wall:'walls',door:'doors',window:'windows',roof:'roofs',object:'furniture'})[role]||'';$('asset-search').value='';$('asset-dialog').showModal();try{const state=await this.source();if(state.ready)await this.search();else{$('asset-grid').replaceChildren();this.status(state.error||'Paste the game folder path or find an installed Steam copy.');}}catch(e){this.status(e.message,true);}}
 async source(){const s=await json('/api/assets/status');await updateSources(this.provider);$('asset-source').value=s.gamePath;$('asset-source-state').textContent=s.ready?`${s.count.toLocaleString('en-GB')} resources · images are read only from this computer`:s.error||'Select game installation.';return s;}
 status(text,error=false){$('asset-status').textContent=text;$('asset-status').classList.toggle('error',error);}
 allowed(a){return resourceAllowed(a,this.role);}
 async search(){const revision=++this.revision;this.paletteControls.setRole(this.role);this.status('Reading the catalog...');try{const data=await json(resourceSearchUrl({role:this.role,q:$('asset-search').value,category:$('asset-category').value,source:this.provider.value,offset:this.offset,limit:60,...this.paletteControls.options()}));if(revision!==this.revision)return;$('asset-grid').replaceChildren();for(const a of data.items)$('asset-grid').append(createResourceCard(a,{role:this.role,images:spriteImages,onChoose:asset=>this.choose(asset),width:120,height:130}));$('asset-prev').disabled=this.offset===0;$('asset-next').disabled=this.offset+60>=data.total;this.status(`${Math.min(this.offset+1,data.total)}–${Math.min(this.offset+60,data.total)} from ${data.total.toLocaleString('en-GB')} · ${this.role==='browse'?'Select a resource role to use this asset.':'Select a resource. Multi-tile objects are assembled automatically.'}`);}catch(e){if(revision===this.revision)this.status(e.message,true);}}
 async choose(a){try{if(this.role==='browse'){this.status(`${a.name} · ${a.id}${a.group?' · '+a.group:''}${a.facing?' · '+a.facing:''}. Select “Map decor” or “Map surface” to place resources.`);return;}await this.callback?.(await resolveResource(a,this.role));$('asset-dialog').close();}catch(e){this.status(e.message,true);}}
}
// The workshop palette keeps the current scene visible while a brush is selected.
export class EmbeddedResourcePalette{
 constructor({choose,images}){
  this.choose=choose;this.images=images;this.role='object';this.offset=0;this.revision=0;this.selectionRevision=0;this.recent=[];this.abort=null;this.timer=null;this.selected=null;
  this.provider=sourceFilter('be-palette-provider');const providerLabel=document.createElement('label');providerLabel.textContent='Source';providerLabel.append(this.provider);$('be-palette-category').parentElement.after(providerLabel);this.provider.onchange=()=>{this.offset=0;this.search();};document.addEventListener('assets-catalog-changed',()=>{this.images.clear();this.recent=[];this.selected=null;this.selectionRevision++;if($('building-dialog').open)this.search();});
  this.paletteControls=createResourcePaletteControls({onChange:()=>{this.offset=0;this.selectionRevision++;this.search();}});$('be-palette-grid').before(this.paletteControls.element);
  for(const [id,name] of Object.entries(categories))$('be-palette-category').add(new Option(name,id));
  $('be-palette-role').onchange=()=>this.open($('be-palette-role').value);
  $('be-palette-category').onchange=()=>{this.offset=0;this.search();};
  $('be-palette-search').oninput=()=>{clearTimeout(this.timer);this.timer=setTimeout(()=>{this.offset=0;this.search();},220);};
  $('be-palette-prev').onclick=()=>{this.offset=Math.max(0,this.offset-36);this.search();};$('be-palette-next').onclick=()=>{this.offset+=36;this.search();};
  $('building-dialog').addEventListener('close',()=>{this.abort?.abort();clearTimeout(this.timer);this.revision++;this.selectionRevision++;});
 }
 open(role='object'){
  this.role=role;this.offset=0;this.selectionRevision++;$('be-palette-role').value=role;$('be-palette-category').value=({floor:'floors',wall:'walls',door:'doors',window:'windows',roof:'roofs',object:'furniture'})[role]||'';$('be-palette-search').value='';this.search();
 }
 status(text,error=false){$('be-palette-status').textContent=text;$('be-palette-status').classList.toggle('error',error);}
 async search(){
  this.abort?.abort();this.paletteControls.setRole(this.role);const abort=this.abort=new AbortController(),revision=++this.revision;this.status('Reading the resources of the installed game...');
  try{await updateSources(this.provider);if(revision!==this.revision)return;const response=await fetch(resourceSearchUrl({role:this.role,q:$('be-palette-search').value,category:$('be-palette-category').value,source:this.provider.value,offset:this.offset,limit:36,...this.paletteControls.options()}),{signal:abort.signal});if(!response.ok)throw Error('The game catalog is unavailable. Open Game Resources and check the path.');const data=await response.json();if(revision!==this.revision)return;
   $('be-palette-grid').replaceChildren();for(const a of data.items)$('be-palette-grid').append(this.card(a,this.role));$('be-palette-prev').disabled=this.offset===0;$('be-palette-next').disabled=this.offset+36>=data.total;
   this.status(data.total?`${this.offset+1}–${Math.min(this.offset+36,data.total)} / ${data.total.toLocaleString('en-GB')} · gray resources cannot be placed with this tool`:'No resources found. Change your search or category.');this.renderRecent();
  }catch(e){if(revision===this.revision&&e.name!=='AbortError')this.status(e.message,true);}
 }
 card(a,role,recent=false){
  return createResourceCard(a,{role,images:this.images,selected:this.selected===a.id,onChoose:asset=>this.select(asset,role)});
 }
 async select(a,role){
  const revision=++this.selectionRevision;this.status('Collecting a solid resource...');
  try{const value=await resolveResource(a,role);if(revision!==this.selectionRevision||!$('building-dialog').open)return;if(await this.choose(role,value)===false){this.status('Placement is disabled in “Whole building with roof” mode.',true);return;}this.selected=a.id;this.recent=[{asset:a,role},...this.recent.filter(r=>r.asset.id!==a.id)].slice(0,8);for(const button of $('be-palette-grid').querySelectorAll('.asset-card'))button.classList.toggle('active',button.dataset.assetId===a.id);this.renderRecent();this.status(role==='object'?`Brush: ${value.name} · ${value.parts.length} tiles, direction ${a.facing||'from the resource'}`:`Brush: ${value.id} · click on the building`);
  }catch(e){if(revision===this.selectionRevision)this.status(e.message,true);}
 }
 renderRecent(){$('be-palette-recent').replaceChildren();for(const r of this.recent)$('be-palette-recent').append(this.card(r.asset,r.role,true));$('be-recent-section').hidden=!this.recent.length;}
}
export class GamePreview{
 constructor(){this.scene=null;this.loader=null;this.revision=0;$('scene-close').onclick=()=>$('scene-dialog').close();$('scene-level').onchange=()=>this.reload();new ResizeObserver(()=>this.draw()).observe($('scene-canvas'));$('scene-dialog').addEventListener('close',()=>{this.revision++;this.scene=null;spriteImages.endScene();});}
 async open(title,loader,min=minZ,max=maxZ,selected='all'){this.loader=loader;$('scene-title').textContent=title;$('scene-level').replaceChildren(new Option('All levels','all'));for(let z=min;z<=max;z++)$('scene-level').add(new Option(z<0?`Basement ${z}`:`Floor ${z+1}`,String(z)));$('scene-level').value=selected;$('scene-dialog').showModal();await this.reload();}
 async reload(){const revision=++this.revision,owner=spriteImages.beginScene();this.scene=null;this.draw();$('scene-state').textContent='Assembling the layout from the exporter...';try{const value=$('scene-level').value;const scene=await this.loader(value==='all'?null:Number(value));if(revision!==this.revision)return;if(!await spriteImages.load(scene.sprites.map(s=>s.tile),owner)||revision!==this.revision)return;this.scene=scene;const missing=[...new Set(scene.sprites.filter(s=>!spriteImages.meta.get(s.tile)?.sprite).map(s=>s.tile))];$('scene-state').textContent=`${scene.sprites.length.toLocaleString('en-GB')} sprites z=${scene.minLevel}…${scene.maxLevel} game lighting is not modeled ${missing.length?` · without picture: ${missing.length}`:''}`;this.draw();}catch(e){if(revision===this.revision){spriteImages.endScene();$('scene-state').textContent=e.message;this.scene=null;this.draw();}}}
 draw(){const canvas=$('scene-canvas'),rect=canvas.getBoundingClientRect();if(!rect.width||!rect.height)return;const dpr=devicePixelRatio||1;canvas.width=Math.round(rect.width*dpr);canvas.height=Math.round(rect.height*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.fillStyle='#131b18';ctx.fillRect(0,0,rect.width,rect.height);if(!this.scene?.sprites.length)return;let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;for(const s of this.scene.sprites){const x=(s.x-s.y)*64,y=(s.x+s.y)*32-s.z*192,p=spriteImages.meta.get(s.tile)?.sprite;if(!p)continue;minX=Math.min(minX,x+p.offsetX-p.frameWidth/2);minY=Math.min(minY,y+p.offsetY-p.frameHeight+32);maxX=Math.max(maxX,x+p.offsetX-p.frameWidth/2+p.width);maxY=Math.max(maxY,y+p.offsetY-p.frameHeight+32+p.height);}if(!Number.isFinite(minX))return;const scale=Math.min((rect.width-36)/(maxX-minX),(rect.height-36)/(maxY-minY)),ox=(rect.width-(maxX-minX)*scale)/2-minX*scale,oy=(rect.height-(maxY-minY)*scale)/2-minY*scale;for(const s of [...this.scene.sprites].sort((a,b)=>nativeSpriteOrder(a,b,spriteImages.meta))){spriteImages.draw(ctx,s.tile,ox+(s.x-s.y)*64*scale,oy+((s.x+s.y)*32-s.z*192)*scale,scale);}}
}
