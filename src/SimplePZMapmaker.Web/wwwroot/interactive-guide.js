import {workspaceForGuideContext} from './workspace-flow.js';
import {guideTours,guideCopy} from './guide-content.js';
import {GuideProgress,placeGuideCard} from './guide-state.js';

const safeStorage=()=>{try{return globalThis.localStorage;}catch{return null;}};

/** Read-only teaching UI. Only the user may execute a project operation. */
export function installInteractiveGuide({openLibrary=()=>{},isBusy=()=>false,storage=safeStorage()}={}){
 const progress=new GuideProgress(storage),$=id=>document.getElementById(id);
 const node=(tag,className,text)=>{const e=document.createElement(tag);if(className)e.className=className;if(text)e.textContent=text;return e;};
 const button=(id,run)=>{const b=node('button');b.type='button';b.id=id;b.onclick=run;return b;};
 const copy=()=>guideCopy[progress.language],local=value=>value[progress.language];
 const visible=e=>{
  if(!e||!e.isConnected||e.closest('[hidden]'))return false;
  if(e.checkVisibility&&!e.checkVisibility({checkVisibilityCSS:true,checkOpacity:true}))return false;
  for(let parent=e.parentElement;parent;parent=parent.parentElement){
   if(parent.tagName==='DETAILS'&&!parent.open&&!parent.querySelector(':scope > summary')?.contains(e))return false;
   if(parent.tagName==='DIALOG'&&!parent.open)return false;
  }
  return e.getClientRects().length>0;
 };
 const viewportRect=e=>{
  if(!visible(e))return null;
  const r=e.getBoundingClientRect();let left=Math.max(4,r.left),top=Math.max(4,r.top),right=Math.min(innerWidth-4,r.right),bottom=Math.min(innerHeight-4,r.bottom);
  for(let p=e.parentElement;p&&p!==document.body;p=p.parentElement){
   const style=getComputedStyle(p),b=p.getBoundingClientRect();
   if(/auto|scroll|hidden|clip/.test(style.overflowX)){left=Math.max(left,b.left);right=Math.min(right,b.right);}
   if(/auto|scroll|hidden|clip/.test(style.overflowY)){top=Math.max(top,b.top);bottom=Math.min(bottom,b.bottom);}
  }
  return right>left&&bottom>top?{left,top,width:right-left,height:bottom-top}:null;
 };
 const hub=node('dialog','guide-hub');hub.id='guide-dialog';hub.setAttribute('aria-labelledby','guide-hub-title');
 const header=node('div','dialog-heading'),heading=node('h2');heading.id='guide-hub-title';
 const close=button('guide-close',()=>hub.close());header.append(heading,close);
 const intro=node('p','guide-intro'),languageLabel=node('label','guide-language'),language=node('select');language.id='guide-language';
 language.add(new Option('English','en'));language.add(new Option('Русский','ru'));
 const languageText=node('span');languageLabel.append(languageText,language);
 const tasks=node('div','guide-task-list');tasks.id='guide-task-list';
 const note=node('p','hint'),reference=button('guide-reference',()=>{hub.close();$('help-dialog').showModal();});
 const finished=node('p','guide-finished');finished.setAttribute('role','status');finished.hidden=true;
 hub.append(header,intro,languageLabel,finished,tasks,reference,note);document.body.append(hub);

 const overlay=node('div','guide-overlay');overlay.id='guide-overlay';overlay.setAttribute('popover','manual');overlay.hidden=true;
 const ring=node('div','guide-target-ring');ring.id='guide-target-ring';ring.hidden=true;ring.setAttribute('aria-hidden','true');
 const card=node('section','guide-card');card.id='guide-card';card.setAttribute('role','region');card.setAttribute('aria-labelledby','guide-step-title');
 const route=node('p','guide-route'),stepCount=node('p','guide-count'),title=node('h2');title.id='guide-step-title';title.tabIndex=-1;
 const body=node('p','guide-body'),status=node('p','guide-status');status.id='guide-step-status';status.setAttribute('role','status');status.setAttribute('aria-live','polite');
 const reveal=button('guide-show',event=>{event.stopPropagation();showControls();}),back=button('guide-back',()=>move(-1)),next=button('guide-next',()=>move(1));
 const pause=button('guide-pause',()=>stop()),all=button('guide-tasks',()=>open());
 const actions=node('div','guide-actions'),navigation=node('div','guide-navigation');next.className='primary';navigation.append(back,next);actions.append(all,pause);
 card.append(route,stepCount,title,body,status,reveal,navigation,actions);overlay.append(ring,card);document.body.append(overlay);
 let invitation=null,frame=0,seen=false,notice='',lastTarget=null,returnFocus=null,destroyed=false;
 let modalOrder=[...document.querySelectorAll('dialog:modal')];
 const topModal=()=>modalOrder.filter(e=>e.isConnected&&e.matches(':modal')).at(-1)??null;
 const currentTour=()=>guideTours.find(t=>t.id===progress.active);
 const currentStep=()=>currentTour()?.steps[progress.step];
 const editorBusy=()=>isBusy()||Boolean($('operation-dialog')?.open)||Boolean($('gs-dialog')?.open&&$('gs-cancel')&&!$('gs-cancel').disabled);
 const allowedTarget=e=>e&&(!topModal()||topModal().contains(e));
 const setText=(e,text)=>{if(e.textContent!==text)e.textContent=text;};
 const focusTitle=()=>title.focus({preventScroll:true});
 const setStyle=(e,key,value)=>{if(e.style[key]!==value)e.style[key]=value;};
 const focusBack=()=>{
  if(visible(returnFocus)&&allowedTarget(returnFocus))returnFocus.focus({preventScroll:true});
  else{const e=topModal()?.querySelector('button')??$('map-canvas');e?.focus({preventScroll:true});}
 };

 function renderHub(){
  const c=copy();hub.lang=progress.language;heading.textContent=c.title;intro.textContent=c.intro;close.textContent='×';close.setAttribute('aria-label',c.close);
  languageText.textContent=c.language;language.value=progress.language;reference.textContent=c.reference;note.textContent=c.storage;finished.textContent=c.done;
  tasks.replaceChildren();
  for(const tour of guideTours){
   const row=node('section','guide-task'),name=node('h3','',local(tour.title)),description=node('p','',local(tour.description)),state=node('p','guide-task-state');
   const visited=progress.visited(tour.id),done=progress.completed(tour.id);
   state.textContent=done?c.complete:visited?`${c.step} ${progress.index(tour.id)+1} / ${tour.steps.length}`:`${tour.steps.length} ${progress.language==='ru'?'шагов':'steps'}`;
   const start=button('guide-start-'+tour.id,()=>startTour(tour.id,done));start.textContent=done?c.restart:visited?c.resume:c.start;
   const controls=node('div','guide-task-controls');controls.append(start);
   if(visited&&!done){const restart=button('guide-restart-'+tour.id,()=>startTour(tour.id,true));restart.textContent=c.restart;controls.append(restart);}
   row.append(name,description,state,controls);tasks.append(row);
  }
 }
 language.onchange=()=>{progress.setLanguage(language.value);renderHub();};
 hub.addEventListener('keydown',event=>event.stopPropagation());
 hub.addEventListener('close',()=>{if(!progress.active)focusBack();});

 function renderCard(){
  const tour=currentTour(),step=currentStep();if(!step)return;
  const c=copy();overlay.lang=progress.language;
  route.textContent=local(tour.title);stepCount.textContent=`${c.step} ${progress.step+1} / ${tour.steps.length}`;
  title.textContent=local(step.title);body.textContent=local(step.body);
  back.textContent=c.back;back.disabled=progress.step===0;next.textContent=progress.step===tour.steps.length-1?c.finish:c.next;
  reveal.textContent=c.show;pause.textContent=c.pause;all.textContent=c.tasks;
  overlay.dataset.tour=tour.id;overlay.dataset.step=step.id;
 }
 function hideOverlay(){
  if(overlay.matches(':popover-open'))overlay.hidePopover();overlay.hidden=true;ring.hidden=true;lastTarget=null;
 }
 function stop(restoreFocus=true){progress.pause();hideOverlay();if(restoreFocus)focusBack();}
 function open(){
  returnFocus=document.activeElement===document.body?returnFocus:document.activeElement;
  if(progress.active)stop(false);
  invitation?.remove();invitation=null;progress.dismiss();finished.hidden=true;renderHub();
  if(!hub.open)hub.showModal();language.focus();
 }
 function startTour(id,restart=false){
  progress.start(id,restart);hub.close();invitation?.remove();invitation=null;seen=false;notice='';syncModals();renderCard();refresh();focusTitle();
 }
 function move(offset){
  if(!currentStep())return;
  if(offset>0&&progress.step===currentTour().steps.length-1){progress.finish();hideOverlay();renderHub();finished.hidden=false;hub.showModal();heading.tabIndex=-1;heading.focus();return;}
  progress.move(offset);seen=false;notice='';renderCard();refresh();focusTitle();
 }
 function schedule(){if(!frame&&progress.active)frame=requestAnimationFrame(()=>{frame=0;refresh();});}
 function refresh(){
  const step=currentStep();if(!step||destroyed)return;
  const host=topModal()??document.body;
  if(overlay.parentElement!==host){if(overlay.matches(':popover-open'))overlay.hidePopover();host.append(overlay);}
  overlay.hidden=false;if(!overlay.matches(':popover-open'))overlay.showPopover();
  const target=document.querySelector(step.target),rect=allowedTarget(target)?viewportRect(target):null;
  lastTarget=rect?target:null;ring.hidden=!rect;
  if(rect)for(const key of ['left','top','width','height'])setStyle(ring,key,`${rect[key]}px`);
  const busy=editorBusy();
  // Modal editors may lock all descendant buttons while a request is pending.
  // These controls belong to the guide, and must keep their own availability.
  for(const [control,disabled] of [[back,progress.step===0],[next,false],[pause,false],[all,false],[reveal,busy]])
   if(control.disabled!==disabled)control.disabled=disabled;
  const c=copy();setText(status,busy?c.busy:notice?notice:rect?(seen?c.seen:c.ready):`${c.missing} ${local(step.hint)}`);
  overlay.dataset.targetVisible=String(Boolean(rect));
  const cardRect=card.getBoundingClientRect(),position=placeGuideCard(rect,{width:cardRect.width,height:cardRect.height},{width:innerWidth,height:innerHeight});
  setStyle(card,'left',`${position.left}px`);setStyle(card,'top',`${position.top}px`);
 }

 /** Fixed allowlist of UI-only reveals; never click an import/apply/export/save. */
 function revealContext(context){
  const workspace=workspaceForGuideContext(context);if(workspace)document.dispatchEvent(new CustomEvent('workspace-show',{detail:workspace}));
  if(context==='layers')document.getElementById('workspace-layers-toggle')?.click();
  const modal=topModal(),main=()=>!modal;
  if(context==='check')return main();
  if(context.startsWith('menu-')){
   if(!main())return false;
   const menu=document.querySelector(`[data-command-menu="${context.slice(5)}"]`);if(!menu)return false;
   menu.open=true;return true;
  }
  if(context==='overview'||context==='layers'||context==='jobs'){
   if(!main())return false;
   if(context==='layers'){const box=document.querySelector('.map-layers-workbench');if(box)box.open=true;}
   if(context==='jobs'){const jobs=$('export-jobs');if(!jobs||jobs.hidden)return false;jobs.classList.remove('collapsed');const toggle=jobs.querySelector('.export-jobs-toggle');if(toggle){toggle.textContent='Minimize';toggle.setAttribute('aria-expanded','true');}}
   return true;
  }
  const routes={new:['new-dialog','new-map'],import:['source-workbench','open-import'],world:['world-dialog','open-world'],workspaces:['world-dialog','open-world'],studio:['gs-dialog','open-generation-studio']};
  if(context in routes){
   const [dialog,command]=routes[context];
   if(modal&&modal.id!==dialog)return false;
   if(!$(dialog)?.open)$(command)?.click();
   if(context==='import')$('rp-place-tab')?.click();
   if(context==='workspaces'&&$('world-workspaces'))$('world-workspaces').open=true;
   return true;
  }
  if(context==='world-import'){
   if(modal&&!['world-dialog','osm-world-dialog'].includes(modal.id))return false;
   if(!$('osm-world-dialog')?.open)$('world-import-osm')?.click();return true;
  }
  if(context==='library'){
   if(modal&&modal.id!=='building-list-dialog')return false;
   if(!$('building-list-dialog').open)openLibrary();return true;
  }
  if(context==='workshop')return modal?.id==='building-dialog';
  return false;
 }
 function showControls(){
  if(editorBusy())return;
  notice='';const step=currentStep();if(!step)return;
  if(!revealContext(step.context))notice=['workshop','jobs'].includes(step.context)?local(step.hint):topModal()?copy().blocked:copy().unavailable;
  // Opening a native modal is synchronous; update its top-layer host before focus.
  syncModals();
  const target=document.querySelector(step.target);
  if(allowedTarget(target))for(let ancestor=target?.parentElement;ancestor;ancestor=ancestor.parentElement)if(ancestor.tagName==='DETAILS')ancestor.open=true;
  if(allowedTarget(target)&&visible(target)){target.scrollIntoView({block:'nearest',inline:'nearest'});if(target.matches('button,input,select,summary,canvas'))target.focus({preventScroll:true});}
  refresh();
 }
 function syncModals(records=[]){
  modalOrder=modalOrder.filter(e=>e.isConnected&&e.matches(':modal'));
  for(const e of document.querySelectorAll('dialog:modal'))if(!modalOrder.includes(e))modalOrder.push(e);
  for(const record of records)if(record.type==='attributes'&&record.attributeName==='open'&&record.target.matches('dialog:modal')){
   modalOrder=modalOrder.filter(e=>e!==record.target);modalOrder.push(record.target);
  }
 }
 const observer=new MutationObserver(records=>{
  const guideLockChanged=records.some(r=>r.attributeName==='disabled'&&overlay.contains(r.target));
  const external=records.filter(r=>!overlay.contains(r.target)&&r.target!==overlay&&!hub.contains(r.target)&&!invitation?.contains(r.target));
  if(!progress.active)return;
  if(external.length)syncModals(external);
  if(external.length||guideLockChanged)schedule();
 });
 observer.observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['open','hidden','class','style','disabled']});
 const interact=event=>{
  if(progress.active&&!overlay.contains(event.target)&&lastTarget?.contains(event.target)){seen=true;notice='';schedule();}
 };
 for(const name of ['click','input','change'])document.addEventListener(name,interact,true);
 const keydown=event=>{
  // Window capture precedes the editor's existing document-capture shortcuts.
  // Stopping propagation preserves native button activation without editing a draft.
  if(hub.contains(event.target)||invitation?.contains(event.target)){event.stopImmediatePropagation();return;}
  if(!progress.active)return;
  if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();stop();}
  else if(overlay.contains(event.target))event.stopImmediatePropagation();
 };
 window.addEventListener('keydown',keydown,true);
 window.addEventListener('resize',schedule);document.addEventListener('scroll',schedule,true);
 const sizeObserver=new ResizeObserver(schedule);sizeObserver.observe(card);
 $('open-help').textContent='Interactive guide and help';$('open-help').onclick=open;

 function welcome(){
  if(destroyed||progress.dismissed||progress.active||hub.open||topModal()||isBusy())return;
  invitation=node('aside','guide-invitation');invitation.id='guide-invitation';invitation.setAttribute('aria-label',copy().title);invitation.lang=progress.language;
  const name=node('h2','',copy().welcome),body=node('p','',copy().welcomeBody),actions=node('div','guide-actions');
  const start=button('guide-welcome-open',open),later=button('guide-welcome-later',()=>{progress.dismiss();invitation.remove();invitation=null;});start.textContent=copy().open;later.textContent=copy().later;
  actions.append(start,later);invitation.append(name,body,actions);document.body.append(invitation);
 }
 return {open,welcome,dispose(){
  destroyed=true;stop(false);observer.disconnect();sizeObserver.disconnect();cancelAnimationFrame(frame);
  window.removeEventListener('keydown',keydown,true);document.removeEventListener('scroll',schedule,true);window.removeEventListener('resize',schedule);
  for(const name of ['click','input','change'])document.removeEventListener(name,interact,true);
  invitation?.remove();hub.remove();overlay.remove();
 }};
}
