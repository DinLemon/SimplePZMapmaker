using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class GeofabrikTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()
 {
  yield return("regional stalled transfer stops on idle budget and retains partial bytes",()=>WithDirectory(root=>{
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   using var http=new HttpClient(new Handler(_=>SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(900),16)));
   try{new GeofabrikClient(root,http,FastTiming()).FetchAsync(Smolensk(),default).GetAwaiter().GetResult();}
   catch(HttpRequestException e){Check(e.Message.Contains("stalled")&&Directory.GetFiles(root,"*.part").Any(p=>new FileInfo(p).Length>0),"Idle failure lost partial bytes or its cause");return;}
   throw new Exception("A stalled transfer ignored its idle budget");
  }));
  yield return("regional healthy transfer outlives idle interval while byte progress continues",()=>WithDirectory(root=>{
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   using var http=new HttpClient(new Handler(_=>SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(70),16)));
   // Test the sliding idle window independently of slow CI disk writes and the separate hard-cap case.
   var timing=FastTiming() with{TransferMaximum=TimeSpan.FromMinutes(1),ProcessingMaximum=TimeSpan.FromSeconds(30)};var elapsed=System.Diagnostics.Stopwatch.StartNew();
   var result=new GeofabrikClient(root,http,timing).FetchAsync(Smolensk(),default).GetAwaiter().GetResult();Check(result.Requests==1&&elapsed.Elapsed>timing.TransferIdle,"Healthy progressing source did not outlive its idle window and complete");
  }));
  yield return("regional hard transfer cap remains bounded despite healthy byte progress",()=>WithDirectory(root=>{
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   using var http=new HttpClient(new Handler(_=>SlowResponse(UpstreamTests.PbfFixture(true),TimeSpan.Zero,TimeSpan.FromMilliseconds(70),16)));
   try{new GeofabrikClient(root,http,FastTiming() with{TransferMaximum=TimeSpan.FromMilliseconds(350)}).FetchAsync(Smolensk(),default).GetAwaiter().GetResult();}
   catch(HttpRequestException e){Check(e.Message.Contains("maximum"),"Hard transfer cap was mistaken for inactivity");return;}
   throw new Exception("A progressing transfer ignored its hard cap");
  }));
  yield return("regional local processing has its own budget and retains the complete source",()=>WithDirectory(root=>{
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));using var http=new HttpClient(new Handler(_=>Response(UpstreamTests.PbfFixture(true))));
   var delayed=false;var progress=new InlineProgress<OsmProgress>(p=>{if(p.Phase=="pbf-index"&&!delayed){delayed=true;Thread.Sleep(300);}});
   try{new GeofabrikClient(root,http,FastTiming() with{ProcessingMaximum=TimeSpan.FromMilliseconds(80)}).FetchAsync(Smolensk(),default,progress).GetAwaiter().GetResult();}
   catch(ArgumentException e){Check(e is RegionalProcessingException{Retained:true}&&e.Message.Contains("processing")&&Directory.GetFiles(root,"*.part").Length==1,"Processing timeout lost the complete source or became a network failure");return;}
   throw new Exception("Local processing ignored its separate deadline");
  }));
  yield return("regional malformed wire lengths are discarded instead of retrying as a budget failure",()=>WithDirectory(root=>{
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   foreach(var length in new[]{0,-1,65536}){
    var bytes=UpstreamTests.PbfFixture(true).Concat(new byte[4]).ToArray();System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(bytes.Length-4),length);using var http=new HttpClient(new Handler(_=>Response(bytes)));
    Fails<ArgumentException>(()=>new GeofabrikClient(root,http).FetchAsync(Smolensk(),default).GetAwaiter().GetResult());
    Check(!Directory.EnumerateFiles(root,"*.part*").Any()&&!Directory.EnumerateFiles(root,"*.osm.pbf").Any(),"Malformed wire framing was retained as a completed pending source");
   }
  }));
  yield return("regional 416 completion remains reusable offline after extraction fails",()=>WithDirectory(root=>{
   var region=FixtureRegion();var bytes=UpstreamTests.PbfFixture(true);var partial=Path.Combine(root,RegionalDownloadCache.CacheFileName(region.Id)+".part");File.WriteAllBytes(partial,bytes);File.WriteAllText(partial+".json",JsonSerializer.Serialize(new RegionalFileMetadata(region.Url,bytes.Length,"\"snapshot\"",null,DateTimeOffset.UtcNow)));
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));var calls=0;
   using var http=new HttpClient(new Handler(_=>{if(++calls>1)throw new HttpRequestException("Now offline");var response=new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable){Content=new ByteArrayContent([])};response.Content.Headers.ContentRange=new(bytes.Length);return response;}));var client=new GeofabrikClient(root,http);
   Fails<ArgumentException>(()=>client.FetchAsync(Smolensk(),default,maxBytes:1).GetAwaiter().GetResult());var result=client.FetchAsync(Smolensk(),default).GetAwaiter().GetResult();Check(result.FromCache&&result.Requests==0&&calls==1,"A 416-confirmed complete source required network again");
  }));
  yield return("regional cold output limit retains complete download without marking it validated",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var handler=new Handler(_=>Response(bytes));using var http=new HttpClient(handler);
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   Fails<ArgumentException>(()=>new GeofabrikClient(root,http).FetchAsync(Smolensk(),default,maxBytes:1).GetAwaiter().GetResult());
   var cache=new RegionalDownloadCache(root,http);var partial=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture")+".part");
   Check(File.Exists(partial)&&File.ReadAllBytes(partial).SequenceEqual(bytes)&&cache.Cached(FixtureRegion()) is null,"A cold budget failure deleted the source or promoted an incompletely processed file");
  }));
  yield return("regional complete pending source is reprocessed offline after a budget failure",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var calls=0;using var http=new HttpClient(new Handler(_=>++calls==1?Response(bytes):throw new HttpRequestException("Network is now offline")));
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));var client=new GeofabrikClient(root,http);
   Fails<ArgumentException>(()=>client.FetchAsync(Smolensk(),default,maxBytes:1).GetAwaiter().GetResult());
   var result=client.FetchAsync(Smolensk(),default).GetAwaiter().GetResult();Check(result.FromCache&&result.Requests==0&&calls==1,"A complete pending source required an online redownload");
   Check(new RegionalDownloadCache(root,http).Cached(FixtureRegion()) is not null,"Successful retry did not validate and promote the retained source");
  }));
  yield return("regional output limit preserves an already validated source cache",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var handler=new Handler(_=>Response(bytes));using var http=new HttpClient(handler);var cache=new RegionalDownloadCache(root,http);var region=FixtureRegion();
   using(var lease=cache.AcquireAsync(region,default).GetAwaiter().GetResult()){PbfExtractReader.Read(lease.Path,Smolensk());lease.Commit();}
   File.WriteAllText(Path.Combine(root,"catalogue.json"),Catalogue(Region("fixture",null,32,54.6,32.2,54.9)));
   Fails<ArgumentException>(()=>new GeofabrikClient(root,http).FetchAsync(Smolensk(),default,maxBytes:1).GetAwaiter().GetResult());
   Check(cache.Cached(region) is not null&&handler.Calls==1,"A selected-data limit discarded or redownloaded the valid regional source");
  }));
  yield return("regional malformed JSON is a recoverable source error",()=>{
   Fails<ArgumentException>(()=>new GeofabrikCatalog("not JSON"));
  });
  yield return("regional downloads follow official dated redirects preserving resume and latest identity",()=>WithDirectory(root=>{
   var region=FixtureRegion();var bytes=UpstreamTests.PbfFixture(true);var part=Path.Combine(root,RegionalDownloadCache.CacheFileName(region.Id)+".part");var split=bytes.Length/2;File.WriteAllBytes(part,bytes[..split]);File.WriteAllText(part+".json",JsonSerializer.Serialize(new RegionalFileMetadata(region.Url,bytes.Length,"\"snapshot\"",null,DateTimeOffset.UtcNow)));
   var dated=region.Url.Replace("-latest.osm.pbf","-261020.osm.pbf",StringComparison.Ordinal);
   var handler=new Handler(request=>{
    if(request.RequestUri!.AbsoluteUri==region.Url){var redirect=new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);redirect.Headers.Location=new(dated);return redirect;}
    Check(request.RequestUri.AbsoluteUri==dated,"Redirect changed the selected regional source");
    if(request.Method==HttpMethod.Head){var head=Response([]);head.Content.Headers.ContentLength=bytes.Length;return head;}
    Check(request.Headers.Range?.Ranges.Single().From==split&&request.Headers.IfRange?.EntityTag?.Tag=="\"snapshot\"","Redirect lost the resume headers");var response=Response(bytes[split..],HttpStatusCode.PartialContent);response.Content.Headers.ContentRange=new(split,bytes.Length-1,bytes.Length);return response;
   });using var http=new HttpClient(handler);var cache=new RegionalDownloadCache(root,http);
   Check(cache.SizeAsync(region,default).GetAwaiter().GetResult()==bytes.Length,"HEAD did not follow the official dated redirect");
   using var lease=cache.AcquireAsync(region,default).GetAwaiter().GetResult();Check(File.ReadAllBytes(lease.Path).SequenceEqual(bytes)&&lease.Metadata.Url==region.Url,"Redirect broke resumed bytes or logical latest identity");lease.Commit();Check(cache.Cached(region)?.Url==region.Url,"Dated redirect made the latest cache unusable");
  }));
  yield return("regional redirects reject other origins paths queries and unbounded chains",()=>WithDirectory(root=>{
   foreach(var location in new[]{"https://evil.example/test/fixture-261020.osm.pbf","http://download.geofabrik.de/test/fixture-261020.osm.pbf","https://download.geofabrik.de/other/fixture-261020.osm.pbf","https://download.geofabrik.de/test/other-261020.osm.pbf","https://download.geofabrik.de/test/fixture-261020.osm.pbf?x=1","https://download.geofabrik.de/test/fixture-261020.osm.pbf#fragment","https://user@download.geofabrik.de/test/fixture-261020.osm.pbf","https://download.geofabrik.de:444/test/fixture-261020.osm.pbf"}){
    var handler=new Handler(_=>{var redirect=new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);redirect.Headers.Location=new(location);return redirect;});using var http=new HttpClient(handler);Fails<HttpRequestException>(()=>new RegionalDownloadCache(root,http).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult());Check(handler.Calls==1,"Unsafe redirect was contacted");
   }
   var loop=new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);response.Headers.Location=new("fixture-261020.osm.pbf",UriKind.Relative);return response;});using var looping=new HttpClient(loop);Fails<HttpRequestException>(()=>new RegionalDownloadCache(root,looping).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult());Check(loop.Calls<=4,"Official redirects exceeded the hop budget");
  }));
  yield return("global region catalogue selects smallest complete region and covers shared borders",()=>{
   var catalogue=new GeofabrikCatalog(Catalogue(Region("country",null,29.9,49.9,30.1,50.1),Region("west","country",29.9,49.9,30.001,50.1),Region("east","country",30.001,49.9,30.1,50.1)));
   var left=catalogue.Select(new(){West=30,East=30.0005,South=50,North=50.001});Check(left.Count==1&&left[0].Id=="west","Parent selected instead of the smallest full extract");
   var border=catalogue.Select(new(){West=30,East=30.002,South=50,North=50.001});Check(border.Count==2&&border.Select(r=>r.Id).Order().SequenceEqual(new[]{"east","west"}),"A cross-border area is not fully covered by adjacent extracts");
  });
  yield return("global catalogue accepts slash-qualified US identities without filesystem traversal",()=>{
   var catalogue=new GeofabrikCatalog(Catalogue(Region("us/kentucky",null,29.9,49.9,30.1,50.1)));Check(catalogue.Select(new(){West=30,East=30.001,South=50,North=50.001}).Single().Id=="us/kentucky","Official US region identity was rejected");Check(!RegionalDownloadCache.CacheFileName("us/kentucky").Contains('/'),"Logical region ID became a path");Check(RegionalDownloadCache.CacheFileName("us/kentucky")!=RegionalDownloadCache.CacheFileName("us_kentucky"),"Regional cache keys collide");Reject(()=>new GeofabrikCatalog(Catalogue(Region("../outside",null,30,50,30.001,50.001))));
  });
  yield return("global region coverage detects interior holes and missing child coverage",()=>{
   var parent=Region("country",null,29.9,49.9,30.1,50.1);var hole=Region("hole","country",29.9,49.9,30.1,50.1,"[[30.0004,50.0004],[30.0006,50.0004],[30.0006,50.0006],[30.0004,50.0006],[30.0004,50.0004]]");
   var bounds=new GeoBounds{West=30,East=30.001,South=50,North=50.001};var catalogue=new GeofabrikCatalog(Catalogue(parent,hole));var selected=catalogue.Select(bounds);Check(selected.Count==1&&selected[0].Id=="country","A hole was treated as complete coverage or parent+child downloaded redundantly");
   var incomplete=new GeofabrikCatalog(Catalogue(hole));Reject(()=>incomplete.Select(bounds));
   var missing=new GeofabrikCatalog(Catalogue(parent,Region("tiny","country",30,50,30.0005,50.001)));Check(missing.Select(bounds).Single().Id=="country","Missing subdivision coverage was not filled from a parent");
  });
  yield return("global region catalogue rejects untrusted URLs duplicate IDs and malformed boundaries",()=>{
   foreach(var url in new[]{"http://download.geofabrik.de/europe/a-latest.osm.pbf","https://127.0.0.1/a-latest.osm.pbf","https://download.geofabrik.de@evil.example/a-latest.osm.pbf","https://download.geofabrik.de/europe/a-latest.osm.pbf?token=secret","https://osm-internal.download.geofabrik.de/a-latest.osm.pbf"})Reject(()=>GeofabrikCatalog.ValidateUrl(url));
   var r=Region("one",null,30,50,30.001,50.001);Reject(()=>new GeofabrikCatalog(Catalogue(r,r)));Reject(()=>new GeofabrikCatalog("{\"features\":[]}"));
  });
  yield return("malformed catalogue shapes are rejected through the public validation contract",()=>{
   foreach(var json in MalformedCatalogues())Reject(()=>new GeofabrikCatalog(json));
  });
  yield return("malformed catalogue refresh retains the last validated catalogue",()=>WithDirectory(root=>{
   var valid=Catalogue(Region("fixture",null,32,54.6,32.2,54.9));var file=Path.Combine(root,"catalogue.json");
   foreach(var malformed in MalformedCatalogues().Append("{broken")){
    File.WriteAllText(file,valid);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-8));
    using var http=new HttpClient(new Handler(request=>Response(System.Text.Encoding.UTF8.GetBytes(request.Method==HttpMethod.Get?malformed:""))));
    var plan=new GeofabrikClient(root,http).PlanAsync(Smolensk()).GetAwaiter().GetResult();Check(plan.Regions.Single().Id=="fixture"&&File.ReadAllText(file)==valid,"Malformed refresh replaced or bypassed the validated fallback");
   }
  }));
  yield return("malformed cached catalogue is replaced from the official catalogue",()=>WithDirectory(root=>{
   var valid=Catalogue(Region("fixture",null,32,54.6,32.2,54.9));var file=Path.Combine(root,"catalogue.json");
   foreach(var malformed in MalformedCatalogues()){
    File.WriteAllText(file,malformed);using var http=new HttpClient(new Handler(_=>Response(System.Text.Encoding.UTF8.GetBytes(valid))));
    var plan=new GeofabrikClient(root,http).PlanAsync(Smolensk()).GetAwaiter().GetResult();Check(plan.Regions.Single().Id=="fixture"&&File.ReadAllText(file)==valid,"Malformed local catalogue blocked a valid refresh");
   }
  }));
  yield return("regional source download promotes only validated complete PBF and reuses fresh cache",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var handler=new Handler(_=>Response(bytes));using var http=new HttpClient(handler);var cache=new RegionalDownloadCache(root,http);var region=FixtureRegion();using(var lease=cache.AcquireAsync(region,default).GetAwaiter().GetResult()){Check(!lease.Cached&&File.Exists(lease.Path)&&!File.Exists(Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture"))),"Unvalidated source was promoted");PbfExtractReader.Read(lease.Path,Smolensk());lease.Commit();}
   using var cached=cache.AcquireAsync(region,default).GetAwaiter().GetResult();Check(cached.Cached&&handler.Calls==1&&File.ReadAllBytes(cached.Path).SequenceEqual(bytes),"Validated cache was redownloaded");
  }));
  yield return("cancelled regional requests cannot succeed through fresh cached files",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var handler=new Handler(_=>Response(bytes));using var http=new HttpClient(handler);var cache=new RegionalDownloadCache(root,http);
   using(var lease=cache.AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult())lease.Commit();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
   Fails<OperationCanceledException>(()=>cache.AcquireAsync(FixtureRegion(),cancellation.Token).GetAwaiter().GetResult());Fails<OperationCanceledException>(()=>cache.SizeAsync(FixtureRegion(),cancellation.Token).GetAwaiter().GetResult());Check(handler.Calls==1&&cache.Cached(FixtureRegion()) is not null,"Cancelled cache lookup changed a valid source");
  }));
  yield return("regional restart budgets reclaim the partial file being overwritten",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var part=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture")+".part");File.WriteAllBytes(part,bytes[..(bytes.Length/2)]);
   using var http=new HttpClient(new Handler(_=>Response(bytes)));using var lease=new RegionalDownloadCache(root,http,bytes.Length,bytes.Length).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult();Check(File.ReadAllBytes(lease.Path).SequenceEqual(bytes),"Restart counted discarded partial bytes twice");lease.Commit();
  }));
  yield return("unknown length downloads fit the actual cache budget and cannot overflow it",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);HttpResponseMessage Chunked()=>new(HttpStatusCode.OK){Content=new StreamContent(new NonSeekableStream(bytes))};
   using var http=new HttpClient(new Handler(_=>Chunked()));var cache=new RegionalDownloadCache(root,http,bytes.Length*2,bytes.Length);
   using(var lease=cache.AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult()){Check(File.ReadAllBytes(lease.Path).SequenceEqual(bytes),"Unknown length source reserved more than its actual size");lease.Reject();}
   Fails<ArgumentException>(()=>new RegionalDownloadCache(root,http,bytes.Length*2,bytes.Length-1).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult());Check(Directory.EnumerateFiles(root,"*.part").Sum(p=>new FileInfo(p).Length)<=bytes.Length-1,"Unknown length source overflowed the cache budget");
  }));
  yield return("truncated PBF header is rejected and removed from resumable cache",()=>WithDirectory(root=>{
   var bytes=new byte[16];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes,64);using var http=new HttpClient(new Handler(_=>Response(bytes)));
   Reject(()=>new RegionalDownloadCache(root,http).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult());Check(!Directory.EnumerateFiles(root,"*.part*").Any(),"Truncated header remained resumable");
  }));
  yield return("regional download resumes against matching validator without duplicating bytes",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var part=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture")+".part");var split=bytes.Length/2;File.WriteAllBytes(part,bytes[..split]);File.WriteAllText(part+".json",JsonSerializer.Serialize(new RegionalFileMetadata(FixtureRegion().Url,bytes.Length,"\"snapshot\"",null,DateTimeOffset.UtcNow)));
   var handler=new Handler(request=>{Check(request.Headers.Range?.Ranges.Single().From==split&&request.Headers.IfRange?.EntityTag?.Tag=="\"snapshot\"","Resume omitted range or validator");var response=Response(bytes[split..],HttpStatusCode.PartialContent);response.Content.Headers.ContentRange=new(split,bytes.Length-1,bytes.Length);return response;});using var http=new HttpClient(handler);var cache=new RegionalDownloadCache(root,http);using var lease=cache.AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult();Check(File.ReadAllBytes(lease.Path).SequenceEqual(bytes),"Resumed source duplicated or lost bytes");lease.Commit();
  }));
  yield return("regional download restarts when provider ignores range and rejects inconsistent ranges",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var part=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture")+".part");void Prepare(){File.WriteAllBytes(part,bytes[..(bytes.Length/2)]);File.WriteAllText(part+".json",JsonSerializer.Serialize(new RegionalFileMetadata(FixtureRegion().Url,bytes.Length,"\"snapshot\"",null,DateTimeOffset.UtcNow)));}Prepare();
   using(var http=new HttpClient(new Handler(_=>Response(bytes)))){using var lease=new RegionalDownloadCache(root,http).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult();Check(File.ReadAllBytes(lease.Path).SequenceEqual(bytes),"Ignored range appended a second source");lease.Reject();}
   Prepare();var original=File.ReadAllBytes(part);using var bad=new HttpClient(new Handler(_=>{var response=Response(bytes,HttpStatusCode.PartialContent);response.Content.Headers.ContentRange=new(0,bytes.Length-1,bytes.Length);return response;}));try{new RegionalDownloadCache(root,bad).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult();}catch(HttpRequestException){Check(File.ReadAllBytes(part).SequenceEqual(original),"A bad range corrupted the retained partial source");return;}throw new Exception("Inconsistent resumed data accepted");
  }));
  yield return("regional cache retains previous validated source on rejected replacement",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);using var http=new HttpClient(new Handler(_=>Response(bytes)));var cache=new RegionalDownloadCache(root,http);using(var first=cache.AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult())first.Commit();var path=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture"));var before=File.ReadAllBytes(path);
   using(var replacement=cache.AcquireAsync(FixtureRegion(),default,forceRefresh:true).GetAwaiter().GetResult())replacement.Reject();Check(File.ReadAllBytes(path).SequenceEqual(before)&&cache.Cached(FixtureRegion()) is not null,"Rejected replacement deleted the last good source");
  }));
  yield return("failed regional metadata promotion restores the previous source",()=>WithDirectory(root=>{
   var bytes=UpstreamTests.PbfFixture(true);var replacement=bytes.Concat(new byte[]{0}).ToArray();var calls=0;using var http=new HttpClient(new Handler(_=>Response(++calls==1?bytes:replacement)));var cache=new RegionalDownloadCache(root,http);
   using(var original=cache.AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult())original.Commit();var target=Path.Combine(root,RegionalDownloadCache.CacheFileName("fixture"));var metadata=File.ReadAllText(target+".json");
   using var candidate=cache.AcquireAsync(FixtureRegion(),default,forceRefresh:true).GetAwaiter().GetResult();File.Delete(target+".json");Directory.CreateDirectory(target+".json");
   try{var denied=false;try{candidate.Commit();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){denied=true;}Check(denied,"Metadata promotion unexpectedly replaced a directory");Check(File.ReadAllBytes(target).SequenceEqual(bytes)&&File.Exists(candidate.Path),"Failed metadata promotion destroyed the previous source or candidate");}
   finally{Directory.Delete(target+".json");File.WriteAllText(target+".json",metadata);candidate.Reject();}
   Check(cache.Cached(FixtureRegion()) is not null,"Previous validated cache could not be recovered");
  }));
  yield return("regional downloader rejects oversized or non-PBF responses before promotion",()=>WithDirectory(root=>{
   var stream=new GuardedStream();using(var http=new HttpClient(new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stream)};response.Content.Headers.ContentLength=1000;return response;}))){try{new RegionalDownloadCache(root,http,100).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult();}catch(ArgumentException){Check(stream.Reads==0,"Oversized source was read before its size guard");}}
   using var bad=new HttpClient(new Handler(_=>Response("<html>blocked source</html>"u8.ToArray())));Reject(()=>new RegionalDownloadCache(root,bad).AcquireAsync(FixtureRegion(),default).GetAwaiter().GetResult());Check(!Directory.EnumerateFiles(root,"*.part*").Any()&&!Directory.EnumerateFiles(root,"*.osm.pbf").Any(),"Rejected HTML persisted as a resumable PBF");
  }));
 }
 public static string Catalogue(params string[] regions)=>"{\"type\":\"FeatureCollection\",\"features\":["+string.Join(',',regions)+"]}";
 public static OsmImportTimeouts FastTiming()=>new(){TransferMaximum=TimeSpan.FromSeconds(8),TransferIdle=TimeSpan.FromMilliseconds(500),ProcessingMaximum=TimeSpan.FromSeconds(2)};
 public static HttpResponseMessage SlowResponse(byte[] bytes,TimeSpan initialDelay,TimeSpan subsequentDelay,int chunk){var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new SlowStream(bytes,initialDelay,subsequentDelay,chunk))};response.Content.Headers.ContentLength=bytes.Length;response.Headers.ETag=new("\"slow-snapshot\"");return response;}
 private sealed class SlowStream(byte[] bytes,TimeSpan initial,TimeSpan subsequent,int chunk):MemoryStream(bytes){private int reads;public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(reads++==0?initial:subsequent,token);return await base.ReadAsync(buffer[..Math.Min(buffer.Length,chunk)],token);}}
 private static IEnumerable<string> MalformedCatalogues(){
  yield return "{\"features\":[{}]}";yield return "{\"features\":[{\"properties\":[]}]}";
  var valid=Catalogue(Region("fixture",null,32,54.6,32.2,54.9));
  yield return valid.Replace("\"name\":\"fixture\"","\"name\":17",StringComparison.Ordinal);
  yield return Catalogue("{\"properties\":{\"id\":\"empty\",\"name\":\"empty\",\"urls\":{\"pbf\":\"https://download.geofabrik.de/test/empty-latest.osm.pbf\"}},\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":[]}}");
  yield return valid.Replace("[32,54.6]","[\"bad\",54.6]",StringComparison.Ordinal);
 }
 public static string Region(string id,string? parent,double west,double south,double east,double north,string? hole=null){var ring=JsonSerializer.Serialize(new[]{new[]{west,south},new[]{east,south},new[]{east,north},new[]{west,north},new[]{west,south}});return "{\"type\":\"Feature\",\"properties\":"+JsonSerializer.Serialize(new{id,name=id,parent,urls=new{pbf="https://download.geofabrik.de/test/"+id+"-latest.osm.pbf"}})+",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":["+ring+(hole is null?"":","+hole)+"]}}";}
 private static GeographicRegion FixtureRegion()=>new GeofabrikCatalog(Catalogue(Region("fixture",null,32,54.6,32.2,54.9))).Regions.Single();
 private static GeoBounds Smolensk()=>new(){South=54.777,North=54.779,West=32.046,East=32.049};
 private static HttpResponseMessage Response(byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){var response=new HttpResponseMessage(status){Content=new ByteArrayContent(bytes)};response.Headers.ETag=new("\"snapshot\"");return response;}
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler{public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;return Task.FromResult(run(request));}}
 private sealed class GuardedStream:MemoryStream{public int Reads;public override int Read(byte[] buffer,int offset,int count){Reads++;throw new Exception("Unexpected body read");}}
 private sealed class NonSeekableStream(byte[] bytes):MemoryStream(bytes){public override bool CanSeek=>false;}
 private static void WithDirectory(Action<string> test){var root=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"work","geofabrik-tests"));var path=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);try{test(path);}finally{if(!Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe test cleanup");Directory.Delete(path,true);}}
 private static void Check(bool value,string message){if(!value)throw new Exception(message);}
 private static void Fails<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 private static void Reject(Action action){try{action();}catch(Exception e) when(e is ArgumentException or JsonException){return;}throw new Exception("Invalid regional source accepted");}
}
