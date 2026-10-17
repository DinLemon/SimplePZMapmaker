using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;
/// <summary>Builds native archives into bounded workspace files; HTTP owns and deletes each completed file.</summary>
public sealed class NativeExportService(string workspace,ExportJobStore? jobs=null)
{
 readonly NativeSectionCache cache=jobs?.Cache??new(Path.Combine(workspace,"work","native-section-cache"));
 readonly SemaphoreSlim gate=new(2,2);
 public async Task<IResult> World(WorldProject world,CancellationToken token,NativeModOptions? package=null)=>await Export(s=>NativeWorldExporter.ExportWithProgress(world,s,new(cache),token),"SimplePZWorld.zip",token,package);
 public async Task<IResult> Map(MapProject p,CancellationToken token,NativeModOptions? package=null)=>await Export(s=>NativeMapExporter.ExportWithProgress(p,s,new(cache),token),"SimplePZMap.zip",token,package);
 public async Task<IResult> Section(WorldProject world,string id,CancellationToken token)=>await Export(s=>NativeWorldExporter.ExportSectionWithProgress(world,id,s,new(cache),token),"Section-"+id+".zip",token);
 public async Task<IResult> Workspace(WorldSectionArchive.RevisionLease lease,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token,string? memberId=null,NativeModOptions? package=null){
  using(lease){var metadata=await lease.ReadMetadataAsync(token);var source=new WorkspaceExportSource(lease,metadata,assets,models);return await ExportAsync(s=>NativeStreamedWorldExporter.ExportAsync(source,s,new(cache),token,memberId),memberId is null?"SimplePZWorld.zip":"SimplePZSection.zip",token,package);}
 }
 /// <summary>Mod naming, layout and install options from the query: modId, mapFolder, modName, commonMaps=true, install=true.</summary>
 public static NativeModOptions? Options(HttpRequest request){
  string? Text(string key)=>request.Query.TryGetValue(key,out var v)&&!string.IsNullOrWhiteSpace(v.ToString())?v.ToString().Trim():null;bool Flag(string key)=>Text(key) is "true" or "1";
  var o=new NativeModOptions(Text("modId"),Text("mapFolder"),Text("modName"),Flag("commonMaps"),Flag("install"));if(!o.Renames&&!o.Install)return null;NativeModPackage.Validate(o);return o;
 }
 /// <summary>Applies naming/layout to a finished archive, then streams it or installs it into the mods folder. Takes ownership of the archive.</summary>
 public IResult Package(Stream archive,string name,NativeModOptions? package){
  if(package is null)return Results.Stream(archive,"application/zip",name);
  var result=archive;
  if(package.Renames){var folder=Path.Combine(workspace,"work","native-exports");Directory.CreateDirectory(folder);var repacked=new FileStream(Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.DeleteOnClose);try{NativeModPackage.Repack(archive,repacked,package);}catch{repacked.Dispose();throw;}archive.Dispose();repacked.Position=0;result=repacked;}
  if(!package.Install)return Results.Stream(result,"application/zip",name);
  using(result){var installed=NativeModPackage.Install(result,ModsDirectory);return Results.Ok(new{installed,modsDirectory=ModsDirectory,note="Enable the mod and create a NEW game, or set Mods= and Map= on a server. A later install of the same mod id replaces this folder."});}
 }
 /// <summary>Zomboid/mods of the current user, or PZMAP_MODS_DIR.</summary>
 public string ModsDirectory {get;init;}=NativeModPackage.DefaultModsDirectory();
 async Task<IResult> ExportAsync(Func<Stream,Task> write,string name,CancellationToken token,NativeModOptions? package=null){
  if(!await gate.WaitAsync(0,token))throw new ArgumentException("Two native exports are already running. Wait or cancel an export.");FileStream? stream=null;
  try{var folder=Path.Combine(workspace,"work","native-exports");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip");stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.DeleteOnClose);using(var bounded=new BoundedArchiveStream(stream,256L*1024*1024))await write(bounded);token.ThrowIfCancellationRequested();stream.Position=0;var archive=stream;stream=null;try{return Package(archive,name,package);}catch{archive.Dispose();throw;}}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException){throw new ArgumentException("The native export could not write its workspace file. Check free disk space and folder permissions, then retry.");}
  finally{stream?.Dispose();gate.Release();}
 }
 Task<IResult> Export(Action<Stream> write,string name,CancellationToken token,NativeModOptions? package=null)=>ExportAsync(stream=>{write(stream);return Task.CompletedTask;},name,token,package);
}
