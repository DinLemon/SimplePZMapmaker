const modes=['create','edit','check'];
export class WorkspaceFlowState{
 constructor(){this.mode='create';this.pendingProject=false;}
 show(mode,{busy=false}={}){if(!modes.includes(mode))throw Error('Unknown workspace.');if(busy)return false;this.mode=mode;return true;}
 expectProject(){this.pendingProject=true;}
 projectOpened(){if(this.pendingProject){this.mode='edit';this.pendingProject=false;}}
}
export function workspaceForGuideContext(context){
 if(['menu-import','import','new'].includes(context))return 'create';
 if(['overview','layers','library','workshop','studio','menu-tools'].includes(context))return 'edit';
 if(['jobs','export','check'].includes(context))return 'check';
 return null;
}

// All commands keep their existing handlers and project owners. This state is presentation only.
export function installWorkspaceFlow({isBusy=()=>false}={}){
 const $=id=>document.getElementById(id),doc=document,state=new WorkspaceFlowState();
 const css=doc.createElement('link');css.rel='stylesheet';css.href='workspace-flow.css';doc.head.append(css);
 const main=doc.querySelector('main.workspace'),top=doc.querySelector('.topbar'),tools=doc.querySelector('.tools'),inspector=doc.querySelector('.inspector'),map=doc.querySelector('.map-workspace');
 const node=(tag,className,text)=>{const n=doc.createElement(tag);if(className)n.className=className;if(text)n.textContent=text;return n;};
 const button=(label,action,className)=>{const b=node('button',className,label);b.type='button';b.onclick=action;return b;};
 const navigation=node('nav','workflow-navigation');navigation.setAttribute('aria-label','Working stage');
 const labels={create:'Create',edit:'Edit map',check:'Check & export'};
 for(const mode of modes){const b=button(labels[mode],()=>show(mode));b.id=`workspace-${mode}`;b.setAttribute('aria-controls',mode==='edit'?'editor-surface':`workflow-${mode}`);navigation.append(b);}
 const jobBadge=node('span','workflow-job-badge');jobBadge.id='workflow-job-count';jobBadge.hidden=true;navigation.querySelector('#workspace-check').append(jobBadge);
 const current=node('p','workflow-current');navigation.append(current);top.after(navigation);map.id='editor-surface';
 const create=node('section','workflow-page workflow-create');create.id='workflow-create';create.setAttribute('aria-labelledby','workflow-create-title');
 create.innerHTML='<div class="workflow-intro"><h1 id="workflow-create-title">Create a map</h1><p>Start with a real place or an empty canvas. Your current map stays available while you choose.</p></div><div class="workflow-start-layout"><div class="workflow-start-primary"><h2>Bring a real place into the game</h2><p>Choose an area on the map. Import streets, water and building outlines into one map or connected world sections.</p></div><div class="workflow-start-other"><h2>Start from scratch</h2></div></div><details class="workflow-extras"><summary>Building files & other sources</summary></details>';
 const real=$('open-import');real.classList.add('workflow-place-command');real.textContent='Choose a real place';real.setAttribute('aria-label','Choose a real place');
 create.querySelector('.workflow-start-primary').append(real,node('p','hint','Large selections can use a larger section layout while keeping their area and scale.'));
 const others=create.querySelector('.workflow-start-other');others.append(button('Create a blank map',()=>$('new-map').click()),button('Open a saved project',()=>$('load-project').click()));
 const resume=node('div','workflow-resume');const resumeName=node('strong'),resumeInfo=node('p','hint');resume.append(node('h2','','Current map'),resumeName,resumeInfo,button('Continue editing',()=>show('edit')));others.append(resume);
 const imports=doc.querySelector('[data-command-menu="import"]');if(imports){imports.querySelector('summary').textContent='Other imports';create.querySelector('.workflow-extras').append(imports);}
 const guideHome=node('div','workflow-guide-home');create.append(guideHome);
 const check=node('section','workflow-page workflow-check');check.id='workflow-check';check.setAttribute('aria-labelledby','workflow-check-title');
 check.innerHTML='<div class="workflow-intro"><h1 id="workflow-check-title">Check and build</h1><p>Inspect the project, review its sources and prepare the game files. Each check or build starts when you choose it.</p></div><div class="workflow-check-layout"><section data-check="doctor"><h2>Project checks</h2><p>Review missing resources, access, seams and tile compatibility.</p></section><section data-check="sources"><h2>Real-place transfer</h2><p>Inspect retained outlines and edit site groups without restarting the geographic import.</p></section><section data-check="build"><h2>Game output</h2><p>Compile the current map or export saved world sections. Installation remains an explicit step.</p></section></div><section id="workflow-jobs"><h2>Build history</h2><p id="workflow-jobs-empty" class="hint">No exports queued. Completed builds will appear here.</p></section>';
 const doctor=$('open-project-doctor');if(doctor)check.querySelector('[data-check="doctor"]').append(doctor);
 check.querySelector('[data-check="doctor"]').append(button('Game resources',()=>$('open-resources').click()));
 check.querySelector('[data-check="sources"]').append(button('Inspect imported objects',()=>doc.dispatchEvent(new CustomEvent('workspace-open-sources'))));
 check.querySelector('[data-check="build"]').append(button('Build current map',()=>$('export-map').click(),'primary'),button('Export a saved world',()=>{$('open-world').click();const box=$('world-workspaces');if(box)box.open=true;}));
 main.prepend(create,check);
 const toolsMenu=doc.querySelector('[data-command-menu="tools"]');if(toolsMenu){toolsMenu.querySelector('summary').textContent='More tools';tools.append(toolsMenu);}
 // Put drawing operations beside the tool palette. The inspector keeps object/project information.
 for(const selector of ['.native-edit-panel','#zone-tools','#fence-controls','#geometry-tools','#gameplay-panel']){const panel=doc.querySelector(selector);if(panel)tools.append(panel);}
 const toolsHeading=node('h2','workflow-tools-title','Tools');tools.prepend(toolsHeading);
 const inspectorHeading=$('inspector-context');if(inspectorHeading)inspectorHeading.tabIndex=-1;
 let inspectorOpen=false,toolsOpen=true;
 const toolbar=doc.querySelector('.map-toolbar');
 const toolsToggle=button('Tools',()=>{toolsOpen=!toolsOpen;applyPresentation();});toolsToggle.id='workspace-tools-toggle';toolsToggle.setAttribute('aria-controls','workspace-tool-panel');tools.id='workspace-tool-panel';
 const layersToggle=button('Layers',()=>{inspectorOpen=true;applyPresentation();const box=doc.querySelector('.map-layers-workbench');if(box)box.open=true;box?.querySelector('summary')?.focus();});layersToggle.id='workspace-layers-toggle';
 const inspectorToggle=button('Properties',()=>{inspectorOpen=!inspectorOpen;applyPresentation();if(inspectorOpen)inspectorHeading?.focus();});inspectorToggle.id='workspace-inspector-toggle';inspectorToggle.setAttribute('aria-controls','workspace-inspector');inspector.id='workspace-inspector';
 toolbar.prepend(toolsToggle);toolbar.append(layersToggle,inspectorToggle);
 const view=doc.querySelector('.view-controls'),viewOptions=node('details','workflow-view-options'),viewSummary=node('summary','','View options'),viewBody=node('div','workflow-view-options-body');
 viewOptions.append(viewSummary,viewBody);for(const id of ['view-iso','check-lighting','toggle-grid'])if($(id))viewBody.append($(id));view.append(viewOptions);
 const announce=node('p','sr-only');announce.setAttribute('role','status');announce.setAttribute('aria-live','polite');navigation.append(announce);
 function applyPresentation(){
  doc.body.dataset.workflow=state.mode;doc.body.dataset.inspectorOpen=String(inspectorOpen);doc.body.dataset.toolsOpen=String(toolsOpen);
  create.hidden=state.mode!=='create';check.hidden=state.mode!=='check';map.hidden=state.mode!=='edit';tools.hidden=state.mode!=='edit'||!toolsOpen;inspector.hidden=state.mode!=='edit'||!inspectorOpen;
  for(const mode of modes)$(`workspace-${mode}`).setAttribute('aria-current',state.mode===mode?'page':'false');
  toolsToggle.setAttribute('aria-expanded',String(toolsOpen));inspectorToggle.setAttribute('aria-expanded',String(inspectorOpen));
  current.textContent=$('project-name').value;resumeName.textContent=$('project-name').value;resumeInfo.textContent=$('map-summary').textContent;
 }
 function show(mode){if(!state.show(mode,{busy:isBusy()}))return false;applyPresentation();announce.textContent=labels[mode];return true;}
 doc.querySelector('.brand').onclick=event=>{event.preventDefault();show('create');};
 navigation.onkeydown=e=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(e.key)||!e.target.id.startsWith('workspace-'))return;e.preventDefault();const index=modes.indexOf(e.target.id.slice('workspace-'.length));const next=e.key==='Home'?0:e.key==='End'?2:(index+(e.key==='ArrowRight'?1:2))%3;if(show(modes[next]))$(`workspace-${modes[next]}`).focus();};
 doc.addEventListener('workspace-show',event=>{if(modes.includes(event.detail))show(event.detail);});
 doc.addEventListener('editor-project-opened',()=>{state.projectOpened();applyPresentation();});
 for(const id of ['new-map','load-project','open-import'])$(id).addEventListener('click',()=>state.expectProject());
 $('export-map').addEventListener('click',()=>{if(!isBusy())show('check');});
 doc.addEventListener('keydown',event=>{if(event.key==='Escape'&&inspector.contains(event.target)&&!doc.querySelector('dialog[open]')){inspectorOpen=false;applyPresentation();inspectorToggle.focus();}});
 function collectJobs(){
  const invitation=$('guide-invitation');if(invitation?.parentElement===doc.body)guideHome.append(invitation);
  const jobs=$('export-jobs');if(!jobs)return;
  const owner=jobs.closest('dialog');
  if(owner&&!owner.open||jobs.parentElement===doc.body&&!doc.querySelector('dialog:modal'))$('workflow-jobs').append(jobs);
  const count=jobs.querySelectorAll('[data-export-job]').length;const label=count?String(count):'';
  if(jobBadge.textContent!==label)jobBadge.textContent=label;if(jobBadge.hidden!==!count)jobBadge.hidden=!count;if($('workflow-jobs-empty').hidden!==!!count)$('workflow-jobs-empty').hidden=!!count;
 }
 let queued=false;new MutationObserver(()=>{if(queued)return;queued=true;requestAnimationFrame(()=>{queued=false;collectJobs();});}).observe(doc.body,{childList:true,subtree:true,attributes:true,attributeFilter:['open','hidden']});
 applyPresentation();collectJobs();
 return {show,update({tool,nativeView,category}){
  const native=doc.querySelector('.native-edit-panel'),active=nativeView&&native&&!native.hidden;
  for(const b of tools.querySelectorAll('.tool-grid > button'))b.hidden=!!active&&['brush','rectangle','building','select','objects'].includes(b.dataset.tool);
  toolsHeading.textContent=active?'Floor tools':({terrain:'Terrain',paths:'Paths & fences',building:'Buildings',objects:'Objects',zones:'Zones',select:'Tools'})[category]??'Tools';
  applyPresentation();
 }};
}
