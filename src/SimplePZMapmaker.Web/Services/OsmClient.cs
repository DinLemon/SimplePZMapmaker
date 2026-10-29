using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmSource(string Host,string? DataTimestamp,DateTimeOffset RetrievedAtUtc,bool FromCache);
public sealed record OsmDownload(string Data,string Source,List<string> Warnings){public DateTimeOffset RetrievedAtUtc {get;init;}=DateTimeOffset.UtcNow;public string? DataTimestamp {get;init;}public bool FromCache {get;init;}public int Requests {get;init;}=1;public List<OsmSource> Sources {get;init;}=[];public string? SourceId {get;init;}}
public sealed partial class OsmClient(string cacheDirectory,HttpMessageHandler? handler=null,TimeSpan? requestInterval=null,TimeSpan? attemptTimeout=null,OsmImportTimeouts? importTimeouts=null) : IDisposable
{
 private readonly HttpClient client=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
 private readonly SemaphoreSlim gate=new(1,1);
 private DateTimeOffset lastRequest=DateTimeOffset.MinValue;
 private static readonly string[] providers=["https://overpass-api.de/api/interpreter","https://overpass.private.coffee/api/interpreter","https://maps.mail.ru/osm/tools/overpass/api/interpreter"];
 public const int ExtractTimeoutSeconds=240;
 public static GeoBounds SupportedExtractBounds=>new(){South=54.6,North=54.95,West=31.8,East=32.3};
 public static bool CanUseExtract(GeoBounds? b)=>b is not null&&double.IsFinite(b.South)&&double.IsFinite(b.North)&&double.IsFinite(b.West)&&double.IsFinite(b.East)&&b.South>=54.6&&b.North<=54.95&&b.West>=31.8&&b.East<=32.3&&b.South<b.North&&b.West<b.East&&b.North-b.South<=0.08&&b.East-b.West<=0.12;
 public async Task<string> Fetch(GeoBounds b,CancellationToken token)=>(await FetchDetailed(b,token)).Data;
 public Task<OsmDownload> FetchDetailed(GeoBounds b,CancellationToken token){
  ProjectValidator.ValidateBounds(b);return b.North-b.South>.01||b.East-b.West>.02?FetchTiledDetailed(b,token):FetchSingleDetailed(b,token,mapBackup:true);
 }
 private Task<OsmDownload> FetchSingleDetailed(GeoBounds b,CancellationToken token,IProgress<OsmProgress>? progress=null,bool mapBackup=false){
  ProjectValidator.ValidateBounds(b);var box=FormattableString.Invariant($"{b.South:F6},{b.West:F6},{b.North:F6},{b.East:F6}");
  var query=$"[out:json][timeout:25];(way[highway]({box});way[building]({box});way[natural]({box});way[landuse]({box});way[waterway]({box});way[railway]({box});relation[natural]({box});relation[landuse]({box});relation[building]({box});nwr[amenity]({box});nwr[shop]({box});nwr[leisure]({box});nwr[site]({box});node[entrance]({box});node[\"man_made\"=\"fuel_pump\"]({box});nwr[\"disused:amenity\"=\"fuel\"]({box});nwr[\"abandoned:amenity\"=\"fuel\"]({box});node[\"disused:man_made\"=\"fuel_pump\"]({box});node[\"abandoned:man_made\"=\"fuel_pump\"]({box});nwr[barrier]({box});nwr[healthcare]({box});node[\"addr:housenumber\"]({box}););out geom;";
  var mapUrl="https://api.openstreetmap.org/api/0.6/map.json?bbox="+FormattableString.Invariant($"{b.West:F6},{b.South:F6},{b.East:F6},{b.North:F6}");
  string[] sources=mapBackup?[providers[0],mapUrl,..providers.Skip(1)]:providers;
  return Request((mapBackup?"osm-v9-small-":"osm-v8-")+box,sources,url=>url==mapUrl?new(HttpMethod.Get,url):new(HttpMethod.Post,url){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"data",query}})},true,token,progress);
 }
 public async Task<JsonElement> Search(string query,CancellationToken token){
  if(string.IsNullOrWhiteSpace(query)||query.Length>180)throw new ArgumentException("Enter a city name or address, up to 180 characters.");
  var url="https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q="+Uri.EscapeDataString(query);
  var data=await Request("search-"+query,[url],uri=>new(HttpMethod.Get,uri),false,token);using var doc=JsonDocument.Parse(data.Data);return doc.RootElement.Clone();
 }
 private static void ValidateJson(string data,bool overpass){using var json=JsonDocument.Parse(data);var root=json.RootElement;if(!overpass){if(root.ValueKind!=JsonValueKind.Array)throw new HttpRequestException("The place search service returned unsuitable data. Retry later.");return;}if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("elements",out var elements)||elements.ValueKind!=JsonValueKind.Array)throw new HttpRequestException("The OSM server returned unsuitable data.");if(root.TryGetProperty("remark",out var remark)&&remark.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(remark.GetString()))throw new HttpRequestException("The OSM server returned an incomplete map. Trying a fallback server.");}
 private async Task<string> Read(HttpResponseMessage response,bool overpass,CancellationToken token){
  if(response.Content.Headers.ContentLength>16_000_000)throw new ArgumentException("Too much geographic data. Reduce the selected area.");
  await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var chunk=new byte[8192];while(true){var n=await stream.ReadAsync(chunk,token);if(n==0)break;if(buffer.Length+n>16_000_000)throw new ArgumentException("Too much geographic data. Reduce the selected area.");buffer.Write(chunk,0,n);}var data=Encoding.UTF8.GetString(buffer.ToArray());ValidateJson(data,overpass);return data;
 }
 private async Task<OsmDownload> Request(string key,string[] urls,Func<string,HttpRequestMessage> requestFactory,bool overpass,CancellationToken cancellationToken,IProgress<OsmProgress>? progress=null){
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(overpass?110:40));var token=deadline.Token;
  Directory.CreateDirectory(cacheDirectory);var file=Path.Combine(cacheDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json");
  await gate.WaitAsync(token);
  try{
   if(File.Exists(file)&&new FileInfo(file).Length<=16_000_000&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7)){var cached=await File.ReadAllTextAsync(file,token);try{ValidateJson(cached,overpass);var origin=CacheMetadata(file,cached);var date=Timestamp(cached);return new(cached,origin.Host,overpass?["Used previously downloaded OSM data from the local cache.",..MapApiWarnings(origin.Host,cached)]:[]){FromCache=true,Requests=0,RetrievedAtUtc=origin.Retrieved,DataTimestamp=date,Sources=[new(origin.Host,date,origin.Retrieved,true)]};}catch(Exception e) when(e is JsonException or HttpRequestException){/* Incomplete cache must not masquerade as a complete map. */}}
   for(var i=0;i<urls.Length;i++){
    var wait=(requestInterval??TimeSpan.FromSeconds(2))-(DateTimeOffset.UtcNow-lastRequest);if(wait>TimeSpan.Zero)await Task.Delay(wait,token);
    using var attempt=CancellationTokenSource.CreateLinkedTokenSource(token);attempt.CancelAfter(attemptTimeout??TimeSpan.FromSeconds(35));
    try{
     progress?.Report(new("download",i,urls.Length,new Uri(urls[i]).Host,null,i>0?"Retrying on the fallback server.":null));using var request=requestFactory(urls[i]);request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.6.0 (local-map-editor)");lastRequest=DateTimeOffset.UtcNow;
     using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,attempt.Token);
     if(!response.IsSuccessStatusCode){var error=new HttpRequestException($"The mapping service returned {(int)response.StatusCode}.",null,response.StatusCode);if(i==urls.Length-1)throw error;continue;}
     var data=await Read(response,overpass,attempt.Token);var source=new Uri(urls[i]).Host;if(overpass&&source=="api.openstreetmap.org")data=MapApiGeometry(data,attempt.Token);var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,data,token);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
     var retrieved=DateTimeOffset.UtcNow;var meta=file+".source.json";var tempMeta=meta+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(tempMeta,JsonSerializer.Serialize(new CacheSource(source,DataHash(data),retrieved)),token);File.Move(tempMeta,meta,true);}finally{if(File.Exists(tempMeta))File.Delete(tempMeta);}var warnings=MapApiWarnings(source,data);if(i>0)warnings.Insert(0,$"The primary OSM server did not respond; data was retrieved from the fallback server {source}.");return new(data,source,warnings){Requests=i+1,RetrievedAtUtc=retrieved,DataTimestamp=Timestamp(data),Sources=[new(source,Timestamp(data),retrieved,false)]};
    }catch(OperationCanceledException) when(!token.IsCancellationRequested){if(i==urls.Length-1)break;}
    catch(HttpRequestException){if(i==urls.Length-1)break;}
    catch(JsonException){if(i==urls.Length-1)break;}
    catch(ArgumentException) when(overpass&&new Uri(urls[i]).Host=="api.openstreetmap.org"){if(i==urls.Length-1)break;}
   }
   throw new HttpRequestException(overpass?"OSM servers are currently unavailable or overloaded. Reduce the area or load a saved geographic source. The current map was preserved.":"Place search is currently unavailable. Enter center coordinates or use a saved geographic source. The current map was preserved.");
  }catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new HttpRequestException(overpass?"OSM servers did not load the map in time. Reduce the area or choose a saved geographic source. The current map was preserved.":"Place search timed out. Enter center coordinates or use a saved geographic source. The current map was preserved.");}
  finally{gate.Release();}
 }
 public void Dispose(){client.Dispose();gate.Dispose();}
}
