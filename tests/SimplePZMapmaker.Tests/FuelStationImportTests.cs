using System.Text.Json;
using SimplePZMapmaker.Core;

public static class FuelStationImportTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return("fuel station closed parent suppresses contained pump nodes on first import and refresh",()=>{
   var pump=Point("pump",24.2,36.2,new(){{"man_made","fuel_pump"}});
   var active=Json([Polygon("station",[Ring(20,30,30,25)],new(){{"amenity","fuel"}}),pump,Road()]);
   var closed=Json([Polygon("station",[Ring(20,30,30,25)],new(){{"amenity","fuel"},{"disused","yes"}}),pump,Road()]);
   Check(!Import(closed).Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Closed site created a live child pump");
   var first=Import(active);Check(first.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Missing active child pump fixture");
   Check(!Import(closed,first,true).Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Closed site retained an unchanged generated child pump");
   first.Decorations.Single(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)).X+=5;
   Check(Import(closed,first,true).Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Closed site erased an authored child pump");
  });
  yield return("fuel station closed parent excludes courtyard holes from inherited closure",()=>{
   var p=Import(Json([Polygon("closed",[Ring(20,30,40,30),Ring(28,34,12,14)],new(){{"amenity","fuel"},{"disused","yes"}}),Point("independent",32.2,40.2,new(){{"man_made","fuel_pump"}}),Road()]));
   Check(p.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Closed parent bbox incorrectly closed a separate pump in its hole");
  });
  yield return("fuel planner does not associate a point in a station hole or concave cutout by bounding box",()=>{
   var p=GroundRoad();
   foreach(var station in new[]{
    new FuelStationSource("station",Contours(Ring(20,30,40,30),Ring(28,34,12,14)),true),
    new FuelStationSource("station",Contours([(20,30),(60,30),(60,34),(28,34),(28,50),(20,50),(20,30)]),true)}){
    var pump=new FuelStationSource("unrelated-pump",Contours([(32.2,40.2)]),false,true);
    Check(FuelStationLayout.Plan(p,[station]).Placements.Count==1,"Station alone needs a safe forecourt");
    Check(FuelStationLayout.Plan(p,[pump]).Placements.Count==1,"Separate point needs a safe forecourt");
    Check(FuelStationLayout.Plan(p,[station,pump]).Placements.Count>0,"False bbox association suppressed both independent sources");
   }
  });
  yield return("fuel station refresh retires unedited generated pumps when a source closes and keeps authored pumps",()=>{
   var active=Json([Point("station",30,40,new(){{"amenity","fuel"}}),Road()]);
   var closed=Json([Point("station",30,40,new(){{"amenity","fuel"},{"disused","yes"}}),Road()]);
   var first=Import(active);Check(first.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"No generated pump in closure fixture");
   var after=Import(closed,first,true);Check(!after.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Closed source retained an unedited operational pump");
   var pump=first.Decorations.Single(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal));pump.X+=5;
   var edited=Import(closed,first,true);Check(edited.Decorations.Any(d=>d.Id==pump.Id&&d.X==pump.X),"Closure erased an authored pump edit");
   Check(edited.ImportSummary!.Notes.Any(n=>n.Contains("1 authored/legacy pumps preserved",StringComparison.Ordinal)),"Authored inactive pump preservation count is missing");
  });
  yield return("fuel station hook imports a native pump with a bounded drivable forecourt and original evidence",()=>{
   var outer=Ring(20,30,30,25);var hole=Ring(28,35,4,8);
   var json=Json([Polygon("station",[outer,hole],new(){{"amenity","fuel"},{"name","Source station"}}),Road()]);
   var baseline=MapGenerator.Generate(new(){Kind="blank"});var before=Serialize(baseline);var p=Import(json,baseline);
   Check(Serialize(baseline)==before,"Import mutated its input");var pump=p.Decorations.Single(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal));
   Check(pump.Asset is "location_shop_fossoil_01_12" or "location_shop_fossoil_01_14","Pump is not one of the two native fuelAmount sprites");
   Check(pump.Parts is [{Dx:0,Dy:0,Flags:1}]&&pump.Parts[0].Tile==pump.Asset,"Pump must be a solid, single native tile");
   Check(p.GameZones.Count==0&&p.Buildings.Count==0&&p.RequiredMods.Count==0,"Station invented buildings, zones or mod dependencies");
   var evidence=p.SourceFeatures.Single(f=>f.SourceId=="geo-station");Check(evidence.SourceGeometry.Count==2&&evidence.Geometry.Count==2,"Original courtyard evidence was lost");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("forecourt",StringComparison.OrdinalIgnoreCase))&&p.ImportSummary.Notes.Any(n=>n.Contains("power",StringComparison.OrdinalIgnoreCase)),"Footprint provenance or native fuel/power assumptions missing");
   ProjectValidator.Validate(p);
  });
  yield return("fuel station hook honors explicit pump points and suppresses an additional inferred station pump",()=>{
   var p=Import(Json([Polygon("station",[Ring(20,30,30,25)],new(){{"amenity","fuel"}}),Point("pump",24.2,36.2,new(){{"man_made","fuel_pump"}}),Road()]));
   var pumps=p.Decorations.Where(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)).ToList();Check(pumps.Count==1,"Explicit source pump was duplicated by a station estimate");
   Check(pumps[0].X==24&&pumps[0].Y==36,"Explicit pump was moved to an invented location");
   var evidence=p.SourceFeatures.Single(f=>f.SourceId=="geo-pump");var original=Projection.Geographic(24.2,36.2);
   Check(evidence.Tags.GetValueOrDefault("man_made")=="fuel_pump"&&evidence.SourceGeometry[0][0]==new SourceOutlinePoint(original.Longitude,original.Latitude),"Explicit native pump original source evidence missing");
  });
  yield return("fuel station hook preserves any existing matching source IDs and authored pump edits on refresh",()=>{
   var json=Json([Point("station",30,40,new(){{"amenity","fuel"}}),Road()]);var first=Import(json);
   var pump=first.Decorations.Single(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal));pump.X=120;pump.Y=120;pump.Asset="trashcontainers_01_17";pump.Parts=[new(0,0,pump.Asset,1)];
   var secondId=pump.Id[..pump.Id.LastIndexOf('-')]+"-7";first.Decorations.Add(new(){Id=secondId,X=122,Y=120,Asset=pump.Asset,Parts=[new(0,0,pump.Asset,1)]});
   var original=Serialize(first.Decorations.Where(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)).ToList());
   var again=Import(json,first,true);Check(Serialize(again.Decorations.Where(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)).ToList())==original,"Refresh moved, replaced or duplicated authored pump IDs");
   Check(again.ImportSummary!.Notes.Any(n=>n.Contains("preserved",StringComparison.OrdinalIgnoreCase)),"Authored pump preservation was not reported");
  });
  yield return("fuel station hook skips and reports disused abandoned station and pump tags",()=>{
   var tags=new Dictionary<string,string>[] {new(){{"amenity","fuel"},{"disused","yes"}},new(){{"amenity","fuel"},{"abandoned","yes"}},
    new(){{"disused:amenity","fuel"}},new(){{"abandoned:amenity","fuel"}},new(){{"man_made","fuel_pump"},{"disused","yes"}},
    new(){{"disused:man_made","fuel_pump"}},new(){{"abandoned:man_made","fuel_pump"}}};
   foreach(var t in tags){var p=Import(Json([Point("inactive",30,40,t),Road()]));Check(!p.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"Inactive fuel feature was operationalized");
    Check(p.ImportSummary!.Notes.Any(n=>n.Contains("disused",StringComparison.OrdinalIgnoreCase)||n.Contains("abandoned",StringComparison.OrdinalIgnoreCase)),"Inactive fuel feature was silently ignored");}
  });
  yield return("fuel planner is pure deterministic and retains concavity and holes in the entire forecourt",()=>{
   var p=GroundRoad();var outer=new (double X,double Y)[]{(20,30),(60,30),(60,40),(40,40),(40,60),(20,60),(20,30)};
   var source=new FuelStationSource("station",Contours(outer,Ring(28,35,4,8)),true);var before=Serialize(p);
   var a=FuelStationLayout.Plan(p,[source]);var b=FuelStationLayout.Plan(p,[source]);Check(Serialize(p)==before&&Serialize(a)==Serialize(b),"Planner mutated input or changed the candidate");
   var layout=a.Placements.Single();Check(layout.Connection.Width==3&&layout.Connection.Height<=24&&layout.Facing=="E","Expected bounded north connection and east-facing pump");
   foreach(var (x,y) in Cells(layout.Forecourt)){Check(x>=20&&y>=30&&x+1<=60&&y+1<=60&&(x+1<=40||y+1<=40),"Forecourt crosses concavity");Check(x+1<=28||x>=32||y+1<=35||y>=43,"Forecourt crosses a courtyard hole");}
   Check(layout.Access.Width==3&&layout.Access.Height==5&&FuelStationLayout.IsNativePump(layout.Pump),"Native pump or >=3-tile access strip malformed");
   Check(a.Notes.Any(n=>n.Contains("inferred",StringComparison.OrdinalIgnoreCase)),"Pump location inference was not disclosed");
  });
  yield return("fuel planner selects the two real native pump facings and discloses point boundaries",()=>{
   foreach(var facing in new[]{"E","S"}){
    var p=MapGenerator.Generate(new(){Kind="blank"});Fill(p,facing=="E"?new(0,10,120,6):new(10,0,6,120),Terrain.Road);
    var plan=FuelStationLayout.Plan(p,[new("point",Contours([(30,30)]),false)]);var layout=plan.Placements.Single();
    Check(layout.Facing==facing&&layout.Pump.Asset==(facing=="E"?"location_shop_fossoil_01_12":"location_shop_fossoil_01_14"),"Wrong native facing/tile");
    Check(layout.InferredBoundary&&plan.Notes.Any(n=>n.Contains("12x12")&&n.Contains("(24,24)")),"Unobserved point boundary was not disclosed precisely");
    Check(facing=="E"?layout.Connection.Width==3:layout.Connection.Height==3,"Driveway is narrower than three tiles");
   }
  });
  yield return("fuel planner shares occupancy protections for fixed observed pumps",()=>{
   var obstacles=new (string Name,Action<MapProject> Apply)[]{
    ("water",p=>p.Terrain[30*p.Width+30]=Terrain.Water),
    ("building",p=>p.Buildings.Add(new(){X=30,Y=30,Width=6,Height=6})),
    ("prop",p=>p.Decorations.Add(new(){Id="authored",X=30,Y=30,Asset="trashcontainers_01_17",Parts=[new(0,0,"trashcontainers_01_17",1)]})),
    ("model",p=>p.StaticModels.Add(new(){X=30,Y=30})),
    ("fence",p=>{p.FenceStyles=[GeographicImporter.DefaultFenceStyles[0]];p.Fences=[new(){Id="fence",Style=p.FenceStyles[0].Id,Points=[new(30,30),new(30,34)]}];}),
    ("manual surface",p=>p.SurfaceTiles[30*p.Width+30]="blends_natural_01_0"),
    ("terrain edit",p=>TerrainEditMask.Mark(p,30*p.Width+30)),
    ("protected area",p=>p.Generation=new(){ProtectedAreas=[new(){X=30,Y=30,Width=2,Height=2}]}),
    ("spawn",p=>p.Spawns.Add(new(){X=30,Y=30})),
    ("vehicle stall",p=>p.GameZones.Add(new(){Id=new string('a',32),Type="ParkingStall",X=30,Y=30,Width=3,Height=5,VehicleDistribution="parkingstall"})),
    ("parking aisle",p=>p.GameZones.Add(new(){Id=new string('a',32),Type="ParkingStall",X=30,Y=25,Width=3,Height=5,VehicleDistribution="parkingstall",Direction="N",SourceTags=new(){{"pzmap:parking-source","mapped-lot"}}}))};
   foreach(var obstacle in obstacles){var p=GroundRoad();obstacle.Apply(p);var before=Serialize(p);var plan=FuelStationLayout.Plan(p,[new("pump",Contours([(30.2,30.2)]),false,true)]);
    Check(plan.Placements.Count==0,$"Observed pump ignores {obstacle.Name}");Check(Serialize(p)==before,$"Planner changed {obstacle.Name}");}
  });
  yield return("fuel planner rejects a sub-tile hole even when no tile centre falls inside it",()=>{
   var plan=FuelStationLayout.Plan(GroundRoad(),[new("tiny-hole",Contours(Ring(20,30,4,5),Ring(21.1,30.1,.2,.2)),true)]);
   Check(plan.Placements.Count==0&&plan.SkippedSources==1,"Forecourt bridged an unobserved tiny courtyard hole");
  });
  yield return("fuel planner rejects blocked narrow remote and elevated road approaches",()=>{
   var p=GroundRoad();Fill(p,new(0,25,120,1),Terrain.Water);
   Check(FuelStationLayout.Plan(p,[new("water-barrier",Contours([(30,40)]),false,true)]).Placements.Count==0,"Driveway crossed water");
   p=MapGenerator.Generate(new(){Kind="blank"});Fill(p,new(0,20,120,1),Terrain.Road);
   Check(FuelStationLayout.Plan(p,[new("narrow",Contours([(30,40)]),false,true)]).Placements.Count==0,"One-tile road pretended to be a wide car connection");
   p=GroundRoad();Check(FuelStationLayout.Plan(p,[new("remote",Contours([(30,100)]),false,true)]).Placements.Count==0,"Driveway teleported to a road outside 24 tiles");
   p=GroundRoad();p.LinearFeatures.Add(new(){Id="raised",Kind="road",Level=1,Width=6,Points=[new(5,20),new(115,20)]});
   Check(FuelStationLayout.Plan(p,[new("raised",Contours([(30,40)]),false,true)]).Placements.Count==0,"Elevated road was accepted as a ground connection");
  });
  yield return("fuel planner bounds work geometry candidate counts sources and diagnostic notes",()=>{
   var p=GroundRoad();var source=new FuelStationSource("lot",Contours(Ring(20,30,20,20)),true);
   var plan=FuelStationLayout.Plan(p,[source],workBudget:8);Check(plan.Limited&&plan.Work<=8&&plan.Placements.Count==0,"Small work budget was exceeded");
   plan=FuelStationLayout.Plan(new(){Width=2048,Height=2048},[new("large",Contours(Ring(0,0,2048,2048)),true)]);
   Check(plan.Placements.Count==0&&plan.Notes.Any(n=>n.Contains("oversized")),"Oversized bounds reached occupancy allocation");
   var many=Enumerable.Range(0,200).Select(i=>new FuelStationSource("point-"+i,Contours([(30,100)]),false,true)).ToArray();
   plan=FuelStationLayout.Plan(p,many);Check(plan.Placements.Count==0&&plan.Limited&&plan.Work<=FuelStationLayout.MaximumWork&&plan.Notes.Count<=FuelStationLayout.MaximumNotes,"Source/work/diagnostic limit exceeded");
   plan=FuelStationLayout.Plan(p,many.Take(3).ToArray(),workBudget:30);Check(plan.Limited&&plan.SkippedSources==3,"An unprocessed source disappeared from exhausted-budget diagnostics");
  });
  yield return("fuel planner reserves new forecourts and is independent of source ordering",()=>{
   var p=GroundRoad();var a=new FuelStationSource("a",Contours(Ring(20,30,12,12)),true);var b=new FuelStationSource("b",Contours(Ring(20,30,12,12)),true);
   var first=FuelStationLayout.Plan(p,[a,b]);var second=FuelStationLayout.Plan(p,[b,a]);Check(Serialize(first)==Serialize(second),"Input order changed deterministic placement");
   Check(first.Placements.Count>0,"Overlapping station fixture produced no pump");
   foreach(var left in first.Placements)foreach(var right in first.Placements.Where(r=>r!=left))Check(!Cells(left.Forecourt).Intersect(Cells(right.Forecourt)).Any(),"Two generated forecourts overlap");
  });
  yield return("fuel planner never generates pumps whose footprint provenance exceeds its note budget",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});var sources=new List<FuelStationSource>();
   for(var row=0;row<8;row++){Fill(p,new(0,10+row*30,512,6),Terrain.Road);for(var col=0;col<16;col++)sources.Add(new("point-"+row+"-"+col,Contours([(10+col*30,22+row*30)]),false));}
   var plan=FuelStationLayout.Plan(p,sources);Check(plan.Placements.Count>0&&plan.Notes.Count<=FuelStationLayout.MaximumNotes,"Provenance fixture failed or note budget exceeded");
   foreach(var layout in plan.Placements)Check(plan.Notes.Any(n=>n.StartsWith("Fuel "+layout.SourceId+":",StringComparison.Ordinal)&&n.Contains("forecourt")),"Pump generated without bounded footprint provenance");
  });
  yield return("fuel planner never paves a driveway across a station courtyard hole",()=>{
   var plan=FuelStationLayout.Plan(GroundRoad(),[new("courtyard-barrier",Contours(Ring(20,30,10,20),Ring(20.2,32,9.6,1)),true)]);
   Check(plan.Placements.Count==0,"Road connection crossed and would pave an excluded station courtyard");
  });
  yield return("fuel planner preserves building entrance routes even on otherwise empty ground",()=>{
   var p=GroundRoad();var house=new Building{X=40,Y=45,Width=12,Height=12};house.Design=BuildingDesigner.Generate(house,1,42);p.Buildings.Add(house);
   var raw=new ParcelPlacement(p);var entrances=new ParcelPlacement(p);entrances.ReserveEntrances();
   var passage=(from y in Enumerable.Range(28,5) from x in Enumerable.Range(30,35) where entrances.Passage(x,y)&&raw.Free(x,y) select (X:x,Y:y)).First();
   var plan=FuelStationLayout.Plan(p,[new("observed",Contours([(passage.X+.2,passage.Y+.2)]),false,true)]);
   Check(plan.Placements.Count==0,"A pump blocked the shared reserved entrance route");
  });
  yield return("fuel station real import cannot drive through a new source fence",()=>{
   var fence=new{type="Feature",id="barrier",properties=new Dictionary<string,string>{{"barrier","fence"}},geometry=new{type="LineString",coordinates=new[]{At((10,30)),At((90,30))}}};
   var p=Import(Json([Point("observed",30,40,new(){{"man_made","fuel_pump"}}),fence,Road()]));
   Check(p.Fences.Count==1&&!p.Decorations.Any(d=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)),"New source fence was not present when checking the fuel access corridor");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("no contained forecourt")),"Blocked pump was skipped without a reason");ProjectValidator.Validate(p);
  });
  yield return("fuel planner respects decoration capacity without changing existing decorations",()=>{
   var p=GroundRoad();for(var i=0;i<ProjectValidator.MaximumDecorations;i++)p.Decorations.Add(new(){Id="kept-"+i,X=140+i%100,Y=i/100,Asset="trashcontainers_01_17",Parts=[new(0,0,"trashcontainers_01_17",1)]});
   var plan=FuelStationLayout.Plan(p,[new("point",Contours([(30,40)]),false)]);
   Check(plan.Placements.Count==0&&plan.SkippedSources==1&&plan.Notes.Any(n=>n.Contains("capacity"))&&p.Decorations.Count==10000,"Decoration capacity was exceeded or objects were replaced");
  });
 }
 static readonly WorldGeoReference Reference=new(){Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
 static readonly GeographicProjection Projection=new(Reference,100,100);
 static double[] At((double X,double Y) v){var g=Projection.Geographic(v.X,v.Y);return[g.Longitude,g.Latitude];}
 static (double X,double Y)[] Ring(double x,double y,double w,double h)=>[(x,y),(x+w,y),(x+w,y+h),(x,y+h),(x,y)];
 static IReadOnlyList<IReadOnlyList<(double X,double Y)>> Contours(params (double X,double Y)[][] rings)=>rings;
 static IEnumerable<(int X,int Y)> Cells(ZoneRectangle r){for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)yield return(x,y);}
 static void Fill(MapProject p,ZoneRectangle r,Terrain terrain){foreach(var (x,y) in Cells(r))p.Terrain[y*p.Width+x]=terrain;}
 static MapProject GroundRoad(){var p=MapGenerator.Generate(new(){Kind="blank"});Fill(p,new(0,17,120,7),Terrain.Road);return p;}
 static object Polygon(string id,(double X,double Y)[][] rings,Dictionary<string,string> tags)=>new{type="Feature",id,properties=tags,geometry=new{type="Polygon",coordinates=rings.Select(r=>r.Select(At).ToArray()).ToArray()}};
 static object Point(string id,double x,double y,Dictionary<string,string> tags)=>new{type="Feature",id,properties=tags,geometry=new{type="Point",coordinates=At((x,y))}};
 static object Road()=>new{type="Feature",id="road",properties=new Dictionary<string,string>{{"highway","residential"},{"width","6"}},geometry=new{type="LineString",coordinates=new[]{At((5,20)),At((100,20))}}};
 static string Json(object[] features)=>Serialize(new{type="FeatureCollection",pzTransfer=1,features});
 static string Serialize<T>(T value)=>JsonSerializer.Serialize(value);
 static MapProject Import(string json,MapProject? baseline=null,bool preserve=false){
  return GeographicImporter.Import(json,baseline??MapGenerator.Generate(new(){Kind="blank"}),Reference.Bounds,preserveEdits:preserve,projection:Projection).Project;
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
