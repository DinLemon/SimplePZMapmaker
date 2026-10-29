import {createExportJob,queueNativeExport,readExportJob,exportProgressLabel} from './export-jobs.js';

const safeId=value=>typeof value==='string'&&/^[a-f0-9]{32}$/i.test(value);
const active=job=>job?.status==='queued'||job?.status==='running';
const identity=result=>result?JSON.stringify([result.id,result.resultKind,result.workspaceId,result.workspaceRevision,result.revision,result.key]):null;
const downloadPath=id=>'/api/export/jobs/'+id+'/download';

function validateResult(result){
 if(!result||!safeId(result.id)||!['world','sections'].includes(result.resultKind)||
  !Number.isSafeInteger(result.revision)||result.revision<0||typeof result.key!=='string'||!result.key||
  (result.resultKind==='sections'?!safeId(result.workspaceId):result.workspaceId!=null))
  throw Error('Choose a completed import with valid identifiers and workspace metadata.');
 if(result.resultKind==='sections'&&(!Number.isSafeInteger(result.workspaceRevision)||result.workspaceRevision<1))
  throw Error('The imported workspace has no verified storage revision. Rebuild the import before compiling.');
 return result;
}

// Match NativeModPackage's grammar; additionally reject Windows device names and trailing dots.
export function validateWorldBuildNames({modId,mapFolder,modName}){
 for(const [label,value] of [['Mod id',modId],['Map folder',mapFolder]]){
  if(typeof value!=='string'||!/^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$/.test(value)||value.endsWith('.')||
   /^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)/i.test(value))
   throw Error(label+': use a safe identifier of 1–64 letters, digits, underscores, dots or hyphens.');
 }
 if(typeof modName!=='string'||!modName.trim()||modName.length>80||/[\u0000-\u001f\u007f-\u009f]/.test(modName))
  throw Error('Mod name: use 1–80 characters without control characters.');
 return {modId,mapFolder,modName};
}

async function json(url,options,request){
 const response=await request(url,options);
 let value;try{value=await response.json();}catch{throw Error('Could not read the build response. Retry shortly.');}
 if(!response.ok)throw Error(value?.error||`Build request failed (${response.status}).`);
 return value;
}

/** One import's archive reference, not another export queue. No timers: export-jobs owns job management.
 * refresh() checks this reference; all network boundaries also recheck the parent's full input identity.
 * request uses the fetch signature. queueExport uses createExportJob's signature.
 */
