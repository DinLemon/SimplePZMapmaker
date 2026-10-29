using System.Text.Json;
using SimplePZMapmaker.Core;

public static class StreamedBuildingBatchTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("prepared building batch exposes global source ordered candidates",()=>{
   var reference=Reference();var json=Collection([
    Marker(reference,"context"),
    Polygon(reference,"degenerate",[(200,40),(210,40),(220,40),(200,40)]),
    Box(reference,"oversized",100.25,80.25,613.25,100.25),
    Box(reference,"seam",250.25,40.25,270.25,60.25),
    Box(reference,"local",40.25,80.25,50.25,90.25)]);
   var batch=GeographicImporter.PrepareWorldBuildings(json,reference);
   Check(batch.Candidates.Select(c=>c.Index).SequenceEqual(new[]{3,4}),"Invalid/nonbuilding features changed original candidate ordinals");
   Check(batch.Candidates[0]==new WorldBuildingCandidate(3,25850,25640,21,21),"Prepared bbox is not the original straightened global bbox");
   Check(batch.Candidates.All(c=>c.Width<=512&&c.Height<=512),"Oversized footprint escaped preparation");
   Reject<ArgumentOutOfRangeException>(()=>batch.ImportOne(0,WorldTests.Fixture(2),42,[]));
  });
  foreach(var courtyard in new[]{false,true})yield return($"prepared building batch matches legacy straightened seam rooms courtyard={courtyard}",()=>{
   var reference=Reference();reference.RotationDegrees=-17;
   var angle=Math.PI/6;var points=courtyard?new(double X,double Y)[]{(-12,-12),(12,-12),(12,12),(-12,12),(-12,-12)}:[(-12,-8),(12,-8),(12,0),(2,0),(2,8),(-12,8),(-12,-8)];
   (double X,double Y) Turn((double X,double Y) p)=>(260.25+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),60.25+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
   var rings=new List<(double X,double Y)[]>{points.Select(Turn).ToArray()};
   if(courtyard)rings.Add(new(double X,double Y)[]{(-4,-4),(4,-4),(4,4),(-4,4),(-4,-4)}.Select(Turn).ToArray());
   var json=Collection([Polygon(reference,"tilted",rings[0],rings.Skip(1).ToArray(),new(){["building"]="warehouse",["building:levels"]="2",["roof:shape"]="flat",["pz:room"]="storage"})]);
   var legacy=WorldTests.Fixture(4);legacy.GeoReference=reference;
   var window=Window(legacy,2); // no GeoReference: all geographic work must already be prepared
   var batch=GeographicImporter.PrepareWorldBuildings(json,reference);var candidate=batch.Candidates.Single();
   Check(candidate.X<25856&&candidate.X+candidate.Width>25856,"Fixture stopped crossing the seam");
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(1,0),"Legacy straightened seam fixture was rejected");
   Check(batch.ImportOne(candidate.Index,window,42,[])==(1,0),"Prepared straightened seam fixture was rejected");
   var building=window.Buildings.Single();Parity(legacy.Buildings.Single(),building);
   Check(building.Design!.Floors.Count==2&&building.Design.Floors.All(f=>f.Rooms.Count>0),"Prepared seam lost its multi-floor rooms");
   Check(building.SourceOutline.Count==(courtyard?2:1)&&building.SourceOutline[0][0]==At(reference,rings[0][0]),"Straightening overwrote original geographic evidence");
   var floor=building.Design.Floors[0];bool Filled(int x,int y)=>BuildingDesigner.Cell(building,floor,x,y)!='.';var corners=0;
   for(var y=0;y<=building.Height;y++)for(var x=0;x<=building.Width;x++)if(new[]{Filled(x-1,y-1),Filled(x,y-1),Filled(x-1,y),Filled(x,y)}.Count(v=>v) is 1 or 3)corners++;
   Check(corners==(courtyard?8:6),"Straightened seam contour gained staircase corners or filled a courtyard");
   Check(window.Maps.Count==2&&window.Maps.All(m=>m.Project.Buildings.Count==0),"Prepared seam created local fragments or loaded unrelated sections");WorldAssembler.Validate(window);
  });
  yield return("prepared building batch imports only one source and reuses prepared geometry",()=>{
   var reference=Reference();var json=Collection([Box(reference,"first",250.25,40.25,270.25,60.25),Box(reference,"far",762.25,40.25,782.25,60.25)]);
   var world=WorldTests.Fixture(4);world.GeoReference=reference;var batch=GeographicImporter.PrepareWorldBuildings(json,reference);
   var window=Window(world,2);Check(batch.ImportOne(0,window,42,[])==(1,0)&&window.Buildings.Single().SourceId=="geo-first","ImportOne processed a different or additional source");
   Check(batch.ImportOne(0,window,42,[])==(0,0),"Repeated source was duplicated");
   var fresh=Window(world,2);Check(batch.ImportOne(0,fresh,42,[])==(1,0),"Batch kept window-specific duplicate state");Parity(window.Buildings.Single(),fresh.Buildings.Single());
   var far=new WorldProject{Maps=WorldAssembler.Clone(world).Maps.Skip(2).ToList()};
   Check(batch.ImportOne(1,far,42,[])==(1,0)&&far.Buildings.Single().SourceId=="geo-far","Prepared geometry was clipped to the first window");
   var before=JsonSerializer.Serialize(fresh);using var cancel=new CancellationTokenSource();cancel.Cancel();
   Reject<OperationCanceledException>(()=>batch.ImportOne(0,fresh,42,[],cancel.Token));Check(JsonSerializer.Serialize(fresh)==before,"Canceled import changed its window");
  });
  yield return("prepared building batch displaces later local source with original evidence intact",()=>{
   var (json,legacy)=PriorityFixture();var window=Window(legacy,2);var batch=GeographicImporter.PrepareWorldBuildings(json,legacy.GeoReference!);
   var prior=window.Maps[0].Project.Buildings.Single();var evidenceBefore=JsonSerializer.Serialize(window.Maps[0].Project.SourceFeatures.Single(f=>f.BuildingId==prior.Id).SourceGeometry);
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(1,0),"Legacy priority fixture failed");
   Check(batch.ImportOne(batch.Candidates[0].Index,window,42,[])==(1,0),"Later automatic outline blocked the earlier seam source");
   Check(window.Maps.All(m=>m.Project.Buildings.All(b=>b.SourceId!="geo-second")),"Deferred local source still occupies tiles");
   var owner=window.Maps[0].Project;var evidence=owner.SourceFeatures.Single(f=>f.SourceId=="geo-second");
   Check(evidence.BuildingId is null&&JsonSerializer.Serialize(evidence.SourceGeometry)==evidenceBefore,"Deferred source evidence lost its geometry or kept a dangling binding");
   Check(owner.ImportSummary!.SkippedOutlines.Any(o=>o.SourceId=="geo-second"&&o.Reason.Contains("source priority")),"Deferred source disappeared from the transfer report");
   Check(JsonSerializer.Serialize(legacy.Maps.Take(2))==JsonSerializer.Serialize(window.Maps),"Batch changed local evidence/reports differently from legacy: "+Difference(JsonSerializer.Serialize(legacy.Maps.Take(2)),JsonSerializer.Serialize(window.Maps)));Parity(legacy.Buildings.Single(),window.Buildings.Single());WorldAssembler.Validate(window);
  });
  yield return("prepared building batch uses noncandidate source priority for repeated identities",()=>{
   var (_,legacy)=PriorityFixture();var reference=legacy.GeoReference!;
   var json=Collection([Marker(reference,"second"),Box(reference,"first",250.25,40.25,270.25,60.25),Box(reference,"second",247.25,44.25,254.25,51.25)]);
   var window=Window(legacy,2);var before=JsonSerializer.Serialize(window.Maps);var batch=GeographicImporter.PrepareWorldBuildings(json,reference);
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(0,1),"Legacy did not retain the first original source identity");
   Check(batch.ImportOne(batch.Candidates[0].Index,window,42,[])==(0,1),"Batch built priority only from building candidates");
   Check(window.Buildings.Count==0&&JsonSerializer.Serialize(window.Maps)==before,"Earlier original source was displaced: "+Difference(before,JsonSerializer.Serialize(window.Maps)));
  });
  yield return("prepared building batch rejects water and restores local outlines dressing and evidence",()=>{
   var (json,legacy)=PriorityFixture();var owner=legacy.Maps[1].Project;
   owner.Terrain[44*256+6]=Terrain.Water;
   owner.Decorations.Add(new(){Id="street-batch",X=3,Y=44,Asset="street_decoration_01_28",Parts=[new(0,0,"street_decoration_01_28")]});WorldAssembler.Validate(legacy);
   var window=Window(legacy,2);var before=JsonSerializer.Serialize(window.Maps);var legacyBefore=JsonSerializer.Serialize(legacy.Maps);
   var batch=GeographicImporter.PrepareWorldBuildings(json,legacy.GeoReference!);
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(0,1)&&JsonSerializer.Serialize(legacy.Maps)==legacyBefore,"Legacy water rejection did not roll back: "+Difference(legacyBefore,JsonSerializer.Serialize(legacy.Maps)));
   Check(batch.ImportOne(batch.Candidates[0].Index,window,42,[])==(0,1),"Prepared seam ignored water");
   Check(window.Buildings.Count==0&&JsonSerializer.Serialize(window.Maps)==before,"Failed candidate changed local outlines, dressing or evidence");WorldAssembler.Validate(window);
  });
  yield return("prepared building batch respects earlier shared overlapping sources",()=>{
   var reference=Reference();var json=Collection([Box(reference,"first",250.25,40.25,270.25,60.25),Box(reference,"overlap",254.25,44.25,274.25,64.25)]);
   var legacy=WorldTests.Fixture(2);legacy.GeoReference=reference;var window=Window(legacy,2);var batch=GeographicImporter.PrepareWorldBuildings(json,reference);
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(1,1),"Legacy overlap fixture changed: "+string.Join(",",legacy.Buildings.Select(b=>b.SourceId)));
   Check(batch.ImportOne(0,window,42,[])==(1,0)&&batch.ImportOne(1,window,42,[])==(0,1),"Prepared source overlapped a prior shared building");Parity(legacy.Buildings.Single(),window.Buildings.Single());WorldAssembler.Validate(window);
  });
  foreach(var protection in new[]{"modified","site","locked"})yield return($"prepared building batch protects {protection} local sources",()=>{
   var (json,legacy)=PriorityFixture();var owner=legacy.Maps[0].Project;var local=owner.Buildings.Single();
   if(protection=="modified")local.Modified=true;
   else if(protection=="locked"){owner.Generation??=new();owner.Generation.LockedBuildingIds.Add(local.Id);}
   else owner.Sites.Add(new(){Id="batch-site",Name="Keep",Locked=true,Boundary=[[new(240,30),new(255,30),new(255,70),new(240,70),new(240,30)]],BuildingIds=[local.Id]});
   var window=Window(legacy,2);var before=JsonSerializer.Serialize(window.Maps);var batch=GeographicImporter.PrepareWorldBuildings(json,legacy.GeoReference!);
   Check(GeographicImporter.ImportWorldBuildings(json,legacy,42,[])==(0,1),"Legacy protected-source fixture changed");
   Check(batch.ImportOne(batch.Candidates[0].Index,window,42,[])==(0,1)&&JsonSerializer.Serialize(window.Maps)==before,"Prepared seam displaced a protected local outline: "+Difference(before,JsonSerializer.Serialize(window.Maps)));
  });
 }
 static WorldGeoReference Reference()=>new(){Bounds=new(){West=32,East=32.02,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
 static SourceOutlinePoint At(WorldGeoReference reference,(double X,double Y) p){var g=new GeographicProjection(reference,100,100).Geographic(p.X,p.Y);return new(g.Longitude,g.Latitude);}
 static object Marker(WorldGeoReference reference,string id){var g=At(reference,(20,20));return new{type="Feature",id,properties=new{name="Context"},geometry=new{type="Point",coordinates=new[]{g.Longitude,g.Latitude}}};}
 static object Polygon(WorldGeoReference reference,string id,(double X,double Y)[] outer,(double X,double Y)[][]? holes=null,Dictionary<string,string>? tags=null)=>new{type="Feature",id,properties=tags??new(){["building"]="house"},geometry=new{type="Polygon",coordinates=new[]{outer}.Concat(holes??[]).Select(r=>r.Select(p=>{var g=At(reference,p);return new[]{g.Longitude,g.Latitude};}).ToArray()).ToArray()}};
 static object Box(WorldGeoReference reference,string id,double x1,double y1,double x2,double y2)=>Polygon(reference,id,[(x1,y1),(x2,y1),(x2,y2),(x1,y2),(x1,y1)]);
 static string Collection(object[] features)=>JsonSerializer.Serialize(new{type="FeatureCollection",features});
 static WorldProject Window(WorldProject source,int count)=>new(){Maps=WorldAssembler.Clone(source).Maps.Take(count).ToList()};
 static (string Json,WorldProject World) PriorityFixture(){
  var reference=Reference();var json=Collection([Marker(reference,"context"),Box(reference,"first",250.25,40.25,270.25,60.25),Box(reference,"second",247.25,44.25,254.25,51.25)]);
  var world=WorldTests.Fixture(3);world.GeoReference=reference;
  foreach(var member in world.Maps){var p=member.Project;p.WorldSection=true;p.Version=6;var projection=new GeographicProjection(reference,p.WorldX,p.WorldY);member.Project=GeographicImporter.Import(json,p,projection.SectionBounds(256,256),projection:projection).Project;}
  Check(world.Maps[0].Project.Buildings.Single().SourceId=="geo-second","Priority fixture needs a later automatic local outline");WorldAssembler.Validate(world);foreach(var member in world.Maps)member.Project.Version=member.Project.Version;return(json,world);
 }
 static void Parity(Building expected,Building actual){var copy=WorldAssembler.CloneBuilding(actual);copy.Id=expected.Id;Check(JsonSerializer.Serialize(expected)==JsonSerializer.Serialize(copy),"Legacy/prepared placement differs in geometry, rooms, floors, tags or source evidence");}
 static string Difference(string a,string b){var n=0;while(n<Math.Min(a.Length,b.Length)&&a[n]==b[n])n++;return $"at {n}: {a.Substring(Math.Max(0,n-30),Math.Min(a.Length-Math.Max(0,n-30),130))} -> {b.Substring(Math.Max(0,n-30),Math.Min(b.Length-Math.Max(0,n-30),130))}";}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
}
