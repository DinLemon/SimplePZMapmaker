import {validateTemplateBytes} from './z-levels.js';
import {minZ,maxZ,usesExtendedZ} from './z-levels.js';
import {NativeLightingPanel} from './native-lighting.js';
import {mountGameplayAuthoring} from './gameplay-authoring.js';
import {MainNativeEditor} from './main-native-editor.js';
import {nativeWorldOperation,applyNativeWorldOperation} from './main-native-operations.js';
import {installLinearEditing} from './linear-editing.js';
import {MapNativePreview} from './map-native-preview.js';
import {ImportQualityPanel} from './import-quality.js';
import {validatePalettes} from './palette-data.js';
import {blank,paint,line,canPlace as canPlaceLocal,History,validateProject as validateLocalProject,boundsFromSearchResult,geographicBase} from './editor.js';
import {kinds,containsPoint,cell,shapeCells,validateDesign,combineAttribution,minLevel,aboveLevels,visibleFloors,containsVisiblePoint,usesAssets,normalizeBuilding,usesInteractiveAuthoring} from './building.js';
import {BuildingWorkshop} from './workshop.js';
import {ResourcePicker,GamePreview} from './resources.js';
import {AppearancePanel,environmentPresets,environmentIndices} from './appearance.js';
import {HousingGallery} from './housing-gallery.js';
import {WorldWorkshop} from './world-workshop.js';
import {AgentSession} from './agent-session.js';
import {openStaticModelPicker} from './static-models.js';
import {StaticModelSprites} from './static-model-sprites.js';
import {EnvironmentWorkshop} from './environment-workshop.js';
import {OsmWorldImport} from './osm-world-import.js';
import {RecoveryVersions} from './recovery-versions.js';
import {TerrainRasterCache,buildingVisible} from './terrain-raster.js';
import {globalBuildingMask,overlapsGlobalBuilding,validateGlobalProtection} from './world-protection.js';

const $=id=>document.getElementById(id);
const colors=['#8b9768','#526b4b','#787975','#507b85','#c6b58d','#8e765e','#a5a395'];
const terrainNames=['grass','forest','asphalt','water','sand','dirt','sidewalk'];
const catalog=Object.fromEntries(Object.entries(kinds).map(([kind,name])=>[kind,{width:kind==='house'?12:kind==='garage'?10:20,height:kind==='house'?10:kind==='garage'?8:16,name}]));
const recoveryKey='simple-pz-mapmaker:project:v1';
const recoveryClientKey='simple-pz-mapmaker:recovery-client:v1';
let recoveryId=crypto.randomUUID().replaceAll('-','');
try{const existing=localStorage.getItem(recoveryClientKey);if(/^[a-f0-9]{32}$/.test(existing||''))recoveryId=existing;else localStorage.setItem(recoveryClientKey,recoveryId);}catch{}
let autosaveRevision=0,recoveryQueue=Promise.resolve();
let project=blank(),history=new History(),tool='brush',terrain=0,buildingKind='house';
let selectedId=null,selectedSpawn=-1,gesture=null,cursor=null,isometric=false,nativeView=false,showGrid=true;
let selectedPlaceBounds=null;
let zoom=1,pan={x:0,y:0},space=false,busy=false,controller=null,saveTimer,toastTimer,frame=0,dirty=true;
const canvas=$('map-canvas'),ctx=canvas.getContext('2d'),mini=$('minimap'),mctx=mini.getContext('2d');
const raster=document.createElement('canvas'),rctx=raster.getContext('2d');
const rasterCache=new TerrainRasterCache(raster);
let viewport={width:800,height:600};
let worldAsset=null,surfaceAsset=null,standaloneId=null,staticModel=null,agentSession=null;
const gamePreview=new GamePreview();
const modelSprites=new StaticModelSprites();
const nativePreview=new MapNativePreview({canvas,requestDraw,status:text=>{if(nativeView)$('status').textContent=text;}});
let evidenceKey='';
const qualityPanel=new ImportQualityPanel({project:()=>project,selectedBuilding:()=>project.buildings.find(b=>b.id===selectedId),editBuilding:id=>{selectedId=id;update();$('edit-building').click();},container:document.querySelector('.inspector')});
const resources=new ResourcePicker({message:toast,pickWorld:a=>{worldAsset=a;setTool('world-asset');$('world-resource-name').textContent=a.name+' · '+a.id;},pickSurface:a=>{surfaceAsset=a;setTool('surface-asset');$('world-resource-name').textContent=a.id;}});
function saveTemplate(b){project.buildingLibrary??=[];if(project.buildingLibrary.length>=64)throw Error('The library can contain up to 64 templates.');b.attribution=combineAttribution(b.attribution||[],project.attribution);history.begin(project);project.version=Math.max(project.version,usesExtendedZ(b)?10:usesInteractiveAuthoring(b)?9:b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length)?6:4);b.id=crypto.randomUUID();b.sourceId=null;b.sourceKind=null;b.modified=false;project.buildingLibrary.push(b);history.commit(project);changed();}
function saveBuildingModule(b){project.buildingLibrary??=[];if(project.buildingLibrary.length>=64)throw Error('The library can contain up to 64 templates and modules.');validateDesign(b);if(!/^module:(entrance|section|annex|stairs)$/.test(b.sourceKind||''))throw Error('Choose a valid module role.');b.attribution=combineAttribution(b.attribution||[],project.attribution);b.id=crypto.randomUUID().replaceAll('-','');b.sourceId=null;b.modified=false;history.begin(project);project.version=Math.max(project.version,usesExtendedZ(b)?10:usesInteractiveAuthoring(b)?9:b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length)?6:4);project.buildingLibrary.push(structuredClone(b));history.commit(project);changed();}
const workshop=new BuildingWorkshop({api,download,resources,preview:gamePreview,getPalettes:()=>project.resourcePalettes??[],savePalettes:palettes=>{validatePalettes(palettes);history.begin(project);project.resourcePalettes=structuredClone(palettes);project.version=Math.max(8,project.version);history.commit(project);changed();},apply:b=>{
 if(b.id===standaloneId){saveTemplate(b);standaloneId=null;toast('Your template has been saved. Select it from the library to put it on the map.');return;}
 const old=project.buildings.find(a=>a.id===b.id);if(!old)throw Error('The original building no longer exists.');
 b.modified=true;if(aboveLevels(b)!==aboveLevels(old))b.levelsOrigin='manual';validateDesign(b);if(!canPlace({...project,buildings:project.buildings.filter(a=>a!==old)},b))throw Error('The new outline intersects with water, another building, a spawn point, or a cell boundary.');
 history.begin(project);Object.assign(old,b);history.commit(project);selectedId=b.id;changed();
},template:saveTemplate,getModules:()=>project.buildingLibrary||[],saveModule:saveBuildingModule,attribution:()=>project.attribution});
function updateLibrary(){const selected=$('building-preset').value;$('building-preset').replaceChildren();for(const [value,name] of Object.entries(kinds))$('building-preset').add(new Option(name,value));for(const b of project.buildingLibrary||[])$('building-preset').add(new Option('Template: '+b.name,'template:'+b.id));$('building-preset').value=[...$('building-preset').options].some(o=>o.value===selected)?selected:buildingKind;}
function listBuildings(){const q=$('building-list-search').value.toLocaleLowerCase('en-GB');$('building-list').replaceChildren();const items=project.buildings.filter(b=>(b.name+' '+kinds[b.kind]).toLocaleLowerCase('en-GB').includes(q)).slice(0,100);for(const b of items){const button=document.createElement('button');button.textContent=`${b.name} · ${kinds[b.kind]} · ${aboveLevels(b)} floor(s) / ${-minLevel(b)} basement(s)`;button.onclick=()=>{selectedId=b.id;$('building-list-dialog').close();update();$('edit-building').click();};$('building-list').append(button);}if(!items.length){const p=document.createElement('p');p.textContent=project.buildings.length?'Specify the name.':'There are no buildings on the map yet. Create your template.';$('building-list').append(p);}}
const housingGallery=new HousingGallery({api,select:id=>{if(busy)return;$('building-list-dialog').close();task('Preparing housing template…',async signal=>{const b=await api('/api/building/presets/'+encodeURIComponent(id),{},signal);standaloneId=b.id;workshop.open(b,true);});}});
function openHousingCatalog(){if(busy)return;$('building-list-search').value='';listBuildings();$('building-list-dialog').showModal();housingGallery.open();}
$('browse-housing-presets').onclick=openHousingCatalog;
$('open-building-editor').onclick=()=>{if(busy)return;if(selectedId){$('edit-building').click();return;}openHousingCatalog();};$('building-list-search').oninput=listBuildings;
$('new-building-template').onclick=()=>{ $('building-list-dialog').close();task('Preparing a new template...',async signal=>{const b=await api('/api/building/design',{building:{id:crypto.randomUUID(),name:'Your home',kind:buildingKind,x:8,y:8,width:catalog[buildingKind].width,height:catalog[buildingKind].height},levels:+$('placement-levels').value,basements:+$('placement-basements').value,seed:project.seed},signal);standaloneId=b.id;workshop.open(b,true);});};
$('open-resources').onclick=()=>resources.open();$('pick-world-resource').onclick=()=>resources.open('world',a=>{worldAsset=a;setTool('world-asset');$('world-resource-name').textContent=a.name+' · '+a.id;});$('pick-surface-resource').onclick=()=>resources.open('surface',a=>{surfaceAsset=a;setTool('surface-asset');$('world-resource-name').textContent=a.id;});
$('erase-world-resource').onclick=()=>setTool('erase-decor');
const appearance=new AppearancePanel({project:()=>project,selectArea:()=>{if(isometric)$('view-top').click();$('map-level').value='0';setTool('appearance-area');toast('Draw a rectangle on the map. Then choose an appearance preset.');},apply:options=>task('Applying appearance to the selected area…',async signal=>{const result=await api('/api/styles/apply',{project,...options},signal);history.begin(project);project.version=result.project.version;project.surfaceTheme=result.project.surfaceTheme;project.surfaceRegions=result.project.surfaceRegions;project.buildings=result.project.buildings;history.commit(project);appearance.done();changed();toast(`Appearance applied: ${options.region?'selected area':'entire map'}. Designed buildings: ${result.changed}.`);}),preview:options=>{const r=options.region,center=r?{x:r.x+Math.floor(r.width/2),y:r.y+Math.floor(r.height/2)}:cursor||{x:Math.floor(project.width/2),y:Math.floor(project.height/2)},cellX=Math.max(0,Math.min(project.width/256-1,Math.floor(center.x/256))),cellY=Math.max(0,Math.min(project.height/256-1,Math.floor(center.y/256))),x=Math.min(208,Math.max(0,center.x-cellX*256-24)),y=Math.min(208,Math.max(0,center.y-cellY*256-24));gamePreview.open('Selected appearance preview',level=>api('/api/preview/appearance',{project,...options,cellX,cellY,x,y,level}),minZ,maxZ,'0');}});
const worldWorkshop=new WorldWorkshop({current:()=>project,adopt:p=>adopt(p,{worldManaged:true}),finish:()=>{finish();history.commit(project);},download,api,task,message:toast,recoveryId});

