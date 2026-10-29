using System.Text.Json;
namespace SimplePZMapmaker.Web.Services;

public sealed partial class OsmClient
{
 private static readonly HashSet<string> mapFeatureTags=["highway","building","natural","landuse","waterway","railway","amenity","shop","leisure","site","entrance","barrier","healthcare","addr:housenumber"];
 private sealed record MapPoint(double Lon,double Lat);
 // The map API returns node references, whereas the importer needs full geometry.
 // Only single small-area imports may use this backup; tiled world downloads do not.
 private static string MapApiGeometry(string data,CancellationToken token){
  using var doc=JsonDocument.Parse(data);var elements=doc.RootElement.GetProperty("elements").EnumerateArray().ToArray();
  if(elements.Length>250000)throw new ArgumentException("Too many OSM map elements. Reduce the selected area.");
  var nodes=new Dictionary<long,MapPoint>();var ways=new Dictionary<long,List<MapPoint>>();var output=new List<object>();var omittedWays=0;var omittedRelations=0;
  static string? Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
  static JsonElement Tags(JsonElement e)=>e.TryGetProperty("tags",out var t)&&t.ValueKind==JsonValueKind.Object?t:JsonSerializer.SerializeToElement(new{});
  static bool Wanted(JsonElement tags)=>tags.EnumerateObject().Any(p=>mapFeatureTags.Contains(p.Name)||SimplePZMapmaker.Core.GeographicEvidence.IsFuelTag(p.Name,p.Value.ToString()));
  static long Id(JsonElement e)=>e.TryGetProperty("id",out var id)&&id.TryGetInt64(out var number)?number:throw new HttpRequestException("The OSM map API returned an element without an ID.");
  foreach(var e in elements){token.ThrowIfCancellationRequested();if(Text(e,"type")!="node")continue;
   if(!e.TryGetProperty("lon",out var lon)||!lon.TryGetDouble(out var x)||!e.TryGetProperty("lat",out var lat)||!lat.TryGetDouble(out var y)||!double.IsFinite(x)||!double.IsFinite(y)||x is <-180 or >180||y is <-85 or >85)throw new HttpRequestException("The OSM map API returned invalid coordinates.");
   var id=Id(e);if(!nodes.TryAdd(id,new(x,y)))throw new HttpRequestException("The OSM map API returned duplicate nodes.");var tags=Tags(e);if(Wanted(tags))output.Add(new{type="node",id,lon=x,lat=y,tags});
  }
  foreach(var e in elements){token.ThrowIfCancellationRequested();if(Text(e,"type")!="way")continue;var points=new List<MapPoint>();var complete=e.TryGetProperty("nodes",out var refs)&&refs.ValueKind==JsonValueKind.Array;
   if(complete)foreach(var reference in refs.EnumerateArray()){if(!reference.TryGetInt64(out var id)||!nodes.TryGetValue(id,out var point)){complete=false;break;}points.Add(point);}
   if(!complete||points.Count<2){omittedWays++;continue;}if(!ways.TryAdd(Id(e),points))throw new HttpRequestException("The OSM map API returned duplicate ways.");
  }
  var relationWays=new HashSet<long>();
  foreach(var e in elements){token.ThrowIfCancellationRequested();if(Text(e,"type")!="relation"||!Wanted(Tags(e)))continue;
   var tags=Tags(e);if(Text(tags,"type") is not ("multipolygon" or "boundary"))continue;
   var members=new List<object>();var referencedWays=new HashSet<long>();var complete=e.TryGetProperty("members",out var source)&&source.ValueKind==JsonValueKind.Array;
   if(complete)foreach(var member in source.EnumerateArray()){
    if(Text(member,"type")=="node")continue;
    if(Text(member,"type")!="way"||!member.TryGetProperty("ref",out var reference)||!reference.TryGetInt64(out var id)){complete=false;continue;}
    referencedWays.Add(id);if(!ways.TryGetValue(id,out var points)){complete=false;continue;}members.Add(new{type="way",@ref=id,role=Text(member,"role")??"outer",geometry=points});
   }
   if(!complete||members.Count==0){omittedRelations++;continue;}relationWays.UnionWith(referencedWays);output.Add(new{type="relation",id=Id(e),tags,members});
  }
  foreach(var e in elements){token.ThrowIfCancellationRequested();if(Text(e,"type")!="way")continue;var id=Id(e);var tags=Tags(e);if(!relationWays.Contains(id)&&Wanted(tags)&&ways.TryGetValue(id,out var points))output.Add(new{type="way",id,tags,geometry=points});}
  var json=JsonSerializer.Serialize(new{elements=output,spzm_incomplete_ways=omittedWays,spzm_incomplete_relations=omittedRelations},new JsonSerializerOptions(JsonSerializerDefaults.Web));
  if(System.Text.Encoding.UTF8.GetByteCount(json)>16_000_000)throw new ArgumentException("Too much geographic geometry. Reduce the selected area.");return json;
 }
 private static List<string> MapApiWarnings(string host,string data){
  if(host!="api.openstreetmap.org")return [];
  var warnings=new List<string>{"Source: read-only OSM map API backup for a small area. Overpass was unavailable. Larger worlds still use Overpass or saved extracts."};
  using var doc=JsonDocument.Parse(data);var root=doc.RootElement;
  var ways=root.TryGetProperty("spzm_incomplete_ways",out var w)&&w.TryGetInt32(out var wc)?wc:0;var relations=root.TryGetProperty("spzm_incomplete_relations",out var r)&&r.TryGetInt32(out var rc)?rc:0;
  if(ways+relations>0)warnings.Add($"Skipped incomplete OSM backup geometry: {ways} ways, {relations} relations. Partial contours were not used.");return warnings;
 }
}
