import {installWorldWarpControls} from './world-warp-controls.js';
import {reviewStreetGrid,applyStreetGridReview,streetGridReviewText} from './street-grid-review.js';
import {readOvertureFile,overtureReport} from './overture-source.js';
import {installWorldBuildOutput} from './world-build-output.js';
import {OsmSourceSelection,osmApi as api,boundsFromCentre,areaDimensions,validBounds,boundsKey,worldImportPreview,WorldLayoutConsent,WORLD_LAYOUT_LIMITS,sourceMode,sourceDate,formatBytes,formatOsmProgress} from './osm-source.js';
const $=id=>document.getElementById(id);
const value=id=>{const raw=$('osm-world-'+id).value;return raw.trim()===''?NaN:Number(raw);};
const frameFields=['lat','lon','width','height','north','south','west','east','scale','size','x','y','rotation'];
const format=n=>Math.round(n).toLocaleString('en-GB');
const recoveryLabels={'resume-download':'Resume regional download','retry-processing':'Retry downloaded source','retry-build':'Retry world build'};
function importRecovery(state){
 if(state.status!=='failed')return null;
 if(Object.hasOwn(recoveryLabels,state.recovery))return state.recovery;
 const p=state.progress||{};
 if(p.phase==='region-download'&&Number.isFinite(p.bytesReceived)&&p.bytesReceived>0&&(p.totalBytes==null||Number.isFinite(p.totalBytes)&&p.totalBytes>p.bytesReceived)&&/timeout|timed[\s-]*out|deadline/i.test(state.error||''))return 'resume-download';
 return state.retainedRegionalSource===true?'retry-processing':null;
}
export class OsmWorldImport {
 constructor({getWorld,acceptWorld,acceptWorkspace,getSelection,onApplied,onBackToArea}){
  Object.assign(this,{getWorld,acceptWorld,acceptWorkspace,getSelection,onApplied,onBackToArea});this.job=null;this.busy=false;this.sourceBusy=false;this.result=null;this.recovery=null;this.recoverySourceId=null;this.revision=0;this.initialized=false;this.searchRevision=0;this.layoutConsent=new WorldLayoutConsent();
  const button=document.createElement('button');button.id='world-import-osm';button.textContent='World from OpenStreetMap';$('world-open').before(button);button.onclick=()=>this.open();
  const dialog=document.createElement('dialog');dialog.id='osm-world-dialog';dialog.className='osm-world-dialog';dialog.setAttribute('aria-labelledby','osm-world-title');
  dialog.innerHTML=`<div class="dialog-heading"><div><h2 id="osm-world-title">Import a real place as a world</h2><p class="hint">One area, one scale. Sections join into a single editable world.</p></div><button type="button" id="osm-world-close" aria-label="Close world import">×</button></div>
   <div id="osm-world-preserve-row" hidden><label class="check"><input id="osm-world-preserve" type="checkbox">Refresh this world and keep manual edits</label><p id="osm-world-frame-state" class="hint"></p></div>
   <div class="osm-world-layout"><div class="osm-world-setup">
    <section id="osm-world-area-step" class="osm-world-step" aria-labelledby="osm-world-area-heading"><h3 id="osm-world-area-heading"><span>1</span>Area &amp; layout</h3><details id="osm-world-place"><summary>Find a different place</summary>
     <form id="osm-world-search-form" class="search-row"><label class="sr-only" for="osm-world-query">City or address</label><input id="osm-world-query" placeholder="City, district or address" maxlength="180" required><button id="osm-world-search" type="submit">Find place</button></form>
     <div id="osm-world-results" role="group" aria-label="Locations found"></div><p id="osm-world-search-state" class="hint" role="status"></p>
     <details id="osm-world-coordinates"><summary>Enter centre coordinates</summary><div class="osm-world-fields"><label>Latitude<input id="osm-world-lat" type="number" min="-84" max="84" step="any"></label><label>Longitude<input id="osm-world-lon" type="number" min="-179" max="179" step="any"></label></div></details>
     <p id="osm-world-centre-label" class="hint"></p>
     </details>
     <div class="osm-world-presets" role="group" aria-label="Area size presets"><button type="button" data-osm-area="0.5">Block<small>0.5 × 0.5 km</small></button><button type="button" data-osm-area="1">Neighbourhood<small>1 × 1 km</small></button><button type="button" data-osm-area="2">District<small>2 × 2 km</small></button><button type="button" data-osm-area="4">Town<small>4 × 4 km</small></button></div>
     <div class="osm-world-fields"><label>Width (km)<input id="osm-world-width" type="number" min="0.001" step="any"></label><label>Height (km)<input id="osm-world-height" type="number" min="0.001" step="any"></label></div>
     <label>World name<input id="osm-world-name" maxlength="80" value="World from OpenStreetMap"></label>
     <label>Scale · metres per game tile<input id="osm-world-scale" type="number" min="0.5" max="20" step="0.5" value="1" aria-describedby="osm-world-scale-help"></label><p id="osm-world-scale-help" class="hint">Recommended: 1 m per tile. Larger values shrink houses and may remove small buildings.</p>
     <details id="osm-world-advanced"><summary>Advanced layout settings</summary><div class="osm-world-fields"><label>Section size (tiles)<select id="osm-world-size"><option value="256">256 × 256 · 1 cell</option><option value="512">512 × 512 · 4 cells</option><option value="768">768 × 768 · 9 cells</option><option value="1024" selected>1024 × 1024 · 16 cells</option><option value="2048">2048 × 2048 · 64 cells</option></select></label><label>Rotation (degrees)<input id="osm-world-rotation" type="number" min="-45" max="45" step="any" value="0"></label><label>Origin cell X<input id="osm-world-x" type="number" min="0" max="1000" value="100"></label><label>Origin cell Y<input id="osm-world-y" type="number" min="0" max="1000" value="100"></label><label>North<input id="osm-world-north" type="number" step="any"></label><label>South<input id="osm-world-south" type="number" step="any"></label><label>West<input id="osm-world-west" type="number" step="any"></label><label>East<input id="osm-world-east" type="number" step="any"></label><label>Seed<input id="osm-world-seed" type="number" value="42"></label></div><p class="hint">Sections are editing units, not separate worlds. The outside edge is padded to whole sections.</p></details>
    </section>
    <section id="osm-world-source-step" class="osm-world-step osm-source-panel" aria-labelledby="osm-world-source-heading"><h3 id="osm-world-source-heading"><span>2</span>Source &amp; build</h3>
     <label>Data source<select id="osm-world-source"><option value="auto">Automatic (recommended)</option><option value="file">Saved JSON or PBF · offline</option><option value="global">Geofabrik only</option><option value="overpass">Online Overpass only</option><option value="regional">Legacy Smolensk extract</option></select></label><label hidden>Choose a saved geographic file<input id="osm-world-source-file" type="file" accept=".json,.geojson,.pbf"></label>
     <p id="osm-world-download-summary" class="hint" role="status"></p><button type="button" data-osm-region-plan>Check download size</button>
     <details id="osm-world-source-details"><summary>Source details and file limits</summary><p id="osm-world-source-state" class="hint"></p><p class="hint">Manual JSON: 64 MB. Manual PBF: 64 MiB, with up to 16 MB selected data. Automatic downloads: 8 GiB per region, 16 GiB cache. Ordinary layout: ${format(WORLD_LAYOUT_LIMITS.normalSectionLimit)} sections / ${format(WORLD_LAYOUT_LIMITS.normalCellLimit)} game cells. Explicit expanded layouts: up to ${format(WORLD_LAYOUT_LIMITS.maxSections)} sections / ${format(WORLD_LAYOUT_LIMITS.maxCells)} cells. Portable world JSON stays within 128 MB. Sections are saved incrementally; only the current section and a bounded neighboring window are retained for assembly and seam checks. Sources and building metadata use separate memory; the selected-source budget stays 64 MB. Source and per-section limits still apply.</p></details>
     <a id="osm-world-source-download" hidden>Save geographic source JSON</a>
    </section>
   </div><aside class="osm-world-overview" aria-labelledby="osm-world-overview-heading"><h3 id="osm-world-overview-heading">Your world layout</h3><p id="osm-world-layout-count"></p><svg id="osm-world-layout-grid" role="img" aria-label="Preview of the selected area and its sections" preserveAspectRatio="xMidYMid meet"></svg><p id="osm-world-grid-key" class="hint">Shaded: selected area. Each square: one editable section. Empty margins pad the world.</p><p id="osm-world-size-preview" class="hint" role="status"></p>
    <section id="osm-world-expanded-proposal" class="osm-world-expanded-proposal" aria-labelledby="osm-world-expanded-title" hidden><h4 id="osm-world-expanded-title">Keep this area as connected sections</h4><p id="osm-world-expanded-summary"></p><button type="button" id="osm-world-expanded-approve" class="primary"></button><p id="osm-world-expanded-memory" class="hint"></p><p id="osm-world-expanded-state" class="hint" role="status"></p></section>
    <button type="button" id="osm-world-preview">View selected area on a map</button><a id="osm-world-preview-link" target="_blank" rel="noopener noreferrer" hidden>Open area in OpenStreetMap</a><div id="osm-world-preview-container" hidden><iframe id="osm-world-preview-map" title="Selected OpenStreetMap world area" loading="lazy" referrerpolicy="no-referrer"></iframe></div><p class="hint">The layout above is a size preview. Buildings appear after import.</p>
   </aside></div>
   <section id="osm-world-result-step" class="osm-world-step" aria-labelledby="osm-world-result-heading" hidden><h3 id="osm-world-result-heading"><span>3</span>Result</h3><p id="osm-world-result-summary" class="hint"></p><div class="osm-world-outcome"><details id="osm-world-report" hidden><summary>Import report and sources</summary><ul id="osm-world-warnings"></ul></details></div></section>
   <div class="osm-world-primary-actions"><progress id="osm-world-progress" max="1" value="0" hidden></progress><p id="osm-world-state" role="status" aria-live="polite"></p><button type="button" id="osm-world-offline" hidden>Use a saved file instead</button><p id="osm-world-safety">Your current world stays open until you apply the result. Applying can be undone.</p><div class="dialog-actions"><button type="button" id="osm-world-plan">Check import</button><button type="button" id="osm-world-start" class="primary">Download and build world</button><button type="button" id="osm-world-apply" class="primary" hidden>Open imported world</button><button type="button" id="osm-world-cancel" hidden>Cancel import</button></div><small>Map data: <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap contributors / ODbL</a></small></div>`;
  document.body.append(dialog);
  const back=document.createElement('button');back.id='osm-world-back-to-area';back.type='button';back.textContent='Back to area selection';back.hidden=!onBackToArea;dialog.querySelector('.dialog-heading').after(back);back.onclick=()=>{if(this.busy||this.sourceBusy)return;const area=this.area();dialog.close();onBackToArea?.(area,this.source);};
  this.output=installWorldBuildOutput({dialog,getResult:()=>this.result,status:(message,error)=>this.status(message,error)});
  const alignment=document.createElement('div');alignment.className='osm-world-street-grid';
  alignment.innerHTML='<button type="button" id="osm-world-grid-detect">Measure street-grid rotation</button><button type="button" id="osm-world-grid-apply" hidden>Use suggested rotation</button><p id="osm-world-grid-state" class="hint" role="status">Measures local streets from the selected source. Review the angle before applying it to this new world.</p>';
  $('osm-world-advanced').append(alignment);
  $('osm-world-grid-detect').onclick=()=>this.detectStreetGrid();$('osm-world-grid-apply').onclick=()=>this.applyStreetGrid();
  const overture=document.createElement('details');overture.id='osm-world-overture';
  overture.innerHTML='<summary>Add missing buildings from Overture</summary><p class="hint">Optional saved Overture buildings GeoJSON, up to 64 MB. Existing mapped buildings take priority. This prepares a composed source before building; no extra download service or Python installation is required.</p><label>Overture buildings GeoJSON<input id="osm-world-overture-file" type="file" accept=".geojson,.json"></label><button type="button" id="osm-world-overture-add">Check and add missing footprints</button><p id="osm-world-overture-state" class="hint" role="status"></p>';
  $('osm-world-source-details').before(overture);$('osm-world-overture-add').onclick=()=>this.addOverture();
  this.deformation=installWorldWarpControls({dialog,frame:()=>({bounds:this.area(),metresPerTile:value('scale'),originCellX:value('x'),originCellY:value('y'),rotationDegrees:value('rotation')}),changed:()=>this.invalidate(),status:(message,error)=>this.status(message,error)});
  this.source=new OsmSourceSelection({select:$('osm-world-source'),file:$('osm-world-source-file'),status:$('osm-world-source-state'),download:$('osm-world-source-download'),bounds:()=>this.area(),onChange:()=>{this.preparedSource=null;$('osm-world-overture-state').textContent='';this.invalidate();},onBusy:busy=>{this.sourceBusy=busy;this.controls();},onMessage:()=>this.refreshSourceSummary()});
  $('osm-world-source-download').addEventListener('click',event=>{if(this.busy||this.sourceBusy)event.preventDefault();});
  $('osm-world-expanded-approve').onclick=()=>this.approveExpandedLayout();
  $('osm-world-close').onclick=()=>dialog.close();$('osm-world-plan').onclick=()=>this.plan();$('osm-world-start').onclick=()=>this.start();$('osm-world-cancel').onclick=()=>this.cancel();$('osm-world-apply').onclick=()=>this.apply();$('osm-world-preview').onclick=()=>this.preview();
  $('osm-world-offline').onclick=()=>{if(this.busy||this.sourceBusy)return;$('osm-world-source').value='file';this.source.refresh();this.invalidate();$('osm-world-source-file').focus();};
  $('osm-world-search-form').onsubmit=event=>{event.preventDefault();this.search();};
  for(const button of dialog.querySelectorAll('[data-osm-area]'))button.onclick=()=>{
   if(this.busy||this.sourceBusy||$('osm-world-preserve').checked)return;
   try{const km=Number(button.dataset.osmArea),b=boundsFromCentre(value('lat'),value('lon'),km,km);this.writeBounds(b);this.writeCentre(b);this.invalidate();}catch(error){this.status(error.message,true);}
  };
  $('osm-world-preserve').onchange=()=>{if($('osm-world-preserve').checked)this.loadFrame();this.invalidate();this.controls();};
  for(const input of dialog.querySelectorAll('.osm-world-fields input,.osm-world-fields select'))input.addEventListener('input',()=>{
   const id=input.id.replace('osm-world-','');
   try{if(['lat','lon','width','height'].includes(id))this.writeBounds(boundsFromCentre(value('lat'),value('lon'),value('width'),value('height')));else if(['north','south','west','east'].includes(id))this.writeCentre(this.area());}catch{}
   this.invalidate();
  });
  for(const id of ['name','scale'])$('osm-world-'+id).addEventListener('input',()=>this.invalidate());
 }
 open({selection:chosenArea,source:chosenSource}={}){
  const world=this.getWorld();let selection=chosenArea;
  if(!selection)try{selection=this.getSelection?.();}catch{}
  const contextKey=JSON.stringify({id:world?.id,reference:world?.geoReference,sections:world?.maps?.map(m=>[m.id,m.project.width,m.project.height,m.project.worldX,m.project.worldY]),selection});
  let sameArea=false;try{sameArea=boundsKey(chosenArea)===boundsKey(this.area());}catch{}
  if(chosenArea&&!this.busy&&!this.sourceBusy&&!sameArea)this.initialized=false;
  if(!this.initialized||(!chosenArea&&!this.busy&&!this.sourceBusy&&!this.result&&contextKey!==this.contextKey)){
   this.contextKey=contextKey;this.reference=!chosenArea&&world?.geoReference?structuredClone(world.geoReference):null;$('osm-world-preserve').checked=!!this.reference;
   if(this.reference){$('osm-world-preserve').checked=true;this.loadFrame();}
   else{let bounds=selection;if(!bounds){const lat=$('import-lat')?.value??54.78,lon=$('import-lon')?.value??32.04,extent=Number($('import-extent')?.value||800)/1000;bounds=boundsFromCentre(lat,lon,extent,extent);}this.writeBounds(bounds);this.writeCentre(bounds);}
   this.initialized=true;this.invalidate();this.status(this.frameError||'Choose an area. Check the download size before building.',!!this.frameError);
  }
  if(chosenSource&&!this.busy&&!this.sourceBusy)this.source.copySelectionFrom(chosenSource);
  this.controls();if(!$('osm-world-dialog').open)$('osm-world-dialog').showModal();
 }
 loadFrame(){
  const world=this.getWorld(),r=world?.geoReference;
  if(!r){$('osm-world-preserve').checked=false;this.status('This world has no geographic frame to preserve.',true);return;}
  this.reference=structuredClone(r);this.deformation.load(r);this.writeBounds(r.bounds);this.writeCentre(r.bounds);
  for(const [id,data] of [['name',world.name],['scale',r.metresPerTile],['x',r.originCellX],['y',r.originCellY],['rotation',r.rotationDegrees??0],['seed',world.maps[0]?.project.seed??42]])$('osm-world-'+id).value=data;
  const section=world.maps[0]?.project.width??1024,select=$('osm-world-size');
  if(![...select.options].some(option=>+option.value===section))select.add(new Option(`${section} × ${section}`,String(section)));select.value=section;
 }
 writeBounds(b){for(const key of ['north','south','west','east'])$('osm-world-'+key).value=b[key];}
 writeCentre(b){const size=areaDimensions(b);for(const [key,val] of [['lat',(b.north+b.south)/2],['lon',(b.west+b.east)/2],['width',size.width/1000],['height',size.height/1000]])$('osm-world-'+key).value=Number(val.toFixed(8));}
 area(){return validBounds(Object.fromEntries(['north','south','west','east'].map(key=>[key,value(key)])));}
 request({includeWorld=false,allowUnapproved=false}={}){
  const preserveEdits=$('osm-world-preserve').checked;
  // Existing valid frames are checked by their exact bounds, not the narrower new-centre picker.
  if(!preserveEdits)boundsFromCentre(value('lat'),value('lon'),value('width'),value('height'));
  const request={bounds:this.area(),metresPerTile:value('scale'),sectionSize:value('size'),originCellX:value('x'),originCellY:value('y'),seed:value('seed'),rotationDegrees:value('rotation'),name:$('osm-world-name').value.trim(),preserveEdits,sourceMode:sourceMode($('osm-world-source').value),preferSections:true};
  request.warp=this.deformation.read(this.getWorld()?.geoReference,preserveEdits);request.regionalProfile=this.deformation.profile;
  if(this.layoutConsent.allows(request))request.allowExpandedLayout=true;
  const preview=worldImportPreview(request),issues=allowUnapproved?preview.hardIssues:preview.issues;if(issues.length)throw Error(issues.join(' '));
  if(!request.name||request.name.length>80||/[\x00-\x1f\x7f]/.test(request.name))throw Error('Enter a world name (1–80 characters).');
  if(![request.originCellX,request.originCellY].every(n=>Number.isInteger(n)&&n>=0&&n<=1000)||!Number.isInteger(request.seed)||request.seed< -2147483648||request.seed>2147483647)throw Error('Cell origins must be integers from 0 to 1000; use a 32-bit integer seed.');
  if(preserveEdits){
   const world=this.getWorld(),r=world?.geoReference;
   if(!r||JSON.stringify(r.warp??null)!==JSON.stringify(request.warp??null)||boundsKey(r.bounds)!==boundsKey(request.bounds)||r.metresPerTile!==request.metresPerTile||r.originCellX!==request.originCellX||r.originCellY!==request.originCellY||(r.rotationDegrees??0)!==request.rotationDegrees||world.maps.some(m=>m.project.width!==request.sectionSize||m.project.height!==request.sectionSize))throw Error('The existing geographic frame changed. Reopen preservation or turn it off before choosing a new frame.');
   if(includeWorld)request.existingWorld=structuredClone(world);
  }
  return request;
 }
 invalidate(){
  this.gridReview=null;$('osm-world-grid-apply').hidden=true;$('osm-world-grid-state').textContent='Measure the selected source to suggest one common world rotation.';
  this.showResultStage=false;this.revision++;this.planData=null;this.result=null;this.recovery=null;this.recoverySourceId=null;$('osm-world-apply').hidden=true;$('osm-world-start').hidden=false;$('osm-world-report').hidden=true;$('osm-world-offline').hidden=true;
  this.output?.reset();
  $('osm-world-preview-container').hidden=true;$('osm-world-preview-map').removeAttribute('src');$('osm-world-preview-link').hidden=true;
  this.source?.changedBounds();this.refreshSize();if(!this.busy)this.status(this.frameError||'Review the layout, then check the import or build your world.',!!this.frameError);this.controls();
 }
 approveExpandedLayout(){
  if(this.busy||this.sourceBusy)return;
  try{const request=this.request({allowUnapproved:true}),preview=worldImportPreview(request);if(!preview.proposal||preview.hardIssues.length)return;this.layoutConsent.approve(request);this.invalidate();this.status('Expanded layout approved for this exact area and scale. Choose “Check import” or “Download and build world” when ready.');}
  catch(error){this.status(error.message,true);}
 }
 refreshSize(){
  this.frameError=null;$('osm-world-expanded-proposal').hidden=true;$('osm-world-expanded-approve').hidden=true;
  try{
   const request=this.request({allowUnapproved:true}),p=worldImportPreview(request);this.frameError=p.issues.join(' ')||null;
   $('osm-world-size-preview').textContent=this.frameError||this.describePlan(p,request);$('osm-world-layout-count').textContent=`${p.sections} ${p.sections===1?'section':'sections'} · ${p.columns} across × ${p.rows} down`;this.drawLayout(p,request.sectionSize);
   if(p.proposal){
    const approved=request.allowExpandedLayout===true;$('osm-world-expanded-proposal').hidden=false;
    $('osm-world-expanded-summary').textContent=`${p.columns} columns × ${p.rows} rows · ${request.sectionSize} × ${request.sectionSize} tiles per section · ${format(p.cells)} game cells. Your area and scale stay as selected.`;
    $('osm-world-expanded-memory').textContent=`Aggregate base rasters: ${formatBytes(p.baseRasterBytes)} of disk/work, not peak RAM or an exact file size. Sections are saved incrementally; only the current section and a bounded neighboring window are retained for assembly and seam checks. Conservative resident raster estimate: ${formatBytes(p.residentRasterBytes)} (${formatBytes(p.composeRasterBytes)} while composing one section). Buildings, sources and other object metadata use separate memory; temporary copies add overhead. Selected-source budget: 64 MB. Source and per-section limits still apply; portable world JSON keeps its 128 MB limit.`;
    $('osm-world-expanded-approve').textContent=`Use ${p.sections} connected sections`;$('osm-world-expanded-approve').hidden=approved;
    $('osm-world-expanded-state').textContent=approved?'Approved for this layout. Changing bounds, scale, section size, rotation, origin or deformation requires approval again.':'This only allows the proposed layout. It does not start a download or build.';
   }
  }catch(error){this.layoutConsent.reset();this.frameError=error.message;$('osm-world-size-preview').textContent=error.message;$('osm-world-layout-count').textContent='Adjust the area or scale to continue';$('osm-world-layout-grid').replaceChildren();$('osm-world-layout-grid').setAttribute('hidden','');$('osm-world-grid-key').hidden=true;}
  $('osm-world-size-preview').classList.toggle('error',!!this.frameError);
  $('osm-world-centre-label').textContent=Number.isFinite(value('lat'))&&Number.isFinite(value('lon'))?`Centre: ${value('lat').toFixed(5)}, ${value('lon').toFixed(5)}`:'Choose a place or enter centre coordinates.';
  for(const button of $('osm-world-dialog').querySelectorAll('[data-osm-area]'))button.setAttribute('aria-pressed',String(Math.abs(value('width')-Number(button.dataset.osmArea))<.00001&&Math.abs(value('height')-Number(button.dataset.osmArea))<.00001));
 }
 drawLayout(plan,sectionSize){
  const svg=$('osm-world-layout-grid'),ns='http://www.w3.org/2000/svg';svg.replaceChildren();svg.removeAttribute('hidden');$('osm-world-grid-key').hidden=false;
  const append=(tag,attributes)=>{const node=document.createElementNS(ns,tag);for(const [key,value] of Object.entries(attributes))node.setAttribute(key,String(value));svg.append(node);return node;};
  svg.setAttribute('viewBox',`-8 -8 ${plan.width+16} ${plan.height+16}`);svg.setAttribute('aria-label',`${plan.columns*plan.rows} sections in ${plan.columns} columns and ${plan.rows} rows. Shading marks the selected geographic area; margins are padding.`);
  append('rect',{x:0,y:0,width:plan.width,height:plan.height,class:'osm-world-grid-background'});
  append('polygon',{points:plan.selection.map(p=>p.join(',')).join(' '),class:'osm-world-grid-area'});
  // One path keeps expanded previews small, even with thousands of sections.
  const lines=[`M0 0H${plan.width}V${plan.height}H0Z`];
  for(let x=1;x<plan.columns;x++)lines.push(`M${x*sectionSize} 0V${plan.height}`);
  for(let y=1;y<plan.rows;y++)lines.push(`M0 ${y*sectionSize}H${plan.width}`);
  append('path',{d:lines.join(''),class:'osm-world-grid-section','vector-effect':'non-scaling-stroke'});
 }
 refreshSourceSummary(){
  if(!this.source)return;const source=this.source,mode=sourceMode($('osm-world-source').value),label=$('osm-world-download-summary');
  label.classList.toggle('error',source.status.classList.contains('error'));
  if(source.status.classList.contains('error')){label.textContent=source.status.textContent;return;}
  if(source.running){label.textContent=mode==='file'?'Preparing your saved file…':'Checking regional files and download size…';return;}
  if(mode==='file'){let fresh=false;try{fresh=source.saved?.key===boundsKey(this.area());}catch{}label.textContent=source.selectedFile?`${source.selectedFile.name}${fresh?' · ready for the selected area':' · selected; check the import to prepare it'}. No OSM download is needed.`:'Choose a saved geographic JSON or PBF covering this area. The import works offline.';return;}
  if(['auto','global'].includes(mode)){
   let key;try{key=mode+':'+boundsKey(this.area());}catch{}
   const plan=source.regionPlans.get(key);
   label.textContent=plan?`${plan.regions.map(r=>r.name||r.id).join(', ')}: ${plan.downloadBytes==null?'download size unknown':formatBytes(plan.downloadBytes)+' to download'}. ${plan.regions.every(r=>r.cached)?'Regional files are already saved locally.':'The first regional download can be much larger than your area; later imports reuse it.'}`:source.regionFallback?.key===key?'The regional check failed. Automatic import will retry, then try online providers and a compatible saved source.':'Downloads a regional file and extracts your area locally. Check the download size first; the initial file can be large.';
  }else label.textContent=mode==='regional'?'Compatibility source for the Smolensk area only. Use Automatic for other places.':'Downloads the selected area from public Overpass servers. If they are busy, choose Automatic or a saved file.';
 }
 describePlan(plan,request){const size=areaDimensions(request.bounds);return `${(size.width/1000).toFixed(2)} × ${(size.height/1000).toFixed(2)} km of land · ${plan.columns} × ${plan.rows} sections · ${format(plan.width)} × ${format(plan.height)} tiles (${(plan.width*request.metresPerTile/1000).toFixed(2)} × ${(plan.height*request.metresPerTile/1000).toFixed(2)} km padded) · ${format(plan.cells)} native cells.`;}
 refreshOffline(){ $('osm-world-offline').hidden=!$('osm-world-state').classList.contains('error')||this.busy||this.sourceBusy||!!this.frameError||!!this.recovery||sourceMode($('osm-world-source').value)==='file'; }
 status(message,error=false){$('osm-world-state').textContent=message;$('osm-world-state').classList.toggle('error',error);this.refreshOffline();}
 controls(){
  const running=this.busy||this.sourceBusy,preserve=$('osm-world-preserve').checked;
  $('osm-world-back-to-area').disabled=running;$('osm-world-expanded-approve').disabled=running;
  $('osm-world-result-step').hidden=!this.result&&!this.recovery&&!this.showResultStage;
  $('osm-world-result-summary').textContent=this.result?'The imported result is ready. Open it or compile it below; your current project remains unchanged.':this.recovery?'The previous attempt retained reusable source data. Retry using the build action.':'Review the import status below. Your current world is preserved.';
  for(const input of $('osm-world-dialog').querySelectorAll('.osm-world-fields input,.osm-world-fields select,#osm-world-name,#osm-world-scale,#osm-world-preserve,#osm-world-query,#osm-world-search,#osm-world-plan,#osm-world-start,#osm-world-preview,#osm-world-results button,#osm-world-offline,[data-osm-area]'))input.disabled=running||(preserve&&(frameFields.includes(input.id.replace('osm-world-',''))||['osm-world-query','osm-world-search'].includes(input.id)||input.hasAttribute('data-osm-area')))||!!this.frameError&&['osm-world-plan','osm-world-start','osm-world-preview'].includes(input.id);
  $('osm-world-preserve-row').hidden=!this.reference;
  $('osm-world-frame-state').textContent=preserve?'Original bounds, scale, rotation, section size and origin are locked to preserve edits. Turn preservation off to choose a new area.':'';
  $('osm-world-plan').hidden=!!this.result;$('osm-world-start').hidden=!!this.result;$('osm-world-apply').disabled=running;$('osm-world-cancel').hidden=!this.busy||this.applying;$('osm-world-cancel').disabled=this.cancelling||false;$('osm-world-progress').hidden=!this.busy;
  $('osm-world-start').textContent=recoveryLabels[this.recovery]||'Download and build world';
  const sections=this.result?.resultKind==='sections';$('osm-world-apply').textContent=sections?'Browse imported sections':'Open imported world';
  $('osm-world-safety').textContent=sections?'Your import is saved as a section workspace. Browsing keeps your current project, active section and unsaved edits unchanged.':this.result?'Your import is ready. Opening it replaces the current world in one undoable step. Edits made during the import are not included.':this.busy?'Your current world is unchanged. You may close this window and return while the import runs.':'Your current world stays open until you apply the result. Applying can be undone.';
  this.deformation.setLocked(running||preserve);this.source.setLocked(running);this.refreshSourceSummary();this.refreshOffline();
  let deformed=false;try{deformed=!!this.deformation.read(this.getWorld()?.geoReference,preserve);}catch{deformed=true;}
  $('osm-world-grid-detect').disabled=running||preserve||deformed||!!this.frameError;$('osm-world-grid-apply').disabled=running||preserve||deformed||!this.gridReview;
  $('osm-world-overture-file').disabled=running||preserve;$('osm-world-overture-add').disabled=running||preserve||!!this.frameError;
  this.output?.setLocked(running);
 }
 async selectedSource(signal){
  const saved=this.preparedSource;
  if(saved?.bounds===boundsKey(this.area())&&saved.mode===sourceMode($('osm-world-source').value))return {sourceMode:'file',sourceId:saved.sourceId,useRegionalExtract:false};
  return this.source.ensure(signal);
 }
 rememberSource(sourceId){
  if(!/^[a-f0-9]{32}$/.test(sourceId??''))throw Error('The prepared geographic source was not saved. Retry.');
  const prepared={sourceId,bounds:boundsKey(this.area()),mode:sourceMode($('osm-world-source').value)},changed=JSON.stringify(prepared)!==JSON.stringify(this.preparedSource);
  this.preparedSource=prepared;this.source.record(sourceId);if(changed)this.invalidate();
 }
 async addOverture(){
  if(this.busy||this.sourceBusy||$('osm-world-preserve').checked)return;
  this.busy=true;this.cancelling=false;this.abort=new AbortController();this.controls();this.status('Checking the building supplement against the selected source…');
  const revision=this.revision;
  try{
   const original=this.request(),json=await readOvertureFile($('osm-world-overture-file').files[0]),request={...original,...await this.selectedSource(this.abort.signal)};this.abort.signal.throwIfAborted();
   const result=await api('/api/world/osm/supplement/overture',{import:request,json},{signal:this.abort.signal});this.abort.signal.throwIfAborted();
   if(revision!==this.revision||JSON.stringify(original)!==JSON.stringify(this.request()))throw Error('World settings changed. Check the supplement again.');
   const text=overtureReport(result);this.rememberSource(result.source?.id);$('osm-world-overture-state').textContent=text;
   this.status('The composed geographic source is ready. Review the report, then build the world.');
  }catch(error){this.status(error.name==='AbortError'?'Building supplement cancelled.':error.message,true);}
  finally{this.busy=false;this.cancelling=false;this.controls();}
 }
 async detectStreetGrid(){
  if(this.busy||this.sourceBusy||$('osm-world-preserve').checked)return;
  this.busy=true;this.cancelling=false;this.abort=new AbortController();this.controls();this.status('Preparing the selected source to measure local street directions…');
  const revision=this.revision;
  try{
   const original=this.request(),request={...original,...await this.selectedSource(this.abort.signal)};
   const response=await api('/api/world/osm/alignment',request,{signal:this.abort.signal});this.abort.signal.throwIfAborted();
   if(revision!==this.revision||JSON.stringify(original)!==JSON.stringify(this.request()))throw Error('World settings changed. Measure the street grid again.');
   const review=reviewStreetGrid(original,response);this.rememberSource(response.sourceId);this.gridReview=review;
   $('osm-world-grid-state').textContent=streetGridReviewText(this.gridReview);$('osm-world-grid-apply').hidden=this.gridReview.alignment.rotationDegrees===null;
   this.status('Street-grid analysis is ready. Review the result under Advanced layout settings.');
  }catch(error){this.status(error.name==='AbortError'?'Street-grid analysis cancelled.':error.message,true);}
  finally{this.busy=false;this.cancelling=false;this.controls();}
 }
 applyStreetGrid(){
  if(this.busy||this.sourceBusy)return;
  try{const choice=applyStreetGridReview(this.request(),this.gridReview);$('osm-world-rotation').value=choice.rotationDegrees;this.invalidate();this.status(`World rotation set to ${choice.rotationDegrees.toFixed(2)}°. Review the updated section layout before building.`);}
  catch(error){this.status(error.message,true);}
 }
 async search(){
  if(this.busy||this.sourceBusy||$('osm-world-preserve').checked)return;
  const query=$('osm-world-query').value.trim(),revision=++this.searchRevision;if(!query)return;
  $('osm-world-search').disabled=true;$('osm-world-search-state').textContent='Looking for a place…';
  try{const items=await api('/api/search',{query});if(revision!==this.searchRevision)return;$('osm-world-results').replaceChildren();if(!Array.isArray(items))throw Error('Invalid location search response.');
   for(const item of items){const lat=Number(item.lat),lon=Number(item.lon);if(!Number.isFinite(lat)||!Number.isFinite(lon))continue;const button=document.createElement('button');button.type='button';button.textContent=item.display_name;button.onclick=()=>{if(this.busy||this.sourceBusy||$('osm-world-preserve').checked)return;$('osm-world-lat').value=lat;$('osm-world-lon').value=lon;try{this.writeBounds(boundsFromCentre(lat,lon,value('width'),value('height')));this.invalidate();$('osm-world-results').replaceChildren();$('osm-world-search-state').textContent=item.display_name;}catch(error){this.status(error.message,true);}};$('osm-world-results').append(button);}
   $('osm-world-search-state').textContent=items.length?'Choose a location; width and height below select the surrounding area.':'No places found. Try a city or address.';this.controls();
  }catch(error){if(revision===this.searchRevision)$('osm-world-search-state').textContent=error.message;}
  finally{if(revision===this.searchRevision)this.controls();}
 }
 preview(){try{const b=this.request().bounds,bbox=[b.west,b.south,b.east,b.north].join(',');$('osm-world-preview-map').src='https://www.openstreetmap.org/export/embed.html?bbox='+bbox+'&layer=mapnik';$('osm-world-preview-container').hidden=false;const link=$('osm-world-preview-link');link.href=`https://www.openstreetmap.org/?minlon=${b.west}&minlat=${b.south}&maxlon=${b.east}&maxlat=${b.north}`;link.hidden=false;}catch(error){this.status(error.message,true);}}
 async prepare(signal,sourceId=this.recovery==='retry-build'?this.recoverySourceId:null){
  const request=this.request({includeWorld:true}),revision=this.revision,key=JSON.stringify(this.request());
  Object.assign(request,sourceId?{sourceMode:'file',sourceId,useRegionalExtract:false}:await this.selectedSource(signal));signal.throwIfAborted();
  const response=await api('/api/world/osm/plan',request,{signal});signal.throwIfAborted();if(revision!==this.revision||key!==JSON.stringify(this.request()))throw Error('Settings changed while checking the plan. Check import again.');
  this.planData={revision,key,response};this.status(this.describePlan(response,request));return {request,revision,key,response};
 }
 async plan(){
  if(this.busy||this.sourceBusy)return;this.busy=true;this.cancelling=false;this.abort=new AbortController();this.controls();this.status('Checking the data source and world layout…');
  try{await this.prepare(this.abort.signal);}catch(error){this.status(error.name==='AbortError'?'Size check cancelled.':error.message,true);}finally{this.busy=false;this.controls();}
 }
 async start(){
  if(this.busy||this.sourceBusy)return;const sourceId=this.recovery==='retry-build'?this.recoverySourceId:null;this.result=null;$('osm-world-apply').hidden=true;$('osm-world-report').hidden=true;this.busy=true;this.cancelling=false;this.abort=new AbortController();this.controls();this.status('Checking the plan before starting…');
  try{
   const prepared=await this.prepare(this.abort.signal,sourceId);this.abort.signal.throwIfAborted();if(prepared.revision!==this.revision)throw Error('Settings changed. Check import again.');
   // Once submitted, retain the response ID even if cancellation races creation.
   this.creating=true;const response=await api('/api/world/osm/jobs',prepared.request);this.creating=false;
   this.job=response.id;this.jobRevision=prepared.revision;this.jobKey=prepared.key;
   if(!this.job)throw Error('The server did not return an import job ID.');
   this.recovery=null;this.recoverySourceId=null;this.controls();
   if(this.cancelling)await api('/api/world/osm/jobs/'+encodeURIComponent(this.job)+'/cancel',{});
   this.status('Import started. The current world is preserved.');this.poll();
  }catch(error){this.creating=false;if(this.job){this.busy=true;this.cancelling=false;this.controls();this.status(error.message+' The server job is still being monitored. You can retry cancellation.',true);this.poll();return;}this.busy=false;this.controls();this.status(error.name==='AbortError'?'Import cancelled; the current world is preserved.':error.message,true);}
 }
 async poll(){
  const id=this.job;if(!id)return;
  try{
   const state=await api('/api/world/osm/jobs/'+encodeURIComponent(id));if(this.job!==id)return;
   const p=state.progress||{};this.source.record(state.sourceId||p.sourceId);this.status(formatOsmProgress(p));
   if(Number.isFinite(p.totalBytes)&&p.totalBytes>0){$('osm-world-progress').max=p.totalBytes;$('osm-world-progress').value=p.bytesReceived||0;}else if(p.phase==='region-download'&&!p.totalBytes){$('osm-world-progress').removeAttribute('value');}else{$('osm-world-progress').max=Math.max(1,p.total||1);$('osm-world-progress').value=p.completed||0;}
   if(['completed','cancelled','failed'].includes(state.status)){
    this.job=null;this.busy=false;this.showResultStage=true;this.recovery=this.jobRevision===this.revision?importRecovery(state):null;this.recoverySourceId=this.recovery==='retry-build'?(state.sourceId||p.sourceId):null;this.controls();
    const warnings=$('osm-world-warnings');warnings.replaceChildren();for(const warning of state.warnings||[]){const li=document.createElement('li');li.textContent=warning;warnings.append(li);}$('osm-world-report').hidden=!warnings.childElementCount;
    if(state.status==='completed'){
     if(this.jobRevision!==this.revision||this.jobKey!==JSON.stringify(this.request())){this.status('The result uses older settings. Assemble again before applying.',true);return;}
     const resultKind=state.resultKind??'world',workspaceId=state.workspaceId,workspaceRevision=state.workspaceRevision;
     if(!['world','sections'].includes(resultKind)||(resultKind==='sections'?typeof workspaceId!=='string'||workspaceId.length!==32||!/^[a-f0-9]{32}$/i.test(workspaceId)||!Number.isSafeInteger(workspaceRevision)||workspaceRevision<1:workspaceId!=null)){this.status('The server returned invalid import result metadata. Assemble again before opening it.',true);return;}
     this.result={id,revision:this.revision,key:this.jobKey,resultKind,workspaceId,workspaceRevision};$('osm-world-apply').hidden=false;this.controls();this.status(resultKind==='sections'?'Sections are ready. Choose “Browse imported sections” to view the saved workspace. Your current project and edits are preserved.':'World is ready. Choose “Open imported world” to use it. The geographic source can be saved separately.');
    }else{
     let message=state.error||'Import cancelled; the original world is preserved.';
     if(this.recovery==='resume-download')message='The regional download stopped. '+(Number.isFinite(p.bytesReceived)&&p.bytesReceived>0?formatBytes(p.bytesReceived)+(Number.isFinite(p.totalBytes)&&p.totalBytes>p.bytesReceived?' of '+formatBytes(p.totalBytes):'')+' retained in the automatic cache.':'Any partial file remains in the automatic cache.')+' Choose “Resume regional download” to continue. The current world is unchanged.';
     else if(this.recovery==='retry-processing')message+=' The complete regional file is retained locally. Choose “Retry downloaded source” to process it again. The current world is unchanged.';
     else if(this.recovery==='retry-build')message+=' The prepared geographic source is retained. Choose “Retry world build” to reuse it. The current world is unchanged.';
     this.status(message+((state.sourceId||p.sourceId)?' The geographic source is available to download.':'')+(p.dataTimestamp?' Data date: '+sourceDate(p.dataTimestamp)+'.':'')+(state.status==='failed'&&!this.recovery?' Choose a local OSM JSON or PBF file to import offline.':''),state.status==='failed');
    }
    return;
   }
   this.timer=setTimeout(()=>this.poll(),1000);
  }catch(error){this.status(error.message+' The server job may still be running; checking again…',true);this.timer=setTimeout(()=>this.poll(),3000);}
 }
 async cancel(){
  if(!this.busy||this.applying||this.cancelling)return;this.cancelling=true;this.controls();
  if(!this.job){if(!this.creating)this.abort?.abort();this.status('Cancellation requested; the current world is preserved.');return;}
  try{await api('/api/world/osm/jobs/'+encodeURIComponent(this.job)+'/cancel',{});this.status('Cancellation requested; waiting for the server.');}catch(error){this.cancelling=false;this.controls();this.status(error.message,true);}
 }
 async apply(){
  if(!this.result||this.busy||this.sourceBusy)return;const result=this.result;
  try{if(result.revision!==this.revision||result.key!==JSON.stringify(this.request()))throw Error('Settings changed. Assemble a new result before applying.');
   this.busy=true;this.applying=true;this.controls();
   if(result.resultKind==='sections'){
    if(typeof this.acceptWorkspace!=='function')throw Error('Section workspace browsing is unavailable.');
    await this.acceptWorkspace(result.workspaceId);
   }else{
    const world=await api('/api/world/osm/jobs/'+encodeURIComponent(result.id)+'/result');
    if(result.revision!==this.revision||result.key!==JSON.stringify(this.request()))throw Error('Settings changed while opening the result. Assemble again.');
    await this.acceptWorld(world);
   }
   $('osm-world-dialog').close();this.result=null;$('osm-world-apply').hidden=true;this.initialized=false;await this.onApplied?.();
  }catch(error){this.status(error.message,true);}finally{this.busy=false;this.applying=false;this.controls();}
 }
}
