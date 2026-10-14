import {SpriteImages} from './resources.js';
import {NativeModelSprites,nativeModelKey} from './native-model-sprites.js';

export function nativeChunkPlan({width,height,zoom,pan,viewport,level=null},limit=96){
 if(![width,height,zoom,pan?.x,pan?.y,viewport?.width,viewport?.height].every(Number.isFinite)||zoom<=0)return [];
 const size=zoom<.8?256:zoom<2?128:zoom<4?64:32,stride=zoom<.8?8:zoom<2?4:zoom<4?2:1;
 // Tall sprites can enter the viewport from outside its ground footprint.
 const lift=level===null?96*zoom:Math.max(0,level)*3*zoom,drop=level===null?48*zoom:Math.max(0,-level)*3*zoom,margin=40+4*zoom;
 const inverse=(x,y)=>({x:((x-pan.x)/zoom+2*(y-pan.y)/zoom)/2,y:(2*(y-pan.y)/zoom-(x-pan.x)/zoom)/2});
 const corners=[inverse(-margin,-margin-drop),inverse(viewport.width+margin,-margin-drop),inverse(viewport.width+margin,viewport.height+margin+lift),inverse(-margin,viewport.height+margin+lift)];
 const left=Math.max(0,Math.floor(Math.min(...corners.map(p=>p.x))/size)*size),right=Math.min(width,Math.ceil(Math.max(...corners.map(p=>p.x))/size)*size),top=Math.max(0,Math.floor(Math.min(...corners.map(p=>p.y))/size)*size),bottom=Math.min(height,Math.ceil(Math.max(...corners.map(p=>p.y))/size)*size);
 const center=inverse(viewport.width/2,viewport.height/2),chunks=[];
 for(let y=top;y<bottom;y+=size)for(let x=left;x<right;x+=size){const px=(x-y)*zoom+pan.x,py=(x+y)*zoom/2+pan.y;
  if(px+size*zoom< -margin||px-size*zoom>viewport.width+margin||py+size*zoom+drop< -margin||py-lift>viewport.height+margin)continue;
  chunks.push({x,y,size,stride,key:[x,y,size,stride,level??'all'].join(':'),distance:(x+size/2-center.x)**2+(y+size/2-center.y)**2});
 }
 return chunks.sort((a,b)=>a.distance-b.distance).slice(0,limit);
}
const estimate=chunk=>chunk.sprites.length*96+chunk.models.length*640;
export class MapNativePreview{
 constructor({canvas,requestDraw,status=()=>{}}){Object.assign(this,{canvas,requestDraw,status});this.images=new SpriteImages();this.images.budget=96*1024*1024;this.models=new NativeModelSprites();this.cache=new Map();this.pending=new Map();this.failed=new Set();this.bytes=0;this.budget=24*1024*1024;this.epoch=0;this.catalogRevision=0;this.session=null;this.opening=null;this.timer=null;this.view=null;this.renderNodes=[];this.sortedDirty=true;this.listener=()=>{this.catalogRevision++;this.invalidate();this.images.clear();this.models.clear();if(this.view)this.update(this.view);};document.addEventListener('assets-catalog-changed',this.listener);}
 async request(url,options={}){const response=await fetch(url,options);const value=await response.json();if(!response.ok)throw Error(value.error||'Native game view could not be loaded.');return value;}
 update(view){if(!view.isometric){if(this.view)this.close();return;}this.view=view;
  const identity=[view.revision,view.memberId??'map',this.catalogRevision].join('|');
  if(this.projectIdentity!==view.project||this.identity!==identity){this.projectIdentity=view.project;this.identity=identity;this.invalidate();this.canvas.dataset.nativeState='loading';clearTimeout(this.timer);this.timer=setTimeout(()=>this.open(view,this.epoch),180);return;}
  this.plan=nativeChunkPlan({width:view.project.width,height:view.project.height,...view});const planKey=this.plan.map(c=>c.key).join('|');if(planKey!==this.planKey){this.sortedDirty=true;this.failed.clear();this.planKey=planKey;}const wanted=new Set(this.plan.map(c=>c.key));for(const [key,p] of this.pending)if(!wanted.has(key)){p.abort.abort();this.pending.delete(key);}
  this.trim(wanted);this.schedule();this.report();
 }
 async open(view,epoch){if(epoch!==this.epoch)return;const abort=new AbortController();this.opening=abort;
  try{const body=view.world?{world:view.world,memberId:view.memberId}:{project:view.project};const session=await this.request('/api/map-scene',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal:abort.signal});if(epoch!==this.epoch){this.release(session.id);return;}this.session=session.id;this.opening=null;this.update(this.view);}
  catch(error){if(epoch===this.epoch&&error.name!=='AbortError'){this.canvas.dataset.nativeState='error';this.status(error.message);this.opening=null;}}
 }
 invalidate(){this.epoch++;clearTimeout(this.timer);this.opening?.abort();this.opening=null;for(const p of this.pending.values())p.abort.abort();this.pending.clear();this.models.cancelPending();if(this.session)this.release(this.session);this.session=null;this.cache.clear();this.failed.clear();this.bytes=0;this.renderNodes=[];this.sortedDirty=true;this.plan=[];this.requestDraw();}
 release(id){fetch('/api/map-scene/'+id+'/close',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}',keepalive:true}).catch(()=>{});}
 close(){this.invalidate();this.images.endScene();this.view=null;this.identity=null;this.projectIdentity=null;this.canvas.dataset.nativeState='off';}
 dispose(){this.close();document.removeEventListener('assets-catalog-changed',this.listener);this.images.clear();this.models.clear();}
 trim(wanted){const remove=key=>{const chunk=this.cache.get(key);if(!chunk)return;this.cache.delete(key);this.bytes-=estimate(chunk);this.sortedDirty=true;};for(const [key] of this.cache){if(this.cache.size<=96&&this.bytes<=this.budget)break;if(!wanted.has(key))remove(key);}for(const chunk of [...this.plan].reverse()){if(this.cache.size<=96&&this.bytes<=this.budget)break;if(this.cache.has(chunk.key)){remove(chunk.key);this.failed.add(chunk.key);this.status('Native preview budget reached. Zoom in or select one floor.');this.canvas.dataset.nativeState='error';}}}
 schedule(){if(!this.session||!this.view)return;const epoch=this.epoch;
  for(const chunk of this.plan){if(this.pending.size>=2)break;if(this.cache.has(chunk.key)||this.pending.has(chunk.key)||this.failed.has(chunk.key))continue;const abort=new AbortController();this.pending.set(chunk.key,{abort});
   const query=new URLSearchParams({x:chunk.x,y:chunk.y,size:chunk.size,stride:chunk.stride});if(this.view.level!==null)query.set('level',this.view.level);
   this.request('/api/map-scene/'+this.session+'?'+query,{signal:abort.signal}).then(async scene=>{if(epoch!==this.epoch||abort.signal.aborted)return;await this.images.load(scene.sprites.map(s=>s.tile));if(epoch!==this.epoch||abort.signal.aborted)return;const models=scene.models.map(m=>({...m.placement,x:m.x,y:m.y,z:m.z}));await Promise.all(models.map(m=>this.models.load(m)));if(epoch!==this.epoch||abort.signal.aborted)return;scene.models=models;const bytes=estimate(scene);if(bytes>this.budget)throw Error('This visible chunk exceeds the preview budget. Select one floor.');this.cache.set(chunk.key,scene);this.bytes+=bytes;this.sortedDirty=true;this.requestDraw();})
   .catch(error=>{if(epoch!==this.epoch||error.name==='AbortError')return;if(error.message.includes('snapshot expired')&&Date.now()-(this.renewedAt||0)>60000){this.renewedAt=Date.now();const view=this.view;this.invalidate();this.status('Refreshing the expired native view…');this.open(view,this.epoch);return;}this.failed.add(chunk.key);this.status(error.message);this.canvas.dataset.nativeState='error';})
   .finally(()=>{if(epoch===this.epoch){this.pending.delete(chunk.key);this.trim(new Set(this.plan.map(c=>c.key)));this.schedule();this.report();}});
  }
 }
 report(){const loaded=this.plan.filter(c=>this.cache.has(c.key)).length;Object.assign(this.canvas.dataset,{nativeLevel:String(this.view?.level??'all'),nativeWidth:String(this.view?.project.width??0),nativeHeight:String(this.view?.project.height??0),nativeChunks:String(this.cache.size),nativeVisibleChunks:String(this.plan.length),nativePending:String(this.pending.size),nativeBytes:String(this.bytes),nativeAtlasBytes:String(this.images.bytes)});if(!this.failed.size){this.canvas.dataset.nativeState=this.pending.size||loaded<this.plan.length?'loading':'ready';this.status(`Native game view · ${loaded}/${this.plan.length} visible chunks · ${this.view?.level===null?'all levels':'z='+this.view?.level}${this.plan[0]?.stride>1?' · terrain sampling 1:'+this.plan[0].stride+'; trees thinned':''}`);}}
 draw(ctx){if(!this.view?.isometric)return false;const {zoom,pan,viewport}=this.view,visible=new Set(this.plan.map(c=>c.key));
  if(this.sortedDirty){this.renderNodes=[];for(const [key,scene] of this.cache)if(visible.has(key)){for(const sprite of scene.sprites)this.renderNodes.push({...sprite,type:'tile'});for(const model of scene.models)this.renderNodes.push({...model,type:'model',layer:1,span:1});}this.renderNodes.sort((a,b)=>a.z-b.z||(a.layer??1)-(b.layer??1)||a.x+a.y-b.x-b.y||a.x-b.x);this.sortedDirty=false;}
  const scale=zoom/64;let painted=0,missing=0,paintedModels=0;for(const n of this.renderNodes){const span=n.span||1,x=n.x+span/2,y=n.y+span/2,px=(x-y)*zoom+pan.x,py=(x+y)*zoom/2+pan.y-n.z*3*zoom;if(n.type==='model'){const model=this.models.images.get(nativeModelKey(n));if(!model){missing++;continue;}const size=model.extent*zoom*(n.scale??1)*2.5,[cx,cy,cz]=model.center,dx=((n.offsetX??.5)-(n.offsetY??.5))*zoom+(cx-cz)*zoom*(n.scale??1),dy=((n.offsetX??.5)+(n.offsetY??.5)-1)*zoom/2-(n.offsetZ??0)*(n.profile==='pickup'?3:1)*zoom+((cx+cz)/2-cy)*zoom*(n.scale??1);if(px+dx+size<0||px+dx-size>viewport.width||py+dy+size<0||py+dy-size>viewport.height)continue;ctx.drawImage(model.image,px+dx-size/2,py+dy-size/2,size,size);painted++;paintedModels++;continue;}
   const a=this.images.meta.get(n.tile),s=a?.sprite,image=a&&this.images.pages.get(a.page);if(!s||!image){missing++;continue;}const factor=scale*span,left=px+(s.offsetX-s.frameWidth/2)*factor,top=py+(s.offsetY-s.frameHeight+32)*factor,w=s.width*factor,h=s.height*factor;if(left+w<0||left>viewport.width||top+h<0||top>viewport.height)continue;ctx.drawImage(image,s.x,s.y,s.width,s.height,left,top,w,h);painted++;}
  this.canvas.dataset.nativeSprites=String(painted);this.canvas.dataset.nativeModels=String(paintedModels);this.canvas.dataset.nativeMissing=String(missing);if(missing&&this.models.limited)this.status('Native model preview budget reached. Zoom in or narrow the visible area.');return true;
 }
}
