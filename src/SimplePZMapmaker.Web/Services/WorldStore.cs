using SimplePZMapmaker.Core;
using System.Text.Json;
namespace SimplePZMapmaker.Web.Services;
public sealed class WorldCompatibilityBudgetException():ArgumentException("The compatibility complete-world JSON exceeds 128 MB. Open or export this workspace by section.") {}
public sealed class WorldStore(string directory):IDisposable {
 public const int MaxBytes=128*1024*1024;
 readonly SemaphoreSlim gate=new(1,1);
 readonly WorldSectionArchive sections=new(directory);
 public async Task<WorldSectionArchive.ImportWriter> BeginImportAsync(string id,CancellationToken token=default){
  await gate.WaitAsync(token);try{return sections.BeginImport(id,gate,token);}finally{gate.Release();}
 }
 string FileName(string id){if(!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid world autosave ID.");return Path.Combine(directory,id+".pzworld.json");}
 public async Task SaveAsync(string id,WorldProject world,CancellationToken token=default){FileName(id);await gate.WaitAsync(token);try{await sections.Save(id,world,token);}finally{gate.Release();}}
 public async Task<WorldProject?> LoadAsync(string id,CancellationToken token=default,int? version=null){var file=FileName(id);await gate.WaitAsync(token);try{var found=false;foreach(var n in WorldSectionArchive.VersionsToTry(version)){try{var m=await sections.ReadManifest(id,n,token);if(m is null)continue;found=true;return await sections.ReadWorld(id,m,token);}catch(Exception e) when(version is null&&e is not WorldCompatibilityBudgetException&&(e is JsonException or ArgumentException or IOException)){found=true;}}if(found)throw new ArgumentException("All complete section revisions are damaged. Open a saved world file.");return await RecoveryArchive.Read<WorldProject>(file,MaxBytes,w=>WorldAssembler.Validate(w),token,version);}finally{gate.Release();}}
 public RecoveryArchive.VersionInfo[] Versions(string id){var a=RecoveryArchive.Versions(sections.FileName(id));return a.Length>0?a:RecoveryArchive.Versions(FileName(id));}
 public async Task<WorldSectionArchive.Manifest?> ManifestAsync(string id,CancellationToken token=default,int version=0){await gate.WaitAsync(token);try{return await sections.ReadManifest(id,version,token);}finally{gate.Release();}}
 public async Task<WorldSectionArchive.Manifest> SaveWorkspaceAsync(string id,WorldProject w,long? revision,CancellationToken token=default){await gate.WaitAsync(token);try{var m=await sections.ReadManifest(id,0,token);if(m is not null&&revision!=m.Revision)throw new AgentRevisionConflict(m.Revision);if(m is null&&revision is not(null or 0))throw new AgentRevisionConflict(0);await sections.Save(id,w,token);return (await sections.ReadManifest(id,0,token))!;}finally{gate.Release();}}
 // A new import owns its identity. Publishing the archive index is its commit
 // point: no cancellable IO follows it, and failed creation removes owned data.
 internal async Task<(string File,long Revision)> CreateImportWorkspaceAsync(string id,WorldProject world,CancellationToken token){
  await gate.WaitAsync(token);try{
   if(await sections.ReadManifest(id,0,token) is {} existing)throw new AgentRevisionConflict(existing.Revision);
   try{var revision=await sections.Save(id,world,token);return(sections.FileName(id),revision);}
   catch{sections.Clear(id);throw;}
  }finally{gate.Release();}
 }
 public async Task<WorldSectionArchive.Manifest[]> ListAsync(CancellationToken token=default){await gate.WaitAsync(token);try{if(!Directory.Exists(directory))return [];var result=new List<WorldSectionArchive.Manifest>();foreach(var f in Directory.EnumerateFiles(directory,"*.pzworld.index.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(64)){try{var id=Path.GetFileName(f)[..32];var m=await sections.ReadManifest(id,0,token);if(m is not null){m.StorageId=id;result.Add(m);}}catch(Exception e) when(e is ArgumentException or JsonException or IOException){}}return result.OrderByDescending(m=>m.Updated).ToArray();}finally{gate.Release();}}
 public async Task<WorldSectionArchive.SectionSnapshot> LoadSectionAsync(string id,string memberId,CancellationToken token=default,int version=0){await gate.WaitAsync(token);try{var m=await sections.ReadManifest(id,version,token)??throw new ArgumentException("Save this world as a section workspace first.");return await sections.ReadSection(id,m,memberId,token);}finally{gate.Release();}}
 public async Task<WorldSectionArchive.Manifest> SaveSectionAsync(string id,string memberId,long revision,MapProject p,CancellationToken token=default){await gate.WaitAsync(token);try{return await sections.SaveSection(id,memberId,revision,p,token);}finally{gate.Release();}}
 public async Task<WorldSectionArchive.Manifest> AppendSectionAsync(string id,string memberId,long revision,MapProject p,CancellationToken token=default){await gate.WaitAsync(token);try{return await sections.AppendSection(id,memberId,revision,p,token);}finally{gate.Release();}}
 public async Task<WorldSectionArchive.RevisionLease> PinAsync(string id,CancellationToken token=default,int version=0,long? expectedRevision=null){if(expectedRevision is <1)throw new ArgumentException("Expected workspace revision must be positive.");await gate.WaitAsync(token);try{var manifest=await sections.ReadManifest(id,version,token)??throw new ArgumentException("Saved world not found.");if(expectedRevision is {} expected&&manifest.Revision!=expected)throw new AgentRevisionConflict(manifest.Revision);return sections.Pin(id,manifest);}finally{gate.Release();}}
 public async Task ValidateSectionAsync(string id,string memberId,long revision,MapProject project,CancellationToken token=default){await gate.WaitAsync(token);try{await sections.ValidateSection(id,memberId,revision,project,token);}finally{gate.Release();}}
 public async Task ClearAsync(string id,CancellationToken token=default){var file=FileName(id);await gate.WaitAsync(token);try{RecoveryArchive.Clear(file);sections.Clear(id);}finally{gate.Release();}}
 public void Dispose()=>gate.Dispose();
}
