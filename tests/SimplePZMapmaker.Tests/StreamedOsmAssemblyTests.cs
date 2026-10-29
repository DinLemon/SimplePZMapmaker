using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class StreamedOsmAssemblyTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("streamed osm assembly deduplicates shared source identities across distant windows",()=>Direct((root,store,request,plan,compose)=>{
   var projection=new GeographicProjection(plan.GeoReference,100,100);
   double[] P(int x,int y){var g=projection.Geographic(x,y);return [g.Longitude,g.Latitude];}
   object House(int x)=>new{type="Feature",id="repeated",geometry=new{type="Polygon",coordinates=new[]{new[]{P(x,30),P(x+18,30),P(x+18,44),P(x,44),P(x,30)}}},properties=new{building="house"}};
   var source=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{House(250),House(506)}});
   var assets=new GameAssetCatalog(root,"");var id=Guid.NewGuid().ToString("N");
   var result=new OsmWorldSectionImport(store,assets,new(assets)).BuildAsync(id,request,plan,source,compose,new Inline(_=>{}),[],default).GetAwaiter().GetResult();
   using var lease=store.PinAsync(id).GetAwaiter().GetResult();var shared=lease.ReadMetadataAsync().GetAwaiter().GetResult();
   Check(shared.Buildings.Count==1&&result.Assembly.SharedBuildings==1,"Same original source was imported again in a distant seam window");
  }));
  yield return ("streamed osm assembly cancellation after written sections preserves existing worlds and source",()=>Direct((root,store,request,plan,compose)=>{
   var existing=WorldTests.Fixture(1);store.SaveAsync(existing.Id,existing).GetAwaiter().GetResult();var before=store.ManifestAsync(existing.Id).GetAwaiter().GetResult()!;
   var source="{\"type\":\"FeatureCollection\",\"features\":[]}";var sourceFile=Path.Combine(root,"retained-source.geojson");File.WriteAllText(sourceFile,source);
   using var cancel=new CancellationTokenSource();var assets=new GameAssetCatalog(root,"");var id=Guid.NewGuid().ToString("N");
   try{new OsmWorldSectionImport(store,assets,new(assets)).BuildAsync(id,request,plan,source,compose,new Inline(p=>{if(p.Phase=="compose"&&p.Completed==2)cancel.Cancel();}),[],cancel.Token).GetAwaiter().GetResult();throw new Exception("Canceled assembly completed");}catch(OperationCanceledException){}
   Check(store.ManifestAsync(id).GetAwaiter().GetResult() is null,"Canceled assembly exposed a partial manifest");
   Check(store.ManifestAsync(existing.Id).GetAwaiter().GetResult()!.Revision==before.Revision&&File.ReadAllText(sourceFile)==source,"Cancellation changed prior world or source");
   Check(!Directory.EnumerateDirectories(root,".*.import-*").Any(),"Canceled assembly retained staged data");
  }));

  yield return ("streamed osm assembly writes sections immediately and reports bounded residency",()=>RunFixture((store,state,world)=>{
   Check(state.TryGetProperty("assembly",out var stats)&&stats.ValueKind==JsonValueKind.Object,"Disk assembly residency statistics are missing");
   Check(stats.GetProperty("mode").GetString()=="incremental-sections","Import retained whole-world assembly");
   Check(stats.GetProperty("peakResidentCells").GetInt32()<6,"Small seam import retained the complete world");
   Check(world.Maps.Count==6,"Streamed assembly lost sections");
  }));
  yield return ("streamed osm assembly preserves shared houses gates and roads across seams",()=>RunFixture((store,state,world)=>{
   Check(world.Buildings.Any(b=>b.SourceId=="geo-seam-house"),"Cross-section house disappeared");
   Check(world.Maps.All(m=>m.Project.Buildings.All(b=>b.SourceId!="geo-seam-house")),"Shared house remains duplicated locally");
   Check(world.Fences.Count==1&&world.Fences[0].Gates.Count==2,"Cross-section fence or gates were lost/duplicated");
   Check(world.Maps.All(m=>m.Project.Fences.All(f=>f.Id!=world.Fences[0].Id)),"Shared fence remains duplicated locally");
   var left=world.Maps.Single(m=>m.Project.WorldX==100&&m.Project.WorldY==100).Project;
   var right=world.Maps.Single(m=>m.Project.WorldX==101&&m.Project.WorldY==100).Project;
   Check(left.Terrain[80*256+255]==Terrain.Road&&right.Terrain[80*256]==Terrain.Road,"Road ends at section seam");
   WorldAssembler.Validate(world);
  }));
 }
 sealed class Inline(Action<OsmProgress> report):IProgress<OsmProgress>{public void Report(OsmProgress progress)=>report(progress);}
 static void Direct(Action<string,WorldStore,OsmWorldImportRequest,OsmWorldPlan,Func<int,int,WorldMapMember>> run){
  var root=Path.Combine(Path.GetTempPath(),"pz-direct-stream-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{using var store=new WorldStore(root);var north=50.01;var south=north-510d/111320;var east=30+766d/(111320*Math.Cos((north+south)/2*Math.PI/180));var request=new OsmWorldImportRequest(new(){West=30,East=east,South=south,North=north},SectionSize:256,PreferSections:true);var plan=OsmWorldImportService.Plan(request);
   WorldMapMember Compose(int x,int y){var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=100+x;p.WorldY=100+y;p.WorldSection=true;p.Version=6;return new(){Project=p};}
   run(root,store,request,plan,Compose);
  }finally{Directory.Delete(root,true);}
 }
 static void RunFixture(Action<WorldStore,JsonElement,WorldProject> inspect){
  var root=Path.Combine(Path.GetTempPath(),"pz-stream-osm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var north=50.01;var south=north-510d/111320;var east=30+766d/(111320*Math.Cos((north+south)/2*Math.PI/180));
   var request=new OsmWorldImportRequest(new(){West=30,East=east,South=south,North=north},SectionSize:256,PreferSections:true);
   var plan=OsmWorldImportService.Plan(request);Check(plan.Columns==3&&plan.Rows==2,"Fixture layout drifted");
   var projection=new GeographicProjection(plan.GeoReference,100,100);
   double[] Point(double x,double y){var g=projection.Geographic(x,y);return [g.Longitude,g.Latitude];}
   object Feature(string id,string type,object coordinates,object properties)=>new{type="Feature",id,geometry=new{type,coordinates},properties};
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new object[]{
    Feature("seam-house","Polygon",new[]{new[]{Point(250,30),Point(266,30),Point(266,44),Point(250,44),Point(250,30)}},new{building="house"}),
    Feature("road","LineString",new[]{Point(20,80),Point(700,80)},new{highway="residential"}),
    Feature("fence","LineString",new[]{Point(230,60),Point(300,60)},new{barrier="fence"}),
    Feature("gate1","Point",Point(240,60),new{barrier="gate"}),Feature("gate2","Point",Point(280,60),new{barrier="gate"})}});
   using var client=new OsmClient(Path.Combine(root,"cache"),new Offline(),TimeSpan.Zero);var saved=client.Sources.ImportAsync(new("Seam proof",request.Bounds,Json:json),default).GetAwaiter().GetResult();
   using var store=new WorldStore(Path.Combine(root,"worlds"));
   using var service=new OsmWorldImportService(client,new GameAssetCatalog(root,""),Path.Combine(root,"jobs"),workspaceStore:store);
   var start=JsonSerializer.SerializeToElement(service.Start(request with{SourceMode="file",SourceId=saved.Id}),NativeMapExporter.JsonOptions);var id=start.GetProperty("id").GetString()!;
   JsonElement state=default;var until=DateTime.UtcNow.AddSeconds(60);
   do{state=JsonSerializer.SerializeToElement(service.Status(id),NativeMapExporter.JsonOptions);if(state.GetProperty("status").GetString() is "failed" or "cancelled" or "completed")break;Thread.Sleep(10);}while(DateTime.UtcNow<until);
   Check(state.GetProperty("status").GetString()=="completed",state.ToString());
   var workspace=state.GetProperty("workspaceId").GetString()!;var world=store.LoadAsync(workspace).GetAwaiter().GetResult()!;inspect(store,state,world);
  }finally{Directory.Delete(root,true);}
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 sealed class Offline:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("Offline fixture attempted network access");}
}
