import {StaticModelViewer} from './static-models.js';
import * as THREE from './vendor/three/three.module.js';
export const nativeModelKey=model=>[model.sourceId??'game',(model.requiredMods??[]).join(','),model.modelId,model.rotation,model.worldScale,(model.worldRotation??[]).join(',')].join('|');
// Render a small local image with the map's exact isometric axes. Bounds preserve the registered world attachment.
export class NativeModelSprites{
 constructor(){this.images=new Map();this.pending=new Map();this.failed=new Set();this.queue=Promise.resolve();this.epoch=0;}
 cancelPending(){this.epoch++;this.pending.clear();this.activeViewer?.dispose();this.activeViewer=null;this.limited=false;}
 clear(){this.cancelPending();this.images.clear();this.failed.clear();}
 load(model){const key=nativeModelKey(model),epoch=this.epoch;if(this.images.has(key)){const value=this.images.get(key);this.images.delete(key);this.images.set(key,value);return Promise.resolve(value);}if(this.pending.has(key))return this.pending.get(key);if(this.failed.has(key))return Promise.resolve(null);if(this.pending.size>=64){this.limited=true;return Promise.resolve(null);}
  const task=this.queue.then(async()=>{if(epoch!==this.epoch)return null;const canvas=document.createElement('canvas');canvas.style.width='128px';canvas.style.height='128px';let viewer;
   try{viewer=new StaticModelViewer(canvas);this.activeViewer=viewer;if(!await viewer.load(model.modelId)||epoch!==this.epoch)return null;const object=viewer.group.children[0],original=viewer.nativeBounds;
    // Undo the gallery's normalization, then apply the same Euler attachment used by native spriteModel export.
    object.scale.multiplyScalar(original.extent/2);object.position.set(0,0,0);const angles=model.worldRotation??[0,0,0];object.rotation.set(angles[0]*Math.PI/180,(angles[1]+(model.rotation??0))*Math.PI/180,angles[2]*Math.PI/180);object.updateMatrixWorld(true);
    const box=new THREE.Box3().setFromObject(object),size=box.getSize(new THREE.Vector3()),center=box.getCenter(new THREE.Vector3()),extent=Math.max(size.x,size.y,size.z,.001);object.position.sub(center);object.scale.multiplyScalar(2/extent);object.position.multiplyScalar(2/extent);
    const camera=new THREE.Camera();camera.projectionMatrix.set(.4,0,-.4,0,-.2,.4,-.2,0,.1,.1,.1,0,0,0,0,1);camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert();viewer.scene.background=null;viewer.renderer.setClearColor(0,0);viewer.renderer.setSize(128,128,false);viewer.renderer.render(viewer.scene,camera);
    const image=new Image();image.src=canvas.toDataURL();await image.decode();if(epoch!==this.epoch)return null;const value={image,extent,center:center.toArray()};this.images.set(key,value);while(this.images.size>128)this.images.delete(this.images.keys().next().value);return value;
   }catch{if(epoch===this.epoch)this.failed.add(key);return null;}finally{viewer?.dispose();if(this.activeViewer===viewer)this.activeViewer=null;if(this.pending.get(key)===task)this.pending.delete(key);}
  });this.queue=task.then(()=>{},()=>{});this.pending.set(key,task);return task;
 }
}
