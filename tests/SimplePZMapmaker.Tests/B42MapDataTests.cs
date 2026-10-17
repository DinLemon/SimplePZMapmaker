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
   Check(At(190,190)==(0,0,0),"Water should be 0");Check(At(155,45)==(115,115,115),"A building chunk is TownZone");
   Check(At(240,120).G==BiomeMap.FarmLand,"Open ground away from town is FarmLand");Check(At(240,201).G==BiomeMap.Nav,"A road chunk away from town is ForagingNav");
   for(var y=0;y<256;y+=8)for(var x=0;x<256;x+=8){var g=At(x,y).G;for(var dy=0;dy<8;dy++)for(var dx=0;dx<8;dx++)Check(At(x+dx,y+dy).G==g,"A chunk must have one foraging zone");}
   p.GameZones.Add(new(){Id=new string('a',32),Type="Farm",X=200,Y=8,Width=40,Height=24});Check(Png.Decode(BiomeMap.Png(p,0,0)).Rgba[(12*256+210)*4+1]==BiomeMap.Farm,"An authored Farm zone wins");
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));Check(zip.Entries.Count(e=>e.FullName.Contains("/maps/biomemap_")&&e.Name.EndsWith(".png"))==1,"Each native cell needs its biome map");
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