function getNativeContext(){return {project,world:worldWorkshop.editedId?worldWorkshop.world:null,memberId:worldWorkshop.editedId,revision:autosaveRevision+':'+worldWorkshop.revision,level:$('map-level').value==='all'?null:Number($('map-level').value),transform:{zoom,pan,viewport}};}
const nativeSessions=new WeakSet();
function installNativeHistory(){const session=worldWorkshop.session;if(nativeSessions.has(session))return;const original=session.apply;session.apply=function(operation,reverse){if(operation.kind==='native-edit')applyNativeWorldOperation(this.world,operation,reverse);else original.call(this,operation,reverse);};nativeSessions.add(session);}
function syncNativeWorld(){const member=worldWorkshop.world.maps.find(m=>m.id===worldWorkshop.editedId);if(!member){worldWorkshop.editedId=null;worldWorkshop.changed();update();return;}project=member.project;history=new History();history.extraBlockedWater=index=>worldMask().has(index);selectedId=null;selectedSpawn=-1;protectionCache=null;worldWorkshop.changed();changed();}
const nativeEditor=new MainNativeEditor({canvas,getContext:getNativeContext,api,run:task,message:toast,requestDraw,modelImages:nativePreview.models,toolChanged:()=>{if(tool==='pan')tool='select';update();},commit:result=>{if(result.world){installNativeHistory();const operation=nativeWorldOperation(worldWorkshop.world,result.world);if(!operation.maps.length&&!operation.buildings.length&&operation.beforeVersion===operation.afterVersion)return;worldWorkshop.session.history.push(operation);applyNativeWorldOperation(worldWorkshop.world,operation,false);syncNativeWorld();}else{history.begin(project);const {terrain:ignoredTerrain,zombies:ignoredZombies,...metadata}=result.project;Object.assign(project,metadata);history.commit(project);changed();}},selectionChanged:s=>{selectedId=s?.scope==='map'?s.building?.id??null:null;selectedSpawn=-1;update();}});
const nativeLighting=new NativeLightingPanel({getContext:getNativeContext,getNodes:()=>nativePreview.renderNodes,message:toast,requestDraw,openNative:()=>{if(!nativeView)$('view-native').click();if($('map-level').value==='all')nativeEditor.selectFloor(0);}});
const gameplayPanel=mountGameplayAuthoring($('gameplay-panel'),{getProject:()=>project,apply:p=>{const context=getNativeContext();return nativeEditor.execute(context.world?{world:{...context.world,version:Math.max(3,context.world.version),maps:context.world.maps.map(m=>m.id===context.memberId?{...m,project:p}:m)}}:{project:p},context);},onDrawRectangle:callback=>{if(!nativeView)$('view-native').click();nativeEditor.selectFloor(0);nativeEditor.setTool('zone-area');nativeEditor.rectangleCallback=callback;}});
const linearWorkshop=installLinearEditing({project:()=>project,apply:p=>{finish();history.checkpoint(project,p);changed();},api,message:toast,busy:task,preview:(p,feature)=>{const node=feature?.points?.[0]??p.linearFeatures?.at(-1)?.points?.[0]??{x:p.width/2,y:p.height/2},cellX=Math.floor(node.x/256),cellY=Math.floor(node.y/256),x=Math.max(0,Math.min(208,Math.floor(node.x-cellX*256)-24)),y=Math.max(0,Math.min(208,Math.floor(node.y-cellY*256)-24));gamePreview.open('Linear feature draft from game resources',level=>api('/api/preview/cell',{project:p,cellX,cellY,x,y,level}),Math.min(0,...p.linearFeatures.map(f=>f.level??0)),Math.max(0,...p.linearFeatures.map(f=>f.level??0)),String(feature?.level??0));}});
$('open-linear-editor').onclick=()=>linearWorkshop.open();

