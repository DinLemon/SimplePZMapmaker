import {mountPlaceMap} from './place-map.js';

// Reparent the existing controls: the import source and editor remain their sole owners.
export function installRealPlaces({workbench,getBounds,selectBounds,selectSearchResult,search,isBusy=()=>false,onOpen=()=>{}}){
 const dialog=workbench.dialog,old=document.getElementById('import-dialog'),$=id=>document.getElementById(id);
 dialog.classList.add('real-places-dialog');dialog.setAttribute('aria-labelledby','rp-title');
 const heading=dialog.querySelector('.dialog-heading');heading.querySelector('.eyebrow')?.remove();
 const title=heading.querySelector('h2');title.id='rp-title';title.textContent='Real places';
 const sites=document.createElement('section');sites.id='rp-sites';sites.setAttribute('role','tabpanel');sites.setAttribute('aria-labelledby','rp-sites-tab');
 for(const node of [...dialog.childNodes])if(node!==heading)sites.append(node);
 const place=document.createElement('section');place.id='rp-place';place.setAttribute('role','tabpanel');place.setAttribute('aria-labelledby','rp-place-tab');
 const tabs=document.createElement('div');tabs.className='rp-tabs';tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','Real places tasks');
 for(const [id,text] of [['place','Choose a place'],['sites','Imported objects & sites']]){
  const button=document.createElement('button');button.type='button';button.id=`rp-${id}-tab`;button.setAttribute('role','tab');button.setAttribute('aria-controls',`rp-${id}`);button.textContent=text;button.onclick=()=>activate(id);tabs.append(button);
 }
 dialog.append(tabs,place,sites);
 const searchForm=$('search-form'),results=$('place-results');
 searchForm.querySelector('input').setAttribute('aria-label','Find a city or address');
 const searchState=document.createElement('p');searchState.id='rp-search-state';searchState.setAttribute('role','status');searchState.setAttribute('aria-live','polite');
 const grid=document.createElement('div');grid.className='rp-place-layout';
 const map=document.createElement('div');map.id='rp-map';
 const setup=document.createElement('aside');setup.className='rp-setup';setup.setAttribute('aria-label','Import selected area');
 const areaTitle=document.createElement('h3');areaTitle.textContent='Selected area';
 const coordinates=document.createElement('details');coordinates.id='rp-coordinates';coordinates.innerHTML='<summary>Coordinates & area width</summary>';
 const settings=document.createElement('details');settings.id='rp-map-settings';settings.innerHTML='<summary>Single-map size & preservation</summary>';
 const coordinateFields=old.querySelector('.two-fields'),sizeLabel=$('import-size').closest('label');
 settings.append(sizeLabel,$('preserve-building-edits').closest('label'),$('import-regional-profile').closest('label'));
 $('use-place-bounds').closest('label').lastChild.textContent=' Keep selected boundary';
 coordinates.append($('use-place-bounds').closest('label'),coordinateFields);
 const source=old.querySelector('.osm-source-panel'),sourceHelp=document.createElement('details');
 sourceHelp.innerHTML='<summary>Download details & limits</summary>';
 for(const node of [...source.children])if(node.matches('p.hint')&&!node.id)sourceHelp.append(node);
 const sourceStatus=$('import-source-state'),sourceSummary=document.createElement('p');sourceSummary.id='rp-source-summary';sourceSummary.className='hint';sourceSummary.setAttribute('role','status');sourceSummary.setAttribute('aria-live','polite');sourceHelp.prepend(sourceStatus);source.append(sourceSummary);
 source.append(sourceHelp);
 const summarizeSource=()=>{const text=sourceStatus.textContent;sourceSummary.classList.toggle('error',sourceStatus.classList.contains('error'));sourceSummary.textContent=sourceStatus.classList.contains('error')||text.length<=180?text:text.slice(0,177)+'… See download details.';};
 new MutationObserver(summarizeSource).observe(sourceStatus,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class']});summarizeSource();
 const actions=old.querySelector('.dialog-actions');actions.className='rp-import-actions';
 $('import-world-button').textContent='Set up linked world';$('import-world-button').classList.add('primary');
 $('import-osm-button').textContent='Import single map';$('import-osm-button').classList.remove('primary');
 // Saved JSON/PBF source selection already covers direct GeoJSON. Keep the legacy command callable.
 $('import-file-button').hidden=true;
 actions.prepend($('import-world-button'));
 const note=document.createElement('p');note.className='hint';note.textContent='For a town or region, use linked sections. A single map fits this area into the chosen tile size.';
 const areaAdvice=()=>{try{const b=getBounds(),needsWorld=b.north-b.south>.08||b.east-b.west>.12;if(!isBusy())$('import-osm-button').disabled=needsWorld;note.textContent=needsWorld?'This selection needs a linked world. Set up connected sections to keep the selected area.':'For a town or region, use linked sections. A single map fits this area into the chosen tile size.';}catch{note.textContent='Choose valid coordinates or select an area on the map.';}};
 new MutationObserver(areaAdvice).observe($('import-area-state'),{childList:true,subtree:true});areaAdvice();
 const safety=document.createElement('p');safety.className='hint rp-safety';safety.textContent='Selecting an area leaves your map unchanged. Import only when you are ready.';
 setup.append(areaTitle,$('import-area-state'),coordinates,settings,source,note);
 const footer=document.createElement('div');footer.className='rp-footer';footer.append(actions,$('import-state'),$('import-offline'),safety);
 grid.append(map,setup);place.append(searchForm,results,searchState,grid,footer);old.remove();
 $('sw-map').textContent='Return to editor';
 const intro=sites.querySelector(':scope > p.hint');if(intro)intro.textContent='Review imported outlines and edit site groups. Blue: source outlines. Amber: editable sites. Interiors remain inferred.';
 const empty=document.createElement('p');empty.id='rp-sites-empty';empty.className='hint';empty.textContent='No geographic objects yet. Choose a place and import it to inspect source outlines and create sites.';sites.prepend(empty);
 const layout=sites.querySelector('.sw-layout');
 for(const section of [...layout.children].slice(1)){
  const detail=document.createElement('details');detail.className='rp-site-detail';const summary=document.createElement('summary');summary.textContent=section.querySelector('h3').textContent;section.querySelector('h3').remove();layout.replaceChild(detail,section);detail.append(summary,section);
 }
 let active='place',searchController=null,searchRevision=0;
 const picker=mountPlaceMap(map,{getBounds,onSelect:bounds=>{
  if(isBusy()){picker.setBounds(getBounds(),{fit:false});return;}
  selectBounds(bounds);picker.setBounds(getBounds(),{fit:false});
 }});
 const cancelSearch=()=>{searchRevision++;searchController?.abort();searchController=null;};
 function activate(tab,{focus=false}={}){
  active=tab;for(const id of ['place','sites']){const selected=id===tab;const button=$(`rp-${id}-tab`);button.setAttribute('aria-selected',String(selected));button.tabIndex=selected?0:-1;$(`rp-${id}`).hidden=!selected;if(selected&&focus)button.focus();}
  if(tab==='place'){if(dialog.open)void picker.show();}
  else{picker.cancel();empty.hidden=!!(workbench.project().bounds||workbench.project().sourceFeatures?.length);}
 }
 tabs.onkeydown=event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();activate(event.key==='Home'?'place':event.key==='End'?'sites':active==='place'?'sites':'place',{focus:true});};
 searchForm.onsubmit=async event=>{
  event.preventDefault();if(isBusy())return;cancelSearch();const query=$('place-query').value.trim();results.replaceChildren();
  if(!query){searchState.textContent='Enter a city or address.';return;}
  const revision=searchRevision,controller=new AbortController();searchController=controller;searchState.textContent='Finding places…';
  try{
   const found=await search(query,controller.signal);if(controller.signal.aborted||revision!==searchRevision||!dialog.open)return;
   searchState.textContent=found.length?'Choose a result to set the area.':'No place found. Try a nearby city, coordinates or the map.';
   for(const item of found){const button=document.createElement('button');button.type='button';button.textContent=item.display_name;button.onclick=()=>{if(isBusy())return;try{selectSearchResult(item);picker.setBounds(getBounds());results.replaceChildren();searchState.textContent='Area selected. Adjust the rectangle or continue to import.';}catch(error){searchState.textContent=error.message;}};results.append(button);}
  }catch(error){if(!controller.signal.aborted&&revision===searchRevision)searchState.textContent=error.message+' You can still choose an area on the map or enter coordinates.';}
  finally{if(revision===searchRevision)searchController=null;}
 };
 $('place-query').addEventListener('input',()=>{cancelSearch();results.replaceChildren();searchState.textContent='';});
 dialog.addEventListener('close',()=>{cancelSearch();picker.cancel();});
 activate('place');
 return {
  open(tab='place'){onOpen();workbench.open();activate(tab);},
  refresh({fit=true}={}){if(active==='place'&&dialog.open)picker.setBounds(getBounds(),{fit});},
  showImported(){activate('sites');},
  picker
 };
}
