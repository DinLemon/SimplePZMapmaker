using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SimplePZMapmaker.Web.Services;

public sealed record RegionalFileMetadata(string Url,long Length,string? ETag,DateTimeOffset? Modified,DateTimeOffset RetrievedAtUtc,bool Complete=false);
public sealed class RegionalFileLease(string path,bool cached,RegionalFileMetadata metadata,Action commit,Action reject):IDisposable
{
 public string Path {get;}=path;public bool Cached {get;}=cached;public RegionalFileMetadata Metadata {get;}=metadata;private bool finished;
 public void Commit(){if(!finished){commit();finished=true;}}
 public void Reject(){if(!finished){reject();finished=true;}}
 public void Dispose(){/* An interrupted complete download remains resumable until validated or explicitly rejected. */}
}

public sealed class RegionalDownloadCache(string directory,HttpClient client,long maximumFileBytes=8L*1024*1024*1024,long maximumCacheBytes=16L*1024*1024*1024)
{
 private static readonly JsonSerializerOptions jsonOptions=new(){PropertyNameCaseInsensitive=true};
 public long MaximumFileBytes=>maximumFileBytes;
 private static void SafeRegion(GeographicRegion region){GeofabrikCatalog.ValidateUrl(region.Url);if(!GeofabrikCatalog.ValidId(region.Id))throw new ArgumentException("Invalid extract identifier.");}
 public static string CacheFileName(string id)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..32]+".osm.pbf";
 private string FileFor(GeographicRegion region)=>System.IO.Path.Combine(directory,CacheFileName(region.Id));
 public RegionalFileMetadata? Cached(GeographicRegion region){SafeRegion(region);var path=FileFor(region);var metadata=ReadMetadata(path+".json");return metadata is not null&&metadata.Length<=maximumFileBytes&&metadata.Url==region.Url&&File.Exists(path)&&new FileInfo(path).Length==metadata.Length?metadata:null;}
 public async Task<long?> SizeAsync(GeographicRegion region,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();var cached=Cached(region);if(cached is not null)return cached.Length;
  using var request=Request(HttpMethod.Head,region.Url);using var response=await Send(request,token);return response.IsSuccessStatusCode?response.Content.Headers.ContentLength:null;
 }
 public async Task<RegionalFileLease> AcquireAsync(GeographicRegion region,CancellationToken token,IProgress<OsmProgress>? progress=null,bool forceRefresh=false)
 {
  token.ThrowIfCancellationRequested();SafeRegion(region);Directory.CreateDirectory(directory);var target=FileFor(region);var cached=Cached(region);
  if(!forceRefresh&&cached is not null&&DateTimeOffset.UtcNow-cached.RetrievedAtUtc<TimeSpan.FromDays(7))return new(target,true,cached,()=>{},()=>Invalidate(target));
  var partial=target+".part";var metadataPath=partial+".json";var prior=ReadMetadata(metadataPath);var offset=File.Exists(partial)?new FileInfo(partial).Length:0;
  // Only the EOF/size-checked transfer marks Complete. A full pending source can
  // be retried locally after extraction limits, without pretending it is validated.
  if(prior is {Complete:true}&&prior.Url==region.Url&&offset==prior.Length&&offset<=maximumFileBytes&&DateTimeOffset.UtcNow-prior.RetrievedAtUtc<TimeSpan.FromDays(7)){
   token.ThrowIfCancellationRequested();ValidatePartial(partial,metadataPath);return Candidate(partial,target,metadataPath,prior,reused:true);
  }
  if(prior is null||prior.Url!=region.Url||offset>prior.Length||offset>maximumFileBytes||prior.ETag is null&&prior.Modified is null){offset=0;prior=null;}
  using var request=Request(HttpMethod.Get,region.Url);
  if(offset>0){request.Headers.Range=new(offset,null);if(prior!.ETag is {} etag)request.Headers.IfRange=new(EntityTagHeaderValue.Parse(etag));else request.Headers.IfRange=new(prior.Modified!.Value);}
  using var response=await Send(request,token);
  token.ThrowIfCancellationRequested();
  if(response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable&&offset>0&&prior is not null&&offset==prior.Length&&response.Content.Headers.ContentRange?.Length==offset){ValidatePartial(partial,metadataPath);prior=prior with{Complete=true};await WriteMetadata(metadataPath,prior,token);return Candidate(partial,target,metadataPath,prior);}
  response.EnsureSuccessStatusCode();
  long? total;var append=response.StatusCode==HttpStatusCode.PartialContent&&offset>0;
  if(response.StatusCode==HttpStatusCode.PartialContent){var range=response.Content.Headers.ContentRange;if(!append||range?.From!=offset||range.To is null||range.Length is null||range.To.Value!=range.Length.Value-1||range.Length<=offset||response.Content.Headers.ContentLength is {} length&&length!=range.Length-offset||prior!.ETag is {} expected&&response.Headers.ETag?.ToString()!=expected||prior.ETag is null&&prior.Modified!=response.Content.Headers.LastModified)throw new HttpRequestException("The extract provider returned an inconsistent resume response. Retry the regional download.");total=range.Length;}
  else{offset=0;total=response.Content.Headers.ContentLength;}
  if(total>maximumFileBytes)throw new ArgumentException($"The {region.Name} extract exceeds the {maximumFileBytes/1024/1024/1024} GiB source-file budget. Select a smaller supported region or prepare a local extract.");
  var bytesNeeded=total.HasValue?total.Value-offset:0;var replacedBytes=!append&&File.Exists(partial)?new FileInfo(partial).Length:0;var capacity=EnsureCapacity(bytesNeeded,target,partial,replacedBytes);
  var metadata=new RegionalFileMetadata(region.Url,total??maximumFileBytes,StrongTag(response.Headers.ETag),response.Content.Headers.LastModified,DateTimeOffset.UtcNow);if(!append){using var reset=File.Create(partial);}await WriteMetadata(metadataPath,metadata,token);
  await using(var input=await response.Content.ReadAsStreamAsync(token)){
   await using var output=new FileStream(partial,FileMode.Append,FileAccess.Write,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);var buffer=new byte[81920];var received=offset;var last=DateTimeOffset.MinValue;long nextCapacityCheck=0;
   while(true){var read=await input.ReadAsync(buffer,token);if(read==0)break;received+=read;if(received>maximumFileBytes||total is {} expectedLength&&received>expectedLength)throw new ArgumentException("The regional download exceeds its declared size or source-file budget.");
    if(total is null){if(capacity<read||received-offset>=nextCapacityCheck){await output.FlushAsync(token);capacity=EnsureCapacity(read,target,partial);nextCapacityCheck=received-offset+64L*1024*1024;}capacity-=read;}
    await output.WriteAsync(buffer.AsMemory(0,read),token);if(DateTimeOffset.UtcNow-last>TimeSpan.FromMilliseconds(250)){Report(received,total);last=DateTimeOffset.UtcNow;}}
   await output.FlushAsync(token);if(total is {} expectedTotal&&received!=expectedTotal)throw new HttpRequestException("The regional download was interrupted; the partial file was retained for resume.");metadata=metadata with{Length=received,Complete=true};Report(received,received);
  }
  token.ThrowIfCancellationRequested();ValidatePartial(partial,metadataPath);await WriteMetadata(metadataPath,metadata,token);return Candidate(partial,target,metadataPath,metadata);
  void Report(long bytes,long? length){progress?.Report(new("region-download",length>0?(int)Math.Min(100,bytes*100/length.Value):0,100,region.Name){BytesReceived=bytes,TotalBytes=length});}
 }
 public RegionalFileLease? Stale(GeographicRegion region){var metadata=Cached(region);if(metadata is null)return null;var file=FileFor(region);return new(file,true,metadata,()=>{},()=>Invalidate(file));}
 private RegionalFileLease Candidate(string partial,string target,string metadataPath,RegionalFileMetadata metadata,bool reused=false)=>new(partial,reused,metadata,()=>{
  // Only a caller that has read and validated the entire PBF may promote it.
  var backup=target+"."+Guid.NewGuid().ToString("N")+".previous";var replacing=File.Exists(target);
  if(replacing)File.Replace(partial,target,backup);else File.Move(partial,target);
  try{File.Move(metadataPath,target+".json",true);}
  catch{File.Move(target,partial,true);if(replacing)File.Move(backup,target);throw;}
  if(replacing){try{File.Delete(backup);}catch(IOException){/* The validated pair is already published; an orphan backup is harmless. */}}
 },()=>{if(File.Exists(partial))File.Delete(partial);if(File.Exists(metadataPath))File.Delete(metadataPath);});
 private static void Invalidate(string target){if(File.Exists(target))File.Delete(target);if(File.Exists(target+".json"))File.Delete(target+".json");}
 private static string? StrongTag(EntityTagHeaderValue? value)=>value is {IsWeak:false}?value.ToString():null;
 private static RegionalFileMetadata? ReadMetadata(string path){try{if(!File.Exists(path)||new FileInfo(path).Length>8192)return null;var value=JsonSerializer.Deserialize<RegionalFileMetadata>(File.ReadAllText(path),jsonOptions);if(value is null||value.Length<1||value.Length>8L*1024*1024*1024||value.ETag is {} tag&&(!EntityTagHeaderValue.TryParse(tag,out var etag)||etag.IsWeak))return null;return value;}catch(Exception e) when(e is JsonException or IOException){return null;}}
 private static async Task WriteMetadata(string path,RegionalFileMetadata metadata,CancellationToken token){var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(metadata),token);token.ThrowIfCancellationRequested();File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 private long EnsureCapacity(long expected,string target,string partial,long replacedBytes=0){
  long used=Directory.EnumerateFiles(directory).Where(f=>f.EndsWith(".osm.pbf",StringComparison.Ordinal)||f.EndsWith(".part",StringComparison.Ordinal)).Sum(f=>new FileInfo(f).Length)-replacedBytes;
  foreach(var old in Directory.EnumerateFiles(directory,"*.osm.pbf").Where(f=>f!=target).OrderBy(File.GetLastWriteTimeUtc)){
   if(used+expected<=maximumCacheBytes)break;used-=new FileInfo(old).Length;Invalidate(old);
  }
  if(used+expected>maximumCacheBytes)throw new ArgumentException("The geographic source cache is full. Remove unused regional/partial downloads or increase the source-cache budget.");
  var root=System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(directory));if(root is not null&&new DriveInfo(root).AvailableFreeSpace<Math.Max(0,expected-replacedBytes)+512L*1024*1024)throw new ArgumentException("Not enough disk space for the regional source and temporary index. Free space in the workspace drive before importing.");return maximumCacheBytes-used;
 }
 private static HttpRequestMessage Request(HttpMethod method,string url){var request=new HttpRequestMessage(method,GeofabrikCatalog.ValidateUrl(url));request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.13.1 (+https://github.com/DinLemon/SimplePZMapmaker)");request.Headers.AcceptEncoding.ParseAdd("identity");return request;}
 private async Task<HttpResponseMessage> Send(HttpRequestMessage request,CancellationToken token)
 {
  HttpRequestMessage? redirected=null;
  try{
   for(var hop=0;;hop++){
    var current=redirected??request;var response=await client.SendAsync(current,HttpCompletionOption.ResponseHeadersRead,token);
    if((int)response.StatusCode is <300 or >=400)return response;
    Uri destination;
    using(response){
     if(hop>=3||response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)||response.Headers.Location is not {} location)throw new HttpRequestException("The regional provider returned an invalid redirect or exceeded the redirect budget.");
     try{destination=GeofabrikCatalog.ValidateRedirect(request.RequestUri!,new Uri(current.RequestUri!,location));}
     catch(Exception e) when(e is ArgumentException or UriFormatException){throw new HttpRequestException("The regional provider redirected outside the selected official snapshot.",e);}
    }
    redirected?.Dispose();redirected=new(request.Method,destination);
    foreach(var header in request.Headers)redirected.Headers.TryAddWithoutValidation(header.Key,header.Value);
   }
  }finally{redirected?.Dispose();}
 }
 private static void ValidatePartial(string path,string metadataPath){try{ValidateHeader(path);}catch(ArgumentException){if(File.Exists(path))File.Delete(path);if(File.Exists(metadataPath))File.Delete(metadataPath);throw;}}
 private static void ValidateHeader(string path){using var stream=File.OpenRead(path);Span<byte> bytes=stackalloc byte[4];if(stream.Length<12)throw new ArgumentException("The regional download is not a complete PBF source.");stream.ReadExactly(bytes);var length=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes);if(length is <8 or >65535||stream.Length<4L+length)throw new ArgumentException("The regional download is not a PBF source.");var header=new byte[Math.Min(length,100)];stream.ReadExactly(header);if(!System.Text.Encoding.ASCII.GetString(header).Contains("OSMHeader",StringComparison.Ordinal))throw new ArgumentException("The regional provider did not return an OSM PBF source.");}
}
