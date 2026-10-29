using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record CadastreLimits
{
 public int MaxRequests {get;init;}=2048;
 public int MaxPlannedSamples {get;init;}=2048;
 public long MaxCacheBytes {get;init;}=512L*1024*1024;
 public int MaxResponseBytes {get;init;}=2*1024*1024;
 public int MaxTotalBytes {get;init;}=16*1024*1024;
 public TimeSpan MinimumRequestInterval {get;init;}=TimeSpan.FromMilliseconds(200);
 public TimeSpan RequestTimeout {get;init;}=TimeSpan.FromSeconds(20);
 public TimeSpan OperationTimeout {get;init;}=TimeSpan.FromMinutes(20);
 public TimeSpan CacheLifetime {get;init;}=TimeSpan.FromDays(7);
 internal void Validate()
 {
  if(MaxRequests is <1 or >4096||MaxPlannedSamples is <1 or >4096||MaxCacheBytes is <1024 or >1073741824||MaxResponseBytes is <1 or >4*1024*1024||MaxTotalBytes<MaxResponseBytes||MaxTotalBytes>32*1024*1024||
   MinimumRequestInterval<TimeSpan.FromMilliseconds(100)||MinimumRequestInterval>TimeSpan.FromMinutes(1)||RequestTimeout<=TimeSpan.Zero||RequestTimeout>TimeSpan.FromMinutes(1)||
   OperationTimeout<=TimeSpan.Zero||OperationTimeout>TimeSpan.FromHours(1)||CacheLifetime<TimeSpan.Zero||CacheLifetime>TimeSpan.FromDays(30))
   throw new ArgumentException("Invalid cadastre limits: requests/samples 1..4096, cache <=1 GiB, response <=4 MiB, total <=32 MiB, interval >=100 ms, request <=60 s, operation <=60 min, cache <=30 days.");
 }
}
public sealed record CadastreRequest
{
 public GeoBounds Bounds {get;init;}=new();
 public int[] Layers {get;init;}=[36048,36049];
 public int GridSize {get;init;}
 public double StepMetres {get;init;}=9;
 public string? ExpectedRegionCode {get;init;}
 public bool Refresh {get;init;}
}
public sealed record CadastreResult(string GeoJson,int Features,int Requests,int SkippedSamples,bool FromCache,
 DateTimeOffset FetchedAt,IReadOnlyList<string> Diagnostics)
{
 public CadastreProgress? Coverage {get;init;}
 public CadastrePlan? Plan {get;init;}
}
public sealed record CadastrePlan(int Columns,int Rows,int SamplesPlanned,double WidthMetres,double HeightMetres,double? StepMetres,bool CoarseGrid,double MinimumSecondsWithoutCoverageSkipping,int RequestBudget);
public sealed record CadastreProgress(int SamplesCompleted,int SamplesPlanned,int Requests,int PointsFromCache,int SkippedSamples)
{
 public double CoverageFraction=>SamplesPlanned==0?0:(double)SamplesCompleted/SamplesPlanned;
 public bool PlannedGridComplete=>SamplesCompleted==SamplesPlanned;
 public bool ParcelInventoryComplete=>false;
}
/// <summary>Safe diagnostic; never includes provider bodies, request URLs, headers or nested network exceptions.</summary>
public sealed class CadastreException(string code,string message):Exception(message)
{
 public string Code {get;}=code;
 public CadastreProgress? Progress {get;internal set;}
}

