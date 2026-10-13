using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed class WorldStore(string directory):IDisposable {
 public const int MaxBytes=128*1024*1024;
 readonly SemaphoreSlim gate=new(1,1);
 string FileName(string id){if(!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid world autosave ID.");return Path.Combine(directory,id+".pzworld.json");}
 public async Task SaveAsync(string id,WorldProject world,CancellationToken token=default){var file=FileName(id);WorldAssembler.Validate(world);await gate.WaitAsync(token);try{await RecoveryArchive.Write(file,world,MaxBytes,token);}finally{gate.Release();}}
 public async Task<WorldProject?> LoadAsync(string id,CancellationToken token=default,int? version=null){var file=FileName(id);await gate.WaitAsync(token);try{return await RecoveryArchive.Read<WorldProject>(file,MaxBytes,w=>WorldAssembler.Validate(w),token,version);}finally{gate.Release();}}
 public RecoveryArchive.VersionInfo[] Versions(string id)=>RecoveryArchive.Versions(FileName(id));
 public async Task ClearAsync(string id,CancellationToken token=default){var file=FileName(id);await gate.WaitAsync(token);try{RecoveryArchive.Clear(file);}finally{gate.Release();}}
 public void Dispose()=>gate.Dispose();
}
