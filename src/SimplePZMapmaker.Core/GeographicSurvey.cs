using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimplePZMapmaker.Core;

/// <summary>Offline, attributed source intake. Does not fetch imagery, scrape providers or change an open project.</summary>
public static class GeographicSurvey
{
 public const int MaximumInputBytes=16*1024*1024;
 public const int MaximumFeatures=10000;
 public const int MaximumPoints=250000;
 public static IReadOnlyList<string> EssentialProjectTags {get;}=Array.AsReadOnly(new[]{"pz:source_id","source","source:attribution","source:author","source:license","source:date","source:retrieved_at","source:reference","source:evidence","source:region","source:expected_region","source:inferred_fields","cadastre:number","pz:parcel","pz:evidence","pz:region_exception"});
 public static GeographicSurveyResult Normalize(string geoJson,GeographicSurveyContext context,CancellationToken cancellationToken=default)
 {
  ArgumentNullException.ThrowIfNull(context);
  if(!Enum.IsDefined(context.Source)||!Enum.IsDefined(context.Evidence)||!Enum.IsDefined(context.InputCrs)||!Enum.IsDefined(context.Kind)||
   string.IsNullOrWhiteSpace(context.Attribution)||context.Attribution.Length>2000||context.SourceReference?.Length>2000||context.License?.Length>2000||context.Author?.Length>2000||
   context.ExpectedRegionCode is { } region&&(region.Length is <2 or >3||region.Any(c=>c is <'0' or >'9')))
   throw new ArgumentException("Survey source, CRS, evidence and nonempty attribution (up to 2,000 characters) are required.");
  if(!GeographicSurveySources.All.Single(s=>s.Source==context.Source).CanNormalizeVectors)throw new ArgumentException("This source accepts image references, not vector normalization.");
  if(geoJson is null||Encoding.UTF8.GetByteCount(geoJson)>MaximumInputBytes)throw new ArgumentException("Survey GeoJSON exceeds 16 MiB.");
  try{return NormalizeCore(geoJson,context,cancellationToken);}
  catch(JsonException){throw new ArgumentException("Survey input must be valid GeoJSON.");}
  catch(InvalidOperationException){throw new ArgumentException("Survey GeoJSON contains invalid property or coordinate types.");}
  catch(FormatException){throw new ArgumentException("Survey GeoJSON contains invalid coordinate values.");}
 }
 static GeographicSurveyResult NormalizeCore(string json,GeographicSurveyContext context,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  var root=JsonNode.Parse(json,documentOptions:new(){MaxDepth=64}) as JsonObject??throw new ArgumentException("Survey input must be a GeoJSON object.");
  var type=root["type"]?.GetValue<string>();
  var input=type=="FeatureCollection"?root["features"] as JsonArray:type=="Feature"?new JsonArray(root.DeepClone()):null;
  if(input is null||input.Count>MaximumFeatures)throw new ArgumentException("Expected GeoJSON FeatureCollection/Feature with at most 10,000 features.");
  // Explicit caller CRS is mandatory for projected input. An advertised conflicting CRS is an error, not a guess.
  if(root["crs"] is JsonObject crs){var name=crs["properties"]?["name"]?.GetValue<string>()??"";
   var matches=context.InputCrs==GeographicSurveyCrs.Epsg3857?name.EndsWith(":3857",StringComparison.OrdinalIgnoreCase):name.EndsWith(":4326",StringComparison.OrdinalIgnoreCase)||name.EndsWith(":CRS84",StringComparison.OrdinalIgnoreCase);
   if(!matches)throw new ArgumentException("Advertised GeoJSON CRS conflicts with the selected input CRS.");
  }
  var output=new JsonArray();var ids=new HashSet<string>(StringComparer.Ordinal);int points=0,duplicates=0,truncated=0,regionExceptions=0;
  foreach(var node in input){
   token.ThrowIfCancellationRequested();
   if(node is not JsonObject feature||feature["type"]?.GetValue<string>()!="Feature"||feature["geometry"] is not JsonObject geometry)
    throw new ArgumentException("Every survey feature must have a non-null geometry.");
   var g=(JsonObject)geometry.DeepClone();g.Remove("bbox");g.Remove("crs");var geometryType=g["type"]?.GetValue<string>();
   var depth=geometryType switch{"Point"=>0,"MultiPoint" or "LineString"=>1,"MultiLineString" or "Polygon"=>2,"MultiPolygon"=>3,_=>throw new ArgumentException("Unsupported survey geometry; supply Point/LineString/Polygon or their Multi variants.")};
   ConvertCoordinates(g["coordinates"],depth,context.InputCrs,ref points,token);
   ValidateGeometry(g,geometryType!);
   var raw=feature["properties"] as JsonObject??new JsonObject();var properties=(JsonObject)raw.DeepClone();
   var fields=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in raw)fields[p.Key]=p.Value;
   foreach(var key in new[]{"options","attributes"})if(raw[key] is JsonObject nested)foreach(var p in nested)fields.TryAdd(p.Key,p.Value);
   string? Field(params string[] keys){foreach(var key in keys)if(fields.TryGetValue(key,out var value)&&value is JsonValue){var text=value.ToString().Trim();if(text.Length>0)return text;}return null;}
   var cad=Field("cadastre:number","cad_num","cad_number","cad_number_value","cadastral_number","cn","Кадастровый номер");
   if(cad is not null){cad=string.Concat(cad.Where(c=>!char.IsWhiteSpace(c)));if(cad.Length>80||cad.Split(':') is not{Length:4} parts||parts.Any(p=>p.Length==0||p.Any(c=>c is <'0' or >'9')))throw new ArgumentException("Invalid cadastral number; expected four numeric colon-separated components.");}
   var originalId=feature["id"]?.ToString();
   var id=cad is not null?"cad-"+cad.Replace(':','-'):"survey-"+context.Source.ToString().ToLowerInvariant()+"-"+Hash(string.IsNullOrWhiteSpace(originalId)?geometry.ToJsonString()+raw.ToJsonString():originalId)[..24];
   var canonical=GeographicImporter.ExplicitGeoJsonSourceIdentity(raw)??GeographicImporter.CanonicalGeoJsonSourceId(id);
   if(raw.TryGetPropertyValue("pz:replaces",out var replacement)){
    if(context.Source!=GeographicSurveySourceKind.LocalSurvey||replacement is not JsonValue replacementValue||!replacementValue.TryGetValue<string>(out var target))throw new ArgumentException("Explicit source replacement needs a local survey and a valid stable target identity.");
    canonical=GeographicImporter.ExplicitSourceIdentity(new Dictionary<string,string>{{"pz:source_id",target}})!;id=canonical;
   }
   properties["pz:source_id"]=canonical;
   if(!ids.Add(canonical)){duplicates++;continue;}
   var evidence=context.Evidence;
   if(raw["pz:evidence"] is { } suppliedEvidence){
    if(suppliedEvidence.ToString() is not("observed" or "inferred"))throw new ArgumentException("Evidence must be observed or inferred.");
    if(suppliedEvidence.ToString()=="inferred")evidence=GeographicSurveyEvidence.Inferred;
   }
   var fieldEvidence=raw["pz:field_evidence"] is JsonObject suppliedFields?(JsonObject)suppliedFields.DeepClone():new JsonObject();
   foreach(var entry in fieldEvidence)if(entry.Value?.ToString() is not("observed" or "inferred"))throw new ArgumentException("Field evidence must be observed or inferred.");
   void Set(string key,string? value,params string[] aliases){if(value is not null){properties[key]=value;
    fieldEvidence[key]=evidence==GeographicSurveyEvidence.Inferred||aliases.Prepend(key).Any(a=>fieldEvidence[a]?.ToString()=="inferred")?"inferred":"observed";
   }}
   Set("name",Field("name","name_readable","label","Наименование"));Set("cadastre:number",cad);
   if(context.Kind==GeographicSurveyKind.Parcel){properties["pz:parcel"]="yes";properties.Remove("building");}
   if(context.Kind==GeographicSurveyKind.Building){properties["building"]=Field("building")??"yes";
    Set("building:levels",Field("building:levels","floors","floors_count","floors_cnt","storeys","Количество этажей"),"floors","floors_count","floors_cnt","storeys","Количество этажей");
    Set("building:material",Field("building:material","materials","material","wall_material","Материал наружных стен"),"materials","material","wall_material","Материал наружных стен");
    Set("start_date",Field("start_date","year_built","year_build","year_commissioning","Год завершения строительства"),"year_built","year_build","year_commissioning","Год завершения строительства");
   }
   properties["pz:source_attributes"]=raw.DeepClone();
   properties["source"]=context.Source.ToString();properties["pz:evidence"]=evidence.ToString().ToLowerInvariant();properties["pz:field_evidence"]=fieldEvidence;
   void Essential(string key,string? value){if(string.IsNullOrWhiteSpace(value))return;var safe=string.Concat(value.Select(c=>char.IsControl(c)?' ':c));if(safe.Length>300){safe=safe[..300];truncated++;}properties[key]=safe;}
   Essential("source:inferred_fields",string.Join(",",fieldEvidence.Where(p=>p.Value?.ToString()=="inferred").Select(p=>p.Key).Order(StringComparer.Ordinal)));
   Essential("source:attribution",context.Attribution);Essential("source:author",context.Author);Essential("source:license",context.License);
   Essential("source:date",context.SourceDate?.ToString("O",CultureInfo.InvariantCulture));Essential("source:retrieved_at",context.RetrievedAt?.ToString("O",CultureInfo.InvariantCulture));
   Essential("source:reference",context.SourceReference);Essential("source:evidence",evidence.ToString().ToLowerInvariant());
   var observedRegion=cad?.Split(':')[0];Essential("source:region",observedRegion);Essential("source:expected_region",context.ExpectedRegionCode);
   var regionException=observedRegion is not null&&context.ExpectedRegionCode is not null&&observedRegion!=context.ExpectedRegionCode;
   if(regionException){regionExceptions++;properties["pz:region_exception"]="yes";}
   properties["pz:provenance"]=new JsonObject{
    ["source"]=context.Source.ToString(),["attribution"]=context.Attribution,["author"]=context.Author,["reference"]=context.SourceReference,
    ["sourceDate"]=context.SourceDate?.ToString("O",CultureInfo.InvariantCulture),["retrievedAt"]=context.RetrievedAt?.ToString("O",CultureInfo.InvariantCulture),
    ["license"]=context.License,["inputCrs"]=context.InputCrs.ToString(),["evidence"]=evidence.ToString().ToLowerInvariant(),
    ["regionCode"]=observedRegion,["expectedRegionCode"]=context.ExpectedRegionCode,["regionException"]=regionException,
    ["meaning"]="Observed means reported by the attributed source, not independently verified. Full attributes remain in this external GeoJSON, not necessarily in project evidence."
   };
   var normalized=(JsonObject)feature.DeepClone();normalized.Remove("bbox");normalized.Remove("crs");normalized["id"]=id;normalized["geometry"]=g;normalized["properties"]=properties;output.Add(normalized);
  }
  // Retain collection foreign members/metadata; never retain a stale projected CRS/bbox.
  var collection=type=="FeatureCollection"?(JsonObject)root.DeepClone():new JsonObject();collection.Remove("crs");collection.Remove("bbox");collection["type"]="FeatureCollection";collection["features"]=output;
  collection["pz:normalization"]=new JsonObject{["version"]=1,["outputCrs"]="EPSG:4326",["attribution"]=context.Attribution};
  var result=collection.ToJsonString();if(Encoding.UTF8.GetByteCount(result)>MaximumInputBytes)throw new ArgumentException("Normalized survey exceeds 16 MiB.");
  var diagnostics=new List<string>{"Full nested source attributes are retained in external GeoJSON. Project evidence keeps only registered concise tags (64 tags, 300 characters each); keep this source file for complete metadata."};
  if(duplicates>0)diagnostics.Add($"Deduplicated {duplicates} repeated source identities; first geometry retained.");
  if(truncated>0)diagnostics.Add($"Truncated {truncated} essential provenance tag values to 300 characters; full values retained in external GeoJSON provenance.");
  if(regionExceptions>0)diagnostics.Add($"Retained {regionExceptions} cadastral identifiers outside the supplied expected region code. This is an identifier check, not administrative boundary verification.");
  return new(result,output.Count,diagnostics);
 }
 static void ConvertCoordinates(JsonNode? node,int depth,GeographicSurveyCrs crs,ref int points,CancellationToken token)
 {
  if(node is not JsonArray a)throw new ArgumentException("Survey coordinates must be arrays.");
  if(depth>0){if(a.Count==0)throw new ArgumentException("Empty survey geometry is unsupported.");foreach(var child in a)ConvertCoordinates(child,depth-1,crs,ref points,token);return;}
  token.ThrowIfCancellationRequested();if(++points>MaximumPoints)throw new ArgumentException("Survey exceeds 250,000 points.");
  if(a.Count is <2 or >3)throw new ArgumentException("Positions need two coordinates and optional elevation.");
  var x=a[0]?.GetValue<double>()??double.NaN;var y=a[1]?.GetValue<double>()??double.NaN;
  if(crs==GeographicSurveyCrs.Epsg3857){var ll=GeographicSurveyProjection.FromWebMercator(x,y);a[0]=ll.Longitude;a[1]=ll.Latitude;}
  else if(!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)>180||Math.Abs(y)>90)throw new ArgumentException("Invalid WGS84 coordinate.");
  if(a.Count==3&&!double.IsFinite(a[2]?.GetValue<double>()??double.NaN))throw new ArgumentException("Invalid elevation.");
 }
 static void ValidateGeometry(JsonObject g,string type)
 {
  var a=g["coordinates"]!.AsArray();
  void Line(JsonArray line,bool ring){if(line.Count<(ring?4:2)||ring&&!JsonNode.DeepEquals(line[0],line[^1]))throw new ArgumentException("Lines need two points; polygon rings need four positions and exact closure.");}
  if(type=="LineString")Line(a,false);
  if(type=="MultiLineString")foreach(var line in a)Line(line!.AsArray(),false);
  if(type=="Polygon")foreach(var ring in a)Line(ring!.AsArray(),true);
  if(type=="MultiPolygon")foreach(var polygon in a)foreach(var ring in polygon!.AsArray())Line(ring!.AsArray(),true);
 }
 static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
