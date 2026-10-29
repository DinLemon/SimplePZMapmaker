export const maximumReferenceBytes=8*1024*1024;

function coordinates(longitude,latitude){
 if(!Number.isFinite(longitude)||!Number.isFinite(latitude)||Math.abs(longitude)>180||Math.abs(latitude)>90)throw Error('Select valid longitude/latitude.');
}
export function externalReferenceLinks(longitude,latitude){
 coordinates(longitude,latitude);
 return {
  google:`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${latitude},${longitude}`)}`,
  yandex:`https://yandex.com/maps/?ll=${encodeURIComponent(`${longitude},${latitude}`)}&z=18&l=map%2Cstv`
 };
}
export function validateOwnPhoto(file){
 if(!file||!['image/png','image/jpeg'].includes(file.type))throw Error('Select an owned PNG or JPEG photo.');
 if(!Number.isFinite(file.size)||file.size<1||file.size>maximumReferenceBytes)throw Error('The image must be at most 8 MiB.');
 return file;
}
function distance(a,b,c,d){
 const rad=Math.PI/180,h=Math.sin((d-b)*rad/2)**2+Math.cos(b*rad)*Math.cos(d*rad)*Math.sin((c-a)*rad/2)**2;
 return 6371008.8*2*Math.asin(Math.sqrt(Math.min(1,Math.max(0,h))));
}
export function nearestReferences(items,longitude,latitude){
 coordinates(longitude,latitude);
 const rank=r=>Number.isFinite(r.longitude)&&Number.isFinite(r.latitude)?distance(longitude,latitude,r.longitude,r.latitude):Infinity;
 return [...items].sort((a,b)=>rank(a)-rank(b));
}
function sessionPath(session){
 if(!/^[a-f0-9]{64}$/i.test(session??''))throw Error('Connect the current map/world agent session first.');
 return `/api/visual-references/${encodeURIComponent(session)}`;
}
function referenceId(id){if(!/^[a-f0-9]{32}$/i.test(id??''))throw Error('Invalid reference ID.');return id;}
export function createReferenceClient(fetcher=globalThis.fetch.bind(globalThis)){
 async function request(url,options={}){
  const response=await fetcher(url,{...options,credentials:'same-origin',cache:'no-store'});
  const body=await response.json();
  if(!response.ok)throw Error(body.error??`Reference request failed (${response.status}).`);
  return body;
 }
 const post=(url,body,signal)=>request(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal});
 return {
  capabilities:signal=>request('/api/visual-references/capabilities',{signal}),
  target:(session,revision,target,signal)=>post(`${sessionPath(session)}/target`,{revision,target},signal),
  list:(session,revision,target,signal)=>post(`${sessionPath(session)}/list`,{revision,target},signal),
  search:(session,revision,target,radiusMetres=150,signal)=>post(`${sessionPath(session)}/mapillary/search`,{revision,target,radiusMetres,provider:'mapillary'},signal),
  importMapillary:async(session,revision,target,sourceId,signal)=>{
   if(!/^\d{1,32}$/.test(sourceId??''))throw Error('Select a numeric Mapillary source ID.');
   return post(`${sessionPath(session)}/mapillary/import`,{revision,target,sourceId,provider:'mapillary'},signal);
  },
  upload:async(session,revision,target,file,provenance,signal)=>{
   validateOwnPhoto(file);
   signal?.throwIfAborted();
   if(JSON.stringify(provenance).length>16384)throw Error('Photo metadata is too long.');
   const bytes=new Uint8Array(await file.arrayBuffer());signal?.throwIfAborted();
   let binary='';for(let at=0;at<bytes.length;at+=32768)binary+=String.fromCharCode(...bytes.subarray(at,at+32768));
   return post(`${sessionPath(session)}/own`,{revision,target,provenance,base64:btoa(binary),mimeType:file.type},signal);
  },
  bind:(session,revision,target,id,signal)=>post(`${sessionPath(session)}/${referenceId(id)}/bind`,{revision,target},signal),
  observations:(session,id,signal)=>request(`${sessionPath(session)}/${referenceId(id)}/observations`,{signal}),
  propose:(session,revision,observationId,draft,constraints,interiorMode='preserve',signal)=>post(`${sessionPath(session)}/propose`,{revision,observationId,draft,constraints,interiorMode},signal),
  imageUrl:(session,id)=>`${sessionPath(session)}/${referenceId(id)}/image`
 };
}

