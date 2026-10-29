using System.Net;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class OsmResilienceTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()
 {
  yield return("OSM resilient world snapshots retain more than a single-map feature allowance",()=>WithDirectory(root=>{
   var files=new OsmSourceFiles(root);var data=JsonSerializer.Serialize(new{elements=Enumerable.Range(1,30_001).Select(id=>new{type="node",id,lon=30.0001,lat=50.0001,tags=new{amenity="bench"}})});
   var saved=files.SaveAsync(new(data,"download.geofabrik.de",[]),Elsewhere()).GetAwaiter().GetResult();Check(saved.Features==30_001,"Snapshot silently omitted source features");
   Check(files.ReadAsync(saved.Id,Elsewhere(),64_000_000).GetAwaiter().GetResult().Data==data,"World snapshot did not round trip");
   Reject(()=>files.ReadAsync(saved.Id,Elsewhere(),16_000_000).GetAwaiter().GetResult());
  }));
  yield return("OSM resilient stalled regional transfer offers resume without online fallback",()=>WithDirectory(root=>{
   var catalogue=GeofabrikTests.Catalogue(GeofabrikTests.Region("fixture",null,32,54.6,32.2,54.9));var handler=new Handler(i=>i==1?Response(catalogue):GeofabrikTests.SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(900),16));using var client=new OsmClient(root,handler,TimeSpan.Zero,importTimeouts:GeofabrikTests.FastTiming());
   try{client.FetchForImport(Smolensk(),"auto",null,default,world:true).GetAwaiter().GetResult();}
   catch(HttpRequestException e){Check(e is RegionalTransferTimeoutException&&handler.Calls==2&&handler.Hosts.All(h=>h=="download.geofabrik.de")&&e.Message.Contains("stalled"),"A resumable transfer timeout was replaced by online provider failure");return;}
   throw new Exception("A stalled regional source did not fail");
  }));
  yield return("OSM resilient timed out world job exposes resume and preserves incomplete source",()=>WithDirectory(root=>{
   var catalogue=GeofabrikTests.Catalogue(GeofabrikTests.Region("fixture",null,32,54.6,32.2,54.9));var handler=new Handler(i=>i==1?Response(catalogue):GeofabrikTests.SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(900),16));using var client=new OsmClient(Path.Combine(root,"cache"),handler,TimeSpan.Zero,importTimeouts:GeofabrikTests.FastTiming());
   var assets=new GameAssetCatalog(root,"");using var service=new OsmWorldImportService(client,assets,Path.Combine(root,"jobs"),timeouts:GeofabrikTests.FastTiming());var id=JsonSerializer.SerializeToElement(service.Start(new(Smolensk(),SectionSize:256))).GetProperty("id").GetString()!;
   var wait=System.Diagnostics.Stopwatch.StartNew();JsonElement state=default;while(wait.Elapsed<TimeSpan.FromSeconds(5)){state=JsonSerializer.SerializeToElement(service.Status(id));if(state.GetProperty("status").GetString()=="failed")break;Thread.Sleep(10);}
   Check(state.GetProperty("status").GetString()=="failed"&&state.GetProperty("recovery").GetString()=="resume-download"&&!state.GetProperty("retainedRegionalSource").GetBoolean()&&state.GetProperty("sourceId").ValueKind==JsonValueKind.Null,"Timeout status confuses a partial transfer with a ready file");
   Check(Directory.GetFiles(Path.Combine(root,"cache","regions"),"*.part").Length==1&&handler.Calls==2,"World timeout lost the partial source");Reject(()=>service.ResultFile(id));
  }));
  yield return("OSM resilient user cancellation is not converted into a resumable timeout",()=>WithDirectory(root=>{
   var catalogue=GeofabrikTests.Catalogue(GeofabrikTests.Region("fixture",null,32,54.6,32.2,54.9));var handler=new Handler(i=>i==1?Response(catalogue):GeofabrikTests.SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(900),16));using var client=new OsmClient(root,handler,TimeSpan.Zero,importTimeouts:GeofabrikTests.FastTiming());using var cancelled=new CancellationTokenSource();var progress=new InlineProgress<OsmProgress>(p=>{if(p.Phase=="region-download"&&p.BytesReceived>0)cancelled.Cancel();});
   try{client.FetchForImport(Smolensk(),"auto",null,cancelled.Token,progress,world:true).GetAwaiter().GetResult();}catch(OperationCanceledException){Check(handler.Calls==2,"Cancellation fell through to an online provider");return;}throw new Exception("User cancellation became a different failure");
  }));
  yield return("OSM resilient final error retains the regional failure reason",()=>WithDirectory(root=>{
   var handler=new Handler(_=>new(HttpStatusCode.ServiceUnavailable));using var client=new OsmClient(root,handler,TimeSpan.Zero);
   try{client.FetchForImport(Elsewhere(),"auto",null,default,world:true).GetAwaiter().GetResult();}
   catch(HttpRequestException e){Check(e.Message.Contains("Geofabrik")&&e.Message.Contains("503")&&e.Message.Contains("Saved file")&&handler.Calls==4,"The regional error was replaced by a generic online outage");return;}
   throw new Exception("Provider failures became a successful map");
  }));
  yield return("OSM resilient downloaded PBF processing failure cannot masquerade as an online outage",()=>WithDirectory(root=>{
   var catalogue=GeofabrikTests.Catalogue(GeofabrikTests.Region("fixture",null,32,54.6,32.2,54.9));var bytes=UpstreamTests.PbfFixture(true);
   var handler=new Handler(i=>i==1?Response(catalogue):new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});using var client=new OsmClient(root,handler,TimeSpan.Zero);
   try{client.FetchForImport(Smolensk(),"auto",null,default,maxBytes:1,world:true).GetAwaiter().GetResult();}
   catch(ArgumentException e){var parts=Directory.GetFiles(Path.Combine(root,"regions"),"*.part");Check(handler.Calls==2&&parts.Length==1&&File.ReadAllBytes(parts[0]).SequenceEqual(bytes),"A processing budget error deleted or redownloaded the complete source");Check(e.Message.Contains("downloaded")&&e.Message.Contains("retained")&&e.Message.Contains("extract"),"The error does not distinguish download from extraction");return;}
   throw new Exception("Processing failure was hidden or produced a map");
  }));
  foreach(var refusal in new[]{HttpStatusCode.Forbidden,HttpStatusCode.NotAcceptable})yield return("OSM resilient provider refusal continues to another host "+(int)refusal,()=>WithDirectory(root=>{
   var handler=new Handler(i=>i==1?new(refusal):Response(Overpass()));using var client=new OsmClient(root,handler,TimeSpan.Zero);var result=client.FetchForImport(Elsewhere(),"overpass",null,default,world:true).GetAwaiter().GetResult();Check(handler.Calls==2&&result.Source=="overpass.private.coffee","Provider-specific refusal stopped the fallback chain");
  }));
  yield return("OSM resilient auto prefers independent regional source and keeps provenance",()=>WithDirectory(root=>{
   var catalogue=GeofabrikTests.Catalogue(GeofabrikTests.Region("fixture",null,32,54.6,32.2,54.9));var handler=new Handler(i=>i==1?Response(catalogue):new(HttpStatusCode.OK){Content=new ByteArrayContent(UpstreamTests.PbfFixture(true))});using var client=new OsmClient(root,handler,TimeSpan.Zero);
   var download=client.FetchForImport(Smolensk(),"auto",null,default).GetAwaiter().GetResult();Check(handler.Calls==2&&handler.Hosts.All(h=>h=="download.geofabrik.de"),"Automatic import contacted Overpass instead of its independent source");
   Check(download.SourceId is not null&&download.DataTimestamp is not null&&!download.FromCache&&download.Requests==1&&download.Sources.Count==1,"Regional metadata missing");
   var again=client.FetchForImport(Smolensk(),"auto",null,default).GetAwaiter().GetResult();Check(again.FromCache&&again.Requests==0&&handler.Calls==2&&again.SourceId==download.SourceId,"Validated regional cache was not reused");
   using var source=JsonDocument.Parse(File.ReadAllText(client.Sources.DownloadFile(download.SourceId!)));Check(source.RootElement.GetProperty("elements").GetArrayLength()>0,"The source cannot be downloaded for offline use");
  }));
  yield return("OSM resilient stale regional cache survives provider failure with explicit date",()=>WithDirectory(root=>{
   var file=Path.Combine(root,"region-smolensk-oblast.pbf");var original=UpstreamTests.PbfFixture(true);File.WriteAllBytes(file,original);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-9));
   var handler=new Handler(_=>new(HttpStatusCode.TooManyRequests));using var client=new OsmClient(root,handler,TimeSpan.Zero);var download=client.FetchExtractDetailed(Smolensk(),default).GetAwaiter().GetResult();
   Check(handler.Calls==1&&download.FromCache&&download.DataTimestamp is not null&&download.Warnings.Any(w=>w.Contains("out of date"))&&File.ReadAllBytes(file).SequenceEqual(original),"A failed refresh destroyed or silently reused stale data");Check(!Directory.GetFiles(root,"*.tmp").Any(),"Partial extract left behind");
  }));
  yield return("OSM resilient corrupt regional cache is replaced only by a valid extract",()=>WithDirectory(root=>{
   var file=Path.Combine(root,"region-smolensk-oblast.pbf");File.WriteAllBytes(file,[1,2,3]);var bytes=UpstreamTests.PbfFixture(true);var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});using var client=new OsmClient(root,handler,TimeSpan.Zero);
   var result=client.FetchExtractDetailed(Smolensk(),default).GetAwaiter().GetResult();Check(!result.FromCache&&handler.Calls==1&&File.ReadAllBytes(file).SequenceEqual(bytes),"Corrupt fresh cache prevented recovery");
  }));
  yield return("OSM resilient automatic regional failure falls back to complete online data",()=>WithDirectory(root=>{
   var handler=new Handler(i=>i==1?new(HttpStatusCode.ServiceUnavailable):Response(Overpass()));using var client=new OsmClient(root,handler,TimeSpan.Zero);
   var result=client.FetchForImport(Smolensk(),"auto",null,default,world:true).GetAwaiter().GetResult();Check(handler.Calls==2&&handler.Hosts.SequenceEqual(new[]{"download.geofabrik.de","overpass-api.de"})&&result.SourceId is not null&&result.Warnings.Any(w=>w.Contains("regional source could not be loaded")),"Independent source refusal prevented online recovery");
  }));
  yield return("OSM resilient auto reuses last downloaded snapshot when all online providers fail",()=>WithDirectory(root=>{
   var handler=new Handler(_=>new(HttpStatusCode.ServiceUnavailable));using var client=new OsmClient(root,handler,TimeSpan.Zero);var old=client.Sources.SaveAsync(new(Overpass(),"overpass-api.de",[]){DataTimestamp="2026-10-20T00:00:00Z",RetrievedAtUtc=DateTimeOffset.UtcNow.AddDays(-10)},Elsewhere()).GetAwaiter().GetResult();
   var result=client.FetchForImport(Elsewhere(),"auto",null,default,world:true).GetAwaiter().GetResult();Check(handler.Calls==4&&!handler.Hosts.Contains("api.openstreetmap.org")&&result.FromCache&&result.SourceId==old.Id&&result.Warnings.Any(w=>w.Contains("out of date")),"Last good snapshot was lost or the editing API used for a bulk import");
  }));
  yield return("OSM resilient local GeoJSON is offline and bound to its prepared area",()=>WithDirectory(root=>{
   var handler=new Handler(_=>throw new Exception("Offline file import contacted the network"));using var client=new OsmClient(root,handler,TimeSpan.Zero);var saved=client.Sources.ImportAsync(new("../../a.geojson",Elsewhere(),Json:GeoJson())).GetAwaiter().GetResult();
   var data=client.FetchForImport(Elsewhere(),"file",saved.Id,default,world:true).GetAwaiter().GetResult();Check(data.Data==GeoJson()&&data.SourceId==saved.Id&&data.Requests==0&&data.FromCache&&handler.Calls==0&&saved.Source=="local file: a.geojson","Offline source or its safe label changed");
   var larger=Elsewhere();larger.North+=.001;Reject(()=>client.FetchForImport(larger,"file",saved.Id,default).GetAwaiter().GetResult());Reject(()=>client.Sources.DownloadFile("../unsafe"));Check(Directory.GetFiles(root,"*.json").Length==0,"A file escaped the saved-source directory");
  }));
  yield return("OSM resilient local PBF retains complete geometry and metadata without network",()=>WithDirectory(root=>{
   var files=new OsmSourceFiles(root);var saved=files.ImportAsync(new("custom.osm.pbf",Smolensk(),PbfBase64:Convert.ToBase64String(UpstreamTests.PbfFixture(true)))).GetAwaiter().GetResult();
   var data=files.ReadAsync(saved.Id,Smolensk(),16_000_000).GetAwaiter().GetResult();using var json=JsonDocument.Parse(data.Data);Check(saved.Format=="overpass"&&saved.DataTimestamp is not null&&json.RootElement.GetProperty("elements").GetArrayLength()>0&&data.Warnings.Any(w=>w.Contains("No OSM server")),"Local PBF was not normalized into a reusable offline source");
  }));
  yield return("OSM resilient partial empty or malformed source never becomes reusable",()=>WithDirectory(root=>{
   var files=new OsmSourceFiles(root);foreach(var json in new[]{"[]","{","{\"elements\":[]}","{\"remark\":\"runtime error\",\"elements\":[]}"})Reject(()=>files.ImportAsync(new("bad.json",Elsewhere(),Json:json)).GetAwaiter().GetResult());
   Reject(()=>files.ImportAsync(new("bad.pbf",Elsewhere(),PbfBase64:"AQID")).GetAwaiter().GetResult());Reject(()=>files.ImportAsync(new("both.json",Elsewhere(),Json:GeoJson(),PbfBase64:"AA==")).GetAwaiter().GetResult());Check(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any(),"Rejected source persisted as a reusable snapshot");
  }));
  yield return("OSM resilient cancelled local source preparation creates no snapshot",()=>WithDirectory(root=>{
   var files=new OsmSourceFiles(root);using var cancel=new CancellationTokenSource();cancel.Cancel();try{files.ImportAsync(new("source.json",Elsewhere(),Json:GeoJson()),cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){Check(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any(),"Cancelled source was promoted");return;}throw new Exception("Cancelled source preparation succeeded");
  }));
  yield return("OSM resilient missing backup yields actionable error without a fake empty map",()=>WithDirectory(root=>{
   var handler=new Handler(_=>new(HttpStatusCode.ServiceUnavailable));using var client=new OsmClient(root,handler,TimeSpan.Zero);try{client.FetchForImport(Elsewhere(),"auto",null,default,world:true).GetAwaiter().GetResult();}catch(HttpRequestException e){Check(e.Message.Contains("Saved file")&&e.Message.Contains("preserved")&&handler.Calls==4,"No actionable independent fallback in final error");return;}throw new Exception("Provider failure became a successful map");
  }));
 }
 private static GeoBounds Smolensk()=>new(){South=54.777,North=54.779,West=32.046,East=32.049};
 private static GeoBounds Elsewhere()=>new(){South=50,North=50.001,West=30,East=30.001};
 private static string Overpass()=>"""{"osm3s":{"timestamp_osm_base":"2026-10-20T00:00:00Z"},"elements":[{"type":"way","id":1,"tags":{"highway":"residential"},"geometry":[{"lon":30,"lat":50},{"lon":30.001,"lat":50.001}]}]}""";
 private static string GeoJson()=>"""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"highway":"residential"},"geometry":{"type":"LineString","coordinates":[[30,50],[30.001,50.001]]}}]}""";
 private sealed class Handler(Func<int,HttpResponseMessage> response):HttpMessageHandler{public int Calls;public List<string> Hosts=[];protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Hosts.Add(request.RequestUri!.Host);return Task.FromResult(response(++Calls));}}
 private static HttpResponseMessage Response(string data)=>new(HttpStatusCode.OK){Content=new StringContent(data)};
 private static void WithDirectory(Action<string> action){var root=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"work","osm-resilience-tests"));Directory.CreateDirectory(root);var path=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);try{action(path);}finally{if(!Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe test cleanup");Directory.Delete(path,true);}}
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 private static void Reject(Action action){try{action();}catch(Exception e) when(e is ArgumentException or JsonException){return;}throw new Exception("Invalid source accepted");}
}
