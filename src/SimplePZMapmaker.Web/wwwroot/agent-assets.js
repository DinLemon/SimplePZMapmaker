// Session-bound reference and sprite controls. No editor writes or image generation calls.
export function requirePublishedSession(session,{idle=false}={}){
 if(!/^[a-f0-9]{64}$/i.test(session.token??'')||!Number.isSafeInteger(session.revision)||session.revision<1)throw Error('Connect the current map/world agent session first.');
 if(session.dirty)throw Error('Submit editor changes before using references or attaching assets.');
 if(session.remote!==session.revision)throw Error('Review and receive agent changes before using references or attaching assets.');
 if(session.busy?.()||idle&&session.running)throw Error('Wait for the current editor operation to finish.');
 return {sessionToken:session.token,revision:session.revision,scope:session.mode};
}

// The host supplies VisualTargetRequest, not renderer coordinates.
export function referenceTarget(selection,mode){
 if(!selection)throw Error('Select a building or a geographic cursor point first.');
 if(selection.scope&&selection.scope!==mode)throw Error('Selection belongs to a different working scope.');
 const target={scope:mode};for(const key of ['memberId','buildingId','sourceId','longitude','latitude'])if(selection[key]!=null)target[key]=selection[key];
 if(mode==='map'&&target.memberId)throw Error('Choose the world scope for a section selection.');
 if(target.buildingId||target.sourceId)return target;
 if(Number.isFinite(target.longitude)&&Number.isFinite(target.latitude)&&Math.abs(target.longitude)<=180&&Math.abs(target.latitude)<=90)return target;
 throw Error('Select a building, source ID or longitude/latitude first.');
}

export function assetDependencyChanges(before,after,world=false){
 const result=[],pairs=world?(after.maps??[]).map(m=>[m.name??m.id,(before.maps??[]).find(old=>old.id===m.id)?.project??{},m.project]):[['Map',before,after]];
 for(const [label,old,next] of pairs){
  const previous=[...(old.requiredMods??[])].sort(),current=[...(next.requiredMods??[])].sort();
  const providers=new Set([...Object.keys(old.assetSources??{}),...Object.keys(next.assetSources??{})]);
  const changed=[...providers].filter(id=>old.assetSources?.[id]!==next.assetSources?.[id]).length;
  if(JSON.stringify(previous)!==JSON.stringify(current)||changed)result.push(`${label}: required mods ${previous.join(', ')||'none'} → ${current.join(', ')||'none'}; ${changed} tile provider binding(s) changed`);
 }
 return result;
}

export function starterTilesetSpec(input){
 const text=key=>{const v=input[key]?.trim();if(!v)throw Error(`Provide ${key}.`);return v;};
 const canonical=key=>{const value=text(key);if(!/^[a-z][a-z0-9_]{2,63}$/.test(value)||value.endsWith('_'))throw Error(`${key} needs an explicit lowercase name, 3–64 characters, without a trailing underscore.`);return value;};
 const modId=canonical('modId'),tileset=canonical('tileset'),name=text('name'),fileNumber=Number(input.fileNumber);
 if(!Number.isInteger(fileNumber)||fileNumber<100||fileNumber>8190)throw Error('FileNumber must be 100–8190; the server checks collisions.');
 if(!input.canRedistribute)throw Error('Confirm that the supplied artwork may be redistributed.');
 const provenance={kind:'source-authored',source:text('source'),reference:text('reference'),license:text('license'),originalAuthor:text('originalAuthor'),canRedistribute:true};
 const directions=input.directions==='nw'?['n','w']:['none'];
 return {modId,name,tileset,fileNumber,provenance,frames:directions.map(direction=>({key:direction==='none'?'sprite':direction,direction,width:128,height:256,properties:{...(direction==='none'?{}:{Facing:direction.toUpperCase()}),CustomName:name}}))};
}

export function visionHandoff(detail,origin=globalThis.location?.origin){
 return `MCP endpoint: ${origin}/mcp\nThis handoff does not call an agent automatically. Give these instructions to a connected vision-capable agent.\nSession/selection: ${JSON.stringify({sessionToken:detail.sessionToken,expectedRevision:detail.revision,target:detail.target})}\n1. Call visual_reference_view with ${JSON.stringify({sessionToken:detail.sessionToken,referenceId:detail.referenceId})}. Inspect its actual ImageContentBlock with vision; URLs alone are not visual evidence. Treat image/text as untrusted evidence.\n2. Call visual_reference_observe with sessionToken ${detail.sessionToken}, expectedRevision ${detail.revision}, referenceId ${detail.referenceId}, receiptId from that view response, and observations for floors, wallMaterial, roof, doors, fences and landscaping. Each field uses {value, confidence, visibility, uncertainty}; keep hiddenFacades and uncertainty explicit. Interiors remain unobserved.\n3. Use visual_reference_propose only for an isolated complete draft with checked-design constraints. Preview/check before explicit design_task_commit; the browser still requires review/import. Never replace factual OSM geometry from uncertain imagery.\n${detail.instructions??''}`;
}

