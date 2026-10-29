import {nativeSpriteOrder} from './native-sprite-order.js';
import {SpriteImages} from './resources.js';
import {filterLibraryItems} from './building-library.js';

// SpriteImages has no cancellation contract. Keep its bounded cache and drawing,
// but fetch thumbnail metadata and atlas bytes with the gallery's abort signal.
class ThumbnailImages extends SpriteImages {
 constructor(api,request){super();this.api=api;this.request=request;}
 async load(ids,signal){
  signal.throwIfAborted();const unique=[...new Set(ids)],missing=unique.filter(id=>!this.meta.has(id));
  for(let i=0;i<missing.length;i+=1500){
   const batch=missing.slice(i,i+1500),items=await this.api('/api/assets/describe',batch,signal);signal.throwIfAborted();
   for(const id of batch)this.meta.set(id,null);for(const item of items)this.meta.set(item.id,item);
  }
  const pages=new Set(unique.map(id=>this.meta.get(id)?.page).filter(Boolean));this.pinned=pages;
  try{
   for(const key of pages){
    signal.throwIfAborted();if(this.pages.has(key)){const image=this.pages.get(key);this.pages.delete(key);this.pages.set(key,image);continue;}
    const response=await this.request('/api/assets/page/'+key,{signal});if(!response.ok)throw Error('Game resource image is unavailable.');
    const blob=await response.blob();signal.throwIfAborted();const url=URL.createObjectURL(blob),image=new Image();
    const cancel=()=>{image.src='';};signal.addEventListener('abort',cancel,{once:true});
    try{
     image.src=url;await image.decode();signal.throwIfAborted();const bytes=image.naturalWidth*image.naturalHeight*4;
     const pinnedBytes=[...pages].reduce((sum,page)=>{const cached=this.pages.get(page);return sum+(cached?cached.naturalWidth*cached.naturalHeight*4:0);},0);
     if(!Number.isFinite(bytes)||bytes+pinnedBytes>this.budget)throw Error('Thumbnail exceeds the 192 MB preview memory budget.');
     this.pages.set(key,image);this.bytes+=bytes;this.trim();
    }finally{signal.removeEventListener('abort',cancel);URL.revokeObjectURL(url);}
   }
  }finally{if(this.pinned===pages){this.pinned=new Set();this.trim();}}
  return true;
 }
}