let protectionCache=null;const emptyWorldMask=new Set();
function worldMask(p=project){if(!worldWorkshop.editedId)return emptyWorldMask;const world=worldWorkshop.world,key=p.worldX+':'+p.worldY+':'+p.width+':'+p.height;if(!protectionCache||protectionCache.world!==world||protectionCache.key!==key)protectionCache={world,key,mask:globalBuildingMask(world,p)};return protectionCache.mask;}
function canPlace(p,b){return canPlaceLocal(p,b)&&!overlapsGlobalBuilding(p,b,worldMask(p));}
function validateProject(p){validateLocalProject(p);validateGlobalProtection(p,worldMask(p));}
history.extraBlockedWater=index=>worldMask().has(index);
worldWorkshop.setBuildingEditor((building,apply)=>{const oldApply=workshop.apply;workshop.apply=apply;$('building-dialog').addEventListener('close',()=>{workshop.apply=oldApply;},{once:true});workshop.open(building);});
agentSession=new AgentSession({project:()=>project,changeVersion:()=>autosaveRevision+":"+worldWorkshop.revision,world:()=>{finish();history.commit(project);worldWorkshop.flush();return worldWorkshop.world;},applyWorld:w=>worldWorkshop.acceptWorld(w),busy:()=>busy,apply:p=>{finish();for(const b of [...p.buildings,...(p.buildingLibrary||[])])normalizeBuilding(b);history.checkpoint(project,p);selectedId=null;selectedSpawn=-1;appearance.reset();changed();},message:toast});
new RecoveryVersions({id:recoveryId,message:toast,apply:p=>{finish();history.checkpoint(project,p);changed();},applyWorld:w=>worldWorkshop.acceptWorld(w)});
const modelButton=document.createElement('button');modelButton.id='pick-static-model';modelButton.textContent='Static models from the game and mods';$('pick-world-resource').after(modelButton);modelButton.onclick=()=>openStaticModelPicker({onPlace:m=>{staticModel=m;modelSprites.load(m).then(()=>requestDraw());setTool('static-model');$('world-resource-name').textContent=m.modelId+' · placement model';}});
const environmentWorkshop=new EnvironmentWorkshop({project:()=>project,busy:()=>busy,center:()=>cursor||{x:project.width/2,y:project.height/2},apply:p=>{finish();history.checkpoint(project,p);selectedId=null;selectedSpawn=-1;appearance.reset();for(const m of (p.staticModels||[]).slice(0,200))modelSprites.load(m).then(()=>requestDraw());changed();},message:toast});
const osmWorldImport=new OsmWorldImport({getWorld:()=>{finish();history.commit(project);worldWorkshop.flush();return worldWorkshop.world;},acceptWorld:w=>worldWorkshop.acceptWorld(w)});
$('open-appearance').onclick=()=>appearance.open();$('apply-visual-style').onclick=()=>appearance.open();
$('preview-region').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId),center=b?{x:b.x+Math.floor(b.width/2),y:b.y+Math.floor(b.height/2)}:cursor||{x:Math.floor(project.width/2),y:Math.floor(project.height/2)},cellX=Math.min(project.width/256-1,Math.max(0,Math.floor(center.x/256))),cellY=Math.min(project.height/256-1,Math.max(0,Math.floor(center.y/256))),x=Math.min(208,Math.max(0,Math.floor(center.x-cellX*256)-24)),y=Math.min(208,Math.max(0,Math.floor(center.y-cellY*256)-24));gamePreview.open('Map area 48 × 48',level=>worldWorkshop.editedId?api('/api/world/preview',{world:worldWorkshop.world,memberId:worldWorkshop.editedId,cellX,cellY,x,y,level}):api('/api/preview/cell',{project,cellX,cellY,x,y,level}),minZ,maxZ,$('map-level').value==='all'?'0':$('map-level').value);};

