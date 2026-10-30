// Historical pre-load-all module for the warm browser cache regression.
// One editable raster and a small manifest. Unloaded sections never become fabricated blank maps.
export class StreamedSectionSession{
 constructor({request,getProject,applySnapshot,validateManifest=m=>m}){Object.assign(this,{request,getProject,applySnapshot,validateManifest});this.workspaceId=null;this.manifest=null;this.activeId=null;this.shared=null;this.sections=[];this.infrastructure=null;this.dirty=false;this.mutations=0;this.epoch=0;this.loading=false;this.saving=null;this.applying=null;this.abort=null;}
 get active(){return !!(this.workspaceId&&this.activeId);}
 markChanged(){if(this.loading||!this.active)return;this.mutations++;this.dirty=JSON.stringify(this.getProject())!==this.savedSnapshot;}
 context(){return this.active?{id:this.workspaceId,memberId:this.activeId,revision:this.manifest.revision,shared:this.shared,sections:this.sections,infrastructure:this.infrastructure}:null;}
 async save(signal,{force=false}={}){
  if(this.saving)await this.saving;if(!this.active||!this.dirty&&!force)return this.manifest;
  const id=this.workspaceId,member=this.activeId,revision=this.manifest.revision,project=structuredClone(this.getProject());
  this.saving=(async()=>{const m=this.validateManifest(await this.request(`/api/world/workspaces/${id}/sections/${member}`,{project,expectedRevision:revision},signal));if(this.workspaceId!==id||this.activeId!==member)throw Error('The active workspace changed during saving. Refresh its section index.');if(m.id!==this.manifest.id||!Number.isSafeInteger(m.revision)||m.revision<=revision||!Array.isArray(m.sections)||!m.sections.some(s=>s.id===member))throw Error('The section save returned an invalid workspace revision.');this.manifest=m;this.sections=m.sections;this.savedSnapshot=JSON.stringify(project);this.dirty=JSON.stringify(this.getProject())!==this.savedSnapshot;return m;})();
  try{return await this.saving;}finally{this.saving=null;}
 }
 async open(id,manifest,memberId,signal){
  const epoch=++this.epoch;this.abort?.abort();const abort=new AbortController();this.abort=abort;const forward=()=>abort.abort();signal?.addEventListener('abort',forward,{once:true});
  try{
   if(this.applying)await this.applying.catch(()=>{});
   if(signal?.aborted)throw new DOMException('Operation cancelled','AbortError');
   if(epoch!==this.epoch||abort.signal.aborted)return null;
   if(this.active&&this.dirty)await this.save(abort.signal);
   if(epoch!==this.epoch||abort.signal.aborted)return null;
   const expected=id===this.workspaceId?this.manifest:manifest,previousProject=JSON.stringify(this.getProject());
   const snapshot=await this.request(`/api/world/workspaces/${id}/sections/${memberId}`,null,abort.signal);
   if(epoch!==this.epoch||abort.signal.aborted)return null;
   if(JSON.stringify(this.getProject())!==previousProject)throw Error('The current section was edited while loading. Open the next section again to save these edits first.');
   if(snapshot.member?.id!==memberId||snapshot.revision!==expected.revision)throw Error('The workspace changed. Refresh the section index before opening it. Your current edits remain.');
   this.loading=true;const before={workspaceId:this.workspaceId,manifest:this.manifest,activeId:this.activeId,shared:this.shared,sections:this.sections,infrastructure:this.infrastructure,dirty:this.dirty,savedSnapshot:this.savedSnapshot};
   const assertCurrent=()=>{if(epoch!==this.epoch||abort.signal.aborted)throw new DOMException('Operation cancelled','AbortError');if(JSON.stringify(this.getProject())!==previousProject)throw Error('The current section was edited while loading. Open the next section again to save these edits first.');};
   this.applying=(async()=>{try{this.workspaceId=id;this.manifest=expected;this.activeId=memberId;this.shared=snapshot.shared;this.sections=snapshot.sections??expected.sections;this.infrastructure=snapshot.sharedInfrastructure??null;await this.applySnapshot(snapshot,{assertCurrent,signal:abort.signal});this.savedSnapshot=JSON.stringify(this.getProject());this.dirty=false;this.mutations++;return snapshot;}catch(e){Object.assign(this,before);if(this.active)this.dirty=JSON.stringify(this.getProject())!==this.savedSnapshot;throw e;}finally{this.loading=false;}})();
   try{return await this.applying;}finally{this.applying=null;}
  }finally{signal?.removeEventListener('abort',forward);if(this.abort===abort)this.abort=null;}
 }
 detach(){if(this.dirty)throw Error('Save the active section before opening another project.');this.abort?.abort();this.epoch++;this.workspaceId=null;this.activeId=null;this.shared=null;this.manifest=null;this.sections=[];this.infrastructure=null;}
}
