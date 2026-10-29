using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class CadastreTests
{
 public static IEnumerable<(string Name, Action Run)> Cases()
 {
  yield return ("cadastre projection matches independent EPSG3857 reference and rejects invalid bounds", () => {
   var p=GeographicSurveyProjection.ToWebMercator(30,60);
   Check(Math.Abs(p.X-3339584.723798207)<0.001&&Math.Abs(p.Y-8399737.889818357)<0.001,"Incorrect spherical Mercator");
   var ll=GeographicSurveyProjection.FromWebMercator(p.X,p.Y);
   Check(Math.Abs(ll.Longitude-30)<1e-10&&Math.Abs(ll.Latitude-60)<1e-10,"Projection round trip drifted");
   Reject<ArgumentException>(()=>GeographicSurveyProjection.ProjectBounds(new(){West=1,East=0,South=50,North=51}));
   Reject<ArgumentException>(()=>GeographicSurveyProjection.ToWebMercator(0,90));
  });
  yield return ("cadastre normalization keeps holes metadata stable identities and observed attributes", () => {
   var result=GeographicSurvey.Normalize(Fixture(),Source());var f=JsonNode.Parse(result.GeoJson)!["features"]![0]!;
   Check(result.Features==1&&f["id"]!.ToString()=="cad-52-01-000001-7","Cadastral identity missing");
   Check(f["geometry"]!["coordinates"]!.AsArray().Count==2,"A courtyard hole disappeared");
   Check(Math.Abs((double)f["geometry"]!["coordinates"]![0]![1]![0]!-0.00089831528412)<1e-12,"Coordinates not WGS84");
   var props=f["properties"]!;Check(props["pz:parcel"]!.ToString()=="yes"&&props["name"]!.ToString()=="Fixture plot","Parcel/name not normalized");
   Check(props["pz:source_attributes"]!["options"]!["custom"]!.ToString()=="retained","Provider metadata lost");
   Check(props["pz:provenance"]!["attribution"]!.ToString()=="Fixture survey"&&props["pz:evidence"]!.ToString()=="observed","Attribution/evidence lost");
   var building=GeographicSurvey.Normalize(Fixture(),Source() with{Kind=GeographicSurveyKind.Building});
   var b=JsonNode.Parse(building.GeoJson)!["features"]![0]!["properties"]!;
   Check(b["building:levels"]!.ToString()=="2"&&b["building:material"]!.ToString()=="brick"&&b["start_date"]!.ToString()=="1974","Building attributes lost");
  });
  yield return ("survey offline normalization retains supplied attribution and separates inference", () => {
   var json=Fixture().Replace("\"cad_num\":\"52:01:000001:7\",", "");
   var context=Source() with{Source=GeographicSurveySourceKind.YandexSuppliedVectors,Evidence=GeographicSurveyEvidence.Inferred,Kind=GeographicSurveyKind.Generic};
   var a=GeographicSurvey.Normalize(json,context);var b=GeographicSurvey.Normalize(json,context);
   Check(a.GeoJson==b.GeoJson,"Offline IDs are unstable");var p=JsonNode.Parse(a.GeoJson)!["features"]![0]!["properties"]!;
   Check(p["pz:evidence"]!.ToString()=="inferred"&&p["pz:provenance"]!["source"]!.ToString()=="YandexSuppliedVectors","Inference represented as observation");
   Reject<ArgumentException>(()=>GeographicSurvey.Normalize(json,context with{Attribution=""}));
   Check(GeographicSurveySources.All.Count>=9&&!GeographicSurveySources.All.Single(s=>s.Source==GeographicSurveySourceKind.YandexSuppliedVectors).CanFetch,"Supplied vectors pretend to be a live API");
  });
  yield return ("cadastre grid skips polygon coverage but queries holes and deduplicates cad numbers", () => InDirectory(root=>{
   using var handler=new Handler(_=>Response(Fixture()));using var client=new CadastreClient(root,Fast(),handler);
   var r=client.FetchAsync(Request()).GetAwaiter().GetResult();
   Check(r.Features==1&&r.Requests==2&&r.SkippedSamples==2,"Grid failed to skip only actual polygon interiors or deduplicate");
   Check(handler.Uris.All(u=>u.Scheme=="https"&&u.Host=="nspd.gov.ru"&&u.AbsolutePath=="/api/aeggis/v3/36048/wms"&&u.Query.Contains("CRS=EPSG%3A3857")&&u.Query.Contains("REQUEST=GetFeatureInfo")),"WMS endpoint/CRS mismatch");
   var cached=client.FetchAsync(Request()).GetAwaiter().GetResult();Check(cached.FromCache&&cached.Requests==0&&handler.Uris.Count==2,"Cache required HTTP");
  }));
  yield return ("cadastre request cap fails without replacing previous cache", () => InDirectory(root=>{
   using(var client=new CadastreClient(root,Fast(),new Handler(_=>Response(Fixture()))))client.FetchAsync(Request()).GetAwaiter().GetResult();
   var path=Directory.GetFiles(root,"*.json").Single();var before=File.ReadAllBytes(path);
   using var handler=new Handler(_=>Response(Empty));using var limited=new CadastreClient(root,Fast() with{MaxRequests=1},handler);
   Reject<CadastreException>(()=>limited.FetchAsync(Request() with{Refresh=true}).GetAwaiter().GetResult());
   Check(handler.Uris.Count==1&&File.ReadAllBytes(path).SequenceEqual(before),"Request cap exceeded or previous cache changed");
  }));
  yield return ("cadastre cancellation redirect malformed data and response caps preserve cache", () => InDirectory(root=>{
   using(var client=new CadastreClient(root,Fast(),new Handler(_=>Response(Fixture()))))client.FetchAsync(Request()).GetAwaiter().GetResult();
   var path=Directory.GetFiles(root,"*.json").Single();var before=File.ReadAllBytes(path);
   foreach(var mode in new[]{"redirect","malformed","bytes","cancel","http"}){
    using var cts=new CancellationTokenSource();using var handler=new Handler(_=>{
     if(mode=="cancel"){cts.Cancel();cts.Token.ThrowIfCancellationRequested();}
     if(mode=="redirect"){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new("https://example.invalid/?token=secret");return redirect;}
     if(mode=="http")throw new HttpRequestException("https://example.invalid/?token=secret");
     return Response(mode=="malformed"?"not json":new string('x',2048));
    });using var client=new CadastreClient(root,Fast() with{MaxResponseBytes=1024},handler);
    try{client.FetchAsync(Request() with{Refresh=true},cts.Token).GetAwaiter().GetResult();throw new Exception("Expected intake failure");}
    catch(Exception e) when(e is CadastreException or OperationCanceledException){Check(!e.Message.Contains("secret"),"Sensitive upstream details exposed");}
    Check(File.ReadAllBytes(path).SequenceEqual(before)&&handler.Uris.Count==1,"Failure overwrote cache or followed redirect");
   }
  }));
  yield return ("cadastre invalid layer and overlarge bounds fail before HTTP", () => InDirectory(root=>{
   using var handler=new Handler(_=>Response(Empty));using var client=new CadastreClient(root,Fast(),handler);
   Reject<ArgumentException>(()=>client.FetchAsync(Request() with{Layers=[1]}).GetAwaiter().GetResult());
   Reject<ArgumentException>(()=>client.FetchAsync(Request() with{Bounds=new(){West=0,East=1,South=0,North=1}}).GetAwaiter().GetResult());
   Check(handler.Uris.Count==0,"Rejected inputs contacted provider");
  }));
  yield return ("survey normalization never promotes supplied inferred evidence to observed", () => {
   var json=JsonNode.Parse(Fixture())!;json["features"]![0]!["properties"]!["pz:evidence"]="inferred";
   json["features"]![0]!["properties"]!["pz:field_evidence"]=new JsonObject{["floors"]="inferred"};
   var r=GeographicSurvey.Normalize(json.ToJsonString(),Source() with{Kind=GeographicSurveyKind.Building});
   var p=JsonNode.Parse(r.GeoJson)!["features"]![0]!["properties"]!;
   Check(p["pz:evidence"]!.ToString()=="inferred"&&p["pz:provenance"]!["evidence"]!.ToString()=="inferred","Supplied inference was promoted to observation");
   Check(p["pz:field_evidence"]!["building:levels"]!.ToString()=="inferred","Canonical field lost inference marker");
  });
  yield return ("survey multipolygons retain holes foreign metadata and reject unclosed rings or CRS mismatch", () => {
   var input=JsonNode.Parse(Fixture())!;var g=input["features"]![0]!["geometry"]!;var polygon=g["coordinates"]!.DeepClone();g["type"]="MultiPolygon";g["coordinates"]=new JsonArray(polygon);
   input["metadata"]=new JsonObject{["surveyDate"]="1990-04-01"};input["crs"]=new JsonObject{["properties"]=new JsonObject{["name"]="EPSG:3857"}};
   var r=JsonNode.Parse(GeographicSurvey.Normalize(input.ToJsonString(),Source()).GeoJson)!;
   Check(r["features"]![0]!["geometry"]!["coordinates"]![0]!.AsArray().Count==2&&r["metadata"]!["surveyDate"]!.ToString()=="1990-04-01"&&r["crs"] is null,"MultiPolygon or metadata damaged");
   Reject<ArgumentException>(()=>GeographicSurvey.Normalize(input.ToJsonString(),Source() with{InputCrs=GeographicSurveyCrs.Wgs84}));
   g["coordinates"]![0]![0]![4]![0]=5;
   Reject<ArgumentException>(()=>GeographicSurvey.Normalize(input.ToJsonString(),Source()));
  });
  yield return ("cadastre serializes and rate limits requests across client instances", () => InDirectory(root=>{
   var active=0;var peak=0;var starts=new List<long>();
   using var handler=new AsyncHandler(async token=>{
    var current=Interlocked.Increment(ref active);peak=Math.Max(peak,current);lock(starts)starts.Add(System.Diagnostics.Stopwatch.GetTimestamp());
    try{await Task.Delay(30,token);return Response(Empty);}finally{Interlocked.Decrement(ref active);}
   });
   using var a=new CadastreClient(Path.Combine(root,"a"),Fast(),handler);using var b=new CadastreClient(Path.Combine(root,"b"),Fast(),handler);
   Task.WhenAll(a.FetchAsync(Request() with{GridSize=1}),b.FetchAsync(Request() with{GridSize=1})).GetAwaiter().GetResult();
   Check(peak==1&&starts.Count==2&&System.Diagnostics.Stopwatch.GetElapsedTime(starts[0],starts[1]).TotalMilliseconds>=90,"Global concurrency/rate cap failed");
  }));
  yield return ("cadastre timeouts and total streamed byte budget leave no partial cache", () => InDirectory(root=>{
   using(var handler=new AsyncHandler(async token=>{await Task.Delay(5000,token);return Response(Empty);}))
   using(var client=new CadastreClient(root,Fast() with{RequestTimeout=TimeSpan.FromMilliseconds(50)},handler)){
    try{client.FetchAsync(Request()).GetAwaiter().GetResult();throw new Exception("Expected timeout");}catch(CadastreException e){Check(e.Code=="timeout","Wrong timeout diagnostic");}
   }
   using(var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Empty)))}))
   using(var client=new CadastreClient(root,Fast() with{MaxResponseBytes=Empty.Length+5,MaxTotalBytes=Empty.Length+5},handler)){
    try{client.FetchAsync(Request()).GetAwaiter().GetResult();throw new Exception("Expected byte budget");}catch(CadastreException e){Check(e.Code=="byte-budget","Streaming budget failure misclassified");}
    Check(handler.Uris.Count==2,"Total-byte budget made excess requests");
   }
   Check(!Directory.GetFiles(root).Any(),"Failed operation published partial cache");
  }));
  yield return ("cadastre layer coverage is independent and layer failure preserves previous result", () => InDirectory(root=>{
   var req=Request() with{Layers=[36049,36048]};
   using(var client=new CadastreClient(root,Fast(),new Handler(_=>Response(Fixture())))){
    var r=client.FetchAsync(req).GetAwaiter().GetResult();Check(r.Requests==4&&r.Features==1,"Coverage leaked across layers or identities were duplicated");
   }
   var path=Directory.GetFiles(root,"*.json").Single();var before=File.ReadAllBytes(path);
   using var handler=new Handler(m=>m.RequestUri!.AbsolutePath.Contains("36049")?new(HttpStatusCode.ServiceUnavailable):Response(Fixture()));using var failed=new CadastreClient(root,Fast(),handler);
   Reject<CadastreException>(()=>failed.FetchAsync(req with{Refresh=true}).GetAwaiter().GetResult());
   Check(File.ReadAllBytes(path).SequenceEqual(before),"Later layer failure replaced previous result");
  }));
  yield return ("cadastre endpoint extension serves capabilities offline normalization and cached fetch", () => InDirectory(root=>{
   async Task Exercise(){
    var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton(new CadastreClient(root,Fast(),new Handler(_=>Response(Fixture()))));
    await using var app=builder.Build();app.MapCadastreEndpoints();await app.StartAsync();
    try{
     var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
     using var http=new HttpClient{BaseAddress=new(address)};
     var sources=JsonNode.Parse(await http.GetStringAsync("/api/geographic/sources"))!.AsArray();Check(sources.Any(s=>s!["source"]!.ToString()=="Cadastre"),"Registry is not usable by clients");
     var body=System.Text.Json.JsonSerializer.Serialize(new CadastreNormalizeRequest(Fixture(),Source()));
     using var normalized=await http.PostAsync("/api/cadastre/normalize",new StringContent(body,Encoding.UTF8,"application/json"));
     Check(normalized.IsSuccessStatusCode&&JsonNode.Parse(await normalized.Content.ReadAsStringAsync())!["features"]!.GetValue<int>()==1,"Offline HTTP normalize failed");
     using var invalid=await http.PostAsync("/api/cadastre/normalize",new StringContent("{}",Encoding.UTF8,"application/json"));Check(invalid.StatusCode==HttpStatusCode.BadRequest,"Missing upload became a server error");
     var fetchBody=System.Text.Json.JsonSerializer.Serialize(Request());
     using var fetched=await http.PostAsync("/api/cadastre/fetch",new StringContent(fetchBody,Encoding.UTF8,"application/json"));Check(fetched.IsSuccessStatusCode,"HTTP fetch failed");
     using var cached=await http.PostAsync("/api/cadastre/fetch",new StringContent(fetchBody,Encoding.UTF8,"application/json"));
     Check(cached.IsSuccessStatusCode&&JsonNode.Parse(await cached.Content.ReadAsStringAsync())!["fromCache"]!.GetValue<bool>(),"HTTP cached fetch failed");
    }finally{await app.StopAsync();}
   }
   Exercise().GetAwaiter().GetResult();
  }));
  yield return ("cadastre nine metre quarter grid preflights full coverage and resumes cached points", () => InDirectory(root=>{
   var ne=GeographicSurveyProjection.FromWebMercator(160,270);var req=new CadastreRequest{Bounds=new(){West=0,South=0,East=ne.Longitude,North=ne.Latitude},Layers=[36048],StepMetres=9};
   using(var handler=new Handler(_=>Response(Empty)))using(var client=new CadastreClient(root,Fast() with{MaxRequests=2},handler)){
    try{client.FetchAsync(req).GetAwaiter().GetResult();throw new Exception("Expected resumable budget exhaustion");}
    catch(CadastreException e){Check(e.Progress is{SamplesPlanned:540,SamplesCompleted:2}&&e.Progress.CoverageFraction<1,"Quarter grid silently reduced or progress missing");}
    Check(handler.Uris.Count==2,"Request budget exceeded");
   }
   using var nextHandler=new Handler(_=>Response(Empty));using var next=new CadastreClient(root,Fast() with{MaxRequests=1},nextHandler);
   try{next.FetchAsync(req).GetAwaiter().GetResult();throw new Exception("Expected next resumable budget stop");}
   catch(CadastreException e){Check(e.Progress is{SamplesCompleted:3,PointsFromCache:2,Requests:1},"Continuation did not reuse per-point cache");}
   Check(nextHandler.Uris.Count==1,"Continuation spent requests on cached points");
   using var rejectedHandler=new Handler(_=>Response(Empty));using var rejected=new CadastreClient(root,Fast(),rejectedHandler);
   Reject<ArgumentException>(()=>rejected.FetchAsync(req with{Bounds=new(){West=0,South=0,East=.01,North=.01}}).GetAwaiter().GetResult());
   Check(rejectedHandler.Uris.Count==0,"Overlarge nine metre plan contacted provider");
  }));
 }
 const string Empty="{\"type\":\"FeatureCollection\",\"features\":[]}";
 public static string Fixture()=>"""
 {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"options":{"cad_num":"52:01:000001:7","name":"Fixture plot","floors":2,"materials":"brick","year_built":1974,"custom":"retained"}},"geometry":{"type":"Polygon","coordinates":[[[0,0],[100,0],[100,100],[0,100],[0,0]],[[60,60],[90,60],[90,90],[60,90],[60,60]]]}}]}
 """;
 static GeographicSurveyContext Source()=>new(){Source=GeographicSurveySourceKind.Cadastre,Attribution="Fixture survey",InputCrs=GeographicSurveyCrs.Epsg3857,Kind=GeographicSurveyKind.Parcel};
 static CadastreLimits Fast()=>new(){MinimumRequestInterval=TimeSpan.FromMilliseconds(100)};
 static CadastreRequest Request(){var ne=GeographicSurveyProjection.FromWebMercator(100,100);return new(){Bounds=new(){West=0,South=0,East=ne.Longitude,North=ne.Latitude},Layers=[36048],GridSize=2};}
 static HttpResponseMessage Response(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
 static void InDirectory(Action<string> action){var root=Path.Combine(Path.GetTempPath(),"cadastre-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{action(root);}finally{Directory.Delete(root,true);}}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
 {
  public List<Uri> Uris {get;}=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Uris.Add(request.RequestUri!);return Task.FromResult(send(request));}
 }
 sealed class AsyncHandler(Func<CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(token);
 }
}
