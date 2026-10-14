import * as THREE from './vendor/three/three.module.js';
import {loadStaticModelObject,disposeModelObject} from './static-models.js';

export function materialSettings(value={}){
 const s={roughness:.85,metalness:0,azimuth:135,elevation:45,shadows:true,...value};
 for(const [name,min,max] of [['roughness',0,1],['metalness',0,1],['azimuth',0,360],['elevation',5,85]])if(!Number.isFinite(s[name])||s[name]<min||s[name]>max)throw Error(`Mesh ${name} must be between ${min} and ${max}.`);
 s.shadows=Boolean(s.shadows);return s;
}
export const modelInLightArea=(m,r)=>!!r&&m.z===r.level&&m.x>=r.x&&m.y>=r.y&&m.x<r.x+r.size&&m.y<r.y+r.size;
export const modelWorldPosition=m=>[m.x+(m.offsetX??.5),m.z*3+(m.offsetZ??0)*(m.profile==='pickup'?3:1),m.y+(m.offsetY??.5)];
export function nativeFieldTexture(result){
 const data=new Uint8Array(result.size*result.size*4);for(const c of result.cells){const i=(c[1]*result.size+c[0])*4;for(let j=0;j<3;j++)data[i+j]=Math.round(c[3+j]*255);data[i+3]=255;}
 const texture=new THREE.DataTexture(data,result.size,result.size,THREE.RGBAFormat);texture.colorSpace=THREE.SRGBColorSpace;texture.minFilter=texture.magFilter=THREE.NearestFilter;texture.needsUpdate=true;return texture;
}
export function materialFieldShader(shader,texture,result){
 shader.uniforms.nativeField={value:texture};shader.uniforms.nativeArea={value:new THREE.Vector3(result.x,result.y,result.size)};
 shader.vertexShader='varying vec3 nativePosition;\n'+shader.vertexShader.replace('#include <project_vertex>','#include <project_vertex>\nnativePosition = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;');
 shader.fragmentShader='uniform sampler2D nativeField;\nuniform vec3 nativeArea;\nvarying vec3 nativePosition;\n'+shader.fragmentShader.replace('#include <opaque_fragment>',`vec2 nativeUV = ( nativePosition.xz - nativeArea.xy ) / nativeArea.z;
 if ( all( greaterThanEqual( nativeUV, vec2( 0.0 ) ) ) && all( lessThan( nativeUV, vec2( 1.0 ) ) ) ) {
  vec4 nativeSample = texture2D( nativeField, nativeUV );
  outgoingLight *= nativeSample.rgb * step( 0.5, nativeSample.a );
 }
 #include <opaque_fragment>`);
}
export function materialCamera(t){
 const {zoom:q,pan:p,viewport:v}=t,camera=new THREE.Camera();
 camera.projectionMatrix.set(2*q/v.width,0,-2*q/v.width,2*p.x/v.width-1,-q/v.height,2*q/v.height,-q/v.height,1-2*p.y/v.height,-1/8192,-1/8192,-1/8192,0,0,0,0,1);camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert();return camera;
}
function resourcesBytes(object){const gs=new Set(),ts=new Set();let bytes=0;object.traverse(o=>{if(o.geometry&&!gs.has(o.geometry)){gs.add(o.geometry);for(const a of Object.values(o.geometry.attributes))bytes+=a.array.byteLength;bytes+=o.geometry.index?.array.byteLength??0;}for(const m of Array.isArray(o.material)?o.material:[o.material])if(m)for(const t of Object.values(m))if(t?.isTexture&&!ts.has(t)){ts.add(t);const image=t.image;bytes+=(image?.width??1)*(image?.height??1)*4*1.34;}});return bytes;}
function receiver(result){const positions=[],indices=[];for(const c of result.cells){const x=result.x+c[0],z=result.y+c[1],y=result.level*3-.002,i=positions.length/3;positions.push(x,y,z,x,y,z+1,x+1,y,z+1,x+1,y,z);indices.push(i,i+1,i+2,i,i+2,i+3);}const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));g.setIndex(indices);g.computeVertexNormals();return g;}
// Tile graphics have no meshes. Authored wall dimensions supply depth and shadow proxies.
export function wallShadowSegments(context,result){
 const project=context?.project;if(!project)return [];const entries=[...(project.buildings??[]).map(b=>({b,x:b.x,y:b.y})),...(context.world?.buildings??[]).map(b=>({b,x:b.x-project.worldX*256,y:b.y-project.worldY*256}))],segments=[];
 for(const {b,x:ox,y:oy} of entries){const floor=b.design?.floors[result.level-(b.design.minLevel??0)];if(!floor)continue;for(const e of floor.edges){const x=ox+e.x,y=oy+e.y;if(x<result.x-1||y<result.y-1||x>result.x+result.size||y>result.y+result.size||e.kind==='open'||e.kind==='door'&&e.open)continue;const bounds=e.kind==='window'?[[0,.9],[2.25,3]]:[[0,3]];for(const [bottom,top] of bounds)segments.push({x,y,edge:e.edge,bottom,top});if(segments.length>=4096)return segments;}}
 return segments;
}
function wallGeometry(context,result){const positions=[],indices=[];for(const s of wallShadowSegments(context,result)){const i=positions.length/3,x2=s.x+(s.edge==='n'?1:0),z2=s.y+(s.edge==='w'?1:0),bottom=result.level*3+s.bottom,top=result.level*3+s.top;positions.push(s.x,bottom,s.y,x2,bottom,z2,x2,top,z2,s.x,top,s.y);indices.push(i,i+1,i+2,i,i+2,i+3);}const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));g.setIndex(indices);g.computeVertexNormals();return g;}