function toast(message,error=false){$('toast').textContent=message;$('toast').classList.toggle('error',error);$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,error?10000:5500);}
function status(message){$('status').textContent=message;}
async function api(path,data,signal){
 const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data),signal});
 if(!response.ok){let message=`Request error (${response.status}).`;try{message=(await response.json()).error||message;}catch{}throw Error(message);}
 return path==='/api/export'||path==='/api/world/export'?response.blob():response.json();
}
async function task(message,run){
 if(busy){toast('Wait for the current operation to complete.');return;}
 finish();document.activeElement?.blur();history.commit(project);
 busy=true;controller=new AbortController();$('busy-message').textContent=message;status(message);
 for(const element of document.querySelectorAll('.topbar,main,footer,dialog:not(#operation-dialog)'))element.inert=true;
 $('operation-dialog').showModal();
 try{await run(controller.signal);status('Ready');}catch(e){if(e.name==='AbortError'){toast('The operation was cancelled.');status('Canceled');}else{toast(e.message,true);status('Check data');}}
 finally{busy=false;controller=null;$('operation-dialog').close();for(const element of document.querySelectorAll('.topbar,main,footer,dialog'))element.inert=false;update();}
}
function download(blob,name){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),30000);}
function autosave(){
 const revision=++autosaveRevision;$('save-state').textContent='Unsaved changes';clearTimeout(saveTimer);
 saveTimer=setTimeout(()=>{recoveryQueue=recoveryQueue.catch(()=>{}).then(async()=>{
  if(revision!==autosaveRevision)return;
  try{await api('/api/recovery/'+recoveryId,project);if(revision===autosaveRevision)$('save-state').textContent='Autosaved in the project folder';try{localStorage.removeItem(recoveryKey);}catch{}}
  catch{if(revision===autosaveRevision)$('save-state').textContent='Autosave is not available - save the file';}
 });},650);
}
function update(){
 $('project-name').value=project.name;$('inspector-title').textContent=project.name;$('size-stat').textContent=`${project.width} × ${project.height}`;
 $('map-summary').textContent=`${project.width} × ${project.height} / ${project.width*project.height/65536} cell.`;
 $('building-count').textContent=project.buildings.length;$('spawn-count').textContent=project.spawns.length;$('seed-stat').textContent=project.seed;
 $('world-x').value=project.worldX;$('world-y').value=project.worldY;$('undo').disabled=!(nativeView&&worldWorkshop.editedId?worldWorkshop.session.history.past:history.past).length;$('redo').disabled=!(nativeView&&worldWorkshop.editedId?worldWorkshop.session.history.future:history.future).length;
 $('attribution').textContent=project.attribution.length?project.attribution.join(' · '):'Geodata is not used';
 $('attribution').title=project.attribution.join('\n');
 $('geography-panel').hidden=!project.metresPerTile;
 if(project.metresPerTile){$('scale-stat').textContent=project.metresPerTile.toLocaleString('en-GB',{maximumFractionDigits:2})+' m / tile';const report=project.importSummary;$('import-summary').textContent=report?`Import placed ${report.placedBuildings} of ${report.sourceBuildings} buildings. `+Object.entries(report.skipReasons).map(([k,v])=>`${k}: ${v}`).join('; ')+' '+(report.notes||[]).join(' '):'Geographical basis of the map';}
 $('zoom-value').textContent=Math.round(zoom*100)+'%';
 $('surface-theme').value=project.surfaceTheme??'legacy';$('appearance-current').textContent=`Now: ${environmentPresets.find(s=>s.id===project.surfaceTheme)?.name||'Legacy appearance'} · individual sections: ${(project.surfaceRegions||[]).length}`;
 const b=project.buildings.find(b=>b.id===selectedId);$('selection-panel').hidden=!b;
 if(b){$('building-name').value=b.name;$('building-x').value=b.x;$('building-y').value=b.y;$('building-width').value=b.width;$('building-height').value=b.height;$('building-details').textContent=`${kinds[b.kind]} · ${aboveLevels(b)} floor(s) · ${-minLevel(b)} basement(s) · ${b.levelsOrigin==='osm-levels'?'number of floors OSM':b.levelsOrigin==='osm-height'?'estimated from OSM height':b.levelsOrigin==='manual'?'set manually':'number of floors is assumed'}${b.modified?' · manual edits':''}`;}
 const nextEvidenceKey=autosaveRevision+':'+selectedId+':'+worldWorkshop.revision;if(evidenceKey!==nextEvidenceKey){evidenceKey=nextEvidenceKey;qualityPanel.update();}
 updateLibrary();
 const names={brush:`Brush · ${terrainNames[terrain]}`,rectangle:`Area · ${terrainNames[terrain]}`,road:'Road draw a line',building:`Building · ${catalog[buildingKind].name}`,spawn:'Spawn point',select:'Select objects','world-asset':'Game decor · click on the ground','surface-asset':'Game surface · click terrain','erase-decor':'Remove game decor','static-model':'Static model · press on the ground','appearance-area':'Draw a rectangle to style',pan:'Map panning',zombies:`Zombies · ${$('zombie-density').value}/255`};
 $('map-tool-label').textContent=nativeView?'Game view · '+nativeEditor.tool:isometric?'Isometric overview':names[tool];
 const help={'appearance-area':'Draw a rectangle on the map. Then choose a preset for the selected area.',brush:'Draw with the left button. The wheel changes scale. The middle button or Space moves the map.',rectangle:'Extend a rectangular area from one corner to the other.',road:'Press at the beginning of the road and release at the end. The brush size determines the width of the road.',building:'Click on the vacant ground. A red outline indicates an intersection, water, or cell boundary.',spawn:'Click on the dry land outside the building. You can select and delete a point using the Select tool and the Delete key.',select:'Click on a building to change its position and size. Delete deletes the selected object.',pan:'Drag the map with the mouse. The wheel changes scale.',zombies:'Draw the zombie density. One square of density corresponds to an 8×8 chunk.'};
 $('tool-help').textContent=help[tool];requestDraw();
}
function changed(){nativeEditor.cancel();gameplayPanel.refresh();dirty=true;worldWorkshop.currentChanged(project);agentSession?.localChanged();autosave();update();}
function adopt(p,options={}){validateProject(p);if(!options.worldManaged)worldWorkshop.beforeAdopt(p,options);p.version=Math.max(4,p.version);p.surfaceTheme??='legacy';p.surfaceRegions??=[];p.surfaceTiles??={};p.decorations??=[];p.staticModels??=[];p.requiredMods??=[];p.assetSources??={};p.resourcePalettes??=[];for(const b of [...p.buildings,...(p.buildingLibrary||[])])normalizeBuilding(b);project=p;nativeEditor.select(null);for(const m of (p.staticModels||[]).slice(0,200))modelSprites.load(m).then(()=>requestDraw());appearance.reset();history=new History();history.extraBlockedWater=index=>worldMask().has(index);selectedId=null;selectedSpawn=-1;gesture=null;dirty=true;fit();changed();}
function setTool(value){if(gesture)finish();if(nativeView&&value!=='pan'){nativeEditor.setTool(({building:'building',select:'select','static-model':'model','world-asset':'object','erase-decor':'erase-object'})[value]??'select');}tool=value;document.querySelectorAll('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));if(value==='zombies')$('show-zombies').checked=true;update();}
function fit(){
 const w=viewport.width,h=viewport.height;
 if(isometric){zoom=Math.min((w-90)/(project.width+project.height),(h-90)/((project.width+project.height)/2));pan={x:w/2,y:(h-(project.width+project.height)*zoom/2)/2};}
 else{zoom=Math.min((w-72)/project.width,(h-90)/project.height);pan={x:(w-project.width*zoom)/2,y:(h-project.height*zoom)/2};}
 zoom=Math.max(.08,zoom);update();
}
function gridPoint(event){const rect=canvas.getBoundingClientRect();return inverse(event.clientX-rect.left,event.clientY-rect.top);}
function inverse(x,y){x-=pan.x;y-=pan.y;return isometric?{x:Math.floor((x+2*y)/(2*zoom)),y:Math.floor((2*y-x)/(2*zoom))}:{x:Math.floor(x/zoom),y:Math.floor(y/zoom)};}
function screen(x,y){return isometric?{x:pan.x+(x-y)*zoom,y:pan.y+(x+y)*zoom/2}:{x:pan.x+x*zoom,y:pan.y+y*zoom};}
function within(p){return p.x>=0&&p.y>=0&&p.x<project.width&&p.y<project.height;}
function scaleAt(factor,x=viewport.width/2,y=viewport.height/2){
 const old=zoom;zoom=Math.max(.08,Math.min(nativeView?96:20,zoom*factor));pan.x=x-(x-pan.x)*zoom/old;pan.y=y-(y-pan.y)*zoom/old;update();
}
function requestDraw(){if(!frame)frame=requestAnimationFrame(()=>{frame=0;draw();});}
function rebuildRaster(){
 if(!dirty)return;const blocks=history.pending?[...history.pending.changes.values()].filter(c=>c.layer==='terrain').map(c=>c.start):null;rasterCache.update(project,blocks);dirty=false;
}
function polygon(points,fill,stroke){ctx.beginPath();for(let i=0;i<points.length;i++){const p=points[i];i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y);}ctx.closePath();if(fill){ctx.fillStyle=fill;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.stroke();}}
function draw(){
 const nativeActive=nativeView&&isometric;
 if(nativeActive){const world=worldWorkshop.editedId?{...worldWorkshop.world,maps:worldWorkshop.world.maps.map(m=>m.id===worldWorkshop.editedId?{...m,project}:m)}:null;nativePreview.update({project,world,memberId:worldWorkshop.editedId,revision:autosaveRevision+':'+worldWorkshop.revision,level:$('map-level').value==='all'?null:Number($('map-level').value),viewport,zoom,pan,isometric:true});}
 rebuildRaster();const dpr=window.devicePixelRatio||1;ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,viewport.width,viewport.height);ctx.imageSmoothingEnabled=false;
 ctx.save();if(isometric)ctx.transform(zoom,zoom/2,-zoom,zoom/2,pan.x,pan.y);else ctx.transform(zoom,0,0,zoom,pan.x,pan.y);
 ctx.shadowColor='#0007';ctx.shadowBlur=12;if(!nativeActive||canvas.dataset.nativeState==='loading')ctx.drawImage(raster,0,0);ctx.shadowBlur=0;
 if(nativeActive){ctx.restore();nativePreview.draw(ctx,{skipModel:m=>nativeLighting.modelSuppressed(m)});ctx.save();ctx.transform(zoom,zoom/2,-zoom,zoom/2,pan.x,pan.y);}
 if($('show-zombies').checked){const n=project.width/8;for(let y=0;y<project.height/8;y++)for(let x=0;x<n;x++){const z=project.zombies[y*n+x];if(z){ctx.fillStyle=`rgba(171,53,32,${z/255*.7})`;ctx.fillRect(x*8,y*8,8,8);}}}
 if(showGrid){
  ctx.lineWidth=1/zoom;ctx.strokeStyle='#d5ad6999';ctx.setLineDash([5/zoom,5/zoom]);ctx.beginPath();for(let x=0;x<=project.width;x+=256){ctx.moveTo(x,0);ctx.lineTo(x,project.height);}for(let y=0;y<=project.height;y+=256){ctx.moveTo(0,y);ctx.lineTo(project.width,y);}ctx.stroke();ctx.setLineDash([]);
  if(zoom>=5&&!isometric){ctx.strokeStyle='#25302322';ctx.lineWidth=.5/zoom;ctx.beginPath();const a=inverse(0,0),b=inverse(viewport.width,viewport.height);for(let x=Math.max(0,a.x);x<=Math.min(project.width,b.x+1);x++){ctx.moveTo(x,Math.max(0,a.y));ctx.lineTo(x,Math.min(project.height,b.y+1));}for(let y=Math.max(0,a.y);y<=Math.min(project.height,b.y+1);y++){ctx.moveTo(Math.max(0,a.x),y);ctx.lineTo(Math.min(project.width,b.x+1),y);}ctx.stroke();}
 }
 ctx.restore();
 if(!nativeActive&&$('show-buildings').checked){
  const global=worldWorkshop.editedId?(worldWorkshop.world.buildings||[]).filter(b=>b.x+b.width>=project.worldX*256&&b.y+b.height>=project.worldY*256&&b.x<project.worldX*256+project.width&&b.y<project.worldY*256+project.height).map(b=>({...b,x:b.x-project.worldX*256,y:b.y-project.worldY*256})):[];
  const buildings=isometric?[...project.buildings,...global].sort((a,b)=>(a.x+a.y)-(b.x+b.y)):[...project.buildings,...global];
  ctx.save();polygon([[0,0],[project.width,0],[project.width,project.height],[0,project.height]].map(([x,y])=>screen(x,y)));ctx.clip();
  for(const b of buildings){if(!buildingVisible(b,screen,viewport.width,viewport.height,isometric))continue;const fill=b.kind==='house'?'#d5c8a4':b.kind==='shop'?'#ba9465':'#a3aaa0';
   if(b.design){drawDesignedBuilding(b);continue;}if($('map-level').value!=='all'&&+$('map-level').value!==0)continue;
   if(isometric){const points=[[b.x,b.y],[b.x+b.width,b.y],[b.x+b.width,b.y+b.height],[b.x,b.y+b.height]].map(([x,y])=>screen(x,y)),lift=Math.max(3,zoom*7);const roof=points.map(p=>({x:p.x,y:p.y-lift}));polygon([roof[1],roof[2],points[2],points[1]],'#8b8169');polygon([roof[2],roof[3],points[3],points[2]],'#a49776');polygon(roof,fill,b.id===selectedId?'#f5ce84':'#4b4e3d');}
   else{const point=screen(b.x,b.y);ctx.fillStyle=fill;ctx.fillRect(point.x,point.y,b.width*zoom,b.height*zoom);ctx.strokeStyle=b.id===selectedId?'#fff0be':'#555644';ctx.lineWidth=b.id===selectedId?2:1;ctx.strokeRect(point.x,point.y,b.width*zoom,b.height*zoom);
    if(zoom>2&&b.kind==='house'){ctx.strokeStyle='#706f5a';ctx.beginPath();ctx.moveTo(point.x+b.width/2*zoom,point.y);ctx.lineTo(point.x+b.width/2*zoom,point.y+b.height*zoom);ctx.moveTo(point.x+b.width/2*zoom,point.y+b.height/2*zoom);ctx.lineTo(point.x+b.width*zoom,point.y+b.height/2*zoom);ctx.stroke();}
    if(zoom>1){ctx.fillStyle='#425443';ctx.fillRect(point.x+3*zoom,point.y-1,zoom*1.5,Math.max(2,zoom));}
   }
  }
  ctx.restore();
 }
 if(!nativeActive&&['all','0'].includes($('map-level').value))for(const d of project.decorations){for(const part of d.parts){const p=screen(d.x+part.dx,d.y+part.dy);ctx.fillStyle='#d5b575';ctx.strokeStyle='#28382d';ctx.lineWidth=1;ctx.fillRect(p.x,p.y,Math.max(3,zoom),Math.max(3,zoom));ctx.strokeRect(p.x,p.y,Math.max(3,zoom),Math.max(3,zoom));}}
 if(!nativeActive&&['all','0'].includes($('map-level').value))for(const m of project.staticModels||[]){const point=screen(m.x+.5,m.y+.5);modelSprites.draw(ctx,m,point.x,point.y,Math.max(12,zoom*4));}
 if($('show-spawns').checked&&['all','0'].includes($('map-level').value))for(let i=0;i<project.spawns.length;i++){const s=project.spawns[i],p=screen(s.x+.5,s.y+.5);ctx.beginPath();ctx.arc(p.x,p.y,selectedSpawn===i?8:6,0,Math.PI*2);ctx.fillStyle='#2b3528';ctx.fill();ctx.lineWidth=2;ctx.strokeStyle='#edb16b';ctx.stroke();ctx.beginPath();ctx.moveTo(p.x-3,p.y);ctx.lineTo(p.x+3,p.y);ctx.moveTo(p.x,p.y-3);ctx.lineTo(p.x,p.y+3);ctx.lineWidth=1;ctx.stroke();}
 if(nativeActive){nativeLighting.draw(ctx,{zoom,pan,viewport});nativeEditor.draw(ctx);}
 if(cursor&&within(cursor)&&!isometric&&!busy){
  const p=screen(cursor.x,cursor.y);ctx.lineWidth=1;
  if(tool==='building'){const b=ghost(cursor);const q=screen(b.x,b.y);const valid=canPlace(project,b);ctx.fillStyle=valid?'#ead8a755':'#bd654c66';ctx.strokeStyle=valid?'#e8c690':'#ef9d7b';ctx.fillRect(q.x,q.y,b.width*zoom,b.height*zoom);ctx.strokeRect(q.x,q.y,b.width*zoom,b.height*zoom);}
  else if(tool==='brush'||tool==='zombies'){ctx.beginPath();ctx.arc(p.x,p.y,+$('brush-size').value*zoom,0,Math.PI*2);ctx.strokeStyle='#f4e6be';ctx.stroke();}
  if(gesture?.start&&(tool==='rectangle'||tool==='road'||tool==='appearance-area')){const a=screen(gesture.start.x,gesture.start.y);ctx.strokeStyle='#f5cf86';ctx.lineWidth=tool==='road'?Math.max(2,(+$('brush-size').value*2-1)*zoom):1;ctx.globalAlpha=.6;if(tool==='road'){ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(p.x,p.y);ctx.stroke();}else ctx.strokeRect(Math.min(a.x,p.x),Math.min(a.y,p.y),Math.abs(a.x-p.x),Math.abs(a.y-p.y));ctx.globalAlpha=1;}
 }
 mctx.clearRect(0,0,150,150);mctx.drawImage(raster,0,0,150,150);mctx.fillStyle='#ddd1ae';for(const b of project.buildings)mctx.fillRect(b.x/project.width*150,b.y/project.height*150,Math.max(2,b.width/project.width*150),Math.max(2,b.height/project.height*150));
 mctx.strokeStyle='#f4d293';mctx.lineWidth=1;if(!isometric){const a=inverse(0,0),b=inverse(viewport.width,viewport.height);mctx.strokeRect(a.x/project.width*150,a.y/project.height*150,(b.x-a.x)/project.width*150,(b.y-a.y)/project.height*150);}
}
function drawDesignedBuilding(b){
 const floors=visibleFloors(b,$('map-level').value,isometric);
 floors.forEach(({f,z})=>{const lift=isometric?(z+1)*Math.max(2,zoom*5):0,fill={wood:'#c7b58e',brick:'#b69179',plaster:'#bdc3ae'}[b.design.material];
  for(let y=0;y<b.height;y++)for(let x=0;x<b.width;){if(cell(b,f,x,y)==='.'){x++;continue;}const start=x;while(x<b.width&&cell(b,f,x,y)!=='.')x++;const w=x-start;
   if(isometric){const pts=[[b.x+start,b.y+y],[b.x+x,b.y+y],[b.x+x,b.y+y+1],[b.x+start,b.y+y+1]].map(([xx,yy])=>screen(xx,yy)),roof=pts.map(p=>({x:p.x,y:p.y-lift}));
    if(cell(b,f,x,y)==='.')polygon([roof[1],roof[2],pts[2],pts[1]],'#756c59');if(Array.from({length:w},(_,i)=>cell(b,f,start+i,y+1)).some(c=>c==='.'))polygon([roof[2],roof[3],pts[3],pts[2]],'#8c7e64');polygon(roof,fill,b.id===selectedId?'#f7dca1':null);
   }else{const p=screen(b.x+start,b.y+y);ctx.fillStyle=fill;ctx.fillRect(p.x,p.y,w*zoom,zoom);if(b.id===selectedId){ctx.strokeStyle='#f7dca1';ctx.lineWidth=1;ctx.strokeRect(p.x,p.y,w*zoom,zoom);}}
  }
  if(!isometric&&zoom>2){for(const e of f.edges){const p=screen(b.x+e.x,b.y+e.y);ctx.strokeStyle={wall:'#4e4a3d',door:'#e9b477',window:'#a1cbdb',open:'#87997d'}[e.kind];ctx.lineWidth=e.kind==='wall'?Math.max(1,zoom*.18):Math.max(1,zoom*.25);ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x+(e.edge==='n'?zoom:0),p.y+(e.edge==='w'?zoom:0));ctx.stroke();}}
 });
}
function ghost(point){const template=project.buildingLibrary?.find(b=>'template:'+b.id===$('building-preset').value);const c=template?structuredClone(template):{...catalog[buildingKind],kind:buildingKind};const b={...c,id:'preview',x:Math.floor(point.x-c.width/2),y:Math.floor(point.y-c.height/2)};if(!template){b.design={material:'wood',roof:'flat',shape:$('placement-shape').value,variant:project.seed,floors:[{cells:shapeCells(b.width,b.height,$('placement-shape').value),rooms:{'1':'livingroom'},edges:[],objects:[]}]};}return b;}
function paintAt(point,last){
 const size=+$('brush-size').value;
 if(tool==='zombies'){
  const radius=Math.max(0,Math.floor(size/8));const cx=Math.floor(point.x/8),cy=Math.floor(point.y/8),n=project.width/8;
  for(let y=Math.max(0,cy-radius);y<=Math.min(project.height/8-1,cy+radius);y++)for(let x=Math.max(0,cx-radius);x<=Math.min(n-1,cx+radius);x++){const i=y*n+x;history.touch('zombies',i,project.zombies[i]);project.zombies[i]=+$('zombie-density').value;}
 }else if(last)line(project,last.x,last.y,point.x,point.y,terrain,size,history);else paint(project,point.x,point.y,terrain,size,history);
 dirty=true;requestDraw();
}
canvas.addEventListener('contextmenu',e=>e.preventDefault());
canvas.addEventListener('pointerdown',e=>{
 if(busy)return;canvas.focus();const p=gridPoint(e);cursor=p;
 if(e.button===1||e.button===2||space||tool==='pan'){gesture={pan:true,last:{x:e.clientX,y:e.clientY}};canvas.setPointerCapture(e.pointerId);return;}
 if(nativeView&&nativeEditor.begin(e))return;
 if(isometric){toast('Switch to top view to draw.');return;}if(!within(p))return;
 if(tool==='appearance-area'){gesture={appearance:true,start:p,last:p};canvas.setPointerCapture(e.pointerId);requestDraw();return;}
 history.begin(project);
 if(tool==='static-model'){try{if(!staticModel)throw Error('Select model.');if(!['all','0'].includes($('map-level').value))throw Error('Environment models are placed on terrain; edit floors and basements in the building workshop.');const m={...structuredClone(staticModel),id:crypto.randomUUID().replaceAll('-',''),x:p.x,y:p.y,z:0};const candidate={...project,version:Math.max(6,project.version),staticModels:[...(project.staticModels||[]),m]};validateProject(candidate);project.version=candidate.version;project.staticModels=candidate.staticModels;history.commit(project);changed();}catch(error){history.commit(project);toast(error.message,true);}return;}
 if(tool==='building'){const draft=ghost(p);history.commit(project);task('Creating a building...',async signal=>{const isTemplate=$('building-preset').value.startsWith('template:');const b=isTemplate?draft:await api('/api/building/design',{building:draft,levels:+$('placement-levels').value,basements:+$('placement-basements').value,seed:(project.seed+project.buildings.length)|0,shape:$('placement-shape').value},signal);validateDesign(b);if(!canPlace(project,b))throw Error('You cannot place a building here: water, intersection or cell border.');history.begin(project);b.id=crypto.randomUUID();b.modified=true;b.sourceId=null;b.levelsOrigin='manual';project.buildings.push(b);selectedId=b.id;selectedSpawn=-1;history.commit(project);changed();});return;}
 if(tool==='world-asset'||tool==='surface-asset'||tool==='erase-decor'){try{if(!['all','0'].includes($('map-level').value))throw Error('Decor and terrain surfaces are placed at the ground level.');if(tool==='world-asset'){if(!worldAsset)throw Error('Select a game item.');const d={id:crypto.randomUUID(),x:p.x,y:p.y,asset:worldAsset.id,parts:structuredClone(worldAsset.parts)};validateProject({...project,decorations:[...project.decorations,d]});project.decorations.push(d);}else if(tool==='erase-decor'){project.staticModels=(project.staticModels||[]).filter(m=>m.x!==p.x||m.y!==p.y);project.decorations=project.decorations.filter(d=>!d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y));}else{if(!surfaceAsset)throw Error('Select a game surface resource.');if(project.buildings.some(b=>containsPoint(b,p.x-b.x,p.y-b.y)))throw Error('The floor of the building is edited in the workshop.');const newTerrain=surfaceAsset.water?3:surfaceAsset.id.startsWith('blends_street_')?2:surfaceAsset.id.startsWith('blends_natural_')?0:6,index=p.y*project.width+p.x;if(newTerrain===3&&((project.staticModels||[]).some(m=>m.x===p.x&&m.y===p.y)||project.spawns.some(s=>s.x===p.x&&s.y===p.y)||project.decorations.some(d=>d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y))))throw Error('The water will intersect with the object or spawn point.');history.touch('terrain',index);project.terrain[index]=newTerrain;project.surfaceTiles[index]=surfaceAsset.id;}history.commit(project);changed();}catch(e){history.commit(project);toast(e.message,true);}return;}
 if(tool==='spawn'){if((project.staticModels||[]).some(m=>m.x===p.x&&m.y===p.y)||project.terrain[p.y*project.width+p.x]===3||project.buildings.some(b=>containsPoint(b,p.x-b.x,p.y-b.y))||project.decorations.some(d=>d.parts.some(part=>d.x+part.dx===p.x&&d.y+part.dy===p.y))){toast('Place a dot on dry land outside the building and decor.',true);}else if(project.spawns.length>=100){toast('Maximum 100 spawn points.',true);}else if(!project.spawns.some(s=>s.x===p.x&&s.y===p.y)){project.spawns.push(p);}history.commit(project);changed();return;}
 if(tool==='select'){selectedId=project.buildings.find(b=>containsVisiblePoint(b,p.x-b.x,p.y-b.y,$('map-level').value))?.id||null;selectedSpawn=selectedId||!['all','0'].includes($('map-level').value)?-1:project.spawns.findIndex(s=>Math.abs(s.x-p.x)<4&&Math.abs(s.y-p.y)<4);history.commit(project);update();return;}
 gesture={start:p,last:p};canvas.setPointerCapture(e.pointerId);if(tool==='brush'||tool==='zombies')paintAt(p);
});
canvas.addEventListener('pointermove',e=>{
 const p=gridPoint(e);cursor=p;$('coordinates').textContent=`X ${p.x} · Y ${p.y}`;
 if(gesture?.pan){pan.x+=e.clientX-gesture.last.x;pan.y+=e.clientY-gesture.last.y;gesture.last={x:e.clientX,y:e.clientY};requestDraw();return;}
 if(nativeView){const nativePoint=nativeEditor.point(e);cursor=nativePoint;$('coordinates').textContent=`X ${nativePoint.x} · Y ${nativePoint.y} · Z ${$('map-level').value}`;nativeEditor.move(e);return;}
 if(gesture&&within(p)&&(tool==='brush'||tool==='zombies')){paintAt(p,gesture.last);gesture.last=p;}requestDraw();
});
function finish(){
 if(nativeEditor.drag)nativeEditor.cancel();
 if(!gesture)return;
 if(gesture.appearance){gesture=null;requestDraw();return;}
 if(!gesture.pan&&cursor){const end={x:Math.max(0,Math.min(project.width-1,cursor.x)),y:Math.max(0,Math.min(project.height-1,cursor.y))};
  if(tool==='road')line(project,gesture.start.x,gesture.start.y,end.x,end.y,2,+$('brush-size').value,history);
  if(tool==='rectangle')for(let y=Math.min(gesture.start.y,end.y);y<=Math.max(gesture.start.y,end.y);y++)for(let x=Math.min(gesture.start.x,end.x);x<=Math.max(gesture.start.x,end.x);x++)paint(project,x,y,terrain,1,history);
  history.commit(project);changed();
 }
 gesture=null;requestDraw();
}
canvas.addEventListener('pointerup',e=>{if(nativeView&&nativeEditor.drag){nativeEditor.end();return;}if(gesture?.appearance){const start=gesture.start,point=gridPoint(e),end={x:Math.max(0,Math.min(project.width-1,point.x)),y:Math.max(0,Math.min(project.height-1,point.y))};gesture=null;appearance.setRegion({x:Math.min(start.x,end.x),y:Math.min(start.y,end.y),width:Math.abs(end.x-start.x)+1,height:Math.abs(end.y-start.y)+1});setTool('select');appearance.open('area');}else finish();});canvas.addEventListener('pointercancel',()=>{nativeEditor.cancel();finish();});canvas.addEventListener('lostpointercapture',()=>{nativeEditor.cancel();finish();});canvas.addEventListener('pointerleave',()=>{if(!gesture){cursor=null;requestDraw();}});
canvas.addEventListener('wheel',e=>{e.preventDefault();const r=canvas.getBoundingClientRect();scaleAt(e.deltaY<0?1.15:1/1.15,e.clientX-r.left,e.clientY-r.top);},{passive:false});
new ResizeObserver(entries=>{const r=entries[0].contentRect;viewport={width:r.width,height:r.height};const dpr=window.devicePixelRatio||1;canvas.width=Math.round(r.width*dpr);canvas.height=Math.round(r.height*dpr);fit();}).observe($('canvas-wrap'));
mini.addEventListener('pointerdown',e=>{if(isometric)return;const r=mini.getBoundingClientRect();pan.x=viewport.width/2-(e.clientX-r.left)/r.width*project.width*zoom;pan.y=viewport.height/2-(e.clientY-r.top)/r.height*project.height*zoom;requestDraw();});
document.querySelectorAll('[data-tool]').forEach(b=>b.onclick=()=>setTool(b.dataset.tool));
document.querySelectorAll('[data-terrain]').forEach(b=>b.onclick=()=>{terrain=+b.dataset.terrain;document.querySelectorAll('[data-terrain]').forEach(a=>a.classList.toggle('active',a===b));if(!['brush','rectangle'].includes(tool))setTool('brush');update();});
document.querySelectorAll('[data-building]').forEach(b=>b.onclick=()=>{buildingKind=b.dataset.building;$('building-preset').value=buildingKind;document.querySelectorAll('[data-building]').forEach(a=>a.classList.toggle('active',a===b));setTool('building');});
$('building-preset').onchange=()=>{if(kinds[$('building-preset').value])buildingKind=$('building-preset').value;setTool('building');};
$('import-building-template').onclick=()=>$('building-template-file').click();
$('building-template-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Reading the building template...',async signal=>{validateTemplateBytes(file.size);const value=JSON.parse(await file.text());if(value.format!=='SimplePZBuilding'||![1,2,3,4,5,6,7].includes(value.version))throw Error('This is not a SimplePZMapmaker template.');const b=value.building;if(value.version<7&&usesExtendedZ(b))throw Error('Extended Z levels and No automatic roof require template version 7.');if(value.version<6&&usesInteractiveAuthoring(b))throw Error('Interactive authoring requires template version 6.');if(value.version<4&&(b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length)))throw Error('Mods and models require template version 4.');if(value.version<3&&usesAssets(b))throw Error('Appearance requires template format version 3.');if(value.version===1&&minLevel(b)!==0)throw Error('Basements require template format version 2.');b.attribution=combineAttribution(value.attribution??[],b.attribution??[]);validateDesign(b);await api('/api/building/validate',b,signal);project.buildingLibrary??=[];if(project.buildingLibrary.length>=64)throw Error('The library is full.');const source=combineAttribution(project.attribution,b.attribution);if(source.length>30)throw Error('Too many geography sources.');history.begin(project);b.id=crypto.randomUUID();b.sourceId=null;b.sourceKind=null;b.modified=false;project.attribution=source;project.version=Math.max(project.version,value.version===7?10:value.version===6?9:value.version===5?7:value.version===4?6:4);project.buildingLibrary.push(b);history.commit(project);changed();toast('The template has been added to the library.');}).finally(()=>e.target.value='');};
$('edit-building').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId);if(!b)return;if(b.design)workshop.open(b);else task('We are preparing a building plan...',async signal=>workshop.open(await api('/api/building/design',{building:structuredClone(b),levels:1,seed:project.seed,shape:'rect'},signal)));};
$('regenerate-building').onclick=()=>regenerate(selectedId);$('regenerate-buildings').onclick=()=>regenerate(null);
function regenerate(id){if(id===null&&!project.buildings.length)return;task('Creating new building options...',async signal=>{const result=await api('/api/buildings/regenerate',{project,id},signal);validateProject(result.project);history.begin(project);project.buildings=result.project.buildings;history.commit(project);changed();toast(`Updated: ${result.changed}; saved unchanged: ${result.skipped}.`);});}
for(const [id,out,suffix] of [['brush-size','brush-value',''],['zombie-density','zombie-value',''],['new-forest','new-forest-value','%'],['new-buildings','new-buildings-value','%']])$(id).oninput=()=>{$(out).textContent=$(id).value+suffix;update();};
for(const id of ['show-buildings','show-spawns','show-zombies'])$(id).onchange=requestDraw;
$('map-level').onchange=()=>{nativeEditor.cancel();nativeEditor.sync();requestDraw();};
$('toggle-grid').onclick=()=>{showGrid=!showGrid;$('toggle-grid').classList.toggle('active',showGrid);requestDraw();};
$('view-top').onclick=()=>{nativeLighting.suspend();nativeView=false;nativeEditor.setActive(false);nativePreview.close();$('view-native').classList.remove('active');isometric=false;$('view-top').classList.add('active');$('view-iso').classList.remove('active');fit();};
$('view-iso').onclick=()=>{nativeLighting.suspend();nativeView=false;nativeEditor.setActive(false);nativePreview.close();$('view-native').classList.remove('active');isometric=true;$('view-iso').classList.add('active');$('view-top').classList.remove('active');fit();};
$('fit-map').onclick=fit;$('zoom-in').onclick=()=>scaleAt(1.25);$('zoom-out').onclick=()=>scaleAt(.8);
$('undo').onclick=()=>{finish();if(nativeView&&worldWorkshop.editedId){installNativeHistory();worldWorkshop.flush();if(worldWorkshop.session.undo())syncNativeWorld();nativeEditor.select(null);return;}if(history.undo(project)){selectedId=null;selectedSpawn=-1;nativeEditor.select(null);changed();}};$('redo').onclick=()=>{finish();if(nativeView&&worldWorkshop.editedId){installNativeHistory();worldWorkshop.flush();if(worldWorkshop.session.redo())syncNativeWorld();nativeEditor.select(null);return;}if(history.redo(project)){selectedId=null;selectedSpawn=-1;nativeEditor.select(null);changed();}};
$('project-name').onfocus=()=>history.begin(project);$('project-name').onchange=()=>{const value=$('project-name').value.trim();if(!value||/[\x00-\x1f\x7f]/.test(value)){toast('Enter the name of the map.',true);update();return;}project.name=value;history.commit(project);changed();};
$('project-name').onblur=()=>history.commit(project);
for(const [id,key] of [['world-x','worldX'],['world-y','worldY']])$(id).onchange=()=>{const value=+$(id).value;if(!Number.isInteger(value)||value<0||value>1000){toast('Cell coordinate: integer from 0 to 1000.',true);update();return;}const issue=worldWorkshop.positionIssue(key==='worldX'?value:project.worldX,key==='worldY'?value:project.worldY);if(issue){toast(issue,true);update();return;}history.begin(project);project[key]=value;history.commit(project);changed();};
$('apply-building').onclick=()=>{const b=project.buildings.find(b=>b.id===selectedId);if(!b)return;const updated={...b,name:$('building-name').value,x:+$('building-x').value,y:+$('building-y').value,width:+$('building-width').value,height:+$('building-height').value};
 if(b.design&&(updated.width!==b.width||updated.height!==b.height)){toast('The size of the floors varies in the building’s workshop.',true);return;}
 if(!canPlace({...project,buildings:project.buildings.filter(a=>a!==b)},updated)){toast('A building intersects with objects, water, or a cell boundary.',true);return;}history.begin(project);Object.assign(b,updated,{modified:true});history.commit(project);changed();};
