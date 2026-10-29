import {worldLayoutLimits as WORLD_LAYOUT_LIMITS} from './world-layout-limits.js';
export {WORLD_LAYOUT_LIMITS};
import {validateGeographicWarp,forwardGeographicWarpPath} from './geographic-warp.js';
// Shared OSM source handling. Uploading/downloading a source never changes a map.
export const JSON_SOURCE_LIMIT=64_000_000;
export const PBF_SOURCE_LIMIT=64*1024*1024;
export function sourceDate(value){if(!value)return 'date not recorded';const date=new Date(value);return Number.isFinite(date.getTime())?new Intl.DateTimeFormat(undefined,{year:'numeric',month:'short',day:'numeric',hour:'2-digit',minute:'2-digit',timeZone:'UTC'}).format(date)+' UTC':String(value);}
const number=value=>value===''||value==null?NaN:Number(value);
export function validBounds(value){
 const b=Object.fromEntries(['south','north','west','east'].map(key=>[key,number(value?.[key])]));
 if(!Object.values(b).every(Number.isFinite)||b.south< -85||b.north>85||b.west< -180||b.east>180||b.south>=b.north||b.west>=b.east)throw Error('Enter valid area bounds without crossing the date line.');
 return b;
}
export const boundsKey=b=>{const v=validBounds(b);return JSON.stringify([v.south,v.north,v.west,v.east]);};
export function boundsFromCentre(latitude,longitude,widthKm,heightKm){
 const [lat,lon,w,h]=[latitude,longitude,widthKm,heightKm].map(number);
 if(![lat,lon,w,h].every(Number.isFinite)||Math.abs(lat)>84||Math.abs(lon)>179||w<=0||h<=0)throw Error('Enter a centre latitude (−84 to 84), longitude (−179 to 179), and positive width and height.');
 const dy=h*1000/111320/2,dx=w*1000/111320/Math.cos(lat*Math.PI/180)/2;
 return validBounds({south:lat-dy,north:lat+dy,west:lon-dx,east:lon+dx});
}
export function areaDimensions(value){const b=validBounds(value);return {width:(b.east-b.west)*111320*Math.cos((b.north+b.south)/2*Math.PI/180),height:(b.north-b.south)*111320};}
export function worldSizePreview({bounds,metresPerTile,sectionSize,rotationDegrees=0,warp=null}){
 const size=areaDimensions(bounds),scale=number(metresPerTile),section=number(sectionSize),rotation=number(rotationDegrees);
 if(!Number.isFinite(scale)||scale<.5||scale>20||!Number.isInteger(section)||section<256||section>2048||section%256||!Number.isFinite(rotation)||Math.abs(rotation)>45)throw Error('Use 0.5–20 metres per tile, sections of 256–2,048 tiles (multiples of 256), and rotation between −45° and 45°.');
 const a=rotation*Math.PI/180,c=Math.abs(Math.cos(a)),s=Math.abs(Math.sin(a));
 let tileWidth=(size.width*c+size.height*s)/scale,tileHeight=(size.width*s+size.height*c)/scale;
 if(warp){const reference={bounds,metresPerTile:scale,rotationDegrees,warp};validateGeographicWarp(warp,reference);const outline=warpedOutline(reference);tileWidth=Math.max(...outline.map(p=>p[0]));tileHeight=Math.max(...outline.map(p=>p[1]));}
 const columns=Math.max(1,Math.ceil(Math.ceil(tileWidth)/section)),rows=Math.max(1,Math.ceil(Math.ceil(tileHeight)/section));
 return {...size,physicalWidth:size.width,physicalHeight:size.height,columns,rows,width:columns*section,height:rows*section,cells:columns*rows*(section/256)**2};
}
function warpedOutline(reference){const size=areaDimensions(reference.bounds),a=(reference.rotationDegrees??0)*Math.PI/180,c=Math.cos(a),s=Math.sin(a),w=size.width/reference.metresPerTile,h=size.height/reference.metresPerTile;const points=[[0,0],[w,0],[w,h],[0,h]].map(([x,y])=>[x*c-y*s,x*s+y*c]),minX=Math.min(...points.map(p=>p[0])),minY=Math.min(...points.map(p=>p[1]));const input=points.map(([x,y])=>[Math.max(0,x-minX),Math.max(0,y-minY)]);input.push(input[0]);return forwardGeographicWarpPath(reference.warp,input).map(p=>[p.x,p.y]);}
// Source and portable-result budgets stay separate from the shared layout allowance.
const canonical=value=>Array.isArray(value)?value.map(canonical):value&&typeof value==='object'?Object.fromEntries(Object.keys(value).sort().map(key=>[key,canonical(value[key])])):value;
export function worldLayoutKey(request){return JSON.stringify(canonical({bounds:validBounds(request.bounds),metresPerTile:request.metresPerTile,sectionSize:request.sectionSize,rotationDegrees:request.rotationDegrees??0,originCellX:request.originCellX,originCellY:request.originCellY,warp:request.warp??null}));}
export class WorldLayoutConsent {
 constructor(){this.key=null;}
 reset(){this.key=null;}
 approve(request){this.key=worldLayoutKey(request);}
 allows(request){if(this.key!==worldLayoutKey(request))this.reset();return this.key!==null;}
}
export function worldImportPreview(request){
 const p=worldSizePreview(request),sections=p.columns*p.rows,hardIssues=[],limits=WORLD_LAYOUT_LIMITS;
 if(sections>limits.maxSections)hardIssues.push(`This needs ${sections.toLocaleString('en-GB')} sections; the hard limit is ${limits.maxSections.toLocaleString('en-GB')} connected sections. Reduce the area or use larger sections.`);
 if(p.cells>limits.maxCells)hardIssues.push(`This needs ${p.cells.toLocaleString('en-GB')} game cells; the hard limit is ${limits.maxCells.toLocaleString('en-GB')}. Reduce the area or increase metres per tile.`);
 if(![request.originCellX,request.originCellY].every(n=>Number.isInteger(n)&&n>=0&&n<=1000)||request.originCellX+(p.columns-1)*request.sectionSize/256>1000||request.originCellY+(p.rows-1)*request.sectionSize/256>1000)hardIssues.push('The layout exceeds supported cell coordinates. Choose an integer origin from 0 to 1000 with room for all section origins in Advanced.');
 const requiresExpandedLayout=sections>limits.normalSectionLimit||p.cells>limits.normalCellLimit,normal=!requiresExpandedLayout;
 const baseRasterBytes=p.cells*256*256*(1+4/64),composeRasterBytes=request.sectionSize**2*(1+4/64);
 // Composition and the seam pass run sequentially. Neither retains the whole world.
 // These raster-only estimates exclude sources, building metadata and temporary copies.
 const residentRasterBytes=Math.min(baseRasterBytes,limits.maxResidentCells*69632),assemblyMode='sections';
 // Keep the old aggregate alias for callers; it has never included object/source memory.
 const minimumRasterBytes=baseRasterBytes;
 const proposal=requiresExpandedLayout&&!hardIssues.length?{sections,columns:p.columns,rows:p.rows,sectionSize:request.sectionSize,cells:p.cells,assemblyMode,upperBoundResidentCells:Math.min(p.cells,limits.maxResidentCells),baseRasterBytes,minimumRasterBytes,composeRasterBytes,residentRasterBytes}:null;
 const issues=[...hardIssues];
 if(proposal&&request.allowExpandedLayout!==true)issues.push(`This exceeds the normal allowance of ${limits.normalSectionLimit} sections or ${limits.normalCellLimit} game cells. Review the layout and choose “Use ${sections} connected sections” to allow this exact area and scale.`);
 const a=(request.rotationDegrees??0)*Math.PI/180,c=Math.cos(a),s=Math.sin(a),w=p.physicalWidth/request.metresPerTile,h=p.physicalHeight/request.metresPerTile;
 const rotated=[[0,0],[w,0],[w,h],[0,h]].map(([x,y])=>[x*c-y*s,x*s+y*c]);
 const minX=Math.min(...rotated.map(v=>v[0])),minY=Math.min(...rotated.map(v=>v[1]));
 return {...p,sections,normal,requiresExpandedLayout,assemblyMode,upperBoundResidentCells:Math.min(p.cells,limits.maxResidentCells),baseRasterBytes,minimumRasterBytes,composeRasterBytes,residentRasterBytes,proposal,hardIssues,issues,selection:request.warp?warpedOutline(request):rotated.map(([x,y])=>[x-minX,y-minY])};
}
export function sourceMode(value){const mode=value==='smolensk-extract'?'regional':value;if(!['auto','global','overpass','regional','file'].includes(mode))throw Error('Choose an OSM source.');return mode;}
export function formatBytes(bytes){if(!Number.isFinite(bytes)||bytes<0)return 'size unknown';const units=['B','KiB','MiB','GiB'];let unit=0;while(bytes>=1024&&unit<units.length-1){bytes/=1024;unit++;}return `${Number(bytes.toFixed(unit?1:0)).toLocaleString('en-GB')} ${units[unit]}`;}
const phaseNames={queued:'Queued',regions:'Finding Geofabrik regions','region-download':'Downloading regional PBF','region-index':'Indexing region catalogue','region-ready':'Regional extract ready','region-downloaded':'Regional file complete; extracting selected area','pbf-index':'Indexing PBF on disk',regional:'Loading legacy Smolensk PBF',source:'Reading saved source',download:'Loading online source',downloaded:'Source downloaded',compose:'Assembling sections','world-fences':'Joining fences across sections','world-buildings':'Joining buildings across sections','world-validate':'Checking linked world','world-save':'Saving linked world',completed:'Ready'};
export function formatOsmProgress(p={}){return `${phaseNames[p.phase]||p.phase||'Loading OSM'}${p.source?' · '+p.source:''}${Number.isFinite(p.bytesReceived)?' · '+formatBytes(p.bytesReceived)+(Number.isFinite(p.totalBytes)?' / '+formatBytes(p.totalBytes):' received'):Number.isFinite(p.total)&&p.total>0?' · '+(p.completed??0)+'/'+p.total:''}${p.dataTimestamp?' · data '+sourceDate(p.dataTimestamp):''}${p.warning?' · '+p.warning:''}`;}
export function regionPlanText(plan){
 if(!plan||!Array.isArray(plan.regions)||!plan.regions.length)throw Error('No supported Geofabrik region covers this area.');
 const regions=plan.regions.map(region=>`${region.name||region.id} (${region.cached?'cached':'first download'}${region.bytes!=null?', '+formatBytes(region.bytes):', size unknown'}${region.dataDate?', data '+sourceDate(region.dataDate):''})`).join('; ');
 const total=plan.downloadBytes!=null?formatBytes(plan.downloadBytes):'size not yet known';
 const coverage=typeof plan.coverage==='string'?plan.coverage:typeof plan.coverage==='boolean'?(plan.coverage?'complete':'incomplete'):'see regional coverage';
 return `Geofabrik: ${regions}. Download: ${total}${plan.largeFirstDownload?' · Large first download; allow time and disk space.':'.'} Disk limits: ${formatBytes(plan.maximumFileBytes??8*1024**3)} per file, ${formatBytes(plan.maximumCacheBytes??16*1024**3)} regional cache. Coverage: ${coverage}. Availability and exact download sizes depend on the selected regions.`;
}