export class MaterialLightingLayer{
 constructor({requestDraw=()=>{},status=()=>{},loadObject=loadStaticModelObject}={}){Object.assign(this,{requestDraw,status,loadObject});this.epoch=0;this.assets=new Map();this.instances=new Map();this.pending=new Set();this.failed=new Set();this.bytes=0;this.budget=32*1024*1024;this.disposed=false;}
 initialize(){if(this.renderer)return;this.canvas=document.createElement('canvas');this.renderer=new THREE.WebGLRenderer({canvas:this.canvas,alpha:true,antialias:true,preserveDrawingBuffer:true});this.renderer.setPixelRatio(1);this.renderer.setClearColor(0,0);this.renderer.shadowMap.enabled=true;this.renderer.shadowMap.type=THREE.PCFSoftShadowMap;this.scene=new THREE.Scene();this.meshes=new THREE.Group();this.scene.add(this.meshes);this.scene.add(new THREE.AmbientLight(0xffffff,1.6));this.sun=new THREE.DirectionalLight(0xffffff,2.4);this.scene.add(this.sun,this.sun.target);this.sun.shadow.mapSize.set(1024,1024);this.sun.shadow.bias=-.0002;this.sun.shadow.normalBias=.03;this.sun.shadow.camera.near=.1;this.sun.shadow.camera.far=250;}
 cancel(){this.epoch++;this.controller?.abort();this.controller=null;this.pending.clear();}
 clear(){this.cancel();for(const object of this.instances.values()){object.traverse(o=>{for(const m of Array.isArray(o.material)?o.material:[o.material])m?.dispose();});}this.instances.clear();this.meshes?.clear();for(const object of this.assets.values())disposeModelObject(object);this.assets.clear();this.failed.clear();this.bytes=0;for(const object of [this.ground,this.walls])if(object){this.scene?.remove(object);disposeModelObject(object);}this.ground=this.walls=null;this.texture?.dispose();this.texture=null;this.result=null;this.nodesKey=null;this.status('');}
 suppress(model,result){return this.result===result&&this.instances.has(this.instanceKey(model));}
 instanceKey(m){return [m.id??m.modelId,m.modelId,m.x,m.y,m.z,m.rotation,m.scale,m.worldScale,(m.worldRotation??[]).join(','),m.offsetX,m.offsetY,m.offsetZ,m.profile].join('|');}
 async sync(result,nodes,context){
  if(this.disposed)return;if(!result){if(this.result)this.clear();return;}
  this.initialize();if(this.result!==result){this.clear();this.result=result;this.texture=nativeFieldTexture(result);this.ground=new THREE.Mesh(receiver(result),new THREE.ShadowMaterial({opacity:.5,depthWrite:false,side:THREE.DoubleSide}));this.ground.receiveShadow=true;this.scene.add(this.ground);this.walls=new THREE.Mesh(wallGeometry(context,result),new THREE.MeshBasicMaterial({colorWrite:false,side:THREE.DoubleSide}));this.walls.castShadow=true;this.walls.receiveShadow=false;this.walls.renderOrder=-1;this.scene.add(this.walls);}
  const candidates=nodes.filter(m=>m.type==='model'&&modelInLightArea(m,result)),wanted=candidates.slice(0,96),key=wanted.map(m=>this.instanceKey(m)).join(';');if(key===this.nodesKey)return;this.cancel();this.nodesKey=key;const ids=new Set(wanted.map(m=>this.instanceKey(m)));
  for(const [id,object] of this.instances)if(!ids.has(id)){object.traverse(o=>{for(const m of Array.isArray(o.material)?o.material:[o.material])m?.dispose();});this.meshes.remove(object);this.instances.delete(id);}
  for(const id of this.failed)if(!ids.has(id))this.failed.delete(id);const wantedAssets=new Set(wanted.map(m=>m.modelId));for(const [id,asset] of this.assets)if(!wantedAssets.has(id)){this.bytes-=resourcesBytes(asset);disposeModelObject(asset);this.assets.delete(id);}
  // One request sequence and one WebGL context; no unbounded thumbnail contexts.
  const epoch=this.epoch,controller=this.controller??new AbortController();this.controller=controller;
  for(const model of wanted){const id=this.instanceKey(model);if(this.instances.has(id)||this.pending.has(id)||this.failed.has(id))continue;this.pending.add(id);
   try{let source=this.assets.get(model.modelId);if(!source){if(this.assets.size>=32)throw Error('Mesh asset limit reached');source=await this.loadObject(model.modelId,{signal:controller.signal});if(epoch!==this.epoch||controller.signal.aborted){disposeModelObject(source);return;}const bytes=resourcesBytes(source);if(this.bytes+bytes>this.budget){disposeModelObject(source);throw Error('Mesh memory budget reached');}this.assets.set(model.modelId,source);this.bytes+=bytes;}
    if(epoch!==this.epoch)return;if(!this.nodesKey?.split(';').includes(id))continue;const object=source.clone(true),angles=model.worldRotation??source.userData.modelData?.worldRotation??[0,0,0];object.rotation.set(angles[0]*Math.PI/180,(angles[1]+(model.rotation??0))*Math.PI/180,angles[2]*Math.PI/180);object.scale.multiplyScalar(model.scale??1);const placement=new THREE.Group();placement.position.set(...modelWorldPosition(model));placement.add(object);placement.traverse(o=>{if(!o.isMesh)return;const original=Array.isArray(o.material)?o.material:[o.material],materials=original.map(m=>{const material=new THREE.MeshStandardMaterial({map:m.map??null,color:m.color??0xffffff,side:m.side??THREE.DoubleSide,transparent:m.transparent,opacity:m.opacity,alphaTest:m.alphaTest});material.onBeforeCompile=shader=>materialFieldShader(shader,this.texture,result);material.customProgramCacheKey=()=> 'native-field-material-v1';return material;});o.material=Array.isArray(o.material)?materials:materials[0];o.castShadow=o.receiveShadow=true;});this.meshes.add(placement);this.instances.set(id,placement);this.requestDraw();
   }catch(error){if(epoch!==this.epoch||error.name==='AbortError')return;this.failed.add(id);this.status(error.message);}finally{if(epoch===this.epoch)this.pending.delete(id);}
  }
  if(epoch===this.epoch){this.status(`${this.instances.size} mesh models · ${this.failed.size} unavailable · ${(this.bytes/1048576).toFixed(1)} / 32 MiB${candidates.length>96?' · model limit 96':''}`);this.requestDraw();}
 }
 draw(ctx,transform,result,nodes,settings,context){
  this.sync(result,nodes,context).catch(error=>this.status('Mesh lighting unavailable: '+error.message));if(!this.renderer||this.result!==result||!result)return;const s=materialSettings(settings),center=new THREE.Vector3(result.x+result.size/2,result.level*3,result.y+result.size/2),a=s.azimuth*Math.PI/180,e=s.elevation*Math.PI/180;this.sun.position.copy(center).add(new THREE.Vector3(Math.cos(a)*Math.cos(e),Math.sin(e),Math.sin(a)*Math.cos(e)).multiplyScalar(80));this.sun.target.position.copy(center);this.sun.castShadow=s.shadows;this.ground.visible=s.shadows;this.walls.castShadow=s.shadows;const extent=result.size*.85+8,c=this.sun.shadow.camera;c.left=c.bottom=-extent;c.right=c.top=extent;c.updateProjectionMatrix();for(const object of this.instances.values())object.traverse(o=>{for(const material of Array.isArray(o.material)?o.material:[o.material])if(material){material.roughness=s.roughness;material.metalness=s.metalness;}});
  // Bound the backing buffer even on unusually large/high-DPI displays.
  const factor=Math.min(1,2048/transform.viewport.width,2048/transform.viewport.height);this.renderer.setSize(Math.max(1,Math.round(transform.viewport.width*factor)),Math.max(1,Math.round(transform.viewport.height*factor)),false);this.renderer.render(this.scene,materialCamera(transform));ctx.drawImage(this.canvas,0,0,transform.viewport.width,transform.viewport.height);Object.assign(this.canvas.dataset,{models:String(this.instances.size),triangles:String(this.renderer.info.render.triangles),shadowMap:String(!!this.sun.shadow.map),nativeFrame:String(result.provenance?.counter??0)});
 }
 dispose(){if(this.disposed)return;this.clear();this.disposed=true;this.sun?.shadow.dispose();this.renderer?.dispose();this.renderer?.forceContextLoss();this.renderer=null;}
}