export function createWorldBuildOutput({getResult,request=globalThis.fetch,queueExport=createExportJob,onChange=()=>{}}){
 let result=null,key=null,epoch=0,job=null,verified=false,busy=false,locked=false,error=null;
 function clear(){epoch++;job=null;verified=false;busy=false;error=null;}
 function sync(){
  const next=getResult(),nextKey=identity(next);
  if(nextKey!==key){clear();key=nextKey;}
  result=next?{...next}:null;
 }
 function view(){sync();return {result,identity:key,job:job?{...job}:null,busy,locked,error,
  downloadUrl:verified&&job?.status==='completed'?downloadPath(job.id):null};}
 function changed(){onChange(view());}
 function current(token){sync();return token===epoch;}
 function assertUnlocked(){if(locked)throw Error('Build controls are locked while the import changes.');}
 function acceptJob(next,expectedId){
  if(!next||!safeId(next.id))throw Error('The server returned an invalid export job identifier.');
  if(expectedId&&next.id!==expectedId)throw Error('The returned export job does not match this archive.');
  if(!['queued','running','completed','failed','cancelled','interrupted','expired'].includes(next.status))
   throw Error('The server returned an invalid export job status.');
  if(result?.resultKind==='sections'&&String(next.revision)!==String(result.workspaceRevision))throw Error('The compiled workspace revision differs from the reviewed import. Rebuild or open the updated workspace.');
  job={...next};verified=true;
 }
 async function run(action){
  sync();assertUnlocked();if(busy)throw Error('A build action is already busy.');validateResult(result);
  const token=epoch,snapshot={...result};busy=true;error=null;changed();
  try{return await action({token,snapshot});}
  catch(e){if(!current(token))return null;error=e.message;throw e;}
  finally{if(current(token)){busy=false;changed();}}
 }
 async function read(token,id){
  let next,missing=false;
  try{next=await readExportJob(id,async(...args)=>{
   const response=await request(...args);missing=response.status===404||response.status===410;return response;
  });}catch(e){if(current(token)){verified=false;if(missing)job=null;}throw e;}
  if(!current(token))return null;
  verified=false;acceptJob(next,id);return job;
 }
 async function refresh(){
  sync();
  if(!result||!job||busy||locked){changed();return view();}
  await run(({token})=>read(token,job.id));return view();
 }
 async function compile(){
  return run(async({token,snapshot})=>{
   if(active(job)||job?.status==='completed')return read(token,job.id);
   job=null;verified=false;
   let world=null,url;
   if(snapshot.resultKind==='sections')url='/api/world/workspaces/'+snapshot.workspaceId+'/export/jobs?expectedRevision='+snapshot.workspaceRevision;
   else{
    world=await json('/api/world/osm/jobs/'+snapshot.id+'/result',{},request);
    if(!current(token))return null;
    if(!world||typeof world!=='object'||!Array.isArray(world.maps))throw Error('The imported world result is invalid.');
   }
   assertUnlocked();
   const onUpdate=next=>{
    if(!current(token)||!job||next?.id!==job.id)return;
    try{acceptJob(next,job.id);error=null;}catch(e){verified=false;error=e.message;}
    changed();
   };
   const next=await queueExport('world',world,{url,request,onUpdate});
   if(!current(token))return null;
   acceptJob(next);return job;
  });
 }
 async function install({confirmed,authorize=()=>true,...options}={}){
  if(confirmed!==true)throw Error('Explicitly confirm installation into Zomboid/mods first.');
  const names=validateWorldBuildNames(options);
  return run(async({token})=>{
   if(!job)throw Error('Compile the world before installing a completed archive.');
   const id=job.id,next=await read(token,id);
   if(!current(token))return null;
   if(next?.status!=='completed')throw Error('The archive is not ready. Wait for a completed compile before installing.');
   assertUnlocked();
   if(!authorize())throw Error('Installation confirmation changed. Confirm again before installing.');
   const query=new URLSearchParams({install:'true',...names});
   const installed=await json(downloadPath(id)+'?'+query,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'},request);
   return current(token)?installed:null;
  });
 }
 return {view,refresh,compile,install,
  reset(){clear();key=null;result=null;changed();},
  setLocked(value){locked=Boolean(value);changed();}
 };
}

/** Mount once after the parent has built its dialog. The parent calls refresh() after publishing
 * a result, reset() before changing/importing inputs, and setLocked(busy) from controls().
 * status(message,isError) is the parent's existing status callback; it is optional.
 */
export function installWorldBuildOutput({dialog,getResult,status=()=>{},request=globalThis.fetch,queueExport=queueNativeExport}){
 const doc=dialog.ownerDocument,section=doc.createElement('section');
 section.className='world-build-output';section.setAttribute('aria-label','Compile and install imported world');
 section.innerHTML=`<h3>Compile and install</h3>
  <p>Compile the imported terrain and furnished buildings into a native Build 42 ZIP. Planning and editing remain available without a game path.</p>
  <div class="world-build-output-names">
   <label>Mod id <input data-build="modId" value="OSMWorld" maxlength="64" autocomplete="off"></label>
   <label>Map folder <input data-build="mapFolder" value="OSMWorld" maxlength="64" autocomplete="off"></label>
   <label>Mod name <input data-build="modName" value="Imported world" maxlength="80" autocomplete="off"></label>
  </div>
  <p class="world-build-output-actions"><button type="button" data-build="compile">Compile world</button>
   <a data-build="download" download hidden>Save / download ZIP</a></p>
  <label class="world-build-output-confirm"><input type="checkbox" data-build="confirm"> I confirm installation into Zomboid/mods. A previous editor installation with this mod id will be replaced.</label>
  <button type="button" data-build="install">Install compiled mod</button>
  <p data-build="state" role="status" aria-live="polite"></p>`;
 const anchor=dialog.querySelector('.osm-world-outcome')||dialog.querySelector('.osm-world-primary-actions');
 if(anchor)anchor.before(section);else dialog.append(section);
 const node=name=>section.querySelector(`[data-build="${name}"]`);
 const compile=node('compile'),download=node('download'),install=node('install'),confirm=node('confirm'),state=node('state');
 const fields=['modId','mapFolder','modName'].map(node);
 const names=()=>Object.fromEntries(fields.map(field=>[field.dataset.build,field.value]));
 let seenIdentity=null,message='',controller;
 function render(view){
  if(view.identity!==seenIdentity){seenIdentity=view.identity;confirm.checked=false;message='';}
  section.hidden=!view.result;
  const disabled=view.busy||view.locked||!view.result;
  compile.disabled=disabled;
  compile.textContent=active(view.job)||view.job?.status==='completed'?'Check compile status':view.error||view.job?'Retry compile':'Compile world';
  for(const field of fields)field.disabled=disabled;
  confirm.disabled=disabled||!view.downloadUrl;
  let valid=true;try{validateWorldBuildNames(names());}catch{valid=false;}
  install.disabled=disabled||!view.downloadUrl||!confirm.checked||!valid;
  download.hidden=disabled||!view.downloadUrl;
  download.removeAttribute('href');
  if(!download.hidden)download.href=view.downloadUrl+(valid?'?'+new URLSearchParams(names()):'');
  state.classList.toggle('error',Boolean(view.error));
  state.textContent=view.error||message||(view.job?(view.job.error||exportProgressLabel(view.job)):
   'Import ready. Compile when ready; installation requires the checkbox and a separate click.');
  if(view.job&&!view.error&&!message)status(state.textContent,Boolean(view.job.error));
 }
 controller=createWorldBuildOutput({getResult,request,onChange:render,
  queueExport:(kind,snapshot,options)=>queueExport(kind,snapshot,{...options,container:section})});
 function report(text,isError=false){message=text;render(controller.view());status(text,isError);}
 compile.addEventListener('click',async()=>{
  try{const job=await controller.compile();if(job){message='';render(controller.view());}}
  catch(e){report(e.message,true);}
 });
 install.addEventListener('click',async()=>{
  const captured=names(),signature=JSON.stringify(captured);
  try{
   const installed=await controller.install({...captured,confirmed:confirm.checked,
    authorize:()=>confirm.checked&&signature===JSON.stringify(names())});
   if(installed){confirm.checked=false;report(`Installed compiled mod${typeof installed.installed==='string'?` to ${installed.installed}`:''}. Enable it and create a NEW save, or configure Mods= and Map= on your server.`);}
  }catch(e){confirm.checked=false;report(e.message,true);}
 });
 for(const field of fields)field.addEventListener('input',()=>{confirm.checked=false;render(controller.view());});
 confirm.addEventListener('change',()=>render(controller.view()));
 render(controller.view());
 return {refresh:()=>controller.refresh(),reset(){confirm.checked=false;message='';controller.reset();},setLocked:value=>controller.setLocked(value)};
}
