using System.Globalization;
using System.Text.Json;
using SimplePZMapmaker.Core;

public static class RoadWidthImportTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  foreach(var (value,expected) in new[]{("7.5 m",7.5),("7,5",7.5),("20 ft",6.096),("24'",7.3152),("12'6\"",3.81),("0' 6\"",.1524)})
   yield return ($"road width import respects explicit units {value}",()=>CheckWidth(new(){["width"]=value,["lanes"]="4"},expected,"width"));
  yield return ("road width import uses estimate before lanes and keeps its uncertainty",()=>CheckWidth(new(){["est_width"]="8 m",["lanes"]="4"},8,"est_width"));
  yield return ("road width import falls through malformed explicit values to lane evidence",()=>{
   foreach(var value in new[]{"NaN","Infinity","-4","0","7 bananas"," ","3;9"})CheckWidth(new(){["width"]=value,["lanes"]="3"},10.6,"lanes");
  });
  yield return ("road width import combines directional counts without double counting total lanes",()=>{
   CheckWidth(new(){["lanes:forward"]="2",["lanes:backward"]="1",["lanes:both_ways"]="1"},13.8,"directional-lanes");
   CheckWidth(new(){["lanes"]="2",["lanes:forward"]="2",["lanes:backward"]="2"},7.4,"lanes");
  });
  yield return ("road width import has narrow service and one way fallbacks",()=>{
   CheckWidth(new(){["highway"]="service"},3.5,"highway");
   CheckWidth(new(){["highway"]="residential",["oneway"]="-1"},4.2,"oneway");
   CheckWidth(new(){["highway"]="motorway",["oneway"]="yes"},24,"highway");
  });
  yield return ("road width import applies metre scale once and preserves source evidence",()=>{
   var p=Import(new(){["width"]="9 m",["lanes"]="2",["sidewalk"]="both",["oneway"]="no"},2);
   var line=p.LinearFeatures.Single();Check(Math.Abs(line.Width-4.5)<1e-8,"Metre scale was ignored or applied twice");
   var captured=GeographicEvidence.CaptureTags(line.SourceTags);
   Check(new[]{"width","lanes","sidewalk","oneway"}.All(captured.ContainsKey),"Road evidence discarded by provider tag filtering");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("Road widths:",StringComparison.Ordinal)),"Width estimates have no report");
  });
  yield return ("road width import honors explicit two way roundabouts over implied direction",()=>{
   CheckWidth(new(){["junction"]="roundabout",["oneway"]="no",["lanes:forward"]="1"},6,"highway");
   CheckWidth(new(){["junction"]="roundabout",["lanes:forward"]="1"},4.2,"directional-lanes");
  });
  yield return ("road width import retains simplified richly tagged bridge within the tag budget",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var at=projection.Geographic(x,y);return[at.Longitude,at.Latitude];}
   var tags=Enumerable.Range(0,64).ToDictionary(i=>"extra-"+i,i=>i.ToString());tags["highway"]="residential";tags["bridge"]="yes";tags["width"]="6";
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="bridge",properties=tags,geometry=new{type="LineString",coordinates=Enumerable.Range(0,513).Select(i=>At(20+i/4.0,70)).ToArray()}}}});
   var p=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   var line=p.LinearFeatures.Single();Check(line.Kind=="bridge"&&line.Points.Count==512&&line.SourceTags.Count<=48,"Tag budget silently dropped a bridge");
   Check(line.SourceTags.ContainsKey("pzmap:geometry")&&line.SourceTags.ContainsKey("pzmap:width-source"),"Simplification/width provenance lost");
  });
  yield return ("road width import places signs using preserved authored road widths",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var at=projection.Geographic(x,y);return[at.Longitude,at.Latitude];}
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{
    new{type="Feature",id="major",properties=new{highway="primary"},geometry=new{type="LineString",coordinates=new[]{At(40,100),At(160,100)}}},
    new{type="Feature",id="minor",properties=new{highway="residential"},geometry=new{type="LineString",coordinates=new[]{At(100,100),At(100,180)}}}}});
   var first=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   foreach(var line in first.LinearFeatures){line.Modified=true;line.Width=line.SourceTags["highway"]=="primary"?30:20;}
   var again=GeographicImporter.Import(json,first,reference.Bounds,preserveEdits:true,projection:projection).Project;
   var sign=again.Decorations.Single(d=>d.Id.StartsWith("street-stop-",StringComparison.Ordinal));
   Check(sign.X==111&&sign.Y==117,$"Sign uses stale source dimensions at {sign.X},{sign.Y}");
  });
  yield return ("road width import bounds extreme lane counts and leaves invalid values on evidence",()=>{
   CheckWidth(new(){["lanes"]="99999999999999999999"},6,"highway");
   CheckWidth(new(){["lanes"]="99"},26.6,"lanes");
   CheckWidth(new(){["width"]="1000",["est_width"]="6"},6,"est_width");
  });
  yield return ("road width import raster-only overflow uses the same circular stroke as editable roads",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var at=projection.Geographic(x,y);return[at.Longitude,at.Latitude];}
   foreach(var diagonal in new[]{false,true}){
    var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=Enumerable.Range(0,65).Select(i=>new{type="Feature",id="road"+i,properties=new Dictionary<string,string>{{"highway","residential"},{"width",diagonal?"7.5":"6"}},geometry=new{type="LineString",coordinates=i<64?new[]{At(20,20),At(70,20)}:new[]{At(70,100),At(130,diagonal?130:100)}}})});
    var p=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
    Check(p.LinearFeatures.Count==64,"Fixture did not exhaust editable paths");
    var expected=LinearFeatureEditor.Rasterize(new(){Kind="road",Width=diagonal?7.5:6,Points=[new(70,100),new(130,diagonal?130:100)]},256,256);
    var actual=Enumerable.Range(0,p.Terrain.Length).Where(i=>i/256>80&&p.Terrain[i]==Terrain.Road).ToHashSet();
    Check(expected.SetEquals(actual),$"Raster-only road shape/width differs: expected {expected.Count}, actual {actual.Count}");
   }
  });
 }
 static void CheckWidth(Dictionary<string,string> tags,double expected,string source)
 {
  var p=Import(tags);var line=p.LinearFeatures.Single();
  Check(Math.Abs(line.Width-Math.Clamp(expected,1,48))<1e-8,$"Expected {expected.ToString(CultureInfo.InvariantCulture)} metres (1–48 tile raster), got {line.Width.ToString(CultureInfo.InvariantCulture)} tiles");
  Check(line.SourceTags.GetValueOrDefault("pzmap:width-source")==source,"Width source not disclosed");
  Check(line.SourceTags.GetValueOrDefault("pzmap:width-metres")==expected.ToString("0.####",CultureInfo.InvariantCulture),"Physical width not retained");
 }
 static MapProject Import(Dictionary<string,string> extra,double metresPerTile=1)
 {
  var tags=new Dictionary<string,string>{{"highway","residential"}};foreach(var t in extra)tags[t.Key]=t.Value;
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.003,South=54,North=54.003},MetresPerTile=metresPerTile,OriginCellX=100,OriginCellY=100};
  var projection=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var at=projection.Geographic(x,y);return[at.Longitude,at.Latitude];}
  var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="road",properties=tags,geometry=new{type="LineString",coordinates=new[]{At(20,70),At(150,70)}}}}});
  return GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
