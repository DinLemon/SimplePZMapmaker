using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;

public static class ParkingImportTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return ("parking import contains every whole stall tile in concave polygons and courtyard holes",()=>{
   var outer=new (double X,double Y)[]{(20,20),(80,20),(80,38),(47,38),(47,80),(20,80),(20,20)};
   var hole=new (double X,double Y)[]{(26,26),(38,26),(38,41),(26,41),(26,26)};
   var p=Import([Polygon("lot",[outer,hole],new(){{"amenity","parking"},{"pz:direction","N"}})]);
   var stalls=p.GameZones.Where(z=>z.Vehicle).ToList();Check(stalls.Count>1,"Concave parking must retain multiple usable rows");
   foreach(var z in stalls)foreach(var (x,y) in Cells(z)){
    Check(x>=20&&y>=20&&x+1<=80&&y+1<=80&&(x+1<=47||y+1<=38),$"Stall outside concavity at {x},{y}");
    Check(x+1<=26||x>=38||y+1<=26||y>=41,$"Stall intersects courtyard at {x},{y}");
   }
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("rejected stalls",StringComparison.OrdinalIgnoreCase)),"Rejected polygon/hole candidates must be reported");
   ProjectValidator.Validate(p);
  });
  yield return ("parking import avoids mapped buildings water fences and preserved props without mutating input",()=>{
   var baseline=MapGenerator.Generate(new(){Kind="blank"});
   baseline.Decorations.Add(new(){Id="authored-bin",X=70,Y=20,Asset="trashcontainers_01_17",Parts=[new(0,0,"trashcontainers_01_17",1)]});
   var before=JsonSerializer.Serialize(baseline);
   var features=new object[]{Box("lot",20,20,72,56,new(){{"amenity","parking"},{"pz:direction","N"}}),
    Box("building",25,25,12,12,new(){{"building","warehouse"}}),Box("water",50,20,8,26,new(){{"natural","water"}}),
    Line("fence",new(){{"barrier","fence"}},(65,20),(65,76))};
   var p=Import(features,baseline,true);Check(JsonSerializer.Serialize(baseline)==before,"Import mutated the caller's project");
   Check(p.Buildings.Count==1&&p.Fences.Count==1&&p.GameZones.Any(z=>z.Vehicle),"Obstacle fixture must retain a building, a fence and usable stalls");
   var fenceCells=FenceLines.Compose(p.Fences,p.FenceStyles,p.GateStyles,p.Width,p.Height).Pieces.Select(c=>(c.X,c.Y)).ToHashSet();
   foreach(var z in p.GameZones.Where(z=>z.Vehicle))foreach(var (x,y) in Cells(z)){
    Check(p.Terrain[y*p.Width+x]!=Terrain.Water,"Water stall");
    Check(!p.Buildings.Any(b=>BuildingDesigner.OccupiesAbove(b,x-b.X,y-b.Y)),"Building stall");
    Check(!fenceCells.Contains((x,y))&&(x,y)!=(70,20),"Fence/prop stall");
   }
   ProjectValidator.Validate(p);
  });
  yield return ("parking import honors all directions and retains native distributions and names",()=>{
   foreach(var direction in new[]{"N","S","E","W"}){
    var p=Import([Box("lot",20,20,40,32,new(){{"amenity","parking"},{"pz:direction",direction},{"pz:vehicles","junkyard"},{"name","Depot"}})]);
    var zones=p.GameZones.Where(z=>z.Vehicle).ToList();Check(zones.Count>0,"No directed stalls");
    foreach(var z in zones){Check(z.Direction==direction&&z.VehicleDistribution=="junkyard"&&z.Name=="Depot","Direction/distribution/name lost");
     Check(z.Polygon is null&&(direction is "N" or "S"?z.Width%3==0&&z.Height==5:z.Width==5&&z.Height%3==0),"Rows must consist of whole native 3x5/5x3 stalls");
     var aisle=direction switch{"N"=>new ZoneRectangle(z.X,z.Y+5,z.Width,3),"S"=>new(z.X,z.Y-3,z.Width,3),"E"=>new(z.X-3,z.Y,3,z.Height),_=>new(z.X+5,z.Y,3,z.Height)};
     Check(aisle.X>=20&&aisle.Y>=20&&aisle.X+aisle.Width<=60&&aisle.Y+aisle.Height<=52,"Rear access aisle must stay in lot");
     foreach(var other in zones)Check(!Overlap(aisle,new(other.X,other.Y,other.Width,other.Height)),"Another row consumes the access aisle");
    }
    var lua=GameZonesExporter.ObjectsLua(p,default);Check(lua.Contains("name=\"junkyard\", type=\"ParkingStall\"")&&lua.Contains("Direction=\""+direction+"\""),"Native zone metadata lost");
   }
  });
  yield return ("parking import source rows and wheel stops are deterministic on reimport",()=>{
   var features=new object[]{Box("lot",20,20,60,48,new(){{"amenity","parking"}})};
   var first=Import(features);var again=Import(features,first,true);
   Check(first.GameZones.Count(z=>z.Vehicle)>1,"Reimport fixture needs several rows");
   Check(JsonSerializer.Serialize(first.GameZones)==JsonSerializer.Serialize(again.GameZones),"Reimport changed row identities or coverage");
   Check(first.Decorations.Count==again.Decorations.Count,"Reimport changed wheel stops");ProjectValidator.Validate(again);
  });
  yield return ("parking import reports unknown metadata and preserves explicit rectangular pz zones",()=>{
   var p=Import([Box("legacy",20,20,12,6,new(){{"pz:zone","Vehicle"},{"pz:direction","E"},{"pz:vehicles","junkyard"}}),
    Box("lot",50,20,24,24,new(){{"amenity","parking"},{"pz:direction","diagonal"},{"pz:vehicles","unknown-fleet"}}),
    Box("farm",100,20,12,12,new(){{"pz:zone","Farm"}}),Box("zombies",100,20,12,12,new(){{"pz:zone","ZombiesType"},{"pz:zombies","Farmer"}})]);
   var legacy=p.GameZones.Single(z=>z.VehicleDistribution=="junkyard");Check(legacy.X==21&&legacy.Y==21&&legacy.Width==10&&legacy.Height==3&&legacy.Direction=="E","Explicit rectangular pz contract changed");
   Check(p.GameZones.Any(z=>z.Type=="Farm"&&z.Polygon is not null)&&p.GameZones.Any(z=>z.Type=="ZombiesType"&&z.Name=="Farmer"),"Other zone flows changed");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("diagonal"))&&p.ImportSummary.Notes.Any(n=>n.Contains("unknown-fleet")),"Unknown direction/distribution silently ignored");
  });
  yield return ("parking import rejects tiny courtyard holes intersecting part of a tile",()=>{
   var p=Import([Polygon("lot",[Ring(20,20,30,24),Ring(24.1,22.1,.2,.2)],new(){{"amenity","parking"},{"pz:direction","N"}})]);
   Check(p.GameZones.Any(z=>z.Vehicle),"Tiny-hole fixture lost every stall");
   Check(!p.GameZones.Where(z=>z.Vehicle).SelectMany(Cells).Contains((24,22)),"Centre sampling missed the sub-tile courtyard hole");
  });
  yield return ("parking layout bounds oversized masks and aggregate work including exhausted budgets",()=>{
   var large=new MapProject{Width=1024,Height=1024}; // No terrain: an oversized mask must exit before occupancy allocation.
   var result=ParkingLayout.Build(large,Contours(Ring(0,0,1024,1024)),workBudget:0);
   Check(result.Limited&&result.Rows.Count==0&&result.Work==0,"Exhausted budget still charged work");
   result=ParkingLayout.Build(large,Contours(Ring(0,0,1024,1024)));
   Check(result.Limited&&result.Rows.Count==0&&result.Work<=ParkingLayout.MaximumWork&&result.Notes.Any(n=>n.Contains("mask")),"Oversized mask was allocated or replaced by a rectangle");
   result=ParkingLayout.Build(new(){Width=256,Height=256},Contours(Ring(20,20,60,48)),workBudget:100);
   Check(result.Limited&&result.Rows.Count==0&&result.Work<=100,"Geometry raster exceeded its work budget");
  });
  yield return ("parking layout chooses the orientation with more contained stalls using one bounded mask",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var rings=Contours(Ring(20,20,8,30));
   var result=ParkingLayout.Build(p,rings);Check(result.Direction=="E"&&result.AcceptedStalls==10,"The 8x30 lot must fit ten E-facing cars with a 3-tile aisle");
   var north=ParkingLayout.Build(p,rings,"N");Check(north.Direction=="N"&&north.AcceptedStalls==6,"Explicit direction must outrank greater capacity");
   Check(result.Work==north.Work,"Orientation selection unexpectedly rebuilt or recharged the polygon mask");
  });
  yield return ("parking layout caps native rows and rejects blocked access strips",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var rings=Contours(Ring(20,20,30,24));
   var limited=ParkingLayout.Build(p,rings,"N",maximumZones:1);
   Check(limited.Rows.Count==1&&limited.AcceptedStalls==10&&limited.Limited&&limited.RejectedStalls==20,"Zone capacity lost rejected-stall accounting");
   for(var y=25;y<28;y++)for(var x=20;x<50;x++)p.Terrain[y*p.Width+x]=Terrain.Water;
   var blocked=ParkingLayout.Build(p,rings,"N");Check(blocked.AcceptedStalls==20&&blocked.RejectedStalls==10&&blocked.Rows.All(r=>r.Stalls.Y>=28),"Stalls retained with a water-blocked rear aisle");
   Check(ParkingLayout.Build(p,rings,maximumZones:0).Rows.Count==0,"Zero remaining zone capacity must prevent packing");
  });
  yield return ("parking layout rejects a diagonal boundary cutting only the corner of a stall tile",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});
   var result=ParkingLayout.Build(p,Contours([(20,20),(50,20),(50,50),(20,20)]),"N");
   Check(result.AcceptedStalls>0,"Diagonal fixture lost every stall");
   foreach(var row in result.Rows)foreach(var r in new[]{row.Stalls,row.Aisle})for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)Check(y+1<=x,"Diagonal polygon cuts the interior of a retained tile");
  });
  yield return ("parking import replaces stale rows after source geometry refresh while retaining authored vehicle zones",()=>{
   var first=Import([Box("lot",20,20,60,48,new(){{"amenity","parking"},{"pz:direction","N"}})]);
   first.GameZones.Add(new(){Id=new string('a',32),Type="ParkingStall",X=120,Y=120,Width=3,Height=5,VehicleDistribution="police"});
   var again=Import([Box("lot",20,20,30,24,new(){{"amenity","parking"},{"pz:direction","N"}})],first,true);
   Check(again.GameZones.Any(z=>z.Id==new string('a',32)&&z.VehicleDistribution=="police"),"Authored vehicle zone removed");
   Check(again.GameZones.Where(z=>z.Id!=new string('a',32)).All(z=>z.X+z.Width<=50&&z.Y+z.Height<=44),"Old source rows survived the refresh");ProjectValidator.Validate(again);
  });
  yield return ("parking layout enforces the 4000 native zone ceiling even when callers ask for more",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});
   for(var y=0;y<512;y++)for(var x=3;x<510;x+=6)for(var dx=0;dx<3;dx++)p.Terrain[y*p.Width+x+dx]=Terrain.Water;
   var result=ParkingLayout.Build(p,Contours(Ring(0,0,510,512)),"N",maximumZones:int.MaxValue);
   Check(result.Rows.Count==4000&&result.AcceptedStalls==4000&&result.RejectedStalls>0&&result.Limited,"Fragmented lot exceeded 4000 native rows or lost rejection accounting");
   Check(result.Work<=ParkingLayout.MaximumWork,"Fragmented lot exceeded bounded work");
  });
  yield return ("parking import anticipates generated street props and preserves rows beside a house on refresh",()=>{
   var features=new object[]{Box("lot",20,20,60,48,new(){{"amenity","parking"},{"pz:direction","N"}}),
    Box("house",38.2,36.2,12,12,new(){{"building","house"}}),Line("road",new(){{"highway","residential"}},(10,15),(100,15))};
   var first=Import(features);Check(first.Buildings.Count==1&&first.GameZones.Any(z=>z.Vehicle),"House/parking fixture failed");
   var obstacles=first.Decorations.Where(d=>!d.Id.Contains("stop-block",StringComparison.Ordinal)).SelectMany(d=>d.Parts.Select(part=>(X:d.X+part.Dx,Y:d.Y+part.Dy))).ToHashSet();
   foreach(var z in first.GameZones.Where(z=>z.Vehicle))foreach(var c in Cells(z))Check(!obstacles.Contains(c),"Street prop overlaps parking");
   var again=Import(features,first,true);Check(JsonSerializer.Serialize(first.GameZones)==JsonSerializer.Serialize(again.GameZones),"Generated street props changed rows during refresh");
   ProjectValidator.Validate(again);
  });
  yield return ("parking import retires generated rows when the source changes zone kind or rectangle mode",()=>{
   var first=Import([Box("lot",20,20,60,48,new(){{"amenity","parking"},{"pz:direction","N"}})]);
   foreach(var replacement in new[]{new Dictionary<string,string>{{"pz:zone","Farm"}},new(){{"pz:zone","Vehicle"},{"pz:direction","N"}}}){
    var again=Import([Box("lot",20,20,30,24,replacement)],first,true);
    Check(again.GameZones.All(z=>!z.SourceTags.ContainsKey("pzmap:parking-source")),"Stale generated rows survived zone type change");
    Check(replacement["pz:zone"]=="Farm"?again.GameZones.Any(z=>z.Type=="Farm"):again.GameZones.Count(z=>z.Vehicle)==1,"Replacement zone rejected because stale rows remained");
   }
  });
  yield return ("parking import migrates the previous rectangle using old evidence not new bounds",()=>{
   var first=Import([Box("lot",20,20,60,48,new(){{"amenity","parking"},{"pz:zone","Vehicle"},{"pz:direction","N"}})]);
   foreach(var tags in first.GameZones.Select(z=>z.SourceTags).Concat(first.SourceFeatures.Select(f=>f.Tags))){tags.Remove("pz:zone");tags["amenity"]="parking";}
   var again=Import([Box("lot",20,20,30,24,new(){{"amenity","parking"},{"pz:direction","N"}})],first,true);
   Check(again.GameZones.Any(z=>z.SourceTags.ContainsKey("pzmap:parking-source")),"Old rectangle blocked every replacement row");
   Check(again.GameZones.All(z=>z.X+z.Width<=50&&z.Y+z.Height<=44),"Legacy rectangle extends outside refreshed source");
  });
  yield return ("parking import reserves previous source aisles for later lots and parcel generators",()=>{
   var first=Import([Box("north",20,20,30,24,new(){{"amenity","parking"},{"pz:direction","N"}})]);
   Check(!new ParcelPlacement(first).Free(24,26,allowRoad:true),"Shared placement lost previous parking aisle");
   var again=Import([Box("east",20,25,8,3,new(){{"amenity","parking"},{"pz:direction","E"}})],first,true);
   Check(again.GameZones.Count(z=>z.Vehicle)==first.GameZones.Count(z=>z.Vehicle),"New car row blocks preserved rear access aisle");
  });
  yield return ("parking import preserves moved and resized explicit rectangles on unchanged source refresh",()=>{
   var source=new[]{Box("lot",20,20,60,48,new(){{"pz:zone","Vehicle"},{"pz:direction","N"}})};
   foreach(var move in new[]{false,true}){var first=Import(source);var zone=first.GameZones.Single();if(move){zone.X+=3;zone.Y+=5;}else{zone.Width-=3;zone.Height-=5;}
    var before=JsonSerializer.Serialize(zone);var again=Import(source,first,true);
    Check(JsonSerializer.Serialize(again.GameZones.Single(z=>z.Id==zone.Id))==before,"Source refresh erased manual rectangle geometry");
   }
  });
  yield return ("parking import reports ambiguous legacy rectangles without discarding authored data",()=>{
   var first=Import([Box("lot",20,20,60,48,new(){{"pz:zone","Vehicle"},{"pz:direction","N"}})]);first.SourceFeatures.Clear();
   var old=JsonSerializer.Serialize(first.GameZones.Single());
   var again=Import([Box("lot",20,20,30,24,new(){{"amenity","parking"},{"pz:direction","N"}})],first,true);
   Check(again.GameZones.Any(z=>JsonSerializer.Serialize(z)==old),"Unidentifiable old layout was deleted speculatively");
   Check(again.ImportSummary!.Notes.Any(n=>n.Contains("without original source evidence",StringComparison.Ordinal)),"Ambiguous legacy migration was silent");
  });
 }
 static readonly WorldGeoReference Reference=new(){Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
 static readonly GeographicProjection Projection=new(Reference,100,100);
 static double[] At((double X,double Y) v){var g=Projection.Geographic(v.X,v.Y);return[g.Longitude,g.Latitude];}
 static (double X,double Y)[] Ring(double x,double y,double w,double h)=>[(x,y),(x+w,y),(x+w,y+h),(x,y+h),(x,y)];
 static IReadOnlyList<IReadOnlyList<(double X,double Y)>> Contours(params (double X,double Y)[][] rings)=>rings;
 static object Polygon(string id,(double X,double Y)[][] rings,Dictionary<string,string> tags)=>new{type="Feature",id,properties=tags,geometry=new{type="Polygon",coordinates=rings.Select(r=>r.Select(At).ToArray()).ToArray()}};
 static object Box(string id,double x,double y,double w,double h,Dictionary<string,string> tags)=>Polygon(id,[Ring(x,y,w,h)],tags);
 static object Line(string id,Dictionary<string,string> tags,params (double X,double Y)[] points)=>new{type="Feature",id,properties=tags,geometry=new{type="LineString",coordinates=points.Select(At).ToArray()}};
 static MapProject Import(object[] features,MapProject? baseline=null,bool preserve=false)=>GeographicImporter.Import(JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features}),baseline??MapGenerator.Generate(new(){Kind="blank"}),Reference.Bounds,preserveEdits:preserve,projection:Projection).Project;
 static IEnumerable<(int X,int Y)> Cells(GameZone z){for(var y=z.Y;y<z.Y+z.Height;y++)for(var x=z.X;x<z.X+z.Width;x++)yield return(x,y);}
 static bool Overlap(ZoneRectangle a,ZoneRectangle b)=>a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
