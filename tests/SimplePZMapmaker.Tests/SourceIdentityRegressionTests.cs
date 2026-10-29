using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;

public static class SourceIdentityRegressionTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()
 {
  yield return ("source identity converted Overpass keeps its imported identity",()=>{
   const string osm="""{"elements":[{"type":"node","id":7,"lon":45.001,"lat":55.001,"tags":{"amenity":"bench"}},{"type":"way","id":7,"tags":{"highway":"residential"},"geometry":[{"lon":45,"lat":55},{"lon":45.002,"lat":55.002}]}]}""";
   var combined=GeographicImporter.CombineSources([osm]);
   Check(ImportedIds(combined.GeoJson).SequenceEqual(ImportedIds(osm)),"Overpass identity changed after composition");
   Check(Features(combined.GeoJson).All(f=>f!["properties"]!["pz:source_id"] is not null),"Converted source omitted explicit canonical identities");
   Check(Import(combined.GeoJson).Project.LinearFeatures.Single().SourceId=="osm-way-7","Road identity acquired a GeoJSON prefix");
  });
  yield return ("source identity displayed GeoJSON target replaces raw house and retains nested attributes",()=>{
   var baseline=Collection(Feature("house",new(){["amenity"]="bench",["name"]="before"}));
   var correction=GeographicSurvey.Normalize(Collection(Feature("local",new(){["amenity"]="bench",["name"]="after",["pz:replaces"]="geo-house",["nested"]=new JsonObject{["list"]=new JsonArray(42,true,null,new JsonObject{["value"]="kept"})}})),Context());
   Check(Features(correction.GeoJson)[0]!["properties"]!["pz:source_id"]?.ToString()=="geo-house","Normalization failed to emit the exact target identity");
   var combined=GeographicImporter.CombineSources([baseline,correction.GeoJson]);
   Check(combined.Features==1&&combined.Replaced==1,"Canonical correction did not replace raw house exactly once");
   var feature=Features(combined.GeoJson).Single()!;
   Check(feature["properties"]!["nested"]!["list"]![3]!["value"]!.ToString()=="kept","Nested attributes were flattened");
   Check(feature["properties"]!["pz:source_attributes"]!["nested"]!["list"]![0]!.GetValue<int>()==42,"Original nested attributes were discarded");
   Check(ImportedIds(combined.GeoJson).SequenceEqual(new[]{"geo-house"}),"Correction changed its displayed identity on import");
   Check(Import(combined.GeoJson).Project.SourceFeatures.Single().Name=="after","Correction payload did not win");
  });
  yield return ("source identity repeated combination is idempotent and respects legacy prefixed raw IDs",()=>{
   var input=Collection(Feature("house"),Feature("geo-house"));
   var first=GeographicImporter.CombineSources([input]);
   var second=GeographicImporter.CombineSources([first.GeoJson]);
   var duplicate=GeographicImporter.CombineSources([first.GeoJson,input]);
   Check(first.GeoJson==second.GeoJson,"Composition changed its own output");
   Check(duplicate.Features==2&&duplicate.Replaced==2,"Raw and emitted IDs no longer match on repeated combination");
   Check(ImportedIds(second.GeoJson).SequenceEqual(new[]{"geo-geo-house","geo-house"}),"Bare GeoJSON IDs lost legacy geo- prefix behavior");
  });
  yield return ("source identity anonymous geometries survive composition without proximity matching",()=>{
   var a=Feature(null,new(){["amenity"]="bench",["nested"]=new JsonObject{["full"]=true}});
   var b=Feature(null,new(){["amenity"]="bench"});b["geometry"]!["coordinates"]![0]=45.00101;
   var input=Collection(a,b);var expected=ImportedIds(input);
   var combined=GeographicImporter.CombineSources([input]);
   var repeated=GeographicImporter.CombineSources([combined.GeoJson,input]);
   Check(combined.Features==2&&repeated.Features==2&&repeated.Replaced==2,"Nearby anonymous geometries merged or gained new IDs");
   Check(ImportedIds(combined.GeoJson).SequenceEqual(expected)&&ImportedIds(repeated.GeoJson).SequenceEqual(expected),"Anonymous legacy geometry identities changed");
   Check(Features(combined.GeoJson)[0]!["properties"]!["nested"]!["full"]!.GetValue<bool>(),"Anonymous feature lost full properties");
  });
  yield return ("source identity MultiPolygon keeps legacy part suffixes and holes",()=>{
   var feature=Feature("area",new(){["landuse"]="forest"});
   feature["geometry"]=JsonNode.Parse("""{"type":"MultiPolygon","coordinates":[[[[45,55],[45.0008,55],[45.0008,55.0008],[45,55.0008],[45,55]],[[45.0002,55.0002],[45.0004,55.0002],[45.0004,55.0004],[45.0002,55.0004],[45.0002,55.0002]]],[[[45.001,55.001],[45.002,55.001],[45.002,55.002],[45.001,55.002],[45.001,55.001]]]]}""");
   feature["provider_metadata"]=new JsonObject{["revision"]=42};feature["geometry"]!["provider_metadata"]=new JsonObject{["survey"]="retained"};
   foreach(var anonymous in new[]{false,true}){
    if(anonymous)feature.Remove("id");
    var input=Collection((JsonObject)feature.DeepClone());var expected=ImportedIds(input);
    var combined=GeographicImporter.CombineSources([input]);var repeated=GeographicImporter.CombineSources([combined.GeoJson]);
    Check(ImportedIds(repeated.GeoJson).SequenceEqual(expected),"Multipart source identity changed");
    if(!anonymous)Check(expected.SequenceEqual(new[]{"geo-area-part-0","geo-area-part-1"}),"Named multipart fixture lost legacy suffixes");
    Check(Import(repeated.GeoJson).Project.SourceFeatures.Any(f=>f.SourceGeometry.Count==2),"Multipart courtyard hole disappeared");
    Check(Features(repeated.GeoJson).All(f=>f!["provider_metadata"]?["revision"]?.GetValue<int>()==42&&f["geometry"]?["provider_metadata"]?["survey"]?.ToString()=="retained"),"Multipart feature/geometry metadata disappeared");
   }
  });
  yield return ("source identity byte and coordinate validation survives combination",()=>{
   Reject(()=>GeographicImporter.CombineSources([Collection(Feature("house"))],16));
   var feature=Feature("invalid");feature["geometry"]!["coordinates"]![0]=181;
   Reject(()=>GeographicImporter.CombineSources([Collection(feature)]));
  });
  yield return ("source identity explicit canonical values are exact and malformed values fail closed",()=>{
   foreach(var id in new[]{"osm-way-7","geo-house","Canonical-Case",new string('x',100)}){
    var input=Collection(Feature("ignored",new(){["amenity"]="bench",["pz:source_id"]=id,["__pz_source"]="stale"}));
    Check(ImportedIds(input).SequenceEqual(new[]{id}),"Explicit identity was transformed or lost to an internal tag");
    Check(ImportedIds(GeographicImporter.CombineSources([input]).GeoJson).SequenceEqual(new[]{id}),"Composition changed an explicit identity");
    Check(ImportedIds(GeographicSurvey.Normalize(input,Context()).GeoJson).SequenceEqual(new[]{id}),"Normalization changed an explicit identity");
   }
   foreach(var invalid in new string?[]{null,"","   ","geo-\nhouse",new string('x',101)}){
    var input=Collection(Feature("ignored",new(){["pz:source_id"]=invalid}));
    Reject(()=>GeographicImporter.CombineSources([input]));Reject(()=>GeographicSurvey.Normalize(input,Context()));Reject(()=>Import(input));
    var replacement=Collection(Feature("local",new(){["pz:replaces"]=invalid}));
    Reject(()=>GeographicSurvey.Normalize(replacement,Context()));
   }
   var objectIdentity=Collection(Feature("ignored",new(){["pz:source_id"]=new JsonObject{["id"]="house"}}));
   Reject(()=>GeographicImporter.CombineSources([objectIdentity]));Reject(()=>GeographicSurvey.Normalize(objectIdentity,Context()));
  });
  yield return ("source identity long raw IDs retain the legacy hash threshold",()=>{
   foreach(var raw in new[]{new string('a',75),new string('a',76),"osm-way-7","geo-house"}){
    var input=Collection(Feature(raw));var before=ImportedIds(input);
    var combined=GeographicImporter.CombineSources([input]);
    Check(ImportedIds(combined.GeoJson).SequenceEqual(before),"Legacy raw identity changed across composition");
    var correction=Collection(Feature("local",new(){["amenity"]="bench",["pz:replaces"]=before.Single()}));
    var normalized=GeographicSurvey.Normalize(correction,Context());
    var replaced=GeographicImporter.CombineSources([input,normalized.GeoJson]);
    Check(replaced.Features==1&&replaced.Replaced==1,"Canonical long/raw target failed to replace its source");
   }
   Check(ImportedIds(Collection(Feature(new string('a',75)))).Single()=="geo-"+new string('a',75),"75-character raw ID was hashed");
   const string hashed="geo-f8b8aba652e5b3cde6bc74bcb7bff15289a222cfdb7759a9809dc08574911f60";
   Check(ImportedIds(Collection(Feature(new string('a',76)))).Single()==hashed,"76-character raw ID was not legacy SHA256-mapped");
   Check(GeographicImporter.CanonicalGeoJsonSourceId(new string('a',76))==hashed,"Shared canonical helper changed the legacy hash");
   Check(GeographicImporter.CanonicalGeoJsonSourceId("geo-house")=="geo-geo-house","Shared canonical helper treated a raw ID as canonical");
   var internalTag=Collection(Feature("house",new(){["__pz_source"]="geo-wrong",["amenity"]="bench"}));
   Check(ImportedIds(GeographicImporter.CombineSources([internalTag]).GeoJson).Single()=="geo-house","Internal tag overrode a legacy raw id");
  });
  yield return ("source identity anonymous single-part MultiPolygon does not acquire a part suffix",()=>{
   var feature=Feature(null,new(){["landuse"]="forest"});
   feature["geometry"]=JsonNode.Parse("""{"type":"MultiPolygon","coordinates":[[[[45,55],[45.002,55],[45.002,55.002],[45,55.002],[45,55]]]]}""");
   var input=Collection(feature);var combined=GeographicImporter.CombineSources([input]);
   Check(ImportedIds(input).SequenceEqual(ImportedIds(combined.GeoJson)),"Anonymous single-part polygon gained a suffix");
  });
  yield return ("source identity correcting one named MultiPolygon part is rejected rather than duplicating evidence",()=>{
   var area=Feature("area",new(){["landuse"]="forest"});
   area["geometry"]=JsonNode.Parse("""{"type":"MultiPolygon","coordinates":[[[[45,55],[45.002,55],[45.002,55.002],[45,55.002],[45,55]]]]}""");
   var input=Collection(area);
   var part=GeographicSurvey.Normalize(Collection(Feature("local",new(){["pz:replaces"]="geo-area-part-0"})),Context()).GeoJson;
   Reject(()=>GeographicImporter.CombineSources([input,part]));Reject(()=>GeographicImporter.CombineSources([part,input]));
   var whole=GeographicSurvey.Normalize(Collection(Feature("local",new(){["pz:replaces"]="geo-area"})),Context()).GeoJson;
   var combined=GeographicImporter.CombineSources([input,whole]);
   Check(combined.Features==1&&combined.Replaced==1,"Whole multipart replacement was rejected or duplicated");
  });
  yield return ("source identity local house correction imports one updated building",()=>{
   var house=Feature("house",new(){["building"]="house",["building:levels"]="1"});
   house["geometry"]=JsonNode.Parse("""{"type":"Polygon","coordinates":[[[45.0008,55.0008],[45.0012,55.0008],[45.0012,55.0012],[45.0008,55.0012],[45.0008,55.0008]]]}""");
   var baseline=Collection((JsonObject)house.DeepClone());
   house["id"]="local";house["properties"]!["pz:replaces"]="geo-house";house["properties"]!["building:levels"]="2";
   var correction=GeographicSurvey.Normalize(Collection(house),Context() with{Kind=GeographicSurveyKind.Building});
   var combined=GeographicImporter.CombineSources([baseline,correction.GeoJson]);var imported=Import(combined.GeoJson);
   Check(combined.Features==1&&combined.Replaced==1&&imported.Project.Buildings.Count==1,"House correction duplicated or lost the building");
   Check(imported.Project.Buildings.Single().SourceId=="geo-house"&&imported.Project.SourceFeatures.Single().Tags["building:levels"]=="2","Imported building lost its canonical identity or corrected attributes");
  });
  yield return ("source identity anonymous multiline retains every path with distinct or legacy shared identities",()=>{
   foreach(var legacyShared in new[]{false,true}){
    var feature=Feature(null,new(){["addr:street"]="Survey frontage"});
    feature["geometry"]=JsonNode.Parse("""{"type":"MultiLineString","coordinates":[[[45,55],[45.001,55.001]],[[45.001,55],[45.002,55.001]]]}""");
    if(legacyShared)feature["properties"]!["__pz_source"]="legacy-lines";
    var input=Collection(feature);var combined=GeographicImporter.CombineSources([input]);
    var paths=Features(combined.GeoJson).Sum(f=>f!["geometry"]!["type"]!.ToString()=="MultiLineString"?f["geometry"]!["coordinates"]!.AsArray().Count:1);
    Check(paths==2,"Shared legacy identity discarded a path during composition");
    Check(ImportedIds(input).SequenceEqual(ImportedIds(combined.GeoJson)),"Multiline source identity changed");
   }
  });
 }
 static GeographicSurveyContext Context()=>new(){Source=GeographicSurveySourceKind.LocalSurvey,Attribution="Local source identity regression survey"};
 static ImportResult Import(string json)=>GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),new(){West=44.999,East=45.003,South=54.999,North=55.003});
 static string[] ImportedIds(string json)=>Import(json).Project.SourceFeatures.Select(f=>f.SourceId).Order(StringComparer.Ordinal).ToArray();
 static JsonObject Feature(string? id,JsonObject? properties=null){
  var feature=new JsonObject{["type"]="Feature",["properties"]=properties??new(){["amenity"]="bench"},["geometry"]=new JsonObject{["type"]="Point",["coordinates"]=new JsonArray(45.001,55.001)}};
  if(id is not null)feature["id"]=id;return feature;
 }
 static string Collection(params JsonObject[] features)=>new JsonObject{["type"]="FeatureCollection",["features"]=new JsonArray(features.Cast<JsonNode>().ToArray())}.ToJsonString();
 static JsonArray Features(string json)=>JsonNode.Parse(json)!["features"]!.AsArray();
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid source accepted");}
}
