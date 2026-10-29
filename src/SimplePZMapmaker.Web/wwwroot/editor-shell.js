import {installWorkspaceFlow} from './workspace-flow.js';
const nativeAliases={select:'select',building:'building','world-asset':'object','static-model':'model','erase-decor':'erase-object',zones:'zone-area'};
const nativeTools=new Set(['select','zone-area','building','move','room','footprint','erase-floor','floor','wall','door','window','open','object','model','stairs','erase-object']);

export function nativeToolFor(tool){const value=nativeAliases[tool]??(tool.startsWith('native-')?tool.slice(7):null);return nativeTools.has(value)?value:null;}
export function shellToolForNative(tool){return Object.entries(nativeAliases).find(([,value])=>value===tool)?.[0]??'native-'+tool;}
export function overviewToolFor(tool){return tool.startsWith('native-')||['world-asset','static-model','erase-decor'].includes(tool)?'select':tool;}
export function toolContext(tool){
 const category=['brush','rectangle','surface-asset','appearance-area'].includes(tool)?'terrain':tool==='road'?'paths':tool==='building'||tool.startsWith('native-')&&tool!=='native-move'?'building':['world-asset','static-model','erase-decor','objects'].includes(tool)?'objects':['zones','zombies'].includes(tool)?'zones':'select';
 return {category,terrain:category==='terrain'||category==='paths',building:tool==='building',objects:category==='objects',zones:category==='zones',paths:category==='paths',density:tool==='zombies'};
}

/** Keep command elements and their handlers; move only their presentation. */
export function installEditorShell(){
 const $=id=>document.getElementById(id),nav=document.querySelector('.topbar nav');
 const groups=[['Project',['new-map','load-project','open-world','open-recovery-versions','open-help']],['Import',['open-import','open-building-exchange']],['Tools',['open-building-editor','open-generation-studio','open-project-doctor','open-automapping','open-appearance','open-environment-editor','preview-region']],['Settings',['open-resources','open-agent-session']]];
 const menus=[];
 for(const [name,ids] of groups){const menu=document.createElement('details'),summary=document.createElement('summary'),body=document.createElement('div');menu.className='command-menu';menu.dataset.commandMenu=name.toLowerCase();summary.textContent=name;body.className='command-menu-items';body.id='commands-'+name.toLowerCase();summary.setAttribute('aria-controls',body.id);menu.append(summary,body);for(const id of ids)if($(id))body.append($(id));menus.push(menu);menu.addEventListener('toggle',()=>{summary.setAttribute('aria-expanded',String(menu.open));if(menu.open)for(const other of menus)if(other!==menu)other.open=false;});body.addEventListener('click',event=>{if(event.target.closest('button')&&!event.target.closest('button').disabled)menu.open=false;});}
 nav.replaceChildren(...menus,$('save-project'),$('export-map'));
 document.addEventListener('click',event=>{for(const menu of menus)if(!menu.contains(event.target))menu.open=false;});
 document.addEventListener('keydown',event=>{if(event.key==='Escape'){const menu=menus.find(m=>m.open);if(menu){event.preventDefault();menu.open=false;menu.querySelector('summary').focus();}}});
 // Sections are existing controls, not parallel editors or copies of their state.
 const terrain=$('terrain-palette').closest('section'),buildings=$('building-preset').closest('section'),oldAppearance=$('surface-theme').closest('section');terrain.id='terrain-settings';buildings.id='building-settings';
 const objects=document.createElement('section');objects.id='object-settings';const title=document.createElement('h2');title.textContent='Objects and surfaces';objects.append(title);for(const id of ['pick-world-resource','pick-static-model','pick-surface-resource','erase-world-resource','world-resource-name'])if($(id))objects.append($(id));oldAppearance.before(objects);
 const paths=document.createElement('section');paths.id='path-settings';paths.append($('open-linear-editor'));objects.after(paths);
 $('surface-theme').hidden=true;oldAppearance.replaceChildren($('surface-theme'),$('appearance-current'));oldAppearance.id='appearance-current-panel';
 const density=$('zombie-density').closest('section');density.id='density-settings';const brush=document.createElement('button');brush.type='button';brush.dataset.tool='zombies';brush.textContent='Paint density brush';brush.onclick=()=>document.dispatchEvent(new CustomEvent('editor-density-brush'));density.prepend(brush);
 // One compact inspector for project metadata, rather than always-open unrelated forms.
 const inspector=document.querySelector('.inspector'),project=document.createElement('details'),projectSummary=document.createElement('summary');project.id='project-properties';projectSummary.textContent='Project properties';project.append(projectSummary);
 for(const node of [ $('inspector-title').previousElementSibling,$('inspector-title'),inspector.querySelector('.target'),inspector.querySelector('.stats'),$('world-x').closest('section'),$('geography-panel'),$('game-status').closest('.environment')])if(node)project.append(node);
 inspector.append(project);const help=$('tool-help').closest('section');help.id='tool-help-panel';
 const heading=document.createElement('h2');heading.id='inspector-context';heading.textContent='Map';inspector.prepend(heading);
 const geometryTools=[...inspector.querySelectorAll('.geographic-authoring-workbench')].find(e=>!['fence-controls','zone-tools'].includes(e.id));if(geometryTools)geometryTools.id='geometry-tools';
 for(const menu of menus)menu.querySelector('summary').setAttribute('aria-expanded','false');
 const workflow=installWorkspaceFlow({isBusy:()=>!!document.querySelector('#operation-dialog[open]')});
 return {showWorkspace:mode=>workflow.show(mode),update(tool,{nativeView=false,selection=false}={}){
  const c=toolContext(tool);document.body.dataset.editorContext=c.category;
  terrain.hidden=!c.terrain;buildings.hidden=!c.building||nativeView;objects.hidden=!c.objects||nativeView;paths.hidden=!c.paths;density.hidden=!c.zones;oldAppearance.hidden=!c.terrain;
  const native=document.querySelector('.native-edit-panel');if(native)native.hidden=!nativeView||c.zones||tool==='pan';
  $('selection-panel').hidden=nativeView||!selection;
  const zone=$('zone-tools');if(zone){zone.hidden=!c.zones;if(c.zones)zone.open=true;}
  const ensemble=$('gameplay-panel');if(ensemble)ensemble.hidden=!c.building||nativeView;
  const fence=$('fence-controls');if(fence)fence.hidden=!c.paths;
  const geometry=$('geometry-tools');if(geometry)geometry.hidden=!c.paths&&!c.zones&&!c.building;
  heading.textContent=({terrain:'Terrain',paths:'Paths',building:'Buildings',objects:'Objects',zones:'Zones',select:selection?'Selected building':'Map'})[c.category];
  document.querySelectorAll('[data-tool]').forEach(b=>{const active=b.dataset.tool===tool||b.dataset.tool==='objects'&&c.objects||b.dataset.tool==='zones'&&c.zones||b.dataset.tool==='building'&&c.category==='building';b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));});
  workflow.update({tool,nativeView,selection,category:c.category});
 }};
}