export class HousingGallery {
 constructor({api,select,root=globalThis.document,request=(...args)=>fetch(...args),images}={}){
  this.api=api;this.select=select;this.root=root;this.request=request;this.images=images??new ThumbnailImages(api,request);
  this.items=null;this.active=false;this.search='';this.revision=0;this.abort=null;
  this.$=id=>root.getElementById(id);this.category=this.$('housing-preset-category');this.grid=this.$('building-presets-grid');this.state=this.$('housing-preset-state');this.dialog=this.$('building-list-dialog');
  this.change=()=>{if(this.active)void this.render();};this.stop=()=>this.close();
  this.category.addEventListener('change',this.change);this.dialog.addEventListener('close',this.stop);this.dialog.addEventListener('cancel',this.stop);
 }
 cancel(){this.revision++;this.abort?.abort();this.abort=null;this.grid.setAttribute('aria-busy','false');}
 close(){this.active=false;this.cancel();}
 dispose(){this.close();this.category.removeEventListener('change',this.change);this.dialog.removeEventListener('close',this.stop);this.dialog.removeEventListener('cancel',this.stop);this.images.clear?.();}
 async open({search=this.search}={}){
  this.active=true;this.search=String(search??'');this.cancel();
  if(this.items)return this.render();
  const controller=this.abort=new AbortController(),revision=this.revision;
  this.grid.replaceChildren();this.grid.setAttribute('aria-busy','true');this.state.textContent='Loading housing templates…';
  try{
   const response=await this.request('/api/building/presets',{signal:controller.signal});
   if(!this.current(revision))return;
   if(!response.ok)throw Error('Ready-made templates are currently not available.');
   const items=await response.json();if(!this.current(revision))return;
   if(!Array.isArray(items))throw Error('The housing template list is unavailable.');
   this.items=items;return this.render();
  }catch(error){if(this.current(revision)&&error.name!=='AbortError')this.state.textContent=error.message||'Housing templates could not be loaded.';}
  finally{if(this.current(revision))this.grid.setAttribute('aria-busy','false');}
 }
 setSearch(value){return this.active?this.open({search:value}):(this.search=String(value??''));}
 current(revision){return this.active&&this.revision===revision&&!this.abort?.signal.aborted;}
 async render(){
  if(!this.active||!this.items)return;
  this.cancel();const controller=this.abort=new AbortController(),revision=this.revision,category=this.category.value;
  const items=filterLibraryItems(this.items.filter(item=>item&&(!category||item.category===category)),this.search),document=this.grid.ownerDocument;
  this.grid.replaceChildren();this.grid.setAttribute('aria-busy',String(items.length>0));
  this.state.textContent=items.length?`${items.length} housing template(s). Open a template, customize it, and save it to your library.`:this.search.trim()||category?'No housing templates match your search and category.':'There are no housing templates available.';
  const jobs=[];
  for(const item of items){
   const card=document.createElement('article');card.className='housing-card';
   const canvas=document.createElement('canvas');canvas.width=260;canvas.height=170;canvas.setAttribute('role','img');canvas.setAttribute('aria-label',item.name+' — game resources');
   const name=document.createElement('h3');name.textContent=item.name;
   const info=document.createElement('small');info.textContent=`${item.width} × ${item.height} tiles · ${item.levels} floor(s)${item.basements?' · '+item.basements+' basement':''}`;
   const description=document.createElement('p');description.textContent=item.description??'';
   const button=document.createElement('button');button.type='button';button.dataset.housingPreset=item.id;button.textContent='Build from template';button.onclick=()=>this.select(item.id);
   card.append(canvas,name,info,description,button);
   const references=Array.isArray(item.references)?item.references:[];
   if(references.length){
    const details=document.createElement('details'),summary=document.createElement('summary');summary.textContent='Architectural references';details.append(summary);
    for(const [i,url] of references.entries()){
     if(typeof url!=='string'||!/^https?:\/\//i.test(url))continue;
     const link=document.createElement('a');link.href=url;link.target='_blank';link.rel='noopener noreferrer';link.textContent='Source '+(i+1);details.append(link);
    }
    card.append(details);
   }
   this.grid.append(card);jobs.push({item,canvas});
  }
  try{
   for(const {item,canvas} of jobs){
    if(!this.current(revision))return;
    try{
     const building=await this.api('/api/building/presets/'+encodeURIComponent(item.id),{},controller.signal);
     if(!this.current(revision))return;
     const scene=await this.api('/api/preview/building',{building,level:null},controller.signal);
     if(!this.current(revision))return;
     if(await this.images.load(scene.sprites.map(sprite=>sprite.tile),controller.signal)===false||!this.current(revision))return;
     drawThumbnail(canvas,scene,this.images);
    }catch(error){if(!this.current(revision))return;canvas.setAttribute('aria-label',item.name+' · image not available: '+error.message);}
   }
  }finally{if(this.current(revision))this.grid.setAttribute('aria-busy','false');}
 }
}
function drawThumbnail(canvas,scene,images){
 const ctx=canvas.getContext('2d');ctx.fillStyle='#17201b';ctx.fillRect(0,0,canvas.width,canvas.height);let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
 for(const s of scene.sprites){const p=images.meta.get(s.tile)?.sprite;if(!p)continue;const x=(s.x-s.y)*64+p.offsetX-p.frameWidth/2,y=(s.x+s.y)*32-s.z*192+p.offsetY-p.frameHeight+32;minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x+p.width);maxY=Math.max(maxY,y+p.height);}
 if(!Number.isFinite(minX))return;const scale=Math.min((canvas.width-20)/Math.max(1,maxX-minX),(canvas.height-16)/Math.max(1,maxY-minY)),ox=(canvas.width-(maxX-minX)*scale)/2-minX*scale,oy=(canvas.height-(maxY-minY)*scale)/2-minY*scale;
 for(const s of [...scene.sprites].sort((a,b)=>nativeSpriteOrder(a,b,images.meta)))images.draw(ctx,s.tile,ox+(s.x-s.y)*64*scale,oy+((s.x+s.y)*32-s.z*192)*scale,scale);
}
