using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Retains small portable results; large worlds use independently saved sections.</summary>
internal static class OsmWorldResultWriter
{
 internal sealed record Result(string File,string? WorkspaceId,long? WorkspaceRevision=null);
 internal static async Task<Result> SaveAsync(string directory,string id,WorldProject world,WorldStore? store,CancellationToken token,long maximumBytes=WorldStore.MaxBytes,bool preferSections=false)
 {
  if(!WorldAssembler.SafeId(id)||maximumBytes is <1 or >WorldStore.MaxBytes)throw new ArgumentException("Invalid world result budget or identity.");
  token.ThrowIfCancellationRequested();var layout=WorldAssembler.Validate(world);var requiresSections=world.Maps.Count>WorldLayoutLimits.NormalSections||layout.Cells>WorldLayoutLimits.NormalImportCells;Directory.CreateDirectory(directory);
  if(requiresSections&&store is null)throw new ArgumentException("Expanded world results require a disk section workspace store.");
  if((preferSections||requiresSections)&&store is not null){var published=await store.CreateImportWorkspaceAsync(id,world,token);return new(published.File,id,published.Revision);}
  var target=Path.Combine(directory,id+".pzworld.json");var temporary=target+".tmp";
  try{
   try{
    await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)){
     using var bounded=new PortableOutput(file,maximumBytes);
     await JsonSerializer.SerializeAsync(bounded,world,NativeMapExporter.JsonOptions,token);
    }
    token.ThrowIfCancellationRequested();File.Move(temporary,target,true);return new(target,null);
   }catch(PortableBudgetExceeded) when(store is not null){
    // Only the aggregate portable-file budget triggers fallback. Invalid data, IO
    // errors and cancellation must never be turned into a successful import.
    File.Delete(temporary);token.ThrowIfCancellationRequested();
    var published=await store.CreateImportWorkspaceAsync(id,world,token);
    return new(published.File,id,published.Revision);
   }
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 private sealed class PortableBudgetExceeded():ArgumentException("The complete-world JSON exceeds 128 MB. Save and open this world by section.");
 private sealed class PortableOutput(Stream inner,long maximum):Stream
 {
  long count;
  void Check(int length){if(length>maximum-count)throw new PortableBudgetExceeded();count+=length;}
  public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
  public override long Length=>count;public override long Position{get=>count;set=>throw new NotSupportedException();}
  public override void Write(byte[] buffer,int offset,int length){Check(length);inner.Write(buffer,offset,length);}
  public override void Write(ReadOnlySpan<byte> buffer){Check(buffer.Length);inner.Write(buffer);}
  public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){Check(buffer.Length);return inner.WriteAsync(buffer,token);}
  public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken token)=>inner.FlushAsync(token);
  public override int Read(byte[] buffer,int offset,int length)=>throw new NotSupportedException();
  public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
 }
}