// Bound bytes before decoding. Accumulate chunk slices, then join decoded fragments
// once per NDJSON line, so a large project line is never repeatedly copied.
export const OSM_RESULT_LIMIT=128_000_000;
const STREAM_ENVELOPE=1024*1024,SMALL_LINE_LIMIT=64*1024;
export async function readOsmImportStream(response,{signal,onProgress=()=>{},maxBytes=OSM_RESULT_LIMIT+STREAM_ENVELOPE}={}){
 signal?.throwIfAborted();if(!response.body?.getReader)throw Error('OSM import stream is unavailable. The current map is preserved.');
 const reader=response.body.getReader(),abort=()=>{reader.cancel(signal.reason).catch(()=>{});};signal?.addEventListener('abort',abort,{once:true});
 let bytes=0,lineBytes=0,parts=[],result,hasResult=false,envelopeBytes=0,finished=false;
 function decodeLine(){const decoder=new TextDecoder('utf-8',{fatal:true}),text=parts.map(part=>decoder.decode(part,{stream:true}));text.push(decoder.decode());return text.join('');}
 function consume(){
  const text=decodeLine();if(!text.trim()){parts=[];lineBytes=0;return;}
  let entry;try{entry=JSON.parse(text);}catch{throw Error('OSM import stream is malformed or truncated. The current map is preserved.');}
  if(!entry||typeof entry!=='object'||Array.isArray(entry))throw Error('OSM import stream has an invalid final/progress record.');
  if(!response.ok)throw Error(entry.error||`OSM request failed (${response.status}).`);
  if(entry.kind==='error')throw Error(typeof entry.error==='string'?entry.error:'OSM import failed.');
  if(hasResult)throw Error('OSM import stream contains data after its terminal result.');
  if(entry.kind==='result'){
   if(!entry.result||typeof entry.result!=='object'||!entry.result.project||typeof entry.result.project!=='object'||Array.isArray(entry.result.project))throw Error('OSM import stream has an invalid final result.');
   if(lineBytes>OSM_RESULT_LIMIT+SMALL_LINE_LIMIT)throw Error('OSM result exceeds the 128 MB project limit.');
   result=entry.result;hasResult=true;
  }else if(entry.kind==='progress'&&entry.progress&&typeof entry.progress==='object'){
   envelopeBytes+=lineBytes;if(lineBytes>SMALL_LINE_LIMIT||envelopeBytes>STREAM_ENVELOPE)throw Error('OSM progress exceeds the stream envelope limit.');
   onProgress(entry.progress);
  }else throw Error('OSM import stream has an invalid final/progress record.');
  parts=[];lineBytes=0;
 }
 try{
  while(true){signal?.throwIfAborted();const {value,done}=await reader.read();signal?.throwIfAborted();if(done)break;
   bytes+=value.byteLength;if(bytes>maxBytes)throw Error('OSM import stream exceeds its byte budget.');
   let offset=0;
   while(offset<value.length){const newline=value.indexOf(10,offset),end=newline<0?value.length:newline,part=value.subarray(offset,end);lineBytes+=part.byteLength;
    if(lineBytes>(response.ok?OSM_RESULT_LIMIT+SMALL_LINE_LIMIT:SMALL_LINE_LIMIT))throw Error('OSM import stream line exceeds its size limit.');
    if(part.length)parts.push(part);if(newline<0)break;consume();offset=newline+1;
   }
  }
  if(!response.ok){if(parts.length)consume();throw Error(`OSM request failed (${response.status}).`);}
  if(parts.length||!hasResult)throw Error('OSM import stream is incomplete; no complete final result. The current map is preserved.');
  signal?.throwIfAborted();finished=true;return result;
 }finally{signal?.removeEventListener('abort',abort);if(!finished)await reader.cancel().catch(()=>{});reader.releaseLock();}
}
export async function importOsmStream(request,{signal,onProgress,fetchImpl=globalThis.fetch}={}){
 signal?.throwIfAborted();const response=await fetchImpl('/api/import/osm/stream',{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/x-ndjson'},body:JSON.stringify(request),signal});
 return readOsmImportStream(response,{signal,onProgress});
}
export async function osmApi(url,body,{signal,fetchImpl=globalThis.fetch}={}){
 const response=await fetchImpl(url,{...(body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}),signal});
 let data;try{data=await response.json();}catch{throw Error(`OSM request failed (${response.status||'invalid response'}).`);}
 if(!response.ok)throw Error(data.error||`OSM request failed (${response.status}).`);return data;
}
function fileDataUrl(file,signal){return new Promise((resolve,reject)=>{
 const reader=new FileReader(),clean=()=>signal?.removeEventListener('abort',abort),abort=()=>reader.abort();
 reader.onload=()=>{clean();resolve(reader.result);};reader.onerror=()=>{clean();reject(reader.error||Error('Cannot read the source file.'));};reader.onabort=()=>{clean();reject(new DOMException('Source reading cancelled.','AbortError'));};
 signal?.throwIfAborted();signal?.addEventListener('abort',abort,{once:true});reader.readAsDataURL(file);
});}
export async function uploadOsmSource(file,bounds,{signal,maxJsonBytes=JSON_SOURCE_LIMIT,fetchImpl,readDataUrl=fileDataUrl}={}){
 signal?.throwIfAborted();const selected=validBounds(bounds),pbf=/\.pbf$/i.test(file?.name||'');
 if(!file||!(/\.(json|geojson|pbf)$/i.test(file.name)))throw Error('Choose an OSM JSON, GeoJSON, or .osm.pbf file.');
 const limit=pbf?PBF_SOURCE_LIMIT:maxJsonBytes;
 if(file.size>limit)throw Error(`The ${pbf?'PBF':'JSON'} file exceeds ${pbf?'64 MiB':`${limit/1_000_000} MB`} (${limit.toLocaleString('en-GB')} bytes). Select a smaller extract.`);
 if(!file.size)throw Error('The source file is empty.');
 const payload={name:file.name,bounds:selected};
 if(pbf){const data=await readDataUrl(file,signal),match=/^data:[^,]*;base64,([A-Za-z0-9+/=]+)$/.exec(data);if(!match)throw Error('Cannot read PBF base64 data.');payload.pbfBase64=match[1];}
 else payload.json=await file.text();
 signal?.throwIfAborted();const result=await osmApi('/api/osm/sources',payload,{signal,fetchImpl});
 if(typeof result.id!=='string'||!result.id)throw Error('The server did not return a source ID.');
 return result;
}
export function setSourceDownload(link,id){link.hidden=!id;if(id){link.href='/api/osm/sources/'+encodeURIComponent(id)+'/download';link.download='osm-source.json';}else link.removeAttribute('href');}

// One controller for both import dialogs; bounds are checked again at commit time.
export class OsmSourceSelection {
 constructor({select,file,status,download,bounds,maxJsonBytes=JSON_SOURCE_LIMIT,onChange=()=>{},onBusy=()=>{},onMessage=()=>{},fetchImpl=globalThis.fetch}){
  Object.assign(this,{select,file,status,download,bounds,maxJsonBytes,onChange,onBusy,onMessage,fetchImpl});this.selectedFile=null;this.saved=null;this.running=false;this.locked=false;this.regionPlans=new Map();
  this.planButton=status.closest?.('.osm-source-panel')?.querySelector('[data-osm-region-plan]');
  if(this.planButton)this.planButton.onclick=async()=>{if(this.locked||this.running)return;try{await this.ensure();}catch(error){this.message(error.message,true);}};
  select.addEventListener('change',()=>{this.onChange();this.refresh();});
  file.addEventListener('change',async()=>{this.selectedFile=file.files[0]||null;this.saved=null;setSourceDownload(this.download,null);this.onChange();this.refresh();if(this.selectedFile){try{await this.ensure();}catch(error){this.message(error.message,true);}}});
  this.refresh();
 }
 copySelectionFrom(source){
  if(this.running||this.locked||source.running)throw Error('Wait for source preparation to finish.');
  const changed=this.select.value!==source.select.value||this.selectedFile!==source.selectedFile||!source.selectedFile&&this.saved?.data.id!==source.saved?.data.id;
  this.select.value=source.select.value;this.selectedFile=source.selectedFile;
  if(source.file.files)this.file.files=source.file.files;
  // The larger world allowance must never let cached JSON bypass the single-map limit.
  this.saved=source.saved&&(source.saved.maxJsonBytes??source.maxJsonBytes)<=this.maxJsonBytes?structuredClone({...source.saved,maxJsonBytes:source.saved.maxJsonBytes??source.maxJsonBytes}):null;
  const sourceLink=source.download.getAttribute?.('href')??source.download.href??'';
  const downloaded=/\/api\/osm\/sources\/([^/]+)\/download$/.exec(sourceLink);
  setSourceDownload(this.download,source.saved?.data.id??(!source.download.hidden&&downloaded?decodeURIComponent(downloaded[1]):null));
  if(changed)this.onChange();this.refresh();
 }
 message(text,error=false){this.status.textContent=text;this.status.classList.toggle('error',error);this.onMessage();}
 setLocked(value){this.locked=value;this.select.disabled=value||this.running;this.file.disabled=value||this.running;if(this.planButton)this.planButton.disabled=value||this.running;this.download.setAttribute('aria-disabled',String(value||this.running));this.download.tabIndex=value||this.running?-1:0;}
 refresh(){
  const mode=sourceMode(this.select.value);this.file.closest('label').hidden=mode!=='file';if(this.planButton)this.planButton.hidden=!['auto','global'].includes(mode);
  if(this.running)return;
  if(mode==='file'){
   if(!this.selectedFile)this.message(`Choose local OSM JSON (${this.maxJsonBytes/1_000_000} MB maximum) or PBF (64 MiB maximum), including extracts from other regions. No OSM network requests are used.`);
   else{let fresh=false;try{fresh=this.saved?.key===boundsKey(this.bounds());}catch{}this.message(fresh?`${this.selectedFile.name} ready · ${this.saved.data.features??'unknown'} features · ${sourceDate(this.saved.data.dataTimestamp)}${this.saved.data.warnings?.length?' · '+this.saved.data.warnings.join(' '):''}`:`${this.selectedFile.name} selected. It will be uploaded for the current bounds before import.`);}
  }else if(['auto','global'].includes(mode)){
   let key;try{key=mode+':'+boundsKey(this.bounds());}catch{}
   const plan=this.regionPlans.get(key);
   this.message((plan?regionPlanText(plan):this.regionFallback&&this.regionFallback.key===key?this.regionFallback.text:'Choose “Check regional download” to see Geofabrik regions, first-download size and cache before importing. Automatic regional PBFs use disk budgets of up to 8 GiB per file and 16 GiB cache; coverage and file sizes vary.')+' '+(mode==='auto'?'Automatic tries Geofabrik PBF first, then Overpass if unavailable. A reused saved snapshot discloses its original date.':'Geofabrik PBF only. Unsupported coverage or download failure stops the import; choose Automatic to allow Overpass fallback.'));
  }else this.message(mode==='regional'?'Legacy Smolensk PBF only (cached locally). This optional compatibility source has no automatic geographic preference.':'Online Overpass with small requests and backup servers. If unavailable, choose Local file to import offline.');
 }
 changedBounds(){this.refresh();}
 async ensure(signal){
  signal?.throwIfAborted();const mode=sourceMode(this.select.value);
  if(['auto','global'].includes(mode))return this.ensureRegions(mode,signal);
  if(mode!=='file')return {sourceMode:mode,useRegionalExtract:mode==='regional'};
  if(!this.selectedFile)throw Error('Choose a local OSM JSON or PBF file first.');
  const bounds=this.bounds(),key=boundsKey(bounds);
  if(this.saved?.key!==key){
   if(this.running)throw Error('Wait for the source upload to finish.');
   this.running=true;this.setLocked(this.locked);this.onBusy(true);this.message('Reading and uploading the local source…');
   try{const data=await uploadOsmSource(this.selectedFile,bounds,{signal,maxJsonBytes:this.maxJsonBytes,fetchImpl:this.fetchImpl});if(boundsKey(this.bounds())!==key)throw Error('Area changed during upload. Import again to upload the file for the new bounds.');this.saved={key,data,maxJsonBytes:this.maxJsonBytes};setSourceDownload(this.download,data.id);}
   catch(error){this.message(error.message,true);throw error;}
   finally{this.running=false;this.setLocked(this.locked);this.onBusy(false);}
   this.refresh();
  }
  return {sourceMode:mode,sourceId:this.saved.data.id,useRegionalExtract:false};
 }
 async ensureRegions(mode,signal){
  const bounds=validBounds(this.bounds()),key=mode+':'+boundsKey(bounds);
  if(!this.regionPlans.has(key)){
   if(this.running)throw Error('Wait for the regional download check to finish.');
   this.running=true;this.setLocked(this.locked);this.onBusy(true);this.message('Checking Geofabrik regions, download size and local cache…');
   try{
    const plan=await osmApi('/api/osm/regions/plan',bounds,{signal,fetchImpl:this.fetchImpl});signal?.throwIfAborted();regionPlanText(plan);
    if(boundsKey(this.bounds())!==boundsKey(bounds)||sourceMode(this.select.value)!==mode)throw Error('Area or source changed while checking regional downloads. Check again.');
    this.regionPlans.set(key,plan);if(this.regionPlans.size>16)this.regionPlans.delete(this.regionPlans.keys().next().value);this.regionFallback=null;
   }catch(error){
    if(error.name==='AbortError'||signal?.aborted||boundsKey(this.bounds())!==boundsKey(bounds)||sourceMode(this.select.value)!==mode)throw error;
    if(mode==='global'){this.message(error.message,true);throw error;}
    this.regionFallback={key,text:`Geofabrik planning unavailable: ${error.message} Automatic import will retry regional data, then online providers and a compatible saved snapshot.`};this.message(this.regionFallback.text);return {sourceMode:'auto',useRegionalExtract:false};
   }finally{this.running=false;this.setLocked(this.locked);this.onBusy(false);}
  }
  this.refresh();return {sourceMode:mode,useRegionalExtract:false};
 }
 progressText(progress){return formatOsmProgress(progress);}
 importMap(request,options){return importOsmStream(request,{fetchImpl:this.fetchImpl,...options});}
 record(id){if(id)setSourceDownload(this.download,id);}
}
