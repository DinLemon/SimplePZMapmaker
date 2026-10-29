using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class OrthogonalImportTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("OSM bridges retain native decks after the editable-road budget is filled",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var p=projection.Geographic(x,y);return[p.Longitude,p.Latitude];}
   var features=new List<object>();
   for(var i=0;i<70;i++)features.Add(new{type="Feature",id="road-"+i,properties=new Dictionary<string,string>{{"highway","residential"}},geometry=new{type="LineString",coordinates=new[]{At(20,20+i*3),At(40,20+i*3)}}});
   features.Add(new{type="Feature",id="water",properties=new Dictionary<string,string>{{"natural","water"}},geometry=new{type="Polygon",coordinates=new[]{new[]{At(95,60),At(115,60),At(115,160),At(95,160),At(95,60)}}}});
   features.Add(new{type="Feature",id="bridge",properties=new Dictionary<string,string>{{"highway","residential"},{"bridge","yes"},{"layer","1"}},geometry=new{type="LineString",coordinates=new[]{At(80,100),At(140,100)}}});
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features});
   var imported=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   var bridge=imported.LinearFeatures.Single(f=>f.SourceId=="geo-bridge");
   if(bridge.Kind!="bridge"||bridge.Level!=0||bridge.SourceTags["layer"]!="1"||imported.Terrain[100*256+100]!=Terrain.Water)throw new Exception("Bridge lost its deck semantics or source layer/water");
   if(!NativeScene.Cell(imported,96,96,12,12,0).Sprites.Any(s=>s.X==100&&s.Y==100&&s.Tile==InfrastructureGeometry.DeckTile))throw new Exception("Bridge has no walkable native deck");
  });
  foreach(var courtyard in new[]{false,true})yield return($"rotated source buildings become straight orthogonal walls (courtyard={courtyard})",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
   var projection=new GeographicProjection(reference,100,100);var angle=Math.PI/6;
   double[] At(double x,double y){var p=projection.Geographic(100+x*Math.Cos(angle)-y*Math.Sin(angle),100+x*Math.Sin(angle)+y*Math.Cos(angle));return[p.Longitude,p.Latitude];}
   var outer=courtyard?new[]{At(-10,-10),At(10,-10),At(10,10),At(-10,10),At(-10,-10)}:new[]{At(-10,-6),At(10,-6),At(10,0),At(2,0),At(2,6),At(-10,6),At(-10,-6)};
   var rings=courtyard?new[]{outer,new[]{At(-4,-4),At(4,-4),At(4,4),At(-4,4),At(-4,-4)}}:new[]{outer};
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="tilted",properties=new{building="warehouse"},geometry=new{type="Polygon",coordinates=rings}}}});
   var imported=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   var b=imported.Buildings.Single();var f=b.Design!.Floors[0];var corners=0;
   bool Filled(int x,int y)=>BuildingDesigner.Cell(b,f,x,y)!='.';
   for(var y=0;y<=b.Height;y++)for(var x=0;x<=b.Width;x++){var count=new[]{Filled(x-1,y-1),Filled(x,y-1),Filled(x-1,y),Filled(x,y)}.Count(v=>v);if(count is 1 or 3)corners++;}
   if(corners!=(courtyard?8:6))throw new Exception($"Straight contour grew {corners} staircase corners");
   if(courtyard&&Filled(100-b.X,100-b.Y))throw new Exception("Courtyard was filled");
   if(b.SourceOutline[0][0].Longitude!=outer[0][0]||imported.SourceFeatures.Single().SourceGeometry[0][0].Longitude!=outer[0][0])throw new Exception("Original rotated source was overwritten");
   ProjectValidator.Validate(imported);if(!NativeScene.Building(b,0).Sprites.Any(s=>s.Tile.StartsWith("walls_")))throw new Exception("Straightened building lost native walls");
  });
 }
}
