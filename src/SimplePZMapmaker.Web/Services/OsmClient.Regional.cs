using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed partial class OsmClient
{
 // Independent, read-only bulk source. Its validated cache survives a failed refresh.
 public async Task<OsmDownload> FetchExtractDetailed(GeoBounds bounds,CancellationToken cancellationToken)
 {
  ProjectValidator.ValidateBounds(bounds);if(!CanUseExtract(bounds))throw new ArgumentException("Regional downloads currently support the Smolensk area (54.6–54.95 N; 31.8–32.3 E). Elsewhere choose online OSM or a local regional .osm.pbf file.");
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(ExtractTimeoutSeconds));var token=deadline.Token;
  var acquired=false;var file=Path.Combine(cacheDirectory,"region-smolensk-oblast.pbf");
  try{
   await gate.WaitAsync(token);acquired=true;Directory.CreateDirectory(cacheDirectory);
   if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7)){
    try{return Result(PbfExtractReader.Read(file,bounds,token),true,File.GetLastWriteTimeUtc(file));}
    catch(ArgumentException){/* A corrupt cache is retried through the independent provider, never promoted. */}
   }
   var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using var request=new HttpRequestMessage(HttpMethod.Get,"https://download.openstreetmap.fr/extracts/russia/central_federal_district/smolensk_oblast-latest.osm.pbf");request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.13.1 (local-map-editor)");
    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>OsmSourceFiles.MaximumPbfBytes)throw new ArgumentException("The regional extract exceeds the 64 MiB limit.");
    await using(var input=await response.Content.ReadAsStreamAsync(token)){await using var output=File.Create(temporary);var buffer=new byte[81920];long total=0;while(true){var read=await input.ReadAsync(buffer,token);if(read==0)break;total+=read;if(total>OsmSourceFiles.MaximumPbfBytes)throw new ArgumentException("The regional extract exceeds the 64 MiB limit.");await output.WriteAsync(buffer.AsMemory(0,read),token);}}
    var parsed=PbfExtractReader.Read(temporary,bounds,token);token.ThrowIfCancellationRequested();File.Move(temporary,file,true);return Result(parsed,false,DateTimeOffset.UtcNow);
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }catch(Exception error) when(error is HttpRequestException or IOException or OperationCanceledException or ArgumentException){
   cancellationToken.ThrowIfCancellationRequested();
   if(acquired&&File.Exists(file)){
    // The refresh deadline may be exhausted. Only the caller's token governs a bounded local read.
    try{var fallback=Result(PbfExtractReader.Read(file,bounds,cancellationToken),true,File.GetLastWriteTimeUtc(file));fallback.Warnings.Insert(0,$"The regional refresh failed. Used the last validated local extract, downloaded {fallback.RetrievedAtUtc:u}; data date: {fallback.DataTimestamp??"not specified"}. It may be out of date.");return fallback;}
    catch(ArgumentException){/* Keep damaged data out of the current project. */}
   }
   if(error is ArgumentException)throw;
   throw new HttpRequestException("The regional extract provider is unavailable or timed out. Choose a saved GeoJSON, Overpass JSON or regional .osm.pbf file. The current map was preserved.",error);
  }finally{if(acquired)gate.Release();}
  static OsmDownload Result(PbfExtractReader.Result result,bool cached,DateTimeOffset retrieved){
   var warnings=new List<string>{"Source: daily OSM France regional extract; data date: "+(result.Timestamp??"not specified")+". "+(cached?"Used the locally cached extract.":"The regional download was validated and saved locally.")};
   if(result.OmittedRelations>0)warnings.Add($"Incomplete or nested relation outlines skipped: {result.OmittedRelations} (including outside the selected area).");
   if(result.OmittedWays>0)warnings.Add($"Ways with missing nodes skipped: {result.OmittedWays} (including outside the selected area).");
   return new(result.Data,"download.openstreetmap.fr",warnings){Requests=cached?0:1,FromCache=cached,RetrievedAtUtc=retrieved,DataTimestamp=result.Timestamp,Sources=[new("download.openstreetmap.fr",result.Timestamp,retrieved,cached)]};
  }
 }
}
