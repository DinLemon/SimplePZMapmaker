using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text;
using System.Net;
using System.Text.Json;
using SimplePZMapmaker.Web.Services;
public static class SharedFenceImportTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("imported fence crossing a section seam is promoted once to the world fence layer",()=>{
   var (world,json)=ImportedWorld();
   Check(world.Maps.All(m=>m.Project.Fences.Any(f=>f.SourceId=="geo-osm-way-1")),"Fixture: each section should import its own copy first");
   var notes=new List<string>();var promoted=GeographicImporter.PromoteSharedFences(world,notes);
   Check(promoted==1&&world.Fences.Count==1&&world.Maps.All(m=>m.Project.Fences.All(f=>f.SourceId!="geo-osm-way-1")),"The crossing line was not moved exactly once");
   var shared=world.Fences.Single();Check(shared.Points.Min(v=>v.X)<101*256&&shared.Points.Max(v=>v.X)>101*256&&shared.Points.All(v=>v.Y>=100*256),"Shared points are not absolute world tiles");
   Check(shared.Gates.Count==2,"Gates found near each section were not merged");
   Check(world.FenceStyles.Any(s=>s.Id==shared.Style)&&world.GateStyles.Any(s=>s.Id=="vanilla-wicket"),"Styles were not carried to the world layer");
   Check(world.Maps[0].Project.Fences.Any(f=>f.SourceId=="geo-osm-way-2"),"A line inside one section must stay local");
   Check(notes.Any(n=>n.Contains("shared world fence layer")),"Promotion is not reported");
   WorldAssembler.Validate(world);Check(world.Version==7,"World fence format not raised");
   var left=WorldAssembler.ProjectSection(world,world.Maps[0]);var right=WorldAssembler.ProjectSection(world,world.Maps[1]);
   var a=FenceLines.Compose(left.Fences,left.FenceStyles,left.GateStyles,256,256).Pieces;var b=FenceLines.Compose(right.Fences,right.FenceStyles,right.GateStyles,256,256).Pieces;
   Check(a.Any(p=>p.X==255)&&b.Any(p=>p.X==0),"The promoted fence is not continuous across the seam");
   Check(a.Count(p=>p.Tile=="fixtures_doors_fences_01_13")+b.Count(p=>p.Tile=="fixtures_doors_fences_01_13")==2,"Merged gates are not drawn once each");
   using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(world)));
   foreach(var cell in new[]{"100_100","101_100"}){using var r=new StreamReader(zip.Entries.Single(e=>e.Name==$"{cell}.lotheader").Open(),Encoding.Latin1);Check(r.ReadToEnd().Contains("fencing_01_41"),$"Cell {cell} lost its part of the shared fence");}
  });
  yield return("promotion keeps authored lines local and is idempotent",()=>{
   var (world,_)=ImportedWorld();world.Maps[1].Project.Fences.Add(new(){Id="authored",Style="vanilla-planks",Points=[new(-4,30),new(10,30)]});
   GeographicImporter.PromoteSharedFences(world,[]);var again=GeographicImporter.PromoteSharedFences(world,[]);
   Check(again==0&&world.Fences.Count==1&&world.Maps[1].Project.Fences.Any(f=>f.Id=="authored"),"Authored lines must not be promoted and a second pass must change nothing");
  });
  yield return("a conflicting world fence style keeps the crossing line local",()=>{
   var (world,_)=ImportedWorld();var style=world.Maps[0].Project.Fences.Single(f=>f.SourceId=="geo-osm-way-1").Style;
   world.FenceStyles.Add(new(){Id=style,W="fencing_01_10",N="fencing_01_9"});var notes=new List<string>();
   Check(GeographicImporter.PromoteSharedFences(world,notes)==0&&world.Fences.Count==0&&notes.Any(n=>n.Contains("conflicts")),"A style conflict should keep the local copies");
  });
  yield return("seam check counts every source feature once and catches duplicates and losses",()=>{
   var (world,json)=ImportedWorld();GeographicImporter.PromoteSharedFences(world,[]);var report=GeographicImporter.TransferCheck(json,world);
   Check(report.Passed&&report.ByKind["fence"]==new TransferOutcome(1,1,0,0,0),"Fence outcomes are wrong: "+report.ByKind["fence"]);
   world.Maps[1].Project.Fences.Add(world.Maps[0].Project.Fences.Single(f=>f.SourceId=="geo-osm-way-2"));report=GeographicImporter.TransferCheck(json,world);Check(!report.Passed&&report.ByKind["fence"].Duplicated==1&&report.Problems.Any(p=>p.Contains("geo-osm-way-2")),"A duplicated fence was not caught");
   world.Maps[1].Project.Fences.Clear();world.Maps[0].Project.Fences.Clear();report=GeographicImporter.TransferCheck(json,world);Check(!report.Passed&&report.ByKind["fence"].Lost==1,"A lost fence was not caught");
  });
  yield return("OSM world import promotes a crossing fence and preserve edits keeps authored world fences",()=>WithDirectory(root=>{
   var osm=JsonSerializer.Serialize(new{osm3s=new{timestamp_osm_base="2026-10-13T00:00:00Z"},elements=new object[]{new{type="way",id=7,tags=new Dictionary<string,string>{{"barrier","fence"}},geometry=new[]{new{lon=30.003,lat=50.003},new{lon=30.008,lat=50.003}}}}});
   var assets=new GameAssetCatalog(root,Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??"");using var client=new OsmClient(Path.Combine(root,"cache"),new OneResponse(osm),TimeSpan.Zero);using var service=new OsmWorldImportService(client,assets,Path.Combine(root,"jobs"));
   var request=new OsmWorldImportRequest(new(){West=30,East=30.01,South=50,North=50.006},SectionSize:512);
   WorldProject Run(OsmWorldImportRequest r){var id=JsonSerializer.SerializeToElement(service.Start(r)).GetProperty("id").GetString()!;var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.Elapsed<TimeSpan.FromSeconds(30)){var state=JsonSerializer.SerializeToElement(service.Status(id));var status=state.GetProperty("status").GetString();if(status is "completed" or "failed" or "cancelled"){Check(status=="completed","Import failed: "+state);using var file=File.OpenRead(service.ResultFile(id));return JsonSerializer.Deserialize<WorldProject>(file,NativeMapExporter.JsonOptions)!;}Thread.Sleep(10);}throw new Exception("Import timed out");}
   var world=Run(request);
   Check(world.Fences.Count==1&&world.Fences[0].SourceId?.EndsWith("way-7")==true,"The crossing barrier was not promoted to the world fence layer");
   Check(world.Maps.All(m=>m.Project.Fences.Count==0),"Section copies of the promoted fence remain");
   var style=world.Fences[0].Style;world.Fences.Add(new(){Id="authored-world",Style=style,Points=[new(100*256+500,100*256+100),new(100*256+530,100*256+100)]});WorldAssembler.Validate(world);
   var again=Run(request with{ExistingWorld=world,PreserveEdits=true});
   Check(again.Fences.Count==2&&again.Fences.Any(f=>f.Id=="authored-world")&&again.Fences.Count(f=>f.SourceId is not null)==1,"Preserve edits lost the authored world fence or duplicated the imported one");
  }));
  yield return("preserve edits keeps shared decorations on their prior ground and clears refreshed objects under them",()=>{
   var prior=WorldTests.Fixture(2);var shared=new SharedDecoration{Id="shared-bench",X=100*256+255,Y=100*256+40,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0"),new(1,0,"fixtures_counters_01_1")]};prior.Decorations.Add(shared);WorldAssembler.Validate(prior);
   var next=WorldTests.Fixture(2);next.Decorations=[WorldAssembler.CloneDecoration(shared)];next.Maps[1].Project.Terrain[40*256]=Terrain.Water;next.Maps[0].Project.Decorations.Add(new(){Id="lamp",X=255,Y=40,Asset="lighting_outdoor_01_0",Parts=[new(0,0,"lighting_outdoor_01_0")]});
   var notes=new List<string>();Check(WorldAssembler.KeepSharedDecorations(next,prior,notes)==1&&next.Maps[1].Project.Terrain[40*256]!=Terrain.Water&&next.Maps[0].Project.Decorations.Count==0,"Shared decoration ground or clearance was not restored");
   WorldAssembler.Validate(next);Check(notes.Count==1,"Clearance is not reported");
  });
 }
 sealed class OneResponse(string json):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});}
 static void WithDirectory(Action<string> test){var dir=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Path.GetFullPath("."),"work","shared-fence-import-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{test(dir);}finally{Directory.Delete(dir,true);}}
 /// <summary>Two 256-tile sections importing the same OSM data, the way the world importer does: every section receives every way that touches it.</summary>
 static (WorldProject World,string Json) ImportedWorld(){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
  var first=new GeographicProjection(reference,100,100);double[] At(double x,double y){var g=first.Geographic(x,y);return[g.Longitude,g.Latitude];}
  var json=JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features=new object[]{
   new{type="Feature",id="osm-way-1",properties=new Dictionary<string,string>{{"barrier","fence"},{"fence_type","metal_sheet"}},geometry=new{type="LineString",coordinates=new[]{At(230.2,60.3),At(290.1,60.4)}}},
   new{type="Feature",id="osm-way-2",properties=new Dictionary<string,string>{{"barrier","fence"}},geometry=new{type="LineString",coordinates=new[]{At(40.2,90.3),At(70.1,90.4)}}},
   new{type="Feature",id="osm-node-3",properties=new Dictionary<string,string>{{"barrier","gate"}},geometry=new{type="Point",coordinates=At(240.5,60.4)}},
   new{type="Feature",id="osm-node-4",properties=new Dictionary<string,string>{{"barrier","gate"}},geometry=new{type="Point",coordinates=At(280.5,60.4)}}}});
  var world=new WorldProject{Name="Fenced",GeoReference=reference};
  foreach(var cell in new[]{100,101}){
   var projection=new GeographicProjection(reference,cell,100);var bounds=projection.SectionBounds(256,256);
   var p=MapGenerator.Generate(new(){Kind="blank",Size=256});p.WorldX=cell;p.WorldY=100;p.WorldSection=true;p.Version=Math.Max(p.Version,6);
   p=GeographicImporter.Import(json,p,bounds,projection:projection).Project;if(cell!=100)p.Spawns=[];
   // Each section only sees gate nodes inside its own bounds.
   var own=p.Fences.Single(f=>f.SourceId=="geo-osm-way-1");own.Gates.RemoveAll(g=>cell==100?g.At>=40:g.At<40);
   world.Maps.Add(new(){Project=p});
  }
  if(world.Maps[0].Project.Spawns.Count==0)world.Maps[0].Project.Spawns=[MapGenerator.FindSafeSpawn(world.Maps[0].Project)];
  return(world,json);
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