// The host owns editor selection/session/revision. No app.js globals and no automatic project replacement.
export function mountVisualReferences(container,{getContext,onInspect,onProposal,fetcher}={}){
 if(!container||typeof getContext!=='function')throw Error('Visual references require a container and getContext().');
 const document=container.ownerDocument,client=createReferenceClient(fetcher),events=new AbortController();
 let controller=null,generation=0,disposed=false;
 const element=(tag,text,className)=>{const e=document.createElement(tag);if(text!==undefined)e.textContent=text;if(className)e.className=className;return e;};
 const panel=element('section',undefined,'visual-references');panel.setAttribute('aria-label','Visual references');
 const title=element('h3','Visual references'),status=element('p','Select a building or geographic point.','vr-status');status.setAttribute('role','status');status.setAttribute('aria-live','polite');
 const targetLabel=element('p',undefined,'vr-target'),links=element('nav',undefined,'vr-links');links.setAttribute('aria-label','External reference navigation');
 const refreshButton=element('button','Refresh references');refreshButton.type='button';
 const own=element('section',undefined,'vr-provider'),mapillary=element('section',undefined,'vr-provider');
 own.append(element('h4','Own photos'));mapillary.append(element('h4','Mapillary'));
 const form=element('form'),file=element('input');file.type='file';file.accept='image/png,image/jpeg';file.required=true;
 function input(label,type='text',value=''){
  const row=element('label',label),field=element('input');field.type=type;field.value=value;row.append(field);form.append(row);return field;
 }
 const fileLabel=element('label','Owned photo (PNG/JPEG, 8 MiB maximum)');fileLabel.append(file);form.append(fileLabel);
 const author=input('Photographer / author');author.required=true;author.maxLength=200;
 const license=input('Photo license / permission','text','own-photo');license.required=true;license.maxLength=200;
 const date=input('Capture date (optional)','date'),heading=input('Camera heading (optional)','number');heading.min='0';heading.max='359.999';heading.step='any';
 const longitude=input('Photo longitude (optional)','number'),latitude=input('Photo latitude (optional)','number');longitude.step=latitude.step='any';longitude.min='-180';longitude.max='180';latitude.min='-90';latitude.max='90';
 const upload=element('button','Upload and bind to selection');upload.type='submit';form.append(upload);
 const ownCards=element('div',undefined,'vr-cards'),mlyCards=element('div',undefined,'vr-cards'),searchResults=element('div',undefined,'vr-cards');
 const capability=element('p',undefined,'vr-capability'),search=element('button','Find nearby licensed images');search.type='button';
 own.append(form,ownCards);mapillary.append(capability,search,mlyCards,searchResults);
 panel.append(title,targetLabel,links,refreshButton,status,own,mapillary);container.append(panel);

 const context=()=>{
  const c=getContext();if(!c||!Number.isSafeInteger(c.revision)||c.revision<1||!c.target)throw Error('Select a building and connect its map/world agent session.');
  sessionPath(c.sessionToken);return structuredClone(c);
 };
 const key=c=>JSON.stringify([c.sessionToken,c.revision,c.target]);
 const stillCurrent=c=>{if(disposed||key(context())!==key(c))throw Error('Selection or revision changed. Refresh references for the current building.');};
 const tell=message=>{if(!disposed)status.textContent=message;};
 function link(label,url){const a=element('a',label);a.href=url;a.target='_blank';a.rel='noopener noreferrer';a.referrerPolicy='no-referrer';return a;}
 function error(e){if(e.name!=='AbortError')tell(e.message??'Reference request failed.');}
 function run(button,action){
  button.addEventListener('click',async()=>{button.disabled=true;try{await action();}catch(e){error(e);}finally{if(!disposed)button.disabled=false;}},{signal:events.signal});
 }
 function metadata(r){
  const date=r.capturedAt?new Date(r.capturedAt).toLocaleDateString():'unknown date';
  return `${r.author} · ${date} · heading ${r.heading??'unknown'}${r.heading==null?'':'°'}`;
 }
 function card(r,c,downloaded){
  const article=element('article',undefined,'vr-card');article.dataset.provider=downloaded?r.provider:'mapillary';
  article.append(element('strong',`${downloaded?r.provider==='own'?'Own photo':'Mapillary':'Mapillary source'} · ${r.sourceId}`));
  if(downloaded){
   const image=element('img');image.src=client.imageUrl(c.sessionToken,r.id);image.alt=`${r.provider==='own'?'Owned':'Mapillary'} reference ${r.sourceId}`;image.loading='lazy';image.referrerPolicy='no-referrer';
   const open=link('View original stored image',image.src);article.append(image,open);
  }
  article.append(element('p',metadata(r)));
  if(r.provider==='mapillary'||!downloaded){
   // Construct provider links ourselves. Never trust upstream URLs in cards or use remote thumbnails.
   article.append(link('Mapillary source',`https://www.mapillary.com/app/?pKey=${encodeURIComponent(r.sourceId)}`),' · ',link('CC BY-SA 4.0','https://creativecommons.org/licenses/by-sa/4.0/'));
  }else article.append(element('p',r.license));
  if(Number.isFinite(r.distanceMetres))article.append(element('p',`${Math.round(r.distanceMetres)} m from selection. Facade visibility is unknown.`));
  if(!downloaded){
   const importButton=element('button','Import this source image');importButton.type='button';article.append(importButton);
   run(importButton,async()=>{stillCurrent(c);await client.importMapillary(c.sessionToken,c.revision,c.target,r.sourceId,controller?.signal);stillCurrent(c);await refresh();});
   return article;
  }
  const inspect=element('button','Inspect with vision agent');inspect.type='button';article.append(inspect);
  run(inspect,async()=>{
   stillCurrent(c);
   const detail={...c,referenceId:r.id,instructions:`Call visual_reference_view for referenceId ${r.id}; pass its actual image block to a vision-capable model. Use the returned receipt in visual_reference_observe at expectedRevision ${c.revision}. Record exterior floors, wallMaterial, roof, doors, fences and landscaping, with confidence, visibility and uncertainty. Hidden facades and interiors remain unobserved. Propose only an isolated checked-design draft, preview/check it and require explicit revision-guarded apply.`};
   if(onInspect)await onInspect(detail);else{const E=document.defaultView.CustomEvent;panel.dispatchEvent(new E('visual-reference-inspect',{detail,bubbles:true}));}
   stillCurrent(c);tell('Agent handoff requested. The agent must view the actual image; an image URL alone is not visual evidence.');
  });
  const observationsButton=element('button','Read observations / prepare draft');observationsButton.type='button';article.append(observationsButton);
  const observations=element('div',undefined,'vr-observations');article.append(observations);
  run(observationsButton,async()=>{
   stillCurrent(c);const records=await client.observations(c.sessionToken,r.id,controller?.signal);stillCurrent(c);observations.replaceChildren();
   if(!records.length){observations.append(element('p','No vision observations yet. Unseen interiors and hidden facades remain unknown.'));return;}
   for(const record of records){
    const details=element('details'),summary=element('summary',`Observations ${record.id.slice(0,8)} · interiors unobserved`),pre=element('pre',JSON.stringify(record.observations,null,2));details.append(summary,pre);
    const editor=element('textarea');editor.setAttribute('aria-label','Explicit building draft and checked-design constraints JSON');editor.placeholder='{"draft": {complete building}, "constraints": {checked-design rectangle}, "interiorMode": "preserve"}';
    const propose=element('button','Create checked-design draft (no apply)');propose.type='button';details.append(editor,propose);observations.append(details);
    run(propose,async()=>{
     stillCurrent(c);let proposal;try{proposal=JSON.parse(editor.value);}catch{throw Error('Provide draft and constraints as JSON.');}
     if(!proposal.draft||!proposal.constraints)throw Error('Provide both draft and checked-design constraints.');
     const result=await client.propose(c.sessionToken,c.revision,record.id,proposal.draft,proposal.constraints,proposal.interiorMode??'preserve',controller?.signal);stillCurrent(c);
     if(onProposal)await onProposal(result);else{const E=document.defaultView.CustomEvent;panel.dispatchEvent(new E('visual-reference-proposal',{detail:result,bubbles:true}));}
     tell('Checked-design draft created. Preview the candidate, run its checks, then explicitly apply its exact task revision.');
    });
   }
  });
  return article;
 }
 async function refresh(){
  const ownGeneration=++generation;controller?.abort();controller=new AbortController();const signal=controller.signal;
  searchResults.replaceChildren();ownCards.replaceChildren();mlyCards.replaceChildren();links.replaceChildren();search.disabled=true;
  try{
   const c=context();tell('Loading references…');
   const [caps,binding,items]=await Promise.all([client.capabilities(signal),client.target(c.sessionToken,c.revision,c.target,signal),client.list(c.sessionToken,c.revision,c.target,signal)]);
   if(ownGeneration!==generation||disposed)return;stillCurrent(c);
   targetLabel.textContent=`${binding.sourceId??binding.buildingId??'Geographic point'} · revision ${binding.baseRevision}${binding.sharedBuilding?' · shared world building':''}`;
   if(binding.longitude!=null&&binding.latitude!=null){const urls=externalReferenceLinks(binding.longitude,binding.latitude);links.append(link('Google Maps (external view only)',urls.google),link('Yandex (external view only)',urls.yandex));}
   capability.textContent=caps.mapillary.message;search.disabled=!caps.mapillary.available||binding.longitude==null;
   for(const {reference:r} of items)(r.provider==='own'?ownCards:mlyCards).append(card(r,c,true));
   if(!ownCards.childElementCount)ownCards.append(element('p','No owned photos bound to this selection.'));
   if(!mlyCards.childElementCount)mlyCards.append(element('p','No Mapillary images imported for this selection.'));
   tell('Images are exterior evidence as of capture time. Unknown details remain unknown; proposals require preview and explicit apply.');
  }catch(e){if(ownGeneration===generation)error(e);}
 }
 run(refreshButton,refresh);
 run(search,async()=>{
  const c=context();const result=await client.search(c.sessionToken,c.revision,c.target,150,controller?.signal);stillCurrent(c);
  searchResults.replaceChildren(element('p',result.coverage));
  for(const r of result.items)searchResults.append(card(r,c,false));
  if(!result.items.length)searchResults.append(element('p','No licensed Mapillary coverage returned in this radius. Upload an owned photo.'));
 });
 form.addEventListener('submit',async event=>{
  event.preventDefault();upload.disabled=true;
  try{
   const c=context(),photo=validateOwnPhoto(file.files?.[0]);
   const lon=longitude.value===''?null:Number(longitude.value),lat=latitude.value===''?null:Number(latitude.value);
   if((lon==null)!==(lat==null))throw Error('Provide photo longitude and latitude together, or leave both unknown.');
   if(lon!=null)coordinates(lon,lat);
   const h=heading.value===''?null:Number(heading.value);if(h!=null&&(!Number.isFinite(h)||h<0||h>=360))throw Error('Camera heading must be in [0,360).');
   const provenance={author:author.value.trim(),license:license.value.trim(),capturedAt:date.value?`${date.value}T00:00:00Z`:null,heading:h,longitude:lon,latitude:lat};
   if(!provenance.author||!provenance.license)throw Error('Provide the photographer and photo license/permission.');
   await client.upload(c.sessionToken,c.revision,c.target,photo,provenance,controller?.signal);stillCurrent(c);file.value='';await refresh();
  }catch(e){error(e);}finally{if(!disposed)upload.disabled=false;}
 },{signal:events.signal});
 void refresh();
 return {refresh,destroy(){disposed=true;generation++;controller?.abort();events.abort();panel.remove();}};
}
