import {kinds,minLevel,usesAssets,usesRaisedOverlays,usesLargeFootprint,usesFurniture,usesInteractiveAuthoring,usesNativeModelBehavior} from './building.js';
import {usesExtendedZ} from './z-levels.js';

export function templateProjectVersion(b){
 return usesRaisedOverlays(b)?17:usesLargeFootprint(b)?16:usesFurniture(b)?14:usesExtendedZ(b)?10:usesInteractiveAuthoring(b)?9:usesNativeModelBehavior(b)?7:b.requiredMods?.length||Object.keys(b.assetSources||{}).length||b.design?.floors.some(f=>f.staticModels?.length)?6:usesAssets(b)?4:minLevel(b)!==0?3:2;
}

const tabs=[['map','On map'],['templates','Templates'],['mine','My templates']];
const categoryNames={village:'Village',settlement:'Town',urban:'Urban apartment buildings'};
const mounted=new WeakMap();
const records=items=>Array.isArray(items)?items.filter(item=>item&&typeof item.id==='string'):[];

/** All search words may match name, kind/type, description, category or module role. */
export function filterLibraryItems(items,search=''){
 const words=String(search??'').trim().toLocaleLowerCase('en-GB').split(/\s+/).filter(Boolean);
 return records(items).filter(item=>{
  const text=[item.name,item.kind,kinds[item.kind],item.description,item.category,categoryNames[item.category],item.sourceKind].join(' ').toLocaleLowerCase('en-GB');
  return words.every(word=>text.includes(word));
 });
}

export function buildingLibraryItems(project,{tab='map',search=''}={}){
 return filterLibraryItems(tab==='map'?project?.buildings:tab==='mine'?project?.buildingLibrary:[],search);
}

/**
 * Mount once after constructing HousingGallery and binding legacy create handlers.
 * Callbacks: onEditBuilding(id, snapshot), onEditTemplate(id, snapshot),
 * onPlaceTemplate(id, snapshot), onCreateTemplate(), onImportTemplate().
 * Edit/place/create close the catalog before invoking the callback. Import leaves
 * it open and refreshes when its promise resolves; refresh again after file import
 * if the callback merely opens a file picker. Callbacks own history/persistence.
 * Missing callbacks disable their actions. An existing create onclick is retained
 * when onCreateTemplate is omitted. No project data is changed here.
 * open({tab='map',search=''}), setTab('map'|'templates'|'mine'), setSearch(string),
 * refresh(), close(), dispose(); getters: tab, search. open() resets search unless
 * explicitly supplied; tab/category changes preserve it. refresh() reads getProject.
 */
