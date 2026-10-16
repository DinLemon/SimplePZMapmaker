using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class GenerationQualityTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("quality distinguishes an openable street entrance from a sealed room",()=>{
   var p=Map();var b=House();p.Buildings.Add(b);var open=GenerationQuality.Analyze(p);Check(!open.Issues.Any(i=>i.Code is "unreachable-room" or "entrance-no-street-route"),"Open doorway to street rejected");
   foreach(var e in b.Design!.Floors[0].Edges.Where(e=>e.Kind=="door"))e.Kind="wall";var blocked=GenerationQuality.Analyze(p);Check(blocked.Issues.Any(i=>i.Code=="unreachable-room"&&i.BuildingId==b.Id&&i.X>=b.X&&i.Y>=b.Y)&&blocked.Issues.Any(i=>i.Code=="missing-ground-entrance"),"Sealed rooms or world coordinates missing");
  });
  yield return ("quality checks actual entrance approaches and obstacle footprints",()=>{
   var p=Map();var b=House();p.Buildings.Add(b);var door=b.Design!.Floors[0].Edges.First(e=>e.Kind=="door");p.StaticModels.Add(new(){Profile="obstacle",X=b.X+door.X-1,Y=b.Y+door.Y-1,Footprint=[new(0,0),new(1,0)]});
   var r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="entrance-blocked-approach"&&i.X==b.X+door.X&&i.Y==b.Y-1),"Second collision footprint tile ignored");
  });
  yield return ("quality road components use four neighbors and collision edges",()=>{
   var p=Map();Array.Fill(p.Terrain,Terrain.Grass);p.Terrain[2*p.Width+2]=p.Terrain[3*p.Width+3]=Terrain.Road;var r=GenerationQuality.Analyze(p);Check(r.Counts.RoadComponents==2&&r.Issues.Count(i=>i.Code=="road-island")==1,"Diagonal road tiles merged");
   p.Terrain[2*p.Width+3]=Terrain.Road;r=GenerationQuality.Analyze(p);Check(r.Counts.RoadComponents==1,"Cardinal road link ignored");
   p.Decorations.Add(new(){X=3,Y=2,Parts=[new(0,0,"fixture",4)]});r=GenerationQuality.Analyze(p);Check(r.Counts.RoadComponents==2,"Road fence edge ignored");
  });
  yield return ("quality bridge crossings do not connect distinct ground roads",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Spawns.Clear();p.LinearFeatures.Add(new(){Kind="bridge",Level=1,Width=2,Points=[new(40,60),new(80,60)]});for(var y=40;y<=80;y++)p.Terrain[y*p.Width+60]=Terrain.Road;
   var r=GenerationQuality.Analyze(p,new(32,32,64,64));Check(r.Counts.RoadComponents==2,"Elevated crossing falsely joined the ground road");
  });
  yield return ("quality infrastructure connects streets only through real stair landings",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Spawns.Clear();p.LinearFeatures.Add(new(){Kind="bridge",Level=1,Access="stairs",Width=2,Points=[new(40,60),new(80,60)]});for(var x=35;x<=45;x++)p.Terrain[64*p.Width+x]=Terrain.Road;
   var r=GenerationQuality.Analyze(p,new(32,32,64,64));Check(r.Counts.RoadComponents==1,"Real stair landing failed to join the deck and street");p.StaticModels.Add(new(){Profile="obstacle",X=40,Y=64});r=GenerationQuality.Analyze(p,new(32,32,64,64));Check(r.Counts.RoadComponents>1,"Blocked native lower landing remained connected");
  });
  yield return ("quality does not count a street painted under a sealed building as a safe spawn route",()=>{
   var p=Map();var b=House();foreach(var e in b.Design!.Floors[0].Edges.Where(e=>e.Kind=="door"))e.Kind="wall";p.Buildings.Add(b);p.Spawns=[new(){X=b.X+2,Y=b.Y+2}];p.Terrain[(b.Y+2)*p.Width+b.X+2]=Terrain.Road;var r=GenerationQuality.Analyze(p);Check(r.Counts.SpawnRoutes==0&&r.Issues.Any(i=>i.Code=="spawn-no-street-route"),"Painted floor bypassed sealed facade");
  });
  yield return ("quality forwards real stair and basement landing diagnostics",()=>{
   var p=Map();var b=new Building{X=12,Y=12,Width=12,Height=10};b.Design=BuildingDesigner.Generate(b,2,42,basements:1);foreach(var f in b.Design.Floors)f.Objects.RemoveAll(o=>o.Kind!="stairs");p.Buildings.Add(b);
   var stair=b.Design.Floors[0].Objects.First(o=>o.Kind=="stairs");b.Design.Floors[0].Objects.Add(new(){Kind="asset",X=stair.X,Y=stair.Y+4,Parts=[new(0,0,"fixture",1)]});
   var r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="broken-stair"&&i.BuildingId==b.Id&&i.Z==-1&&i.X==b.X+stair.X)&&r.Issues.Any(i=>i.Code=="basement-access"&&i.Z==-1),"Broken basement transition was not forwarded");
  });
  yield return ("quality warns about residential facilities and exterior daylight",()=>{
   var p=Map();var b=House();b.Design!.Floors[0].Rooms["1"]="bedroom";foreach(var e in b.Design.Floors[0].Edges.Where(e=>e.Kind=="window"))e.Kind="wall";p.Buildings.Add(b);var r=GenerationQuality.Analyze(p);
   Check(r.Issues.Any(i=>i.Code=="residential-wet-room"&&i.Severity=="warning")&&r.Issues.Any(i=>i.Code=="residential-daylight"&&i.BuildingId==b.Id),"Residential advisories missing");
  });
  yield return ("quality locates interior glazing at room partitions and respects explicit authorship",()=>{
   var p=Map();var b=House();var f=b.Design!.Floors[0];f.Cells=string.Concat(Enumerable.Repeat("111222",6));f.Rooms["2"]="bathroom";f.Edges=BuildingDesigner.AutoEdges(b,f,0);var e=f.Edges.Single(e=>e.X==3&&e.Y==2&&e.Edge=="w");e.Kind="window";p.Buildings.Add(b);
   var r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="window-room-partition"&&i.X==b.X+3&&i.Y==b.Y+2),"Interior partition window passed");e.Edited=true;r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="authored-interior-glazing"&&i.Severity=="info")&&!r.Issues.Any(i=>i.Code=="window-room-partition"),"Explicit interior glazing treated as accidental");
  });
  yield return ("quality facade axis variation is advisory only on repeated footprints",()=>{
   var p=Map();var b=House();var copy=WorldAssembler.CloneBuilding(b).Design!.Floors[0];b.Design!.Floors.Add(copy);var window=copy.Edges.First(e=>e.Kind=="window"&&e.Y==0);window.X--;p.Buildings.Add(b);var r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="facade-axis-variation"&&i.Severity=="warning"),"Repeated facade variation hidden");
   var cells=copy.Cells.ToCharArray();cells[^1]='.';copy.Cells=new(cells);r=GenerationQuality.Analyze(p);Check(!r.Issues.Any(i=>i.Code=="facade-axis-variation"),"Setback floor treated as repeated facade");
  });
  yield return ("quality diagnoses enclosed courtyard and structurally isolated spawn",()=>{
   var p=Map();var b=House();var f=b.Design!.Floors[0];var cells=f.Cells.ToCharArray();foreach(var y in new[]{2,3})foreach(var x in new[]{2,3})cells[y*b.Width+x]='.';f.Cells=new(cells);f.Edges=BuildingDesigner.AutoEdges(b,f,0);p.Buildings.Add(b);p.Spawns=[new(){X=b.X+2,Y=b.Y+2}];var r=GenerationQuality.Analyze(p);
   Check(r.Issues.Any(i=>i.Code=="courtyard-isolated")&&r.Issues.Any(i=>i.Code=="spawn-no-street-route"),"Sealed courtyard spawn reported connected");
  });
  yield return ("quality reports gameplay distributions and union coverage",()=>{
   var p=Map();p.GameZones=[new(){Type="TownZone",X=0,Y=0,Width=10,Height=10},new(){Type="Vehicle",X=5,Y=5,Width=10,Height=10}];p.Spawns=[new(){X=2,Y=2}];var r=GenerationQuality.Analyze(p);
   Check(r.Counts.ZoneCoveredTiles==175&&r.Counts.ZonesByType["TownZone"]==1&&r.Counts.SpawnRoutes==1,"Zone union/distribution or spawn route count incorrect");
   p.Terrain[2*p.Width+2]=Terrain.Water;r=GenerationQuality.Analyze(p);Check(r.Issues.Any(i=>i.Code=="spawn-blocked"),"Water spawn accepted");
  });
  yield return ("quality caps building and terrain work without claiming a whole map pass",()=>{
   var p=Map();p.Buildings=[House(),House(30,30)];var r=GenerationQuality.Analyze(p,maximumBuildings:1);Check(!r.Complete&&r.Coverage.BuildingsInArea==2&&r.Coverage.BuildingsAnalyzed==1&&r.Coverage.AccessBuildingsAnalyzed==1,"Building cap silently sampled");
   p=new(){Width=1024,Height=1024,Terrain=new Terrain[1024*1024]};r=GenerationQuality.Analyze(p);Check(!r.Complete&&r.Area.Width==512&&r.Area.Height==512&&r.Coverage.RequestedArea.Width==1024&&r.Coverage.TerrainTiles==262144&&r.Status!="pass","Large area cap silently sampled");
  });
  yield return ("quality cancellation and read-only source preservation are explicit",()=>{
   var p=Map();p.Buildings.Add(House());p.Buildings[0].Modified=true;p.TerrainEdits="";var before=JsonSerializer.Serialize(p);GenerationQuality.Analyze(p);Check(before==JsonSerializer.Serialize(p),"Audit normalized or changed source project");
   using var source=new CancellationTokenSource();source.Cancel();try{GenerationQuality.Analyze(p,token:source.Token);}catch(OperationCanceledException){return;}throw new Exception("Canceled audit ran anyway");
  });
  yield return ("quality audits real contextual generated housing without structural access failures",()=>{
   var p=MapGenerator.Generate(new(){Seed=93,BuildingDensity=100,Rules=new(){Preset="contextual",Era="prewar",Density=100,LandUse="residential"}});var r=GenerationQuality.Analyze(p);
   Check(r.Complete&&r.Coverage.AccessBuildingsAnalyzed==p.Buildings.Count&&r.Counts.Buildings>0&&r.Counts.Errors==0&&r.Counts.RoadComponents==1&&r.Counts.SpawnRoutes>0&&!r.Issues.Any(i=>i.Code is "entrance-no-street-route" or "entrance-blocked-approach"),"Generated contextual housing has structural access failures or incomplete audit coverage");
  });
  yield return ("world quality includes shared crossing buildings roads and bridges without persisting projection",()=>{
   var world=QualityWorld();var before=JsonSerializer.Serialize(world);
   foreach(var member in world.Maps){
    var report=GenerationQualityEndpoints.Analyze(new(member.Project,World:world,MemberId:member.Id));
    Check(report.Counts.Buildings==1&&report.Coverage.AccessBuildingsAnalyzed==1&&!report.Issues.Any(i=>i.Code=="invalid-building"),"Crossing shared building missing or ordinary-map bounds validation applied to projection");
    var projected=GenerationStudioWorldContext.Create(member.Project,WorldAssembler.Clone(world),member.Id).ProjectedProject;
    Check(report.Counts.RoadTiles>projected.Terrain.Count(t=>t==Terrain.Road)&&report.Counts.SpawnRoutes==1&&report.Counts.RoadComponents>=2,"Shared road/deck geometry or structural spawn route missing");
    Check(report.Notes.Any(n=>n.Contains("Shared world scope")),"Projected shared-world scope was not reported");
    Check(member.Project.Buildings.Count==0&&member.Project.SharedInfrastructure is null,"Projected geometry persisted into raw member");
   }
   Check(before==JsonSerializer.Serialize(world),"World quality mutated the raw world snapshot");
  });
  yield return ("world quality rejects a raw member mismatch before auditing shared context",()=>{
   var world=QualityWorld();var member=world.Maps[0];var raw=WorldAssembler.Clone(world).Maps[0].Project;raw.Terrain[0]=Terrain.Dirt;
   try{GenerationQualityEndpoints.Analyze(new(raw,World:world,MemberId:member.Id));}catch(ArgumentException e){Check(e.Message.Contains("does not match"),"Mismatch failed for an unrelated reason");return;}throw new Exception("Stale raw member audited against a different world snapshot");
  });
 }
 static WorldProject QualityWorld(){
  var left=MapGenerator.Generate(new(){Kind="blank"});left.WorldX=left.WorldY=0;var right=MapGenerator.Generate(new(){Kind="blank"});right.WorldX=1;right.WorldY=0;
  var shared=new Building{Id="quality-shared-crossing",X=248,Y=40,Width=16,Height=18};shared.Design=BuildingDesigner.Generate(shared,1,4);foreach(var f in shared.Design.Floors)f.Objects.Clear();
  var world=new WorldProject{Maps=[new(){Project=left},new(){Project=right}],Buildings=[shared],LinearFeatures=[new(){Id="quality-shared-road",Width=6,Points=[new(8,100),new(500,100)]},new(){Id="quality-shared-bridge",Kind="bridge",Width=4,Level=1,Access="stairs",Points=[new(100,70),new(300,70)]}]};WorldAssembler.Validate(world);return world;
 }
 static MapProject Map(){var p=new MapProject{Width=64,Height=64,Terrain=new Terrain[64*64]};for(var x=0;x<p.Width;x++)p.Terrain[3*p.Width+x]=Terrain.Road;return p;}
 static Building House(int x=12,int y=12){var b=new Building{X=x,Y=y,Width=6,Height=6};var f=new BuildingFloor{Cells=new string('1',36),Rooms=new(){["1"]="livingroom"}};b.Design=new(){Floors=[f]};f.Edges=BuildingDesigner.AutoEdges(b,f,0);return b;}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
