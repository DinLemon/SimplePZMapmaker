import {minZ,maxZ} from './z-levels.js';
import {MaterialLightingLayer,materialSettings} from './material-lighting.js';
export function lightingArea(context,size=32,x=null,y=null){
 const {project,transform:t}=context;if(!project||!t||!Number.isFinite(t.zoom)||t.zoom<=0||!Number.isInteger(size)||size<8||size>64)throw Error('Select a map and an area of 8–64 tiles.');
 const level=context.level??0;
 if(x===null||y===null){const sx=(t.viewport.width/2-t.pan.x)/t.zoom,sy=(t.viewport.height/2-t.pan.y+level*3*t.zoom)/t.zoom;x=Math.floor(sx/2+sy-size/2);y=Math.floor(sy-sx/2-size/2);}
 if(!Number.isInteger(x)||!Number.isInteger(y)||!Number.isInteger(level)||level<minZ||level>maxZ)throw Error('Lighting needs whole tile coordinates and one signed floor.');
 return {x:Math.max(0,Math.min(project.width-size,x)),y:Math.max(0,Math.min(project.height-size,y)),size,level};
}
export function lightingPoint(x,y,level,t){return {x:t.pan.x+(x-y)*t.zoom,y:t.pan.y+(x+y)*t.zoom/2-level*3*t.zoom};}
export function validateLightingResult(result,area,revision){
 if(!result||result.revision!==revision||['x','y','size','level'].some(k=>result[k]!==area[k])||!Array.isArray(result.cells)||result.cells.length>area.size*area.size||result.provenance?.engine!=='Project Zomboid LightingJNI'||result.provenance?.gameSession!==false||result.provenance?.protocol!==2||!Number.isInteger(result.provenance?.counter)||result.provenance.counter<1||!Array.isArray(result.warnings)||result.warnings.length>30||result.warnings.some(w=>typeof w!=='string'||w.length>1000))throw Error('The native lighting response does not match this scene.');
 const seen=new Set();for(const cell of result.cells){if(!Array.isArray(cell)||cell.length!==15||!cell.every(Number.isFinite)||![cell[0],cell[1],cell[2]].every(Number.isInteger)||cell[0]<0||cell[1]<0||cell[0]>=area.size||cell[1]>=area.size||cell[2]!==area.level||cell.slice(3,6).some(v=>v<0||v>1)||cell[6]<0||cell[6]>4||cell.slice(7).some(v=>!Number.isInteger(v)||v< -2147483648||v>2147483647)||seen.has(`${cell[0]}:${cell[1]}`))throw Error('Native lighting returned invalid cell data.');seen.add(`${cell[0]}:${cell[1]}`);}
 return result;
}
export const sameLightingScene=(a,b)=>a.revision===b.revision&&a.memberId===b.memberId&&a.project===b.project&&a.world===b.world&&a.level===b.level;
export class NativeLightingPanel{
 constructor({getContext,message,requestDraw,openNative,getNodes=()=>[]}){
  Object.assign(this,{getContext,message,requestDraw,openNative,getNodes});this.result=null;this.snapshot=null;this.controller=null;
  this.panel=document.createElement('section');this.panel.className='native-lighting-panel';this.panel.hidden=true;
  this.panel.innerHTML='<h2>Native lighting check</h2><p>The installed game library calculates this light field. Everything stays inside the editor.</p><div class="native-lighting-controls"><label>Area<select data-field="size"><option>16</option><option selected>32</option><option>64</option></select></label><label>Tile X<input data-field="x" type="number" min="0" step="1" value="0"></label><label>Tile Y<input data-field="y" type="number" min="0" step="1" value="0"></label><label>Sky color<input data-field="sky" type="color" value="#666666"></label><label>Ambient<input data-field="ambient" type="number" min="0" max="1" step="0.05" value="0.4"></label><label><input data-field="power" type="checkbox" checked>Room lights powered</label><label>Display<select data-field="display"><option value="material" selected>Materials and mesh shadows</option><option value="field">Light field</option><option value="tint">Tint game view</option></select></label><label><input data-field="visible" type="checkbox" checked>Show result</label></div><fieldset class="native-material-controls"><legend>Mesh response · editor preview</legend><div class="native-lighting-controls"><label>Roughness<input data-field="roughness" type="range" min="0" max="1" step="0.05" value="0.85"></label><label>Metalness<input data-field="metalness" type="range" min="0" max="1" step="0.05" value="0"></label><label>Sun azimuth<input data-field="azimuth" type="number" min="0" max="360" step="15" value="135"></label><label>Sun elevation<input data-field="elevation" type="number" min="5" max="85" step="5" value="45"></label><label><input data-field="shadows" type="checkbox" checked>Geometry shadows</label></div><p>Native RGB controls brightness and color. Mesh normals, material response and directional shadows render locally in the editor. Game tiles keep their original artwork; authored walls provide shadow and depth geometry.</p><output data-mesh-status role="status"></output></fieldset><div class="native-lighting-actions"><button data-action="center">Visible area</button><button data-action="run">Calculate</button><button data-action="cancel" disabled>Cancel</button><button data-action="close">Close</button></div><p data-status role="status">Select a floor and calculate an area.</p><output data-sample></output><details><summary>Engine and assumptions</summary><p data-provenance></p><ul data-warnings></ul></details>';
  document.querySelector('.inspector').append(this.panel);
  this.find=name=>this.panel.querySelector(`[data-field="${name}"]`);this.action=name=>this.panel.querySelector(`[data-action="${name}"]`);
  this.action('center').onclick=()=>this.center();this.action('run').onclick=()=>this.check();this.action('cancel').onclick=()=>this.controller?.abort();this.action('close').onclick=()=>{this.controller?.abort();this.panel.hidden=true;this.material?.dispose();this.material=null;this.requestDraw();};
  for(const name of ['size','x','y','sky','ambient','power'])this.find(name).onchange=()=>{this.controller?.abort();this.result=null;this.state('Inputs changed. Calculate again.');this.requestDraw();};
  for(const name of ['display','visible','roughness','metalness','azimuth','elevation','shadows'])this.find(name).onchange=()=>{if(this.find('display').value!=='material'||!this.find('visible').checked){this.material?.dispose();this.material=null;}this.requestDraw();};
  for(const name of ['roughness','metalness'])this.find(name).oninput=()=>this.requestDraw();
  const button=document.getElementById('check-lighting');button.onclick=()=>{this.openNative();this.panel.hidden=false;this.center();this.check();};
  document.getElementById('map-canvas').addEventListener('pointermove',event=>this.sample(event));
  window.addEventListener('pagehide',()=>this.suspend());document.addEventListener('assets-catalog-changed',()=>{this.suspend();this.result=null;this.state('Resources changed. Calculate again.');this.requestDraw();});
 }
 suspend(){this.controller?.abort();this.material?.dispose();this.material=null;}
 state(text){this.panel.querySelector('[data-status]').textContent=text;}
 center(){try{const a=lightingArea(this.getContext(),+this.find('size').value);this.controller?.abort();this.result=null;this.find('x').value=a.x;this.find('y').value=a.y;this.state('Area moved. Calculate its lighting.');this.requestDraw();}catch(e){this.message(e.message,true);}}
 async check(){
  if(this.controller)return;this.openNative();let controller;
  try{
   const context=this.getContext(),revision=String(context.revision),area=lightingArea(context,+this.find('size').value,+this.find('x').value,+this.find('y').value);
   if(context.workspace)throw Error('Lighting across unloaded neighbours needs the complete world. Use World → Section workspaces → Load complete world for this check.');
   const color=this.find('sky').value,rgb=[1,3,5].map(i=>parseInt(color.slice(i,i+2),16)/255),ambient=+this.find('ambient').value;
   if(!Number.isFinite(ambient)||ambient<0||ambient>1)throw Error('Ambient strength must be between 0 and 1.');
   const source=context.world?{world:{...context.world,maps:context.world.maps.map(m=>m.id===context.memberId?{...m,project:context.project}:m)},memberId:context.memberId}:{project:context.project};
   const body=JSON.stringify({...source,...area,revision,skyRgb:rgb,ambient,roomLightsPowered:this.find('power').checked});
   if(body.length>120*1024*1024)throw Error('This scene exceeds the request budget. Use a smaller world or a standalone section.');
   controller=new AbortController();this.controller=controller;this.result=null;this.snapshot=context;this.action('run').disabled=true;this.action('cancel').disabled=false;this.state('Calculating with the installed native engine…');this.requestDraw();
   const response=await fetch('/api/lighting/check',{method:'POST',headers:{'Content-Type':'application/json'},body,signal:controller.signal});
   const result=await response.json();if(controller.signal.aborted)throw new DOMException('Canceled','AbortError');if(!response.ok)throw Error(result.error||'Native lighting is unavailable.');
   if(!sameLightingScene(context,this.getContext())){this.state('The map changed during calculation. Calculate its current state again.');return;}
   this.result=validateLightingResult(result,area,revision);this.state(`${result.cells.length} native samples · z=${area.level} · ${result.provenance.milliseconds} ms`);
   this.panel.querySelector('[data-provenance]').textContent=`${result.provenance.engine}; protocol ${result.provenance.protocol}, frame ${result.provenance.counter}. Library SHA-256: ${result.provenance.nativeSha256}. Scene SHA-256: ${result.provenance.inputSha256}.`;
   this.panel.querySelector('[data-warnings]').replaceChildren(...result.warnings.map(text=>{const item=document.createElement('li');item.textContent=text;return item;}));this.requestDraw();
  }catch(e){if(e.name==='AbortError')this.state('Lighting check canceled. The project was not changed.');else{this.state(e.message);this.message(e.message,true);}}
  finally{if(this.controller===controller){this.controller=null;this.action('run').disabled=false;this.action('cancel').disabled=true;}}
 }
 current(){if(!this.result||!sameLightingScene(this.snapshot,this.getContext())){if(this.result){this.result=null;this.material?.dispose();this.material=null;this.state('The map or floor changed. Calculate again.');}return null;}return this.result;}
 modelSuppressed(model){const r=this.current();return !this.panel.hidden&&this.find('visible').checked&&this.find('display').value==='material'&&!!this.material?.suppress(model,r);}
 draw(ctx,t){
  const r=this.current();if(this.panel.hidden||!r||!this.find('visible').checked){if(this.material){this.material.dispose();this.material=null;}return;}ctx.save();const materials=this.find('display').value==='material',tint=materials||this.find('display').value==='tint';ctx.globalCompositeOperation=tint?'multiply':'source-over';ctx.globalAlpha=tint?1:.7;
  for(const cell of r.cells){const x=r.x+cell[0],y=r.y+cell[1],points=[[x,y],[x+1,y],[x+1,y+1],[x,y+1]].map(([xx,yy])=>lightingPoint(xx,yy,r.level,t));ctx.fillStyle=`rgb(${cell.slice(3,6).map(v=>Math.round(v*255)).join(',')})`;ctx.beginPath();points.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.closePath();ctx.fill();}
  ctx.restore();if(materials){try{const settings=materialSettings(Object.fromEntries(['roughness','metalness','azimuth','elevation'].map(n=>[n,+this.find(n).value])));settings.shadows=this.find('shadows').checked;this.material??=new MaterialLightingLayer({requestDraw:this.requestDraw,status:text=>this.panel.querySelector('[data-mesh-status]').textContent=text});this.material.draw(ctx,t,r,this.getNodes(),settings,this.getContext());}catch(error){this.panel.querySelector('[data-mesh-status]').textContent=error.message;}}ctx.save();ctx.globalCompositeOperation='source-over';ctx.globalAlpha=1;ctx.strokeStyle='#e7be73';ctx.lineWidth=1.5;ctx.setLineDash([5,4]);ctx.beginPath();[[r.x,r.y],[r.x+r.size,r.y],[r.x+r.size,r.y+r.size],[r.x,r.y+r.size]].map(([x,y])=>lightingPoint(x,y,r.level,t)).forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.closePath();ctx.stroke();ctx.restore();
 }
 sample(event){const r=this.current();if(!r||this.panel.hidden)return;const t=this.getContext().transform,bounds=event.currentTarget.getBoundingClientRect(),sx=(event.clientX-bounds.left-t.pan.x)/t.zoom,sy=(event.clientY-bounds.top-t.pan.y+r.level*3*t.zoom)/t.zoom,x=Math.floor(sx/2+sy)-r.x,y=Math.floor(sy-sx/2)-r.y,cell=r.cells.find(c=>c[0]===x&&c[1]===y);this.panel.querySelector('[data-sample]').textContent=cell?`Tile ${r.x+x}, ${r.y+y}, z=${r.level} · RGB ${cell.slice(3,6).map(v=>v.toFixed(3)).join(' / ')} · dark ${cell[6].toFixed(3)}`:'';}
}