function deleteSelection(){if(busy)return;history.begin(project);if(selectedId){project.buildings=project.buildings.filter(b=>b.id!==selectedId);selectedId=null;}else if(selectedSpawn>=0){if(project.spawns.length===1){toast('At least one spawn point is required.',true);history.commit(project);return;}project.spawns.splice(selectedSpawn,1);selectedSpawn=-1;}history.commit(project);changed();}
$('delete-building').onclick=deleteSelection;
document.querySelectorAll('[data-close]').forEach(b=>{if(b.dataset.close)b.onclick=()=>$(b.dataset.close).close();});
$('new-map').onclick=()=>{if(busy)return;$('new-dialog').showModal();};
$('new-form').onsubmit=e=>{e.preventDefault();task('Creating a map...',async signal=>{const p=await api('/api/generate',{size:+$('new-size').value,seed:+$('new-seed').value,kind:$('new-kind').value,name:$('new-name').value,surfaceTheme:$('new-surface-theme').value,forestDensity:+$('new-forest').value,buildingDensity:+$('new-buildings').value,zombieDensity:+$('zombie-density').value},signal);adopt(p,{detach:true});$('new-dialog').close();toast('The map has been created. You can draw.');});};
$('save-project').onclick=()=>task('We check the resources and save the project...',async signal=>{const validated=await api('/api/validate',project,signal);Object.assign(project,validated.project);worldWorkshop.currentChanged(project);download(new Blob([JSON.stringify(project)],{type:'application/json'}),project.name.replace(/[<>:"/\\|?*]/g,'_')+'.pzmap.json');$('save-state').textContent='Project saved to file';toast('The project has been saved with resource sources and dependencies.');});
$('load-project').onclick=()=>{if(!busy)$('project-file').click();};
$('project-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Opening the project...',async signal=>{if(file.size>48_000_000)throw Error('The project is larger than 48 MB.');const text=await file.text();signal.throwIfAborted();adopt(JSON.parse(text),{detach:true});toast('The project is open.');}).finally(()=>e.target.value='');};
$('open-help').onclick=()=>$('help-dialog').showModal();
$('open-import').onclick=()=>{if(!busy){describeImportArea();$('import-dialog').showModal();}};
function bounds(){if($('use-place-bounds').checked&&selectedPlaceBounds)return {...selectedPlaceBounds};const lat=+$('import-lat').value,lon=+$('import-lon').value,extent=+$('import-extent').value;const dy=extent/111320/2,dx=dy/Math.cos(lat*Math.PI/180);if(!Number.isFinite(dx)||Math.abs(lat)>84||Math.abs(lon)>179)throw Error('Check the coordinates of the selected area.');return {south:lat-dy,north:lat+dy,west:lon-dx,east:lon+dx};}
function updateImportSource(){let available=false;try{const b=bounds();available=b.south>=54.6&&b.north<=54.95&&b.west>=31.8&&b.east<=32.3;}catch{}const option=$('import-source').querySelector('[value=smolensk-extract]');option.disabled=!available;if(!available&&$('import-source').value==='smolensk-extract')$('import-source').value='overpass';$('import-source-state').textContent=$('import-source').value==='smolensk-extract'?'Daily OSM France extract: approximately 44 MB on the first download. The file is cached beside the application and reused for later imports. Data reflects the extract date.':'Online OSM request with automatic server fallback on HTTP 504 or connection failures. For Smolensk, select the regional extract.';}
$('import-source').onchange=updateImportSource;
function describeImportArea(){updateImportSource();try{const b=bounds(),h=(b.north-b.south)*111320,w=(b.east-b.west)*111320*Math.cos((b.north+b.south)/2*Math.PI/180);$('import-area-state').textContent=`The import covers approximately ${Math.round(w)} × ${Math.round(h)} m. ${$('use-place-bounds').checked?'Boundaries of the found location.':'Around the specified center; It’s not the whole big city.'}`;}catch(e){$('import-area-state').textContent=e.message;}}
function preview(){describeImportArea();try{const b=bounds();$('osm-preview').src=`https://www.openstreetmap.org/export/embed.html?bbox=${b.west},${b.south},${b.east},${b.north}&layer=mapnik`;$('osm-preview').parentElement.classList.add('loaded');}catch(e){toast(e.message,true);}}
$('refresh-preview').onclick=preview;for(const id of ['import-lat','import-lon','import-extent'])$(id).onchange=()=>{$('osm-preview').parentElement.classList.remove('loaded');$('osm-preview').removeAttribute('src');describeImportArea();};
$('use-place-bounds').onchange=()=>{for(const id of ['import-lat','import-lon','import-extent'])$(id).disabled=$('use-place-bounds').checked;preview();};
$('search-form').onsubmit=e=>{e.preventDefault();task('Looking for a place in OpenStreetMap...',async signal=>{const items=await api('/api/search',{query:$('place-query').value},signal);$('place-results').replaceChildren();if(!items.length)toast('Location not found. Specify the city or address.');for(const item of items){const b=document.createElement('button');b.type='button';b.textContent=item.display_name;b.onclick=()=>{
  selectedPlaceBounds=boundsFromSearchResult(item);$('use-place-bounds').disabled=!selectedPlaceBounds;$('use-place-bounds').checked=!!selectedPlaceBounds;
  $('import-lat').value=item.lat;$('import-lon').value=item.lon;updateImportSource();if(!$('import-source').querySelector('[value=smolensk-extract]').disabled)$('import-source').value='smolensk-extract';
  if(selectedPlaceBounds){const metres=(selectedPlaceBounds.north-selectedPlaceBounds.south)*111320;$('import-size').value=metres>2000?'2048':metres>1000?'1024':'512';}
  else{if(+$('import-extent').value>1500)$('import-extent').value='1500';toast('This city exceeds a single map. An area around its center is selected; adjust its width below.');}
  for(const id of ['import-lat','import-lon','import-extent'])$(id).disabled=!!selectedPlaceBounds;
  preview();$('place-results').replaceChildren();
 };$('place-results').append(b);}});};
$('import-osm-button').onclick=()=>task($('import-source').value==='smolensk-extract'?'Downloading the Smolensk extract and reading the selected area…':'Loading OSM data. An overloaded server is automatically replaced by a backup…',async signal=>{try{$('import-state').classList.remove('error');$('import-state').textContent=$('import-source').value==='smolensk-extract'?'Downloading the regional extract and reading the selected area. The first download may take several minutes.':'Loading the selected area. If there is a failure, we automatically try backup servers.';const keep=$('preserve-building-edits').checked&&Boolean(project.terrainEdits?.length||project.staticModels?.length||project.bounds||history.past.length||project.buildings.some(b=>b.modified)||project.decorations.length||Object.keys(project.surfaceTiles).length||(project.surfaceRegions||[]).length);if(keep&&+$('import-size').value!==project.width)throw Error('To save manual changes, leave the same map size.');const result=await api('/api/import/osm',{project:geographicBase(project,+$('import-size').value,keep),bounds:bounds(),preserveEdits:keep,useRegionalExtract:$('import-source').value==='smolensk-extract'},signal);result.project.name=$('place-query').value.trim().slice(0,80)||'Map from OSM';result.project.worldX=project.worldX;result.project.worldY=project.worldY;adopt(result.project);$('import-state').textContent=`Loaded objects: ${result.importedFeatures}. ${result.warnings.join(' ')}`;$('import-dialog').close();toast($('import-state').textContent);}catch(e){$('import-state').classList.add('error');$('import-state').textContent=e.name==='AbortError'?'Import cancelled. The current map is saved.':e.message;throw e;}});
$('import-file-button').onclick=()=>{if(!busy)$('geodata-file').click();};
$('geodata-file').onchange=e=>{const file=e.target.files[0];if(!file)return;task('Reading geodata...',async signal=>{if(file.size>16_000_000)throw Error('The file is larger than 16 MB.');const keep=$('preserve-building-edits').checked&&Boolean(project.terrainEdits?.length||project.staticModels?.length||project.bounds||history.past.length||project.buildings.some(b=>b.modified)||project.decorations.length||Object.keys(project.surfaceTiles).length||(project.surfaceRegions||[]).length);if(keep&&+$('import-size').value!==project.width)throw Error('To save manual buildings, leave the same map size.');const result=await api('/api/import/geojson',{project:geographicBase(project,+$('import-size').value,keep),json:await file.text(),bounds:keep?project.bounds:null,preserveEdits:keep},signal);result.project.name=file.name.replace(/\.(geo)?json$/i,'').slice(0,80)||'Import';result.project.worldX=project.worldX;result.project.worldY=project.worldY;adopt(result.project);$('import-dialog').close();toast(`Imported objects: ${result.importedFeatures}. ${result.warnings.join(' ')}`);}).finally(()=>e.target.value='');};
$('export-map').onclick=()=>{try{validateProject(project);$('export-summary').textContent=`${project.width/256} × ${project.height/256} cells; ${project.buildings.length} buildings; start: ${project.worldX}, ${project.worldY}.`;$('export-dialog').showModal();}catch(e){toast(e.message,true);}};
$('download-mod').onclick=()=>task('Collecting a map for Build 42…',async signal=>{download(await api('/api/export',project,signal),'SimplePZMap.zip');$('export-dialog').close();toast('ZIP assembled. Instructions and original design are included inside.');});
$('cancel-operation').onclick=()=>controller?.abort();
$('operation-dialog').addEventListener('cancel',e=>{e.preventDefault();controller?.abort();});
document.addEventListener('keydown',e=>{if(['INPUT','SELECT','TEXTAREA'].includes(e.target.tagName)||document.querySelector('dialog[open]'))return;
 if(e.code==='Space'){space=true;e.preventDefault();return;}if(busy)return;
 if(e.ctrlKey||e.metaKey){if(e.key.toLowerCase()==='z'){e.preventDefault();$(e.shiftKey?'redo':'undo').click();}else if(e.key.toLowerCase()==='s'){e.preventDefault();$('save-project').click();}else if(e.key.toLowerCase()==='o'){e.preventDefault();$('load-project').click();}return;}
 const keys={b:'brush',a:'rectangle',r:'road',h:'building',p:'spawn',v:'select',z:'zombies'};if(keys[e.key.toLowerCase()])setTool(keys[e.key.toLowerCase()]);if(e.key==='Escape')nativeEditor.cancel();if(e.key==='Delete'){if(nativeView)nativeEditor.deleteSelection();else deleteSelection();}
});document.addEventListener('keyup',e=>{if(e.code==='Space')space=false;});window.addEventListener('blur',()=>{space=false;finish();});
async function start(){
 let recovered=false;try{const response=await fetch('/api/recovery/'+recoveryId);if(response.ok){adopt(await response.json());recovered=true;status('Autosave restored');}else{const text=localStorage.getItem(recoveryKey);if(text){adopt(JSON.parse(text));recovered=true;status('Moving autosave to the project folder');}}}catch{toast('Autosave is not available. Open the previously saved file.',true);}
 if(!recovered)await task('Preparing the first map…',async signal=>adopt(await api('/api/generate',{size:256,seed:42,kind:'town',name:'Quiet city'},signal)));
 try{const environment=await fetch('/api/environment').then(r=>r.json());$('game-status').textContent=environment.gameFound?'Project Zomboid found':'Game not found: export available';}catch{$('game-status').textContent='Could not verify the game';}
 await worldWorkshop.restore();
 update();
}
start();

$('view-native').onclick=()=>{nativeView=true;isometric=true;nativeEditor.setActive(true);$('view-native').classList.add('active');$('view-top').classList.remove('active');$('view-iso').classList.remove('active');fit();};
