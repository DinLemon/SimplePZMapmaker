using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class SourceRoadPlacementTests
{
 public static IEnumerable<(string,Action)> Cases(){
  foreach(var highway in new[]{"footway","residential"})yield return ($"source footprint survives generated {highway} road raster",()=>{
   var (reference,projection,json)=Fixture(highway);var p=GeographicImporter.Import(json,Blank(),reference.Bounds,projection:projection).Project;
   var b=p.Buildings.SingleOrDefault()??throw new Exception("Source house was dropped for generated road pixels");
   Check(b.SourceId=="geo-house"&&b.SourceOutline.Count==1,"Source identity/geometry lost");
   Check(p.LinearFeatures.Single().SourcePath.Count==2,"Source road geometry was rewritten");
   Check(BuildingDesigner.Occupied(b,false).Any(i=>p.Terrain[(b.Y+i/(b.Width+1))*p.Width+b.X+i%(b.Width+1)]==Terrain.Road),"Fixture does not overlap generated road pixels");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("road raster",StringComparison.OrdinalIgnoreCase)),"Raster conflict not reported");
   Check(!MapGenerator.CanPlace(p,b),"Ordinary generated building placement now ignores roads");
   ProjectValidator.Validate(p);Check(NativeScene.Building(b,0).Sprites.Any(s=>s.Tile.StartsWith("floors_")),"Source house lost native floor");
  });
  yield return ("source footprint still rejects water without altering source terrain",()=>{
   var (reference,projection,json)=Fixture("footway",water:true);var p=GeographicImporter.Import(json,Blank(),reference.Bounds,projection:projection).Project;
   Check(p.Buildings.Count==0&&p.SourceFeatures.Any(f=>f.SourceId=="geo-house"),"Wet source was placed or evidence lost");ProjectValidator.Validate(p);
  });
  yield return ("source footprint preserves authored roads during refresh",()=>{
   var (reference,projection,json)=Fixture("footway");var before=Blank();before.Bounds=reference.Bounds;before.Terrain[49*256+54]=Terrain.Road;TerrainEditMask.Mark(before,49*256+54);
   var p=GeographicImporter.Import(json,before,reference.Bounds,preserveEdits:true,projection:projection).Project;
   Check(p.Buildings.Count==0&&TerrainEditMask.IsEdited(TerrainEditMask.Decode(p),49*256+54),"Authored road overwritten");Check(before.Buildings.Count==0,"Original map mutated");
  });
  yield return ("source footprint keeps bridge deck exclusion instead of aborting the import",()=>{
   var (reference,projection,json)=Fixture("footway",bridge:true);var p=GeographicImporter.Import(json,Blank(),reference.Bounds,projection:projection).Project;
   Check(p.Buildings.Count==0&&p.LinearFeatures.Single().Kind=="bridge","Building replaced native deck");ProjectValidator.Validate(p);
  });
 }
 static MapProject Blank()=>MapGenerator.Generate(new(){Kind="blank"});
 static (WorldGeoReference,GeographicProjection,string) Fixture(string highway,bool water=false,bool bridge=false){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var p=projection.Geographic(x,y);return[p.Longitude,p.Latitude];}
  var tags=new Dictionary<string,string>{{"highway",highway},{"surface","asphalt"}};if(bridge)tags["bridge"]="yes";
  var features=new List<object>{new{type="Feature",id="path",properties=tags,geometry=new{type="LineString",coordinates=new[]{At(30,50),At(90,50)}}},new{type="Feature",id="house",properties=new{building="house"},geometry=new{type="Polygon",coordinates=new[]{new[]{At(50,45),At(62,45),At(62,57),At(50,57),At(50,45)}}}}};
  if(water)features.Add(new{type="Feature",id="water",properties=new{natural="water"},geometry=new{type="Polygon",coordinates=new[]{new[]{At(40,40),At(70,40),At(70,65),At(40,65),At(40,40)}}}});
  return(reference,projection,JsonSerializer.Serialize(new{type="FeatureCollection",features}));
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
