using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Web.Services;

public static class OsmMapImport
{
 public static async Task<ImportResult> Run(OsmImportRequest request,OsmClient client,CancellationToken token,IProgress<OsmProgress>? progress=null)
 {
  ProjectValidator.Validate(request.Project);ProjectValidator.ValidateBounds(request.Bounds);
  var download=await client.FetchForImport(request.Bounds,request.UseRegionalExtract?"regional":request.SourceMode,request.SourceId,token,progress);
  progress?.Report(new("compose",0,1,"Local map"));var result=GeographicImporter.Import(download.Data,request.Project,request.Bounds,token,request.PreserveEdits,regionalProfile:request.RegionalProfile);result.Warnings.AddRange(download.Warnings);
  if(result.Project.ImportSummary is {} summary){summary.Notes.InsertRange(0,download.Warnings);if(summary.Notes.Count>20)summary.Notes.RemoveRange(20,summary.Notes.Count-20);}
  token.ThrowIfCancellationRequested();return result with{SourceId=download.SourceId};
 }
 public static IEndpointRouteBuilder MapOsmImportStreaming(this IEndpointRouteBuilder app,string workspace)
 {
  app.MapPost("/api/import/osm/stream",async(HttpContext context,OsmImportRequest request,OsmClient client)=>{
   var token=context.RequestAborted;ProjectValidator.Validate(request.Project);ProjectValidator.ValidateBounds(request.Bounds);
   context.Response.ContentType="application/x-ndjson; charset=utf-8";context.Response.Headers.CacheControl="no-store";context.Response.Headers["X-Accel-Buffering"]="no";
   string? temporary=null;
   async Task Line(object value){await context.Response.WriteAsync(JsonSerializer.Serialize(value,NativeMapExporter.JsonOptions)+"\n",token);await context.Response.Body.FlushAsync(token);}
   var progress=new StreamProgress(line=>{context.Response.WriteAsync(line,token).GetAwaiter().GetResult();context.Response.Body.FlushAsync(token).GetAwaiter().GetResult();});
   try{
    var result=await Run(request,client,token,progress);
    // Stage the completed bounded JSON before emitting a result frame. A size/cancellation failure
    // must not leave an apparently successful partial project in the response.
    var directory=Path.Combine(workspace,"work","osm-map-results");Directory.CreateDirectory(directory);temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json.tmp");
    await using(var file=File.Create(temporary)){await using var bounded=new BoundedResult(file,ProjectValidator.MaximumProjectBytes+64000);await JsonSerializer.SerializeAsync(bounded,result,NativeMapExporter.JsonOptions,token);}
    token.ThrowIfCancellationRequested();await context.Response.WriteAsync("{\"kind\":\"result\",\"result\":",token);await using(var file=File.OpenRead(temporary))await file.CopyToAsync(context.Response.Body,token);await context.Response.WriteAsync("}\n",token);await context.Response.Body.FlushAsync(token);
   }catch(OperationCanceledException) when(token.IsCancellationRequested){/* Aborting the request also stops download/index/compose. No result is emitted. */}
   catch(Exception error) when(error is ArgumentException or HttpRequestException or IOException or JsonException or InvalidOperationException){if(!token.IsCancellationRequested)await Line(new{kind="error",error=error is IOException?"Could not write the geographic source or import result. Check workspace disk space and access; the current map was preserved.":error.Message});}
   finally{if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);}
  });return app;
 }
 internal sealed class StreamProgress(Action<string> write,Func<DateTimeOffset>? clock=null):IProgress<OsmProgress>
 {
  private DateTimeOffset last=DateTimeOffset.MinValue;private string? key;private int bytes;
  internal const int MaximumProgressBytes=512*1024;
  public void Report(OsmProgress value){
   var now=clock?.Invoke()??DateTimeOffset.UtcNow;var nextKey=value.Phase+"\n"+value.Source;
   if(nextKey==key&&now-last<TimeSpan.FromSeconds(2))return;key=nextKey;last=now;
   var line=JsonSerializer.Serialize(new{kind="progress",progress=value},NativeMapExporter.JsonOptions)+"\n";
   var length=System.Text.Encoding.UTF8.GetByteCount(line);
   // Keep even a full-hour download and two PBF scans within the browser's 1 MiB envelope.
   // Empty heartbeats preserve cancellation/connection checks after the reporting budget is used.
   if(bytes+length<=MaximumProgressBytes){bytes+=length;write(line);}else write("\n");
  }
 }
 private sealed class BoundedResult(Stream inner,long maximum):Stream
 {
  private long count;public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>count;public override long Position{get=>count;set=>throw new NotSupportedException();}
  private void Check(int length){if(count+length>maximum)throw new ArgumentException("The completed map exceeds the 128 MB project budget. Reduce the area or use a world with smaller sections.");count+=length;}
  public override void Write(byte[] buffer,int offset,int length){Check(length);inner.Write(buffer,offset,length);}public override void Write(ReadOnlySpan<byte> buffer){Check(buffer.Length);inner.Write(buffer);}public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){Check(buffer.Length);await inner.WriteAsync(buffer,token);}public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken token)=>inner.FlushAsync(token);public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
 }
}