export function mountBuildingLibrary({dialog=globalThis.document?.getElementById('building-list-dialog'),getProject,housingGallery,onEditBuilding,onEditTemplate,onPlaceTemplate,onCreateTemplate,onImportTemplate}={}){
 if(!dialog||typeof getProject!=='function'||!housingGallery)throw Error('Building library requires a dialog, getProject and HousingGallery.');
 if(mounted.has(dialog))return mounted.get(dialog);
 const document=dialog.ownerDocument,$=id=>dialog.querySelector('#'+id);
 const search=$('building-list-search'),list=$('building-list'),create=$('new-building-template'),presets=dialog.querySelector('.housing-catalog');
 if(!search||!list||!create||!presets)throw Error('Building catalog markup is incomplete.');
 const originalChildren=[...dialog.children],oldInput=search.oninput,oldCreate=create.onclick;
 const oldSearch={type:search.type,placeholder:search.placeholder,label:search.getAttribute('aria-label')};
 const oldCreateDisabled=create.disabled;
 const oldPreset={id:presets.id,hidden:presets.hidden,role:presets.getAttribute('role'),label:presets.getAttribute('aria-labelledby'),tabIndex:presets.tabIndex};
 const make=(tag,className,text)=>{const node=document.createElement(tag);if(className)node.className=className;if(text!==undefined)node.textContent=text;return node;};
 const tablist=make('div','building-library-tabs');tablist.setAttribute('role','tablist');tablist.setAttribute('aria-label','Building catalog');
 const state=make('p','hint');state.id='building-library-state';state.setAttribute('role','status');state.setAttribute('aria-live','polite');
 const map=make('section','building-library-panel'),mine=make('section','building-library-panel');
 const mineList=make('div','building-library-mine-list');mineList.id='building-library-mine-list';
 const actions=make('div','building-library-actions'),importButton=make('button','','Import template JSON');importButton.id='building-library-import';importButton.type='button';
 const hint=originalChildren.find(node=>node.tagName==='P'&&node.className?.split(' ').includes('hint'));
 actions.append(create,importButton);mine.append(actions);if(hint)mine.append(hint);mine.append(mineList);map.append(list);
 const panels={map,templates:presets,mine},buttons={};
 let tab='map',disposed=false,actionBusy=false;
 for(const [key,label] of tabs){
  const button=make('button','',label);button.id='building-library-tab-'+key;button.type='button';button.setAttribute('role','tab');
  const panel=panels[key];if(!panel.id)panel.id='building-library-panel-'+key;
  button.setAttribute('aria-controls',panel.id);panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby',button.id);panel.tabIndex=0;
  button.onclick=()=>setTab(key);
  button.addEventListener('keydown',event=>{
   const index=tabs.findIndex(([value])=>value===key);
   const next=event.key==='Home'?0:event.key==='End'?tabs.length-1:event.key==='ArrowRight'?(index+1)%tabs.length:event.key==='ArrowLeft'?(index+tabs.length-1)%tabs.length:null;
   if(next===null)return;event.preventDefault();setTab(tabs[next][0]);buttons[tabs[next][0]].focus();
  });
  buttons[key]=button;tablist.append(button);
 }
 // Keep the public search/list/preset IDs, moving their existing nodes rather
 // than copying them so outside code can still query them.
 search.type='search';search.placeholder='Search buildings and templates';search.setAttribute('aria-label','Search building catalog');
 dialog.append(search,tablist,state,map,presets,mine);
 search.oninput=()=>refresh();
 if(onCreateTemplate)create.onclick=()=>runAction(onCreateTemplate,[]);
 importButton.onclick=()=>runAction(onImportTemplate,[],false);
 const stop=()=>housingGallery.close();
 dialog.addEventListener('close',stop);dialog.addEventListener('cancel',stop);
 function activate(){
  for(const [key] of tabs){const selected=key===tab;buttons[key].setAttribute('aria-selected',String(selected));buttons[key].tabIndex=selected?0:-1;panels[key].hidden=!selected;}
  create.disabled=actionBusy||!(onCreateTemplate||oldCreate);importButton.disabled=actionBusy||typeof onImportTemplate!=='function';
 }
 function setTab(value){
  if(!tabs.some(([key])=>key===value))throw RangeError('Unknown building library tab: '+value);
  if(disposed)return;tab=value;refresh();
 }
 function summary(building){
  const min=building.design?.minLevel??0,count=building.design?.floors?.length??1;
  return `${kinds[building.kind]??building.kind??'Building'} · ${Math.max(0,count+min)} floor(s) / ${Math.max(0,-min)} basement(s)`;
 }
 function actionButton(text,callback,item){
  const button=make('button','',text);button.type='button';button.disabled=actionBusy||typeof callback!=='function';
  button.onclick=()=>runAction(callback,[item.id,structuredClone(item)]);return button;
 }
 async function runAction(callback,args,dismiss=true){
  if(disposed||actionBusy||typeof callback!=='function')return;
  actionBusy=true;const origin={tab,search:search.value};refresh();if(dismiss)close();
  let failure;
  try{await callback(...args);}
  catch(error){failure=error;if(!disposed&&dismiss)open(origin);}
  finally{
   actionBusy=false;
   if(!disposed){
    if(tab==='templates')activate();else refresh();
    if(failure){state.hidden=false;state.textContent=failure?.message||'The catalog action could not be completed.';}
   }
  }
 }
 function refresh(){
  if(disposed)return;activate();state.hidden=tab==='templates';
  if(tab==='templates'){
   if(dialog.open)void housingGallery.open({search:search.value});else stop();
   return;
  }
  stop();const items=buildingLibraryItems(getProject(),{tab,search:search.value}),container=tab==='map'?list:mineList;
  container.replaceChildren();
  state.textContent=items.length?`${items.length} ${tab==='map'?'building(s) on map':'saved template(s)'}`:search.value.trim()?'No buildings or templates match your search.':tab==='map'?'There are no buildings on the map yet.':'There are no saved templates yet. Create a template or import template JSON.';
  for(const item of items){
   if(tab==='map'){container.append(actionButton(`${item.name||'Unnamed building'} · ${summary(item)}`,onEditBuilding,item));continue;}
   const card=make('article','building-library-card'),name=make('h3','',item.name||'Unnamed template'),info=make('p','hint',`${summary(item)} · ${item.width??'?'} × ${item.height??'?'} tiles`),row=make('div','building-library-actions');
   row.append(actionButton('Edit template',onEditTemplate,item),actionButton('Place on map',onPlaceTemplate,item));card.append(name,info,row);container.append(card);
  }
 }
 function open({tab:next='map',search:query=''}={}){
  if(disposed)return;search.value=String(query??'');
  if(!tabs.some(([key])=>key===next))throw RangeError('Unknown building library tab: '+next);
  tab=next;activate();if(!dialog.open)dialog.showModal();refresh();search.focus();
 }
 function close(){stop();if(dialog.open)dialog.close();}
 function dispose(){
  if(disposed)return;disposed=true;close();dialog.removeEventListener('close',stop);dialog.removeEventListener('cancel',stop);
  search.oninput=oldInput;create.onclick=oldCreate;create.disabled=oldCreateDisabled;
  search.type=oldSearch.type;search.placeholder=oldSearch.placeholder;if(oldSearch.label===null)search.removeAttribute('aria-label');else search.setAttribute('aria-label',oldSearch.label);
  for(const node of originalChildren)dialog.append(node);for(const node of [tablist,state,map,mine])node.remove();
  presets.id=oldPreset.id;presets.hidden=oldPreset.hidden;presets.tabIndex=oldPreset.tabIndex;
  for(const [name,value] of [['role',oldPreset.role],['aria-labelledby',oldPreset.label]]){if(value===null)presets.removeAttribute(name);else presets.setAttribute(name,value);}
  mounted.delete(dialog);
 }
 const library={open,setTab,setSearch(value){if(!disposed){search.value=String(value??'');refresh();}},refresh,close,dispose,get tab(){return tab;},get search(){return search.value;}};
 mounted.set(dialog,library);refresh();return library;
}