/// <summary>Bounded NSPD sampling, not a parcel inventory. Cache promotion is transactional across the whole grid.</summary>
public sealed class CadastreClient:IDisposable
{
 readonly string cacheDirectory;readonly CadastreLimits limits;readonly HttpClient http;
 // Shared across clients, layers and cache roots: at most one upstream exchange at a time.
 static readonly SemaphoreSlim ProviderGate=new(1,1);
 static readonly SemaphoreSlim CacheGate=new(1,1);
 static long lastRequest;
 const int SchemaVersion=2;
 const int ImageSize=1024;
 public CadastreClient(string cacheDirectory,CadastreLimits? limits=null,HttpMessageHandler? transport=null)
 {
  this.cacheDirectory=Path.GetFullPath(cacheDirectory);this.limits=limits??new();this.limits.Validate();
  // The optional transport is a trusted embedding/test seam, never a request option. Production uses strict system PKI.
  http=new HttpClient(transport??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,MaxConnectionsPerServer=1,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate,SslProtocols=SslProtocols.Tls12|SslProtocols.Tls13}){Timeout=Timeout.InfiniteTimeSpan};
 }
 public CadastrePlan Plan(CadastreRequest request)
 {
  var b=GeographicSurveyProjection.ProjectBounds(request.Bounds);
  var scale=Math.Cos((request.Bounds.South+request.Bounds.North)/2*Math.PI/180);
  var width=(b.East-b.West)*scale;var height=(b.North-b.South)*scale;
  var layers=request.Layers?.Distinct().ToArray()??[];
  if(layers.Length is <1 or >2||layers.Any(l=>l is not(36048 or 36049)))throw new ArgumentException("Supported cadastre layers: 36048 (parcels), 36049 (buildings).");
  if(request.GridSize is <0 or >16||!double.IsFinite(request.StepMetres)||request.StepMetres is <1 or >100)throw new ArgumentException("Choose step 1..100 ground metres or explicit coarse grid 1..16.");
  if(b.East-b.West>20000||b.North-b.South>20000||(b.East-b.West)*(b.North-b.South)>25_000_000)throw new ArgumentException("Cadastre bounds exceed 20 km per EPSG:3857 axis or 25 square km.");
  if(request.ExpectedRegionCode is { } region&&(region.Length is <2 or >3||region.Any(c=>c is <'0' or >'9')))throw new ArgumentException("Expected region code must be two or three digits; leave it unset if unknown.");
  var columns=request.GridSize>0?request.GridSize:Math.Max(1,(int)Math.Ceiling(width/request.StepMetres-1e-9));
  var rows=request.GridSize>0?request.GridSize:Math.Max(1,(int)Math.Ceiling(height/request.StepMetres-1e-9));
  var samples=(long)columns*rows*layers.Length;
  if(columns>ImageSize||rows>ImageSize||samples>limits.MaxPlannedSamples)throw new ArgumentException($"Entire planned cadastre grid exceeds {limits.MaxPlannedSamples} samples. Reduce bounds or explicitly choose a coarser step; no automatic coverage reduction.");
  return new(columns,rows,(int)samples,width,height,request.GridSize>0?null:request.StepMetres,request.GridSize>0,samples*limits.MinimumRequestInterval.TotalSeconds,limits.MaxRequests);
 }
 public async Task<CadastreResult> FetchAsync(CadastreRequest request,CancellationToken cancellationToken=default,IProgress<CadastreProgress>? progress=null)
 {
  ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();
  // Snapshot mutable input before awaiting; callers cannot change the plan midway through a fetch.
  var b=request.Bounds??throw new ArgumentException("Bounds are required.");
  var bounds=new GeoBounds{West=b.West,South=b.South,East=b.East,North=b.North};
  request=request with{Bounds=bounds,Layers=request.Layers?.ToArray()!};
  var plan=Plan(request);var box=GeographicSurveyProjection.ProjectBounds(bounds);
  var layers=request.Layers?.Distinct().Order().ToArray()??[];
  if(layers.Length is <1 or >2||layers.Any(l=>l is not(36048 or 36049)))throw new ArgumentException("Supported cadastre layers: 36048 (parcels), 36049 (buildings).");
  var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{SchemaVersion,bounds,layers,plan.Columns,plan.Rows,request.GridSize,request.StepMetres,request.ExpectedRegionCode})))).ToLowerInvariant();
  var path=Path.Combine(cacheDirectory,key+".json");
  using var operation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);operation.CancelAfter(limits.OperationTimeout);var token=operation.Token;
  int completed=0,requests=0,skipped=0,pointsFromCache=0;
  CadastreProgress Snapshot()=>new(completed,plan.SamplesPlanned,requests,pointsFromCache,skipped);
  void Report()=>progress?.Report(Snapshot());
  try{
   Report();
   if(!request.Refresh&&await ReadCacheAsync(path,key,token) is { } cached){if(cached.Coverage is { } coverage)progress?.Report(coverage with{Requests=0});return cached;}
   var features=new JsonArray();var ids=new HashSet<string>(StringComparer.Ordinal);int totalBytes=0,missingIds=0,uniquePoints=0;
   var diagnostics=new HashSet<string>(StringComparer.Ordinal);
   var fetchedAt=DateTimeOffset.UtcNow;
   foreach(var layer in layers){
    var covered=new List<JsonObject>();var coveredIds=new HashSet<string>(StringComparer.Ordinal);
    for(var row=0;row<plan.Rows;row++)for(var column=0;column<plan.Columns;column++){
     token.ThrowIfCancellationRequested();
     var i=(int)((column+0.5)*ImageSize/plan.Columns);var j=(int)((row+0.5)*ImageSize/plan.Rows);
     var x=box.West+(i+0.5)/ImageSize*(box.East-box.West);var y=box.North-(j+0.5)/ImageSize*(box.North-box.South);
     var ll=GeographicSurveyProjection.FromWebMercator(x,y);
     if(covered.Any(g=>Contains(g,ll.Longitude,ll.Latitude))){skipped++;completed++;Report();continue;}
     var pointPath=Path.Combine(cacheDirectory,"points",$"{key}-{layer}-{i}-{j}.json");
     var point=request.Refresh?null:await ReadPointAsync(pointPath,key,token);byte[] bytes;DateTimeOffset pointDate;
     if(point is not null){bytes=point.Value.Bytes;pointDate=point.Value.Date;pointsFromCache++;}
     else{
      if(requests>=limits.MaxRequests)throw new CadastreException("request-budget","NSPD request budget reached; rerun the same selection to resume cached points, or use supplied GeoJSON. Previous aggregate cache retained.");
      requests++;bytes=await DownloadAsync(FeatureInfoUri(layer,box,i,j),Math.Min(limits.MaxResponseBytes,limits.MaxTotalBytes-totalBytes),token);pointDate=DateTimeOffset.UtcNow;
     }
     totalBytes+=bytes.Length;if(totalBytes>limits.MaxTotalBytes)throw new CadastreException("byte-budget","NSPD selected source exceeds the total byte budget, including cached points. Reduce bounds or use a bounded supplied export.");
     GeographicSurveyResult normalized;
     try{normalized=GeographicSurvey.Normalize(Encoding.UTF8.GetString(bytes),new(){Source=GeographicSurveySourceKind.Cadastre,Attribution="NSPD / Rosreestr",SourceReference=$"https://nspd.gov.ru/api/aeggis/v3/{layer}/wms",RetrievedAt=pointDate,ExpectedRegionCode=request.ExpectedRegionCode,InputCrs=GeographicSurveyCrs.Epsg3857,Kind=layer==36048?GeographicSurveyKind.Parcel:GeographicSurveyKind.Building},token);}
     catch(ArgumentException){throw new CadastreException("provider-schema","NSPD response is not supported EPSG:3857 GeoJSON. Check provider schema or normalize a supplied export. Previous cache retained.");}
     var rows=JsonNode.Parse(normalized.GeoJson)!["features"]!.AsArray();
     foreach(var d in normalized.Diagnostics)diagnostics.Add(d);
     foreach(var f in rows){
      var id=f!["id"]!.GetValue<string>();
      if(!id.StartsWith("cad-",StringComparison.Ordinal)){missingIds++;continue;}
      // Coverage only uses polygons with a usable cadastral identity. A point/box is not parcel coverage.
      if(f["geometry"] is JsonObject g&&g["type"]?.ToString() is "Polygon" or "MultiPolygon"&&coveredIds.Add(id))covered.Add(g);
      if(ids.Add(id)){uniquePoints+=CountPositions(f["geometry"]!["coordinates"]!.AsArray());if(features.Count>=GeographicSurvey.MaximumFeatures||uniquePoints>GeographicSurvey.MaximumPoints)throw new CadastreException("feature-budget","NSPD feature/point limit reached. Previous cache retained.");features.Add(f.DeepClone());}
     }
     if(point is null&&missingIds==0)await WritePointAsync(pointPath,key,bytes,pointDate,token);
     completed++;Report();
    }
   }
   if(missingIds>0)throw new CadastreException("missing-identity","NSPD returned objects without stable cadastral numbers; refusing an incomplete replacement. Normalize a supplied export instead. Previous cache retained.");
   var collection=new JsonObject{["type"]="FeatureCollection",["features"]=features,["pz:source"]=new JsonObject{["provider"]="NSPD",["attribution"]="NSPD / Rosreestr",["fetchedAt"]=fetchedAt.ToString("O"),["sampling"]="GetFeatureInfo grid; incomplete coverage possible",["bounds"]=JsonSerializer.SerializeToNode(bounds),["layers"]=JsonSerializer.SerializeToNode(layers)}};
   var json=collection.ToJsonString();if(Encoding.UTF8.GetByteCount(json)>GeographicSurvey.MaximumInputBytes)throw new CadastreException("output-budget","Normalized NSPD output exceeds 16 MiB. Previous cache retained.");
   diagnostics.Add($"Planned grid {plan.Columns} x {plan.Rows} x {layers.Length} layers; {completed}/{plan.SamplesPlanned} sample positions processed. Planned-grid completion is not a complete parcel inventory: objects between samples, overlaps and FEATURE_COUNT limits can hide objects. Objects are not clipped to bounds.");
   diagnostics.Add(request.ExpectedRegionCode is null?"No expected region supplied; administrative coverage is unverified. Cadastral identifier prefixes are retained, not used as geographic boundary proof.":"Expected region is checked against cadastral identifier prefixes; mismatches are retained and flagged, not silently dropped. Administrative boundaries remain unverified.");
   var result=new CadastreResult(json,features.Count,requests,skipped,false,fetchedAt,diagnostics.ToArray()){Coverage=Snapshot(),Plan=plan};
   await WriteCacheAsync(path,new(SchemaVersion,key,result),token);return result;
  }
  catch(CadastreException e){e.Progress=Snapshot();throw;}
  catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw Failure("timeout","NSPD request/operation timed out. Completed points remain cached; rerun the same selection to resume.");}
  catch(OperationCanceledException){throw new OperationCanceledException("Cadastre intake cancelled; completed point cache and previous aggregate retained.",cancellationToken);}
  catch(HttpRequestException){throw Failure("transport","NSPD HTTPS request failed (network/TLS). Strict certificate validation is required. Completed points and previous aggregate retained.");}
  catch(JsonException){throw Failure("provider-schema","NSPD returned malformed GeoJSON. Previous aggregate retained.");}
  catch(IOException){throw Failure("cache-io","Cadastre source/cache could not be read/written. Previous aggregate retained.");}
  catch(UnauthorizedAccessException){throw Failure("cache-io","Cadastre cache access denied. Previous aggregate retained.");}
  CadastreException Failure(string code,string message)=>new(code,message){Progress=Snapshot()};
 }
 static int CountPositions(JsonArray a)=>a.Count>0&&a[0] is JsonValue?1:a.Sum(n=>CountPositions(n!.AsArray()));
 sealed record PointEnvelope(int Version,string Key,DateTimeOffset FetchedAt,string Base64);
 async Task<(byte[] Bytes,DateTimeOffset Date)?> ReadPointAsync(string path,string key,CancellationToken token)
 {
  if(!File.Exists(path))return null;
  if(new FileInfo(path).Length>2L*limits.MaxResponseBytes+65536)throw new CadastreException("cache-schema","Point cache exceeds its size limit; refresh explicitly.");
  try{var point=JsonSerializer.Deserialize<PointEnvelope>(await File.ReadAllTextAsync(path,token));
   if(point is null||point.Version!=SchemaVersion||point.Key!=key||point.Base64 is null)throw new JsonException();
   if(point.FetchedAt>DateTimeOffset.UtcNow.AddMinutes(5)||DateTimeOffset.UtcNow-point.FetchedAt>limits.CacheLifetime)return null;
   var bytes=Convert.FromBase64String(point.Base64);if(bytes.Length>limits.MaxResponseBytes)throw new JsonException();return(bytes,point.FetchedAt);
  }catch(Exception e) when(e is JsonException or FormatException){throw new CadastreException("cache-schema","Point cache is invalid; previous bytes retained. Refresh explicitly.");}
 }
 Task WritePointAsync(string path,string key,byte[] bytes,DateTimeOffset fetchedAt,CancellationToken token)=>WriteJsonAsync(path,JsonSerializer.Serialize(new PointEnvelope(SchemaVersion,key,fetchedAt,Convert.ToBase64String(bytes))),token);
 async Task<byte[]> DownloadAsync(Uri uri,int maxBytes,CancellationToken token)
 {
  if(maxBytes<=0)throw new CadastreException("byte-budget","NSPD total byte budget reached. Previous cache retained.");
  await ProviderGate.WaitAsync(token);
  try{
   var interval=Math.Max(limits.MinimumRequestInterval.TotalMilliseconds,100);
   var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(lastRequest).TotalMilliseconds;
   if(lastRequest!=0&&elapsed<interval)await Task.Delay(TimeSpan.FromMilliseconds(interval-elapsed),token);
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(limits.RequestTimeout);var ct=timeout.Token;
   using var message=new HttpRequestMessage(HttpMethod.Get,uri);message.Headers.Accept.ParseAdd("application/json");message.Headers.Referrer=new Uri("https://nspd.gov.ru/");
   lastRequest=System.Diagnostics.Stopwatch.GetTimestamp();
   using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);
   if((int)response.StatusCode is >=300 and <400)throw new CadastreException("redirect","NSPD redirect refused; only the fixed official HTTPS WMS endpoint is allowed. Previous cache retained.");
   if(!response.IsSuccessStatusCode)throw new CadastreException("http-status",$"NSPD returned HTTP {(int)response.StatusCode}. No automatic retries; previous cache retained.");
   if(response.Content.Headers.ContentLength>maxBytes)throw new CadastreException("byte-budget","NSPD response exceeds the byte budget. Previous cache retained.");
   await using var stream=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[16384];
   while(true){var n=await stream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,maxBytes-(int)output.Length+1)),ct);if(n==0)break;
    if(output.Length+n>maxBytes)throw new CadastreException("byte-budget","NSPD response exceeds the byte budget. Previous cache retained.");await output.WriteAsync(buffer.AsMemory(0,n),ct);}
   return output.ToArray();
  }finally{ProviderGate.Release();}
 }
 static Uri FeatureInfoUri(int layer,(double West,double South,double East,double North) b,int i,int j)
 {
  string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
  var query=new Dictionary<string,string>{["SERVICE"]="WMS",["VERSION"]="1.3.0",["REQUEST"]="GetFeatureInfo",["LAYERS"]=layer.ToString(CultureInfo.InvariantCulture),["QUERY_LAYERS"]=layer.ToString(CultureInfo.InvariantCulture),["CRS"]="EPSG:3857",["BBOX"]=string.Join(',',N(b.West),N(b.South),N(b.East),N(b.North)),["WIDTH"]=ImageSize.ToString(CultureInfo.InvariantCulture),["HEIGHT"]=ImageSize.ToString(CultureInfo.InvariantCulture),["I"]=i.ToString(CultureInfo.InvariantCulture),["J"]=j.ToString(CultureInfo.InvariantCulture),["INFO_FORMAT"]="application/json",["FORMAT"]="image/png",["STYLES"]="",["FEATURE_COUNT"]="100"};
  return new($"https://nspd.gov.ru/api/aeggis/v3/{layer}/wms?"+string.Join('&',query.Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value))));
 }
 static bool Contains(JsonObject geometry,double x,double y)
 {
  var c=geometry["coordinates"]!.AsArray();
  int Ring(JsonArray ring){bool inside=false;for(int i=0,j=ring.Count-1;i<ring.Count;j=i++){
   var ax=(double)ring[i]![0]!;var ay=(double)ring[i]![1]!;var bx=(double)ring[j]![0]!;var by=(double)ring[j]![1]!;
   // Boundary points are deliberately queried; boundary-only coverage is not reliable.
   if(Math.Abs((x-ax)*(by-ay)-(y-ay)*(bx-ax))<1e-18&&x>=Math.Min(ax,bx)&&x<=Math.Max(ax,bx)&&y>=Math.Min(ay,by)&&y<=Math.Max(ay,by))return -1;
   if((ay>y)!=(by>y)&&x<(bx-ax)*(y-ay)/(by-ay)+ax)inside=!inside;
  }return inside?1:0;}
  bool Polygon(JsonArray polygon)=>Ring(polygon[0]!.AsArray())==1&&polygon.Skip(1).All(r=>Ring(r!.AsArray())==0);
  return geometry["type"]!.ToString()=="Polygon"?Polygon(c):c.Any(p=>Polygon(p!.AsArray()));
 }
 sealed record CacheEnvelope(int Version,string Key,CadastreResult Result);
 async Task<CadastreResult?> ReadCacheAsync(string path,string key,CancellationToken token)
 {
  if(!File.Exists(path))return null;
  if(new FileInfo(path).Length>2L*GeographicSurvey.MaximumInputBytes+65536)throw new CadastreException("cache-schema","Cadastre cache exceeds its size limit; retain it separately and refresh explicitly.");
  try{
   var cache=JsonSerializer.Deserialize<CacheEnvelope>(await File.ReadAllTextAsync(path,token));
   if(cache is null||cache.Version!=SchemaVersion||cache.Key!=key||cache.Result is null||cache.Result.GeoJson is null)throw new JsonException();
   using var parsed=JsonDocument.Parse(cache.Result.GeoJson);
   if(parsed.RootElement.GetProperty("type").GetString()!="FeatureCollection"||parsed.RootElement.GetProperty("features").GetArrayLength()!=cache.Result.Features)throw new JsonException();
   if(cache.Result.FetchedAt>DateTimeOffset.UtcNow.AddMinutes(5)||DateTimeOffset.UtcNow-cache.Result.FetchedAt>limits.CacheLifetime)return null;
   return cache.Result with{Requests=0,SkippedSamples=0,FromCache=true,Coverage=cache.Result.Coverage is { } coverage?coverage with{Requests=0}:null};
  }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException){throw new CadastreException("cache-schema","Cadastre cache is invalid; previous bytes retained. Refresh explicitly to replace it.");}
 }
 Task WriteCacheAsync(string path,CacheEnvelope cache,CancellationToken token)=>WriteJsonAsync(path,JsonSerializer.Serialize(cache,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}),token);
 async Task WriteJsonAsync(string path,string json,CancellationToken token)
 {
  await CacheGate.WaitAsync(token);var temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);var size=Encoding.UTF8.GetByteCount(json);
   var existing=Directory.EnumerateFiles(cacheDirectory,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
   var replaced=File.Exists(path)?new FileInfo(path).Length:0;
   if(existing-replaced+size>limits.MaxCacheBytes)throw new CadastreException("cache-budget","Cadastre disk cache budget reached. Previous point/aggregate files retained; select another cache or remove unneeded snapshots explicitly.");
   await File.WriteAllTextAsync(temporary,json,token);token.ThrowIfCancellationRequested();File.Move(temporary,path,true);
  }finally{try{if(File.Exists(temporary))File.Delete(temporary);}finally{CacheGate.Release();}}
 }
 public void Dispose()=>http.Dispose();
}
