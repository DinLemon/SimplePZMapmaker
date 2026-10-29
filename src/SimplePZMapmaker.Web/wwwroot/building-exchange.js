const MAX_BYTES=8*1024*1024;
const mounts=new WeakMap();

async function post(path,body){
 const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 const result=await response.json();
 if(!response.ok)throw Error(result.diagnostics?.map(d=>d.message).join('\n')||'Building exchange request failed.');
 return result;
}

/**
 * Parent supplies a host element, getBuilding() and async onImport(building, context).
 * onImport owns resource/project validation, history and ONE atomic state update.
 * request(path, body) must resolve parsed JSON; errors must reject. No global editor state is read.
 * Returns {refresh, dispose}. Call refresh when selection changes. Neither imports nor exports save the project.
 */
export function mountBuildingExchange({container,getBuilding,onImport,request=post,onDiagnostics=()=>{}}={}){
 if(!container||typeof getBuilding!=='function'||typeof onImport!=='function')throw Error('Building exchange requires container, getBuilding and onImport.');
 if(mounts.has(container))return mounts.get(container);
 const document=container.ownerDocument;
 const make=(tag,text)=>{const node=document.createElement(tag);if(text!==undefined)node.textContent=text;return node;};
 const root=make('section');root.className='building-exchange';root.setAttribute('aria-label','BuildingEd / TileZed exchange');
 const title=make('p','BuildingEd / TileZed exchange (bounded subset)');
 const format=make('select');format.setAttribute('aria-label','Exchange format');
 for(const [value,text] of [['tbx','TBX — BuildingEd'],['tmx','TMX — TileZed']]){const option=make('option',text);option.value=value;format.append(option);}
 const exportButton=make('button','Export selected building');exportButton.type='button';
 const importButton=make('button','Import building file');importButton.type='button';
 const input=make('input');input.type='file';input.accept='.tbx,.tmx';input.hidden=true;
 const lossy=make('input');lossy.type='checkbox';
 const label=make('label');label.append(lossy,document.createTextNode(' Allow omission of reported unsupported details'));
 const hint=make('p','TBX: local floors from 0. TMX: signed floors and inline tilesets. Roofs, models and unsupported layers are reported. Import creates a separate building.');
 const status=make('p');status.setAttribute('role','status');status.setAttribute('aria-live','polite');
 const diagnostics=make('ul');diagnostics.setAttribute('aria-label','Exchange diagnostics');
 root.append(title,format,exportButton,importButton,input,label,hint,status,diagnostics);container.append(root);
 let busy=false,disposed=false;
 const refresh=()=>{exportButton.disabled=busy||!getBuilding();importButton.disabled=busy;format.disabled=busy;lossy.disabled=busy;};
 const show=result=>{diagnostics.replaceChildren();for(const d of result.diagnostics??[])diagnostics.append(make('li',`${d.severity}: ${d.path}: ${d.message}`));onDiagnostics(result.diagnostics??[]);};
 const run=async action=>{if(busy||disposed)return;busy=true;refresh();try{await action();}catch(error){if(!disposed)status.textContent=error.message;}finally{busy=false;if(!disposed)refresh();}};
 exportButton.onclick=()=>run(async()=>{
  const building=getBuilding();if(!building)throw Error('Select a building first.');
  const kind=format.value;
  const result=await request('/api/building/exchange/export',{building,format:kind,allowLossy:lossy.checked});
  if(disposed)return;show(result);
  if(!result.success||!result.xml){status.textContent='Export blocked. Review the diagnostics.';return;}
  const url=URL.createObjectURL(new Blob([result.xml],{type:'application/xml;charset=utf-8'}));
  const link=make('a');link.href=url;link.download='building.'+kind;root.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
  status.textContent='Building exported. Keep the source project and supply tilesheet PNGs in the target editor.';
 });
 importButton.onclick=()=>input.click();
 input.onchange=()=>{const file=input.files?.[0];input.value='';if(!file)return;return run(async()=>{
  const kind=file.name.toLowerCase().endsWith('.tbx')?'tbx':file.name.toLowerCase().endsWith('.tmx')?'tmx':null;
  if(!kind)throw Error('Choose a .tbx or .tmx file.');if(file.size>MAX_BYTES)throw Error('Building XML exceeds 8 MiB.');
  const allowLossy=lossy.checked;
  const result=await request('/api/building/exchange/import',{xml:await file.text(),format:kind,allowLossy});
  if(disposed)return;show(result);
  if(!result.success||!result.building){status.textContent='Import blocked. Review the diagnostics.';return;}
  await onImport(result.building,{format:kind,diagnostics:result.diagnostics??[],allowLossy});
  if(!disposed)status.textContent='Building imported.';
 });};
 const controller={refresh,dispose(){disposed=true;exportButton.onclick=importButton.onclick=input.onchange=null;root.remove();mounts.delete(container);}};
 mounts.set(container,controller);refresh();return controller;
}