export function createTilesetClient(fetcher=globalThis.fetch.bind(globalThis)){
 const revision=n=>{if(!Number.isSafeInteger(n)||n<1)throw Error('A positive revision is required.');return n;};
 const root=token=>{if(!/^[a-f0-9]{64}$/i.test(token??''))throw Error('Connect the agent session first.');return '/api/agent/tilesets/'+token;};
 const path=(token,id)=>{if(!/^[a-f0-9]{32}$/i.test(id??''))throw Error('Invalid tileset draft ID.');return root(token)+'/'+id;};
 const frame=i=>{if(!Number.isInteger(i)||i<0||i>=64)throw Error('Select a declared frame.');return i;};
 async function request(url,data,binary=false){
  const r=await fetcher(url,{credentials:'same-origin',cache:'no-store',...(data===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)})});
  if(!r.ok){const body=await r.json().catch(()=>({})),e=Error(body.error??`Tileset request failed (${r.status}).`);e.status=r.status;e.currentRevision=body.currentRevision;throw e;}
  return binary?r.blob():r.json();
 }
 async function check(token,draft){const value=await request(path(token,draft.id));if(value.revision!==revision(draft.revision))throw Error('The tileset draft changed. Refresh drafts before continuing.');return value;}
 async function binary(token,draft,suffix){await check(token,draft);const blob=await request(path(token,draft.id)+suffix,undefined,true);await check(token,draft);return blob;}
 return {
  capabilities:()=>request('/api/agent/tilesets/capabilities'),
  list:async token=>request(root(token)),read:async(token,id)=>request(path(token,id)),
  create:async(context,spec)=>{root(context.sessionToken);revision(context.revision);if(context.scope==='world'&&!context.memberId)throw Error('Select a world section (memberId) before creating tiles.');return request('/api/agent/tilesets',{sessionToken:context.sessionToken,expectedRevision:context.revision,scope:context.scope??'map',...(context.memberId?{memberId:context.memberId}:{}),spec});},
  upload:async(token,draft,index,file)=>{
   const url=path(token,draft.id)+'/frames/'+frame(index),expectedDraftRevision=revision(draft.revision);
   if(!file||file.type!=='image/png')throw Error('Choose a completed PNG frame.');if(!file.size||file.size>2*1024*1024)throw Error('Each PNG must be nonempty and at most 2 MiB.');
   const bytes=new Uint8Array(await file.arrayBuffer());if(bytes.length>2*1024*1024)throw Error('Each PNG must be at most 2 MiB.');
   let value='';for(let at=0;at<bytes.length;at+=32768)value+=String.fromCharCode(...bytes.subarray(at,at+32768));
   return request(url,{expectedDraftRevision,pngBase64:btoa(value)});
  },
  preview:async(token,draft,index)=>{const suffix='/frames/'+frame(index);await check(token,draft);const metadata=await request(path(token,draft.id)+suffix+'/metadata'),blob=await request(path(token,draft.id)+suffix+'.png',undefined,true);await check(token,draft);return {metadata,blob};},
  build:(token,draft)=>request(path(token,draft.id)+'/build',{expectedDraftRevision:revision(draft.revision)}),
  download:(token,draft)=>binary(token,draft,'/mod.zip'),
  attach:(context,draft)=>request(path(context.sessionToken,draft.id)+'/attach',{expectedRevision:revision(context.revision),expectedDraftRevision:revision(draft.revision)})
 };
}

