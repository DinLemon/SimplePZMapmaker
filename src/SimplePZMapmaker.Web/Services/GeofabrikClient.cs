using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record GeographicRegionDownload(string Id,string Name,long? Bytes,bool Cached,string? DataDate);
public sealed record GeographicSourcePlan(List<GeographicRegionDownload> Regions,long? DownloadBytes,long MaximumFileBytes,long MaximumCacheBytes,bool LargeFirstDownload,string Coverage="complete selected area");

public sealed class GeofabrikClient(string directory,HttpClient client,OsmImportTimeouts? timeouts=null)
{
 private readonly OsmImportTimeouts timing=timeouts??new();
 public const long MaximumFileBytes=8L*1024*1024*1024,MaximumCacheBytes=16L*1024*1024*1024;
 private readonly SemaphoreSlim gate=new(1,1);
 private RegionalDownloadCache Downloads=>new(directory,client,MaximumFileBytes,MaximumCacheBytes);
 public async Task<GeographicSourcePlan> PlanAsync(GeoBounds bounds,CancellationToken token=default)
 {
  token.ThrowIfCancellationRequested();
  var catalogue=await Catalogue(token);var selected=catalogue.Select(bounds);var downloads=Downloads;var regions=new List<GeographicRegionDownload>();long total=0;var known=true;
  foreach(var region in selected){token.ThrowIfCancellationRequested();var cached=downloads.Cached(region);long? length=cached?.Length;if(length is null){using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(8));try{length=await downloads.SizeAsync(region,deadline.Token);}catch(Exception e) when(!token.IsCancellationRequested&&e is HttpRequestException or OperationCanceledException){}}
   if(length>MaximumFileBytes)throw new ArgumentException($"The smallest covering extract, {region.Name}, exceeds the 8 GiB source-file budget. Prepare a smaller local extract.");
   var fresh=cached is not null&&DateTimeOffset.UtcNow-cached.RetrievedAtUtc<TimeSpan.FromDays(7);regions.Add(new(region.Id,region.Name,length,fresh,cached?.Modified?.ToString("u")));if(!fresh){if(length is {} size)total+=size;else known=false;}
  }
  token.ThrowIfCancellationRequested();return new(regions,known?total:null,MaximumFileBytes,MaximumCacheBytes,!known||total>64*1024*1024);
 }
 public async Task<OsmDownload> FetchAsync(GeoBounds bounds,CancellationToken cancellationToken,IProgress<OsmProgress>? progress=null,int maxBytes=16_000_000)
 {
  WorldLayoutLimits.ValidateSourceBounds(bounds);if(maxBytes is <1 or >64_000_000)throw new ArgumentException("Geographic data limit: 64 MB.");
  timing.Validate();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(timing.SourceMaximum);var token=timeout.Token;await gate.WaitAsync(token);
  try{
   progress?.Report(new("regions",0,1,"Geofabrik"));var catalogue=await Catalogue(token);var selected=catalogue.Select(bounds);var geometry=new OsmGeometrySet(maxBytes);var warnings=new List<string>();var dates=new HashSet<string>();var sources=new List<OsmSource>();var requests=0;var allCached=true;var retrieved=DateTimeOffset.MinValue;var completed=0;
   foreach(var region in selected){
    token.ThrowIfCancellationRequested();RegionalFileLease lease;
    try{lease=await Acquire(region,token,progress);}
    catch(RegionalTransferTimeoutException){throw;}
    catch(Exception e) when(!token.IsCancellationRequested&&e is HttpRequestException or IOException){lease=Downloads.Stale(region)??throw new HttpRequestException($"Could not download the {region.Name} regional extract. Partial downloads are retained for resume; the current map was preserved.",e);warnings.Add($"The {region.Name} refresh failed. Used a cached source downloaded {lease.Metadata.RetrievedAtUtc:u}; it may be out of date.");}
    using(lease){
     progress?.Report(new("region-downloaded",completed,selected.Count,region.Name,null,$"{region.Name}: {(lease.Cached?"using a complete cached PBF":"regional PBF fully downloaded")} ({lease.Metadata.Length:N0} bytes). Extracting the selected area locally."){BytesReceived=lease.Metadata.Length,TotalBytes=lease.Metadata.Length});
     progress?.Report(new("region-index",completed,selected.Count,region.Name));PbfExtractReader.Result result;
     using var processing=CancellationTokenSource.CreateLinkedTokenSource(token);processing.CancelAfter(timing.ProcessingMaximum);
     try{result=PbfExtractReader.ReadLarge(lease.Path,bounds,Path.Combine(directory,"indexes"),processing.Token,progress,maxBytes);processing.Token.ThrowIfCancellationRequested();lease.Commit();}
     catch(OperationCanceledException e) when(!token.IsCancellationRequested){throw new RegionalProcessingException($"The {region.Name} regional PBF is complete, but local processing exceeded its time budget. The full download is retained; retry processing or reduce the selected area. The current world was preserved.",e,retained:true);}
     catch(PbfExtractReader.ProcessingLimitException e){
      throw new RegionalProcessingException($"The {region.Name} PBF is {(lease.Cached?"cached":"downloaded")} ({lease.Metadata.Length:N0} bytes), but local extraction failed: {RegionalProcessingException.Detail(e)} The complete download is retained for retry; reduce the selected area or free scratch disk space. The current map was preserved.",e,retained:true);
     }
     catch(ArgumentException e){lease.Reject();throw new RegionalProcessingException($"The {region.Name} PBF was downloaded, but could not be read: {RegionalProcessingException.Detail(e)} The rejected source was removed; retry the regional source. The current map was preserved.",e);}
     geometry.Add(result.Data);if(result.Timestamp is {} date)dates.Add(date);sources.Add(new("download.geofabrik.de",result.Timestamp,lease.Metadata.RetrievedAtUtc,lease.Cached));allCached&=lease.Cached;if(!lease.Cached)requests++;retrieved=lease.Metadata.RetrievedAtUtc>retrieved?lease.Metadata.RetrievedAtUtc:retrieved;
     warnings.Add($"Region: {region.Name} ({region.Id}); data date: {result.Timestamp??"not specified"}; {(lease.Cached?"cached":"downloaded and validated")} source {lease.Metadata.Length/1024/1024:N0} MiB.");
     if(result.OmittedRelations>0||result.OmittedWays>0)warnings.Add($"{region.Name}: skipped {result.OmittedRelations} incomplete/nested relations and {result.OmittedWays} ways missing nodes (including outside the selected area).");
    }
    progress?.Report(new("region-ready",++completed,selected.Count,region.Name));
   }
   if(dates.Count>1)warnings.Add("The selected area crosses regional extracts with different snapshot dates; each date is retained in the source report.");
   if(File.Exists(Path.Combine(directory,"catalogue.json"))&&DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(directory,"catalogue.json"))>TimeSpan.FromDays(7))warnings.Add("The region catalogue could not be refreshed; a previously validated catalogue was used.");
   var timestamp=dates.Count==1?dates.Single():null;return new(geometry.Serialize(timestamp),"download.geofabrik.de",warnings.Take(20).ToList()){DataTimestamp=timestamp,RetrievedAtUtc=retrieved,Requests=requests,FromCache=allCached,Sources=sources};
  }catch(OperationCanceledException e) when(!cancellationToken.IsCancellationRequested){throw new HttpRequestException("The regional source exceeded its total time budget. Saved downloads remain in the cache; the current world was preserved.",e);}
  finally{gate.Release();}
 }
 private async Task<RegionalFileLease> Acquire(GeographicRegion region,CancellationToken token,IProgress<OsmProgress>? progress)
 {
  using var maximum=CancellationTokenSource.CreateLinkedTokenSource(token);maximum.CancelAfter(timing.TransferMaximum);
  using var idle=CancellationTokenSource.CreateLinkedTokenSource(maximum.Token);idle.CancelAfter(timing.TransferIdle);long received=0;long? total=null;
  var report=new InlineProgress<OsmProgress>(p=>{if(p.BytesReceived is {} bytes&&bytes>received){received=bytes;idle.CancelAfter(timing.TransferIdle);}total=p.TotalBytes??total;progress?.Report(p);});
  progress?.Report(new("region-download",0,100,region.Name));
  try{return await Downloads.AcquireAsync(region,idle.Token,report);}
  catch(OperationCanceledException e) when(!token.IsCancellationRequested){throw new RegionalTransferTimeoutException(region.Name,received,total,!maximum.IsCancellationRequested,e);}
 }
 private async Task<GeofabrikCatalog> Catalogue(CancellationToken token)
 {
  token.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);var file=Path.Combine(directory,"catalogue.json");GeofabrikCatalog? previous=null;
  try{if(File.Exists(file)&&new FileInfo(file).Length<=32_000_000){previous=new(await File.ReadAllTextAsync(file,token));token.ThrowIfCancellationRequested();if(DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))return previous;}}catch(Exception e) when(e is ArgumentException or JsonException or IOException){}
  try{
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(40));using var request=new HttpRequestMessage(HttpMethod.Get,GeofabrikCatalog.CatalogueUrl);request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.13.0 (+https://github.com/DinLemon/SimplePZMapmaker)");using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>32_000_000)throw new ArgumentException("The regional catalogue exceeds 32 MB.");
   await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var memory=new MemoryStream();var bytes=new byte[81920];while(true){var count=await stream.ReadAsync(bytes,deadline.Token);if(count==0)break;if(memory.Length+count>32_000_000)throw new ArgumentException("The regional catalogue exceeds 32 MB.");memory.Write(bytes,0,count);}
   var json=Encoding.UTF8.GetString(memory.ToArray());var parsed=new GeofabrikCatalog(json);deadline.Token.ThrowIfCancellationRequested();var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,json,deadline.Token);deadline.Token.ThrowIfCancellationRequested();File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}return parsed;
  }catch(Exception e) when(!token.IsCancellationRequested&&previous is not null&&e is HttpRequestException or IOException or OperationCanceledException or ArgumentException or JsonException){return previous;}
 }
}
