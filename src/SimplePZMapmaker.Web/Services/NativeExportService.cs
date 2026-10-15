using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;
/// <summary>Builds native archives into bounded workspace files; HTTP owns and deletes each completed file.</summary>
public sealed class NativeExportService(string workspace,ExportJobStore? jobs=null)
{
 readonly NativeSectionCache cache=jobs?.Cache??new(Path.Combine(workspace,"work","native-section-cache"));
 readonly SemaphoreSlim gate=new(2,2);
 public async Task<IResult> World(WorldProject world,CancellationToken token)=>await Export(s=>NativeWorldExporter.ExportWithProgress(world,s,new(cache),token),"SimplePZWorld.zip",token);
 public async Task<IResult> Map(MapProject p,CancellationToken token)=>await Export(s=>NativeMapExporter.ExportWithProgress(p,s,new(cache),token),"SimplePZMap.zip",token);
 public async Task<IResult> Section(WorldProject world,string id,CancellationToken token)=>await Export(s=>NativeWorldExporter.ExportSectionWithProgress(world,id,s,new(cache),token),"Section-"+id+".zip",token);
 async Task<IResult> Export(Action<Stream> write,string name,CancellationToken token){
  if(!await gate.WaitAsync(0,token))throw new ArgumentException("Two native exports are already running. Wait or cancel an export.");FileStream? stream=null;
  try{var folder=Path.Combine(workspace,"work","native-exports");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip");stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.DeleteOnClose);using(var bounded=new BoundedArchiveStream(stream,256L*1024*1024))write(bounded);token.ThrowIfCancellationRequested();stream.Position=0;var result=Results.Stream(stream,"application/zip",name);stream=null;return result;}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException){throw new ArgumentException("The native export could not write its workspace file. Check free disk space and folder permissions, then retry.");}
  finally{stream?.Dispose();gate.Release();}
 }
}
