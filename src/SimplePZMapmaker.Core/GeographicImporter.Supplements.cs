using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SimplePZMapmaker.Core;

public sealed record GeographicSourceCombination(string GeoJson,int Features,int Replaced,IReadOnlyList<string> Diagnostics);
public static partial class GeographicImporter
{
 /// <summary>Compose attributed supplements with the saved baseline before import. Equal stable
 /// identities are replaced by the later source; geometric overlap alone never proves identity.</summary>
 public static GeographicSourceCombination CombineSources(IReadOnlyList<string> sources,int maxBytes=16_000_000){
  if(sources is null||sources.Count is <1 or >16||maxBytes is <1 or >64_000_000)throw new ArgumentException("Combine 1–16 saved geographic sources, at most 64 MB.");
  var features=new Dictionary<string,JsonNode>(StringComparer.Ordinal);var replaced=0;var omitted=0;
  var partOwners=new Dictionary<string,string>(StringComparer.Ordinal);var identityParts=new Dictionary<string,string[]>(StringComparer.Ordinal);
  void Store(string id,JsonObject feature){
   var properties=feature["properties"] as JsonObject??new JsonObject();
   properties["pz:source_id"]=id;feature["properties"]=properties;
   _=ExplicitGeoJsonSourceIdentity(properties);
   var parts=feature["geometry"]?["type"]?.ToString()=="MultiPolygon"&&feature["geometry"]?["coordinates"] is JsonArray polygons
    ?Enumerable.Range(0,polygons.Count).Select(i=>id+"-part-"+i).ToArray():new[]{id};
   if((partOwners.TryGetValue(id,out var owner)&&owner!=id)||parts.Any(part=>(partOwners.TryGetValue(part,out var existing)&&existing!=id)||(part!=id&&features.ContainsKey(part))))
    throw new ArgumentException("A source identity targets only a part of a MultiPolygon. Supply separate Polygon features with explicit part identities, or replace the whole MultiPolygon identity.");
   if(parts.Any(part=>part.Length>100))throw new ArgumentException("A MultiPolygon part identity exceeds 100 characters. Supply separate Polygon features with explicit identities.");
   if(identityParts.TryGetValue(id,out var previousParts))foreach(var part in previousParts)partOwners.Remove(part);
   identityParts[id]=parts;foreach(var part in parts)partOwners[part]=id;
   // The explicit identity prevents this emitted id from being treated as another raw GeoJSON id.
   feature["id"]=id;if(features.ContainsKey(id))replaced++;features[id]=feature;
  }
  JsonObject Converted(Feature feature,JsonObject? supplied=null){
   var properties=supplied?["properties"] is JsonObject suppliedProperties?(JsonObject)suppliedProperties.DeepClone():new JsonObject();
   if(supplied is null){
    foreach(var (key,value) in feature.Tags)if(key!="__pz_source")properties[key]=value;
    properties["source"]??="OpenStreetMap";properties["source:attribution"]??="OpenStreetMap contributors / ODbL 1.0";
   }
   JsonArray Coordinates(IEnumerable<(double X,double Y)> ring)=>new(ring.Select(p=>(JsonNode)new JsonArray(p.X,p.Y)).ToArray());
   var point=feature.Rings.Count==1&&feature.Rings[0].Count==1;
   var geometry=supplied?["geometry"] is JsonObject suppliedGeometry?(JsonObject)suppliedGeometry.DeepClone():new JsonObject();
   geometry["type"]=feature.Polygon?"Polygon":point?"Point":feature.Rings.Count==1?"LineString":"MultiLineString";
   geometry["coordinates"]=feature.Polygon||feature.Rings.Count>1?new JsonArray(feature.Rings.Select(r=>(JsonNode)Coordinates(r)).ToArray()):point?new JsonArray(feature.Rings[0][0].X,feature.Rings[0][0].Y):Coordinates(feature.Rings[0]);
   var copy=supplied is null?new JsonObject{["type"]="Feature"}:(JsonObject)supplied.DeepClone();
   copy["properties"]=properties;copy["geometry"]=geometry;return copy;
  }
  foreach(var json in sources){
   if(json is null||Encoding.UTF8.GetByteCount(json)>maxBytes)throw new ArgumentException("A geographic source exceeds the selected byte budget.");
   using var document=Parse(json);var read=ReadFeatures(document.RootElement,out var skipped,GeographicGeometryBudget.ForBytes(maxBytes));omitted+=skipped;
   var original=JsonNode.Parse(json,documentOptions:new(){MaxDepth=64}) as JsonObject;
   // Keep full supplied GeoJSON attributes. OSM geometry conversion uses its retained raw tags.
   var overpass=document.RootElement.TryGetProperty("elements",out var elements)&&elements.ValueKind==JsonValueKind.Array;
   if(!overpass&&original?["features"] is JsonArray supplied){foreach(var item in supplied){
    if(item is not JsonObject feature||feature["geometry"] is null)throw new ArgumentException("Combined GeoJSON needs non-null features.");
    var id=ExplicitGeoJsonSourceIdentity(feature["properties"] as JsonObject);
    if(id is null&&feature["id"] is JsonValue raw&&raw.GetValueKind() is JsonValueKind.String or JsonValueKind.Number)id=CanonicalGeoJsonSourceId(raw.ToString());
    if(id is not null)Store(id,(JsonObject)feature.DeepClone());
    else{
     // Reuse the importer's exact legacy hashes, including anonymous multipart identities.
     // Split only when no single identity exists; retain all supplied nested properties on each part.
     var singleton=new JsonObject{["type"]="FeatureCollection",["features"]=new JsonArray(feature.DeepClone())};
     using var single=Parse(singleton.ToJsonString());var parts=ReadFeatures(single.RootElement,out _,GeographicGeometryBudget.ForBytes(maxBytes));
     if(parts.Count==0)throw new ArgumentException("An anonymous feature has no supported complete geometry from which to retain its source identity.");
     var idOfFirst=parts[0].Tags["__pz_source"];
     if(feature["geometry"]?["type"]?.ToString()!="MultiPolygon"&&parts.All(part=>part.Tags["__pz_source"]==idOfFirst))Store(idOfFirst,(JsonObject)feature.DeepClone());
     else for(var i=0;i<parts.Count;i++){
      var copy=Converted(parts[i],feature);
      // Complete multipart inputs can keep their original coordinate arrays, including elevation.
      if(feature["geometry"]?["coordinates"] is JsonArray coordinates&&coordinates.Count==parts.Count)copy["geometry"]!["coordinates"]=coordinates[i]!.DeepClone();
      Store(parts[i].Tags["__pz_source"],copy);
     }
    }
   }}else foreach(var feature in read)Store(feature.Tags["__pz_source"],Converted(feature));
  }
  var result=new JsonObject{["type"]="FeatureCollection",["features"]=new JsonArray(features.Values.ToArray())}.ToJsonString();
  if(Encoding.UTF8.GetByteCount(result)>maxBytes)throw new ArgumentException("Combined source exceeds the import byte budget. Select a smaller area or combine regional source files in a scene.");
  using(var document=Parse(result))_ = ReadFeatures(document.RootElement,out _,GeographicGeometryBudget.ForBytes(maxBytes));
  return new(result,features.Count,replaced,[$"Combined baseline and supplements; replaced {replaced} equal source identities. Overlapping features with different identities remain separate for transfer diagnostics.",$"Incomplete baseline geometries skipped during conversion: {omitted}. Keep the original source files; this is a composed working source, not proof of completeness."]);
 }
}
