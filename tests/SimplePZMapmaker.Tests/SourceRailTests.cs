using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class SourceRailTests{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("source rail platforms remain evidence without fictitious tracks",()=>{
   var p=Import("platform",false);if(p.LinearFeatures.Any(f=>f.Kind=="railway")||p.Decorations.Any(d=>d.Asset.StartsWith("industry_railroad")))throw new Exception("Platform was converted to tracks");
   if(!p.SourceFeatures.Any(f=>f.Tags.GetValueOrDefault("railway")=="platform"))throw new Exception("Platform evidence missing");
  });
  yield return("source rail diagonal retains ballast and original geometry when native artwork is unsupported",()=>{
   var p=Import("rail",false,diagonal:true);var evidence=p.SourceFeatures.Single(f=>f.SourceId=="geo-rail");
   if(evidence.SourceGeometry[0].Count!=2||evidence.Tags.GetValueOrDefault("gauge")!="1520")throw new Exception("Railway geometry/gauge lost");
   if(!p.Terrain.Any(t=>t==Terrain.Dirt))throw new Exception("Entire diagonal railway disappeared");
   if(!p.ImportSummary!.Notes.Any(n=>n.Contains("ballast",StringComparison.OrdinalIgnoreCase)))throw new Exception("Unsupported artwork not distinguished from retained rail bed");ProjectValidator.Validate(p);
  });
  yield return("source rail bridge retains native walkable deck over original water",()=>{
   var p=Import("rail",true);var bridge=p.LinearFeatures.Single(f=>f.SourceId=="geo-rail");if(bridge.Kind!="bridge"||bridge.SourceTags["railway"]!="rail")throw new Exception("Rail bridge imported as ground-only track");
   if(p.Terrain[60*256+60]!=Terrain.Water||!NativeScene.Cell(p,55,55,10,10,0).Sprites.Any(s=>s.X==60&&s.Y==60&&s.Tile==InfrastructureGeometry.DeckTile))throw new Exception("Rail bridge deck missing or source water replaced");ProjectValidator.Validate(p);
  });
 }
 static MapProject Import(string railway,bool bridge,bool diagonal=false){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=1};var projection=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var p=projection.Geographic(x,y);return[p.Longitude,p.Latitude];}
  var tags=new Dictionary<string,string>{{"railway",railway},{"gauge","1520"}};if(bridge)tags["bridge"]="yes";
  var features=new List<object>{new{type="Feature",id="rail",properties=tags,geometry=new{type="LineString",coordinates=diagonal?new[]{At(30,40),At(90,80)}:new[]{At(30,60),At(90,60)}}}};
  if(bridge)features.Add(new{type="Feature",id="water",properties=new{natural="water"},geometry=new{type="Polygon",coordinates=new[]{new[]{At(50,30),At(70,30),At(70,90),At(50,90),At(50,30)}}}});
  return GeographicImporter.Import(JsonSerializer.Serialize(new{type="FeatureCollection",features}),MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
 }
}
