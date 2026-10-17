using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
public static class B42MapDataTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("biome map gives every native cell foraging zones like the vanilla maps",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});
   void Fill(int x0,int y0,int w,int h,Terrain t){for(var y=y0;y<y0+h;y++)for(var x=x0;x<x0+w;x++)p.Terrain[y*p.Width+x]=t;}
   Fill(0,0,96,96,Terrain.Forest);Fill(160,160,64,64,Terrain.Water);Fill(100,200,150,4,Terrain.Road);
   var b=new Building{X=150,Y=40,Width=12,Height=10};b.Design=BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.Spawns=[new(){X=130,Y=130}];ProjectValidator.Validate(p);
   var image=Png.Decode(BiomeMap.Png(p,0,0));(byte R,byte G,byte B) At(int x,int y){var i=(y*256+x)*4;return(image.Rgba[i],image.Rgba[i+1],image.Rgba[i+2]);}
   Check(At(40,40)==(255,255,255),"Forest interior should be DeepForest");Check(At(92,40).G==BiomeMap.Forest,"Forest edge should be BirchMixForest");
   Check(At(190,190)==(0,0,0),"Water should be 0");Check(At(155,45)==(254,115,254),"A building square keeps TownZone foraging but takes the featureless dirt biome");Check(At(170,60)==(115,115,115),"Town ground away from buildings keeps the town biome");Check(At(120,201).R==BiomeMap.NoFeatures&&At(120,206).R!=BiomeMap.NoFeatures,"Roads and their margin are protected from WorldGen trees");
   Check(At(240,120).G==BiomeMap.FarmLand,"Open ground away from town is FarmLand");Check(At(240,201).G==BiomeMap.Nav,"A road chunk away from town is ForagingNav");
   for(var y=0;y<256;y+=8)for(var x=0;x<256;x+=8){var g=At(x,y).G;for(var dy=0;dy<8;dy++)for(var dx=0;dx<8;dx++)Check(At(x+dx,y+dy).G==g,"A chunk must have one foraging zone");}
   p.GameZones.Add(new(){Id=new string('a',32),Type="Farm",X=200,Y=8,Width=40,Height=24});Check(Png.Decode(BiomeMap.Png(p,0,0)).Rgba[(12*256+210)*4+1]==BiomeMap.Farm,"An authored Farm zone wins");
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));Check(zip.Entries.Count(e=>e.FullName.Contains("/maps/biomemap_")&&e.Name.EndsWith(".png"))==1,"Each native cell needs its biome map");
   using(var stream=new MemoryStream()){using(var thumb=zip.Entries.Single(e=>e.Name=="thumb.png").Open())thumb.CopyTo(stream);var t=Png.Decode(stream.ToArray());Check(t.Width==256&&t.Height==256,"thumb.png must be 256x256");
    (byte,byte,byte) Px(int x,int y){var i=(y*256+x)*4;return(t.Rgba[i],t.Rgba[i+1],t.Rgba[i+2]);}Check(Px(155,45)==(150,62,52)&&Px(190,190)==(52,96,148)&&Px(40,40)==(46,84,44),"thumb.png should show roofs, water and forest from above");}
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class RoomCatalogTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("room catalog holds the Build 42 room names and every tag mapping uses one",()=>{
   Check(BuildingDesigner.AllRoomNames.Count>500&&BuildingDesigner.AllRoomNames.IsSupersetOf(BuildingDesigner.RoomNames),"Room catalog is incomplete");
   var missing=GeographicImporter.MappedRoomNames.Where(n=>!BuildingDesigner.AllRoomNames.Contains(n)).ToList();Check(missing.Count==0,"Mapped rooms missing from the catalog: "+string.Join(",",missing));
  });
  yield return("source tags give imported buildings their vanilla rooms and the lot keeps them",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}
   object Box(string id,double x,double y,double w,double h,Dictionary<string,string> t)=>new{type="Feature",id,properties=t,geometry=new{type="Polygon",coordinates=new[]{new[]{At(x,y),At(x+w,y),At(x+w,y+h),At(x,y+h),At(x,y)}}}};
   var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features=new[]{
    Box("osm-way-1",40.2,40.2,14,12,new(){{"building","retail"},{"shop","bakery"}}),Box("osm-way-2",70.2,40.2,12,10,new(){{"building","barn"}}),
    Box("osm-way-3",100.2,40.2,14,12,new(){{"building","yes"},{"amenity","cafe"}}),Box("osm-way-4",130.2,40.2,12,10,new(){{"building","yes"},{"pz:room","church"}}),
    Box("osm-way-5",160.2,40.2,12,10,new(){{"building","yes"},{"pz:room","no-such-room"}})}});
   var p=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   HashSet<string> Rooms(string id)=>p.Buildings.Single(b=>b.SourceId=="geo-"+id).Design!.Floors.SelectMany(f=>f.Rooms.Values).ToHashSet();
   Check(Rooms("osm-way-1").Contains("bakery")&&!Rooms("osm-way-1").Contains("conveniencestore"),"shop=bakery should sell from a bakery room");
   Check(Rooms("osm-way-2").All(r=>r is "barn" or "hall"),"building=barn should be barn rooms");
   Check(Rooms("osm-way-3").Contains("cafe"),"amenity=cafe should have a cafe room");Check(Rooms("osm-way-4").Contains("church"),"pz:room should name the main room");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("no-such-room")),"An unknown pz:room must be reported");
   ProjectValidator.Validate(p);using var zip=new System.IO.Compression.ZipArchive(new MemoryStream(SimplePZMapmaker.Core.Export.NativeMapExporter.Export(p)));using var r=new StreamReader(zip.Entries.Single(e=>e.Name.EndsWith(".lotheader")).Open(),System.Text.Encoding.Latin1);var header=r.ReadToEnd();
   Check(header.Contains("bakery")&&header.Contains("barn")&&header.Contains("church"),"Vanilla room names did not reach the lot header");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class ZoneImportTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("land use parking and pz:zone tags become game zones with Build 42 vehicle rules",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}
   object Box(string id,double x,double y,double w,double h,Dictionary<string,string> t)=>new{type="Feature",id,properties=t,geometry=new{type="Polygon",coordinates=new[]{new[]{At(x,y),At(x+w,y),At(x+w,y+h),At(x,y+h),At(x,y)}}}};
   var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features=new object[]{
    Box("osm-way-1",20,20,60,40,new(){{"landuse","residential"}}),Box("osm-way-2",90,20,40,30,new(){{"landuse","farmland"}}),
    Box("osm-way-3",140,140,100,100,new(){{"natural","wood"}}),Box("osm-way-4",20,80,20,9,new(){{"amenity","parking"}}),
    Box("osm-way-5",50,80,12,6,new(){{"pz:zone","Vehicle"},{"pz:vehicles","junkyard"},{"pz:direction","E"}}),Box("osm-way-6",25,25,20,20,new(){{"pz:zone","ZombiesType"},{"pz:zombies","Farmer"}}),
    Box("osm-way-7",70,80,10,6,new(){{"amenity","parking"},{"pz:vehicles","no-such-cars"}}),
    new{type="Feature",id="osm-way-8",properties=new Dictionary<string,string>{{"highway","residential"}},geometry=new{type="LineString",coordinates=new[]{At(5,2),At(5,250)}}}}});
   var p=MapGenerator.Generate(new(){Kind="blank"});for(var y=0;y<256;y++)p.Terrain[y*256+2]=Terrain.Road;
   p=GeographicImporter.Import(json,p,reference.Bounds,projection:projection).Project;
   Check(p.GameZones.Any(z=>z.Type=="TownZone"&&z.Polygon is not null)&&p.GameZones.Any(z=>z.Type=="Farm"),"Residential or farmland zone missing");
   Check(p.GameZones.Any(z=>z.Type=="DeepForest"),"A wood far from roads should be DeepForest");
   var stalls=p.GameZones.Where(z=>z.Type=="ParkingStall").ToList();Check(stalls.Count==3&&stalls.All(z=>z.Polygon is null&&(z.Width%5==0&&z.Height%3==0||z.Width%3==0&&z.Height%5==0)),"Parking zones must measure whole 5x3 stalls");
   Check(stalls.Any(z=>z.VehicleDistribution=="junkyard"&&z.Direction=="E")&&stalls.Count(z=>z.VehicleDistribution=="parkingstall")==2,"Vehicle distributions were not taken from the tags");
   Check(p.GameZones.Any(z=>z.Type=="ZombiesType"&&z.Name=="Farmer"),"ZombiesType zone missing; it may overlap the town zone");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("no-such-cars")),"An unknown vehicle distribution must be reported");
   ProjectValidator.Validate(p);var lua=SimplePZMapmaker.Core.Export.GameZonesExporter.ObjectsLua(p,default);
   Check(lua.Contains("type=\"ZombiesType\"")&&lua.Contains("name=\"Farmer\"")&&lua.Contains("name=\"junkyard\", type=\"ParkingStall\""),"objects.lua lacks the imported zones");
   var again=GeographicImporter.Import(json,p,p.Bounds,preserveEdits:true,projection:projection).Project;Check(again.GameZones.Count==p.GameZones.Count,"Reimport duplicated source zones");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class ReferenceFenceTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("reference fences alternate west1/west2, frame their wickets and keep corners on alternate pieces",()=>{
   var planks=GeographicImporter.DefaultFenceStyles.Single(s=>s.Id=="vanilla-planks");var wicket=GeographicImporter.DefaultGateStyles.Single(g=>g.Id==GeographicImporter.WicketFor("vanilla-planks"));
   Check(GeographicImporter.DefaultFenceStyles.Length==14&&GeographicImporter.DefaultGateStyles.All(g=>g.OpenW.Count==1),"TileZed reference sets are incomplete");
   var line=new FenceLine{Id="a",Style=planks.Id,Points=[new(10,10),new(14,10)],Gates=[new(){At=1,Style=wicket.Id}]};var corner=new FenceLine{Id="b",Style=planks.Id,Points=[new(20,25),new(20,20),new(25,20)]};
   FenceLines.Validate([line,corner],[planks],[wicket],258048);var pieces=FenceLines.Compose([line,corner],[planks],[wicket],256,256).Pieces;
   string[] At(int x,int y)=>pieces.Where(p=>p.X==x&&p.Y==y).Select(p=>p.Tile).ToArray();
   Check(At(10,10).SequenceEqual(["fencing_01_8"])&&At(12,10).SequenceEqual(["fencing_01_8"])&&At(13,10).SequenceEqual(["fencing_01_9"]),"north1/north2 do not alternate");
   Check(At(11,10).SequenceEqual(["fencing_01_47","fixtures_doors_fences_01_13"]),"The wicket needs its gate frame under it");
   Check(At(20,20).SequenceEqual(["fencing_01_12"]),"An alternate W and N piece should still meet in the NW corner");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class WallCatalogTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("wall catalog draws NW corner pieces, SE pillars and the real door tiles of irregular sets",()=>{
   Check(WallCatalog.Sets.Count>150&&WallCatalog.Find("walls_exterior_house_01_0")?.NorthWest=="walls_exterior_house_01_2","TileZed exterior wall catalog is missing");
   Check(WallCatalog.Tile("location_restaurant_diner_01",0,"door",true)=="location_restaurant_diner_01_5"&&WallCatalog.Tile("walls_exterior_house_01",16,"window",false)=="walls_exterior_house_01_24","Catalog tiles were not used before the +8/+10 rule");
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=100,Y=100,Width=10,Height=8};b.Design=BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);ProjectValidator.Validate(p);
   string[] At(int x,int y)=>NativeScene.Cell(p,x,y,1,1,0).Sprites.Select(s=>s.Tile).ToArray();
   var nw=b.Design.Floors[0].Edges.Single(e=>e.X==0&&e.Y==0&&e.Edge=="n");var next=b.Design.Floors[0].Edges.Single(e=>e.X==1&&e.Y==0&&e.Edge=="n");(nw.Kind,next.Kind,nw.OpeningTile,next.OpeningTile)=(next.Kind,nw.Kind,next.OpeningTile,nw.OpeningTile);
   Check(At(100,100).SequenceEqual(["floors_interior_tilesandwood_01_0","walls_exterior_house_01_34"])||At(100,100).Count(t=>WallCatalog.Sets.Any(w=>w.NorthWest==t))==1&&!At(100,100).Any(t=>WallCatalog.Find(t) is {} w&&(t==w.North||t==w.West)),"A square with plain N and W walls should hold one corner piece instead: "+string.Join(",",At(100,100)));
   Check(At(110,108).Any(t=>WallCatalog.Sets.Any(s=>s.SouthEast==t)),"The outer SE corner needs its pillar: "+string.Join(",",At(110,108)));
   var problems=new List<string>();GeographicImporter.ApplyAppearanceTags(b,new Dictionary<string,string>{{"pz:wall","location_restaurant_diner_01_0"}},null,problems.Add);
   var doors=b.Design!.Floors[0].Edges.Where(e=>e.Kind=="door"&&e.WallTile is not null).ToList();Check(problems.Count==0&&doors.Count>0&&doors.All(e=>e.WallTile==(e.Edge=="n"?"location_restaurant_diner_01_5":"location_restaurant_diner_01_4")),"pz:wall should take the diner's own door walls");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class SpawnPlannerTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("players spawn inside houses with a key for every profession and workplaces for some",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var home=new Building{X=40,Y=40,Width=12,Height=10};home.Design=BuildingDesigner.Generate(home,1,7);
   if(!home.Design.Floors[0].Rooms.ContainsValue("bedroom"))home.Design.Floors[0].Rooms[home.Design.Floors[0].Rooms.Keys.First(k=>k!="0")]="bedroom";
   var police=new Building{X=120,Y=40,Width=12,Height=10};police.Design=BuildingDesigner.Generate(police,1,8);foreach(var k in police.Design.Floors[0].Rooms.Keys.Where(k=>k!="0").ToList())police.Design.Floors[0].Rooms[k]="policeoffice";
   p.Buildings.AddRange([home,police]);ProjectValidator.Validate(p);
   var lua=SpawnPlanner.Lua(p);var plan=new SpawnPlanner.Plan();plan.Add(p.Buildings,0,0);string At(MapProject m)=>$"posX = {m.WorldX*256+m.Spawns[0].X}, posY = {m.WorldY*256+m.Spawns[0].Y}";
   Check(SpawnPlanner.Professions.Length==25&&SpawnPlanner.Professions.All(n=>lua.Contains("        "+n+" = ")),"Every vanilla profession needs a key");
   var point=plan.Homes.Single();var floor=home.Design.Floors[0];var room=floor.Rooms[floor.Cells[(point.Y-40)*12+point.X-40].ToString()];
   Check(room is "livingroom" or "kitchen" or "bedroom"&&!lua.Contains(At(p)),"The spawn should be inside the house, not at the authored outdoor point: "+room);
   Check(plan.Work["policeofficer"].Single().X>=120&&lua.Contains("policeofficer = policeofficer_work")&&lua.Contains("unemployed = houses"),"Police officers should also start at the police station");
   var empty=MapGenerator.Generate(new(){Kind="blank"});Check(SpawnPlanner.Lua(empty).Contains(At(empty)),"Without houses the authored spawn is used");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class FurnitureTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("generated houses take TileZed furniture against their walls and keep it through rotation",()=>{
   var b=new Building{X=40,Y=40,Width=14,Height=12,Kind="house"};b.Design=BuildingDesigner.Generate(b,1,42);var f=b.Design.Floors[-b.Design.MinLevel];
   var furniture=f.Objects.Where(o=>o.Kind=="furniture").ToList();Check(furniture.Count>=3,"A house should get several pieces of furniture");
   foreach(var o in furniture){var squares=BuildingObjects.Cells(o).ToHashSet();var code=BuildingDesigner.Cell(b,f,o.X,o.Y);var (bx,by)=o.Direction switch{"n"=>(0,-1),"s"=>(0,1),"w"=>(-1,0),_=>(1,0)};
    Check(squares.All(s=>BuildingDesigner.Cell(b,f,s.X,s.Y)==code)&&squares.Any(s=>!squares.Contains((s.X+bx,s.Y+by))&&BuildingDesigner.Cell(b,f,s.X+bx,s.Y+by)!=code),$"{o.Asset} {o.Direction} must stand in one room with its back to a wall");}
   var rooms=furniture.Select(o=>f.Rooms[BuildingDesigner.Cell(b,f,o.X,o.Y).ToString()]).ToHashSet();Check(furniture.Any(o=>o.Asset!.StartsWith("bed:"))&&rooms.Contains("bedroom"),"The bedroom needs a bed");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);ProjectValidator.Validate(p);Check(p.Version==14,"Furniture needs map format 14");
   Check(NativeScene.Building(b,0).Sprites.Any(s=>s.Tile.StartsWith("furniture_bedding_01_")),"Beds must reach the composed scene");
   Check(FurnitureCatalog.Rotate("n",false,1)=="e"&&FurnitureCatalog.Rotate("w",true,0)=="e"&&FurnitureCatalog.Transform("n",true,false,false)=="w","Orientation transforms are wrong");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
public static class TemplateStyleTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("pz:style=tilezed:Diner dresses walls, doors, windows, interior walls and floors from the TileZed template",()=>{
   Check(BuildingTemplateStyles.All.Count==25&&BuildingTemplateStyles.Find("tilezed:police") is not null,"TileZed templates are missing");
   var b=new Building{X=40,Y=40,Width=14,Height=12,Kind="shop"};b.Design=BuildingDesigner.Generate(b,1,42);var f=b.Design.Floors[-b.Design.MinLevel];f.Rooms[f.Rooms.Keys.First(k=>k!="0")]="dinerkitchen";
   var problems=new List<string>();GeographicImporter.ApplyAppearanceTags(b,new Dictionary<string,string>{{"pz:style","tilezed:Diner"}},null,problems.Add);
   bool Exterior(BuildingEdge e){var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var o=e.Edge=="n"?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y);return(a=='.')!=(o=='.');}
   Check(problems.Count==0&&f.Edges.Where(e=>e.Kind!="open"&&Exterior(e)).All(e=>e.WallTile!.StartsWith("location_restaurant_diner_01_")),"Exterior walls should be the diner set");
   Check(f.Edges.Where(e=>e.Kind=="door"&&Exterior(e)).All(e=>e.WallTile==(e.Edge=="n"?"location_restaurant_diner_01_5":"location_restaurant_diner_01_4")&&e.OpeningTile==(e.Edge=="n"?"fixtures_doors_01_49":"fixtures_doors_01_48")),"Diner doors use the template door and door wall");
   Check(f.FloorTiles.Count>0&&f.FloorTiles.Values.All(t=>t=="location_restaurant_diner_01_40"),"Rooms take the template floor");
   BuildingDesigner.Validate(b);
   GeographicImporter.ApplyAppearanceTags(b,new Dictionary<string,string>{{"pz:style","tilezed:NoSuch"}},null,problems.Add);Check(problems.Count==1,"An unknown template is reported");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