export function installAgentAssets(session,{mountReferences}={}){
 const document=session.dialog.ownerDocument;
 for(const href of ['agent-assets.css','visual-references.css'])if(!document.querySelector(`link[href="${href}"]`)){const style=document.createElement('link');style.rel='stylesheet';style.href=href;document.head.append(style);}
 const refs=document.createElement('details');refs.id='agent-references';refs.className='agent-asset-panel';refs.innerHTML='<summary>Visual references for selection</summary><p data-reference-context role="status"></p><p class="hint">Submit editor changes first. Own photos need no provider token. Mapillary search needs the server PZMAP_MAPILLARY_TOKEN; Google/Yandex links are external navigation only.</p><div data-reference-host></div><section data-vision-handoff hidden><h4>Vision agent instructions</h4><p>This is a manual handoff. No agent has been called.</p><textarea data-vision-text readonly aria-label="MCP vision agent instructions"></textarea><button type="button" data-vision-copy>Copy vision instructions</button><p data-vision-status role="status"></p></section>';
 const panel=document.createElement('details');panel.id='agent-assets';panel.className='agent-asset-panel';panel.innerHTML=`<summary>Sprite drafts / separate tile mod</summary><p data-assets-context role="status"></p><p class="hint">Create explicit new identities after inspecting the resource catalog. Upload licensed PNGs, or ask the connected agent to use image_gen with transparent_background=true. The editor has no image model. Each starter frame is 128×256; draw N and W separately. Physical flags/copyFrom belong in advanced JSON.</p>
 <div class="agent-asset-grid"><label>New mod ID<input data-assets-modId placeholder="my_custom_signs" maxlength="64"></label><label>Display name<input data-assets-name placeholder="My custom signs"></label><label>New tileset name<input data-assets-tileset placeholder="my_signs_01" maxlength="64"></label><label>Unused FileNumber<input data-assets-fileNumber type="number" min="100" max="8190" placeholder="100–8190"></label><label>Original author<input data-assets-originalAuthor></label><label>Artwork source<input data-assets-source></label><label>Retained original / reference<input data-assets-reference></label><label>License / permission<input data-assets-license></label></div>
 <label class="agent-asset-check"><input type="checkbox" data-assets-canRedistribute>I may redistribute this supplied artwork</label><label>Starter views<select data-assets-directions><option value="none">One nondirectional sprite</option><option value="nw">Separate north and west frames</option></select></label>
 <details><summary>Advanced spec JSON (generated art, physics, more frames)</summary><p>Generated provenance requires actual generator/prompt, source, reference and license. copyFrom must be an exact installed vanilla ID. Nonempty JSON replaces the starter fields.</p><textarea data-assets-spec aria-label="Advanced tileset spec JSON" placeholder="Paste a complete AgentTilesetSpec, or leave empty to use starter fields"></textarea><button type="button" data-assets-starter>Copy starter fields into JSON</button></details>
 <div class="agent-actions"><button type="button" data-assets-create>Create isolated tile draft</button><button type="button" data-assets-refresh>Refresh drafts</button></div><label>Session drafts<select data-assets-drafts><option value="">Refresh drafts after connecting</option></select></label><p data-assets-status role="status" aria-live="polite"></p>
 <section data-assets-detail hidden><p data-assets-summary></p><label>Frame<select data-assets-frames></select></label><label>Completed native PNG (2 MiB maximum)<input type="file" accept="image/png" data-assets-file></label><div class="agent-actions"><button type="button" data-assets-upload>Upload selected frame</button><button type="button" data-assets-preview>Preview actual pixels</button><button type="button" data-assets-build>Build separate B42 mod</button><button type="button" data-assets-download>Download tile mod ZIP</button><button type="button" data-assets-attach>Attach dependency to agent session</button></div><figure data-assets-image hidden><img data-assets-pixels alt="Actual selected sprite frame"><figcaption data-assets-origin></figcaption></figure><pre data-assets-metadata></pre><p data-assets-attached hidden>Dependency attached to the agent session. The editor is unchanged. Review the session below, then explicitly receive reviewed changes.</p><button type="button" data-assets-review hidden>Review session for import</button></section>`;
 session.node('review').before(refs,panel);
 const node=name=>panel.querySelector('[data-assets-'+name+']'),rn=name=>refs.querySelector('[data-'+name+']'),client=createTilesetClient();
 let token=null,draft=null,draftToken=null,previewUrl=null,referencePanel=null,referenceKey=null,handoffContext=null;
 const state=value=>node('status').textContent=value;
 function selection(){return session.getReferenceTarget?.(session.mode)??null;}
 function context({target=false,idle=false}={}){
  const value=requirePublishedSession(session,{idle}),selected=selection();
  if(target)value.target=referenceTarget(selected,session.mode);
  if(value.scope==='world')value.memberId=selected?.memberId;
  return value;
 }
 const fingerprint=c=>JSON.stringify([c,session.localRevision,session.changeVersion?.()]);
 function current(c){if(fingerprint(context())!==c.key)throw Error('Session, selection or editor changed during this operation. Refresh and retry.');}
 async function guarded(){const c=context();c.key=fingerprint(c);const status=await session.request(session.path()+'/status');session.remote=status.revision;current(c);return c;}
 function selectedDraft(){if(!draft||draftToken!==session.token)throw Error('Select a draft from this session.');return draft;}
 function clearPreview(){if(previewUrl)URL.revokeObjectURL(previewUrl);previewUrl=null;node('pixels').removeAttribute('src');node('image').hidden=true;node('metadata').textContent='';}
 function show(value,c){current(c);draft=value;draftToken=c.sessionToken;clearPreview();node('attached').hidden=true;node('review').hidden=true;node('detail').hidden=false;node('summary').textContent=`${draft.spec.name} · ${draft.spec.modId} · ${draft.scope}${draft.memberId?' / '+draft.memberId:''} · source r${draft.baseRevision} · draft r${draft.revision} · ${draft.state}${draft.sha256?' · SHA256 '+draft.sha256:''}`;
  node('frames').replaceChildren(...draft.frames.map(f=>new Option(`${f.index}: ${f.tileId} · ${f.direction} · ${f.width}×${f.height} · ${f.uploaded?'uploaded':'missing PNG'}`,String(f.index))));
 }
 async function list(c,selected=draft?.id){const items=await client.list(c.sessionToken);current(c);node('drafts').replaceChildren(new Option(items.length?'Select a tile draft':'No tile drafts yet',''),...items.map(d=>new Option(`${d.spec.name} · ${d.scope}${d.memberId?' / '+d.memberId:''} · draft r${d.revision} · ${d.state}`,d.id)));if(selected&&items.some(d=>d.id===selected))node('drafts').value=selected;}
 function starter(){const input={};for(const key of ['modId','name','tileset','fileNumber','originalAuthor','source','reference','license','directions'])input[key]=node(key).value;input.canRedistribute=node('canRedistribute').checked;return starterTilesetSpec(input);}
 const run=action=>session.run(async()=>{try{await action();}catch(e){state(e.message);throw e;}});
 node('starter').onclick=()=>{try{node('spec').value=JSON.stringify(starter(),null,2);}catch(e){state(e.message);}};
 node('create').onclick=()=>run(async()=>{const c=await guarded();let spec;if(node('spec').value.trim()){try{spec=JSON.parse(node('spec').value);}catch{throw Error('Advanced spec must be valid JSON.');}}else spec=starter();const value=await client.create(c,spec);show(value,c);await list(c,value.id);state('Isolated draft created. Upload each declared native PNG before building.');});
 node('refresh').onclick=()=>run(async()=>{const c=await guarded();await list(c);if(node('drafts').value)show(await client.read(c.sessionToken,node('drafts').value),c);else{draft=null;node('detail').hidden=true;clearPreview();}state('Drafts refreshed. Source project is unchanged.');});
 node('drafts').onchange=()=>run(async()=>{const c=await guarded();if(node('drafts').value)show(await client.read(c.sessionToken,node('drafts').value),c);else{draft=null;node('detail').hidden=true;clearPreview();}});
 node('frames').onchange=()=>{clearPreview();render();};
 node('upload').onclick=()=>run(async()=>{const c=await guarded(),d=selectedDraft(),index=Number(node('frames').value),value=await client.upload(c.sessionToken,d,index,node('file').files?.[0]);show(value,c);node('frames').value=String(index);node('file').value='';state('PNG validated and uploaded. Preview before build.');});
 node('preview').onclick=()=>run(async()=>{const c=await guarded(),d=selectedDraft(),index=Number(node('frames').value),result=await client.preview(c.sessionToken,d,index);current(c);clearPreview();previewUrl=URL.createObjectURL(result.blob);node('pixels').src=previewUrl;node('image').hidden=false;node('origin').textContent=`${result.metadata.tileId} · ${result.metadata.origin} · ${result.metadata.width}×${result.metadata.height}`;node('metadata').textContent=JSON.stringify(result.metadata,null,2);state(result.metadata.origin==='validated-native-pack'?'Preview reconstructed from the validated native pack.':'Preview of the uploaded PNG. Build, then inspect the native pack preview.');});
 node('build').onclick=()=>run(async()=>{const c=await guarded(),value=await client.build(c.sessionToken,selectedDraft());show(value,c);state('Separate B42 mod built and validated. Preview its native pack pixels before attach.');});
 node('download').onclick=()=>run(async()=>{const c=await guarded(),d=selectedDraft(),blob=await client.download(c.sessionToken,d);current(c);const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=d.spec.modId+'.zip';a.click();setTimeout(()=>URL.revokeObjectURL(url),10000);state('Tile mod ZIP downloaded separately. Keep it with the exported map mod.');});
 node('attach').onclick=()=>run(async()=>{const c=await guarded(),d=selectedDraft();if(d.scope!==c.scope||d.scope==='world'&&d.memberId!==c.memberId)throw Error('Select the world section that owns this draft before attaching.');const result=await client.attach(c,d);if(c.sessionToken!==session.token)throw Error('Session changed after attach; reconnect and review that session.');session.remote=result.revision;session.pendingReview=null;session.node('review').hidden=true;node('attached').hidden=false;node('review').hidden=false;state('Dependency attached to the session; editor unchanged. Review before receiving it.');await session.prepareReview();});
 node('review').onclick=()=>session.run(()=>session.prepareReview());
 refs.addEventListener('toggle',()=>{if(refs.open)refreshReferences();});
 function refreshReferences(){
  try{const c=context({target:true,idle:true});referenceKey=JSON.stringify(c);rn('reference-context').textContent=`${c.scope} · revision ${c.revision} · ${c.target.buildingId??c.target.sourceId??'geographic cursor'}${c.target.memberId?' · section '+c.target.memberId:''}`;
   if(!referencePanel)referencePanel=mountReferences(rn('reference-host'),{getContext:()=>{const {sessionToken,revision,target}=context({target:true,idle:true});return {sessionToken,revision,target};},onInspect:async detail=>{
    handoffContext=JSON.stringify(context({target:true,idle:true}));rn('vision-text').value=visionHandoff(detail);rn('vision-handoff').hidden=false;
    try{await navigator.clipboard.writeText(rn('vision-text').value);rn('vision-status').textContent='Instructions copied. Give them to your connected vision agent; no agent was called.';}catch{rn('vision-status').textContent='Clipboard unavailable. Select and copy the visible MCP instructions.';}
   },onProposal:()=>{session.message('Reference draft created. Open checked-design proposals, refresh, preview and check before committing.');}});
   else void referencePanel.refresh();
  }catch(e){rn('reference-context').textContent=e.message;referencePanel?.destroy();referencePanel=null;referenceKey=null;rn('vision-handoff').hidden=true;}
 }
 rn('vision-copy').onclick=async()=>{try{if(handoffContext!==JSON.stringify(context({target:true,idle:true})))throw Error('Selection or revision changed. Inspect the current reference again.');await navigator.clipboard.writeText(rn('vision-text').value);rn('vision-status').textContent='Instructions copied; no agent called.';}catch(e){rn('vision-status').textContent=e.message+' Select and copy the visible instructions if still applicable.';}};
 function render(){
  if(token!==session.token){token=session.token;draft=null;draftToken=null;clearPreview();node('detail').hidden=true;node('drafts').replaceChildren(new Option('Refresh drafts after connecting',''));referencePanel?.destroy();referencePanel=null;referenceKey=null;rn('vision-handoff').hidden=true;}
  let unavailable;try{context();}catch(e){unavailable=e.message;}
  node('context').textContent=unavailable??`${session.mode} · session revision ${session.revision}${session.mode==='world'?' · section '+(selection()?.memberId??'not selected'):''}. Editor changes require Submit; server changes require review/import.`;
  const disabled=!!unavailable||session.running;for(const name of ['create','refresh','drafts','starter'])node(name).disabled=disabled;
  const selected=!!draft&&draftToken===session.token,built=!!draft?.sha256,ready=draft?.frames?.every(f=>f.uploaded),index=Number(node('frames').value);
  for(const name of ['frames','file','upload','preview','build','download','attach'])node(name).disabled=disabled||!selected;
  node('upload').disabled||=built;node('file').disabled||=built;node('preview').disabled||=!draft?.frames?.[index]?.uploaded;node('build').disabled||=built||!ready;node('download').disabled||=!built;node('attach').disabled||=!built||draft?.scope!==session.mode||draft?.scope==='world'&&draft.memberId!==selection()?.memberId;
  node('review').disabled=!session.token||session.running;
  if(refs.open&&session.dialog.open&&!session.running){let key;try{key=JSON.stringify(context({target:true,idle:true}));}catch{}if(key!==referenceKey||unavailable)refreshReferences();}
  if(handoffContext){let key;try{key=JSON.stringify(context({target:true,idle:true}));}catch{}if(key!==handoffContext)rn('vision-handoff').hidden=true;}
 }
 const previous=session.render.bind(session);session.render=()=>{previous();render();};render();
 return {panel,refs,refreshReferences};
}
