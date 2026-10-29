using SimplePZMapmaker.Core;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;
public sealed class RecoveryStore(string directory):IDisposable {
 readonly SemaphoreSlim gate=new(1,1);
 string FileName(string id){if(id is null||!Regex.IsMatch(id,"^[a-f0-9]{32}$",RegexOptions.CultureInvariant))throw new ArgumentException("Invalid autosave ID.");return Path.Combine(directory,id+".pzmap.json");}
 public async Task SaveAsync(string id,MapProject project,CancellationToken token=default){var file=FileName(id);ProjectValidator.Validate(project);await gate.WaitAsync(token);try{await RecoveryArchive.Write(file,project,ProjectValidator.MaximumProjectBytes,token);}finally{gate.Release();}}
 public async Task<MapProject?> LoadAsync(string id,CancellationToken token=default,int? version=null){var file=FileName(id);await gate.WaitAsync(token);try{return await RecoveryArchive.Read<MapProject>(file,ProjectValidator.MaximumProjectBytes,ProjectValidator.Validate,token,version);}finally{gate.Release();}}
 public RecoveryArchive.VersionInfo[] Versions(string id)=>RecoveryArchive.Versions(FileName(id));
 public void Dispose()=>gate.Dispose();
}
