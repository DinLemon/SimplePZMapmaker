using System.IO.Compression;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class ExpandedWorldLayoutTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("expanded layout HTTP limits and accepted plan expose exact bounded raster cost",()=>WithDirectory(root=>{
   using var source=new OsmClient(root,new WaitingHandler(),TimeSpan.Zero);using var store=new WorldStore(root);using var service=new OsmWorldImportService(source,new GameAssetCatalog(root,""),root,workspaceStore:store);
   var builder=WebApplication.CreateSlimBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();builder.Services.AddSingleton(source);builder.Services.AddSingleton(service);
   var app=builder.Build();try{app.MapOsmWorldImportEndpoints();app.StartAsync().GetAwaiter().GetResult();using var client=new HttpClient{BaseAddress=new(app.Urls.Single())};
    var limits=client.GetFromJsonAsync<JsonElement>("/api/world/osm/limits").GetAwaiter().GetResult();Check(limits.GetProperty("normalSectionLimit").GetInt32()==128&&limits.GetProperty("normalCellLimit").GetInt32()==768&&limits.GetProperty("maxSections").GetInt32()==65536&&limits.GetProperty("maxCells").GetInt32()==65536,"HTTP limits drifted");
    Check(limits.GetProperty("baseRasterBytesPerCell").GetInt64()*8192==544L*1024*1024,"HTTP memory formula drifted");
    var response=client.PostAsJsonAsync("/api/world/osm/plan",Expanded(Request(43,3))).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();var plan=response.Content.ReadFromJsonAsync<OsmWorldPlan>().GetAwaiter().GetResult()!;Check(plan.Sections==129&&plan.PreferSections&&plan.RequiresExpandedLayout,"HTTP expanded plan lost consent/storage metadata");
   }finally{app.DisposeAsync().AsTask().GetAwaiter().GetResult();}
  }));
  yield return ("expanded layout coarse section importer and PBF intake retain world geography",()=>WithDirectory(root=>{
   var request=Request(1,1,2048,20);var reference=OsmWorldImportService.Plan(request).GeoReference;var projection=new GeographicProjection(reference,0,0);
   var p=MapGenerator.Generate(new(){Kind="blank",Size=2048});p.WorldX=0;p.WorldY=0;p.WorldSection=true;p.Version=6;p.Bounds=request.Bounds;ProjectValidator.Validate(p);
   var a=projection.Geographic(20,20);var b=projection.Geographic(30,20);var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="road",geometry=new{type="LineString",coordinates=new[]{new[]{a.Longitude,a.Latitude},new[]{b.Longitude,b.Latitude}}},properties=new{highway="residential"}}}});
   Check(GeographicImporter.Import(json,p,request.Bounds,projection:projection).Project.MetresPerTile==20,"Coarse section import rejected world bounds");
   GeographicImporter.SuggestStreetGrid(json,request.Bounds);GeographicImporter.MergeOvertureBuildings(json,"{\"type\":\"FeatureCollection\",\"features\":[]}",request.Bounds);
   var bounds=new GeoBounds{West=31.9,East=32.2,South=54.6,North=54.9};var path=Path.Combine(root,"survey.pbf");File.WriteAllBytes(path,UpstreamTests.PbfFixture(false));
   Check(OsmGeometrySet.HasFeatures(PbfExtractReader.Read(path,bounds).Data)&&OsmGeometrySet.HasFeatures(PbfExtractReader.ReadLarge(path,bounds,root).Data),"Bounded large-area PBF intake lost source geometry");
  }));
  yield return ("expanded layout real 129-section offline job publishes only linked result",()=>WithDirectory(root=>{
   using var client=new OsmClient(Path.Combine(root,"cache"),new WaitingHandler(),TimeSpan.Zero);using var store=new WorldStore(root);
   var latitude=100d/111320;var longitude=128.1*256/(111320*Math.Cos(50*Math.PI/180));var request=new OsmWorldImportRequest(new(){West=30-longitude/2,East=30+longitude/2,South=50-latitude/2,North=50+latitude/2},SectionSize:256,AllowExpandedLayout:true);Reject(()=>ProjectValidator.ValidateBounds(request.Bounds));
   var plan=OsmWorldImportService.Plan(request);Check(plan.Columns==129&&plan.Rows==1,"Long strip did not produce the requested exact 129 x 1 layout");var projection=new GeographicProjection(plan.GeoReference,100,100);
   double[] Point(int x,int y){var point=projection.Geographic(x,y);return [point.Longitude,point.Latitude];}
   object Feature(string id,string type,object coordinates,object properties)=>new{type="Feature",id,geometry=new{type,coordinates},properties};
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new object[]{Feature("first-road","LineString",new[]{Point(10,80),Point(120,80)},new{highway="residential"}),Feature("first-house","Polygon",new[]{new[]{Point(30,20),Point(42,20),Point(42,30),Point(30,30),Point(30,20)}},new{building="house"})}});
   var saved=client.Sources.ImportAsync(new("world survey",request.Bounds,Json:json),default).GetAwaiter().GetResult();
   using var service=new OsmWorldImportService(client,new GameAssetCatalog(root,""),Path.Combine(root,"jobs"),workspaceStore:store);var id=JsonSerializer.SerializeToElement(service.Start(request with{SourceMode="file",SourceId=saved.Id})).GetProperty("id").GetString()!;
   var until=DateTime.UtcNow.AddSeconds(45);JsonElement state;do{state=JsonSerializer.SerializeToElement(service.Status(id));if(state.GetProperty("status").GetString() is not("queued" or "running"))break;Thread.Sleep(10);}while(DateTime.UtcNow<until);
   Check(state.GetProperty("status").GetString()=="completed",state.GetProperty("error").ToString());Check(state.GetProperty("resultKind").GetString()=="sections"&&state.GetProperty("workspaceId").GetString()==id,"Real expanded job fell back to monolithic world");
   var manifest=store.ManifestAsync(id).GetAwaiter().GetResult()!;Check(manifest.Sections.Count==129&&service.ResultFile(id).EndsWith(".pzworld.index.json"),"Expanded job lost sections or result index");
   var first=store.LoadSectionAsync(id,manifest.Sections[0].Id).GetAwaiter().GetResult();var last=store.LoadSectionAsync(id,manifest.Sections[^1].Id).GetAwaiter().GetResult();Check(first.Member.Project.MetresPerTile==1&&last.Member.Project.MetresPerTile==1&&first.Shared.GeoReference?.Bounds.East==request.Bounds.East,"Real expanded import altered bounds or scale");
   Check(first.Member.Project.Terrain.Contains(Terrain.Road)&&first.Member.Project.Buildings.Count+first.Shared.Buildings.Count>0,"Real expanded source lost its first-cell house or road");
   using var lease=store.PinAsync(id).GetAwaiter().GetResult();var shared=lease.ReadMetadataAsync().GetAwaiter().GetResult();var assets=new GameAssetCatalog(root,"");var prepared=WorkspaceResourceRules.PrepareAsync(lease,manifest.Sections[0].Id,null,assets).GetAwaiter().GetResult();Check(prepared.Project.Width==256,"Expanded section resource validation failed");
   var archive=Path.Combine(root,"actual-strip.zip");using(var output=File.Create(archive))NativeStreamedWorldExporter.ExportAsync(new WorkspaceExportSource(lease,shared,assets,null),output).GetAwaiter().GetResult();using var native=ZipFile.OpenRead(archive);Check(native.Entries.Count(e=>e.Name.EndsWith(".lotheader"))==129,"Real imported strip lost cells during streamed native export");
  }));
  yield return ("expanded layout streamed exporter rejects excessive or overlapping source before output",()=>{
   foreach(var source in new[]{new InvalidSource(65537),new InvalidSource(2),new InvalidSource(1025,fullSized:true)}){using var output=new MemoryStream();Reject(()=>NativeStreamedWorldExporter.ExportAsync(source,output).GetAwaiter().GetResult());Check(output.Length==0,"Invalid streamed layout wrote partial output");}
  });
  yield return ("expanded layout manifest accepts 65536 entries and rejects 65537",()=>WithDirectory(root=>{
   var id=Guid.NewGuid().ToString("N");var manifest=new WorldSectionArchive.Manifest{Id=id,StorageId=id,Name="Bounded maximum",Revision=1,MetadataHash=new string('a',64),MetadataBytes=256};
   for(var i=0;i<65536;i++)manifest.Sections.Add(new(Guid.NewGuid().ToString("N"),new string('b',64),new string('s',80),i%256,i/256,256,256,256,1));
   var file=Path.Combine(root,id+".pzworld.index.json");File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(manifest,NativeMapExporter.JsonOptions));Check(new FileInfo(file).Length>1024*1024,"Fixture did not exceed old manifest cap");
   using var store=new WorldStore(root);Check(store.ManifestAsync(id).GetAwaiter().GetResult()?.Sections.Count==65536,"65536-entry manifest was rejected");
   manifest.Sections.Add(new(Guid.NewGuid().ToString("N"),new string('c',64),"Excess",256,256,256,256,256,1));File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(manifest,NativeMapExporter.JsonOptions));Reject(()=>store.ManifestAsync(id).GetAwaiter().GetResult());
  }));
  yield return ("expanded layout legacy request JSON keeps opt-in false",()=>{
   var request=JsonSerializer.Deserialize<OsmWorldImportRequest>("{\"bounds\":{\"west\":30,\"east\":30.001,\"south\":50,\"north\":50.001}}",NativeMapExporter.JsonOptions)!;
   Check(!request.AllowExpandedLayout&&!OsmWorldImportService.Plan(request).RequiresExpandedLayout,"Legacy client opted into expansion");
   Check(WorldLayoutLimits.EstimateBaseRasterBytes(8192)==544L*1024*1024,"Raster estimate does not match real byte terrain/Int32 density arrays");
  });
  yield return ("expanded layout 129 sections requires explicit consent and preserves scale",()=>{
   var normal=Request(16,8);var before=OsmWorldImportService.Plan(normal);Check(before.Columns*before.Rows==128,"Old 128 layout changed");
   var request=Request(43,3);Reject(()=>OsmWorldImportService.Plan(request));var plan=OsmWorldImportService.Plan(Expanded(request));
   Check(plan.Columns*plan.Rows==129&&plan.GeoReference.MetresPerTile==request.MetresPerTile,"Expanded plan reduced area or scale");
   var json=JsonSerializer.SerializeToElement(plan,NativeMapExporter.JsonOptions);Check(json.GetProperty("requiresExpandedLayout").GetBoolean()&&json.GetProperty("preferSections").GetBoolean(),"Expanded plan did not require disk sections");
   Check(json.GetProperty("estimatedBaseRasterBytes").GetInt64()>=129L*256*256,"Missing terrain memory estimate");
  });
  yield return ("expanded layout native cell budgets 768 and 65536 remain exact",()=>{
   var normal=Request(8,6,1024);Check(OsmWorldImportService.Plan(normal).Cells==768,"Normal cell boundary changed");
   var over=Request(7,7,1024);Reject(()=>OsmWorldImportService.Plan(over));Check(OsmWorldImportService.Plan(Expanded(over)).Cells==784,"Expanded cell opt-in rejected");
   Check(OsmWorldImportService.Plan(Expanded(Request(128,64,256,1))).Cells==8192,"8192-cell boundary rejected");
   Check(OsmWorldImportService.Plan(Expanded(Request(256,256,256,1))).Cells==65536,"65536-cell boundary rejected");
   Check(OsmWorldImportService.Plan(Expanded(Request(54,18,1024,1))).Cells==15552,"Selected 15552-cell layout rejected");
   Reject(()=>OsmWorldImportService.Plan(Expanded(Request(257,256,256,1))));
  });
  yield return ("expanded layout coarse geographic source bounds do not broaden single maps",()=>WithDirectory(root=>{
   var request=Request(16,9,1024,2);Reject(()=>ProjectValidator.ValidateBounds(request.Bounds));
   var plan=OsmWorldImportService.Plan(Expanded(request));Check(plan.Columns==16&&plan.Rows==9,"Coarse world bounds clipped");
   var files=new OsmSourceFiles(root);var saved=files.ImportAsync(new("Coarse survey",request.Bounds,Json:"{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[30,50]},\"properties\":{}}]}"),default).GetAwaiter().GetResult();
   Check(files.ReadAsync(saved.Id,request.Bounds,64_000_000).GetAwaiter().GetResult().SourceId==saved.Id,"Large saved source could not reopen");
   Reject(()=>OsmWorldImportService.Plan(Expanded(request with{Bounds=new(){West=-180,East=180,South=-85,North=85}})));
   Reject(()=>OsmWorldImportService.Plan(Expanded(request with{SourceMode="regional"})));
  }));
  yield return ("expanded layout 129-section disk workspace opens edits and exports independently",()=>WithDirectory(root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(129);Check(WorldAssembler.Validate(world).Cells==129,"Core rejected 129 sections");
   var id=Guid.NewGuid().ToString("N");var result=OsmWorldResultWriter.SaveAsync(root,id,world,store,default).GetAwaiter().GetResult();
   Check(result.WorkspaceId==id&&result.File.EndsWith(".pzworld.index.json"),"Expanded result used portable whole-world JSON");
   var manifest=store.ManifestAsync(id).GetAwaiter().GetResult()!;Check(manifest.Sections.Count==129,"Manifest lost sections");
   var selected=world.Maps[128];var section=store.LoadSectionAsync(id,selected.Id).GetAwaiter().GetResult();section.Member.Project.Name="Edited last section";
   store.SaveSectionAsync(id,selected.Id,manifest.Revision,section.Member.Project).GetAwaiter().GetResult();
   Check(store.LoadSectionAsync(id,selected.Id).GetAwaiter().GetResult().Member.Project.Name=="Edited last section","Expanded independent edit lost");
   using var lease=store.PinAsync(id).GetAwaiter().GetResult();var metadata=lease.ReadMetadataAsync().GetAwaiter().GetResult();using var output=new MemoryStream();
   NativeStreamedWorldExporter.ExportAsync(new WorkspaceExportSource(lease,metadata,null,null),output,memberId:selected.Id).GetAwaiter().GetResult();output.Position=0;using var zip=new ZipArchive(output,ZipArchiveMode.Read);
   Check(zip.Entries.Count(e=>e.Name.EndsWith(".lotheader"))==1,"Selected expanded export included unrelated cells");
   var archive=Path.Combine(root,"complete.zip");using(var destination=File.Create(archive))NativeStreamedWorldExporter.ExportAsync(new WorkspaceExportSource(lease,metadata,null,null),destination).GetAwaiter().GetResult();
   using var complete=ZipFile.OpenRead(archive);Check(complete.Entries.Count(e=>e.Name.EndsWith(".lotheader"))==129&&complete.Entries.Count(e=>e.FullName.Contains("/source/sections/"))==129,"Complete streamed expanded export lost native cells or reconstructible section sources");
  }));
  yield return ("expanded layout cancelled job never publishes partial workspace",()=>WithDirectory(root=>{
   using var handler=new WaitingHandler();using var client=new OsmClient(Path.Combine(root,"cache"),handler,TimeSpan.Zero);using var store=new WorldStore(root);
   using var service=new OsmWorldImportService(client,new GameAssetCatalog(root,""),Path.Combine(root,"jobs"),workspaceStore:store);
   var id=JsonSerializer.SerializeToElement(service.Start(Expanded(Request(43,3)) with{SourceMode="overpass"})).GetProperty("id").GetString()!;
   service.Cancel(id);var until=DateTime.UtcNow.AddSeconds(10);string? status=null;do{status=JsonSerializer.SerializeToElement(service.Status(id)).GetProperty("status").GetString();if(status is "cancelled" or "failed" or "completed")break;Thread.Sleep(10);}while(DateTime.UtcNow<until);
   Check(status=="cancelled","Expanded job ignored cancellation: "+status);Reject(()=>service.ResultFile(id));Check(store.ManifestAsync(id).GetAwaiter().GetResult() is null,"Cancelled expanded job committed a manifest");
   Check(!Directory.Exists(Path.Combine(root,"jobs"))||!Directory.EnumerateFiles(Path.Combine(root,"jobs")).Any(),"Cancelled expanded job retained partial result");
  }));
  yield return ("expanded layout cancellation during section writes removes every unpublished blob",()=>WithDirectory(root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(129);var id=Guid.NewGuid().ToString("N");using var cancel=new CancellationTokenSource();
   var saving=Task.Run(()=>OsmWorldResultWriter.SaveAsync(root,id,world,store,cancel.Token));var folder=Path.Combine(root,id+".sections");var until=DateTime.UtcNow.AddSeconds(20);var wroteBlob=false;
   while(!saving.IsCompleted&&DateTime.UtcNow<until){wroteBlob=Directory.Exists(folder)&&Directory.EnumerateFiles(folder,"*.section.json").Any();if(wroteBlob)break;Thread.Sleep(1);}
   Check(wroteBlob&&!saving.IsCompleted,"Did not reach an unpublished expanded blob");cancel.Cancel();try{saving.GetAwaiter().GetResult();throw new Exception("Expanded save ignored cancellation");}catch(OperationCanceledException){}
   Check(store.ManifestAsync(id).GetAwaiter().GetResult() is null&&!Directory.Exists(folder),"Cancelled expanded save retained a partial manifest or blob");
   Check(world.Maps.Count==129&&world.Maps[128].Project.Name=="Section128","Cancelled expanded save changed its input");
  }));
 }
 static OsmWorldImportRequest Request(int columns,int rows,int size=256,double scale=.5){
  var north=50+(rows*size-1)*scale/111320;var east=30+(columns*size-1)*scale/(111320*Math.Cos((50+north)/2*Math.PI/180));
  return new(new(){West=30,East=east,South=50,North=north},MetresPerTile:scale,SectionSize:size,OriginCellX:0,OriginCellY:0);
 }
 static OsmWorldImportRequest Expanded(OsmWorldImportRequest request)=>request with{AllowExpandedLayout=true};
 static void WithDirectory(Action<string> run){var root=Path.Combine(Path.GetTempPath(),"pz-expanded-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{run(root);}finally{Directory.Delete(root,true);}}
 static void Reject(Action run){try{run();}catch(ArgumentException){return;}throw new Exception("Expected bounded layout rejection");}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 sealed class WaitingHandler:HttpMessageHandler {protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException();}}
 sealed class InvalidSource(int count,bool fullSized=false):IStreamedWorldExportSource {
  public WorldProject Metadata {get;}=new();
  public IReadOnlyList<StreamedWorldSection> Sections {get;}=Enumerable.Range(0,count).Select(i=>new StreamedWorldSection(Guid.NewGuid().ToString("N"),"section",fullSized?i%16*8:0,fullSized?i/16*8:0,fullSized?2048:256,fullSized?2048:256)).ToArray();
  public object AuthoringIndex=>new{};
  public Task<MapProject> ReadSectionAsync(string id,CancellationToken token)=>throw new InvalidOperationException("Invalid layout reached raster IO");
  public Task<MapProject> ProjectSectionAsync(string id,MapProject p,CancellationToken token)=>throw new InvalidOperationException();
  public Task CopySectionSourceAsync(string id,Stream output,CancellationToken token)=>throw new InvalidOperationException();
  public Task CopyMetadataSourceAsync(Stream output,CancellationToken token)=>throw new InvalidOperationException();
 }
}
