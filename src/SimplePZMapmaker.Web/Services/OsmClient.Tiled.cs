using System.Text.Json;
using System.Text;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmProgress(string Phase,int Completed,int Total,string? Source=null,string? DataTimestamp=null,string? Warning=null){public long? BytesReceived {get;init;}public long? TotalBytes {get;init;}}
public sealed partial class OsmClient
{
 public async Task<OsmDownload> FetchTiledDetailed(GeoBounds bounds,CancellationToken token,IProgress<OsmProgress>? progress=null,int maxBytes=16_000_000,bool world=false)
 {
  if(world)WorldLayoutLimits.ValidateSourceBounds(bounds);else ProjectValidator.ValidateBounds(bounds);if(maxBytes is <1 or >64_000_000)throw new ArgumentException("Geographic data limit: 64 MB.");
  var rows=Math.Max(1,(int)Math.Ceiling((bounds.North-bounds.South)/.01));var columns=Math.Max(1,(int)Math.Ceiling((bounds.East-bounds.West)/.02));var total=rows*columns;if(total>64)throw new ArgumentException("Too many geographic requests: reduce the selected area.");
  var geometry=new OsmGeometrySet(maxBytes);var warnings=new HashSet<string>();var sources=new HashSet<string>();var metadata=new List<OsmSource>();var dates=new HashSet<string>();var requests=0;var allCached=true;var retrieved=DateTimeOffset.MinValue;var completed=0;
  foreach(var y in Enumerable.Range(0,rows))foreach(var x in Enumerable.Range(0,columns)){
   token.ThrowIfCancellationRequested();var tile=new GeoBounds{West=bounds.West+(bounds.East-bounds.West)*x/columns,East=bounds.West+(bounds.East-bounds.West)*(x+1)/columns,South=bounds.South+(bounds.North-bounds.South)*y/rows,North=bounds.South+(bounds.North-bounds.South)*(y+1)/rows};
   var report=new InlineProgress<OsmProgress>(attempt=>progress?.Report(attempt with{Completed=completed,Total=total}));var data=await FetchSingleDetailed(tile,token,report);geometry.Add(data.Data);sources.Add(data.Source);metadata.AddRange(data.Sources);if(data.DataTimestamp is {} date)dates.Add(date);foreach(var warning in data.Warnings)warnings.Add(warning);requests+=data.Requests;allCached&=data.FromCache;retrieved=data.RetrievedAtUtc>retrieved?data.RetrievedAtUtc:retrieved;progress?.Report(new("downloaded",++completed,total,data.Source,data.DataTimestamp));
  }
  if(dates.Count>1)warnings.Add("Sections were retrieved from OSM snapshots with different dates; source dates were preserved in the report.");warnings.Add($"Geographic data loaded in {total} small areas; duplicate features merged by type/id, and way geometry retained beyond query boundaries.");
  var timestamp=dates.Count==1?dates.Single():null;return new(geometry.Serialize(timestamp),string.Join(", ",sources),warnings.Take(20).ToList()){Requests=requests,FromCache=allCached,RetrievedAtUtc=retrieved,DataTimestamp=timestamp,Sources=metadata};
 }
 private static string? Timestamp(string json){using var doc=JsonDocument.Parse(json);return doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("osm3s",out var meta)&&meta.ValueKind==JsonValueKind.Object&&meta.TryGetProperty("timestamp_osm_base",out var date)&&date.ValueKind==JsonValueKind.String&&date.GetString() is {Length:<=100} value?value:null;}
 private sealed record CacheSource(string Host,string Hash,DateTimeOffset RetrievedAtUtc);
 private static string DataHash(string data)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(data)));
 private static (string Host,DateTimeOffset Retrieved) CacheMetadata(string file,string data){var meta=file+".source.json";if(File.Exists(meta)&&new FileInfo(meta).Length<4096){try{var saved=JsonSerializer.Deserialize<CacheSource>(File.ReadAllText(meta));if(saved is not null&&saved.Hash==DataHash(data)&&saved.Host.Length<=200&&Uri.CheckHostName(saved.Host)!=UriHostNameType.Unknown)return(saved.Host,saved.RetrievedAtUtc);}catch(Exception e) when(e is JsonException or IOException){}}return("local-cache (original server not recorded)",File.GetLastWriteTimeUtc(file));}
}
internal sealed class InlineProgress<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
public sealed class OsmGeometrySet(int maxBytes=16_000_000)
{
 private sealed record Entry(JsonElement Json,double MinX,double MinY,double MaxX,double MaxY,int Points,int Features,int Bytes){public bool Intersects(GeoBounds b)=>MinX<=b.East&&MaxX>=b.West&&MinY<=b.North&&MaxY>=b.South;}
 private Dictionary<string,Entry> elements=[];private bool? geoJson;private int nextFeature;
 public bool IsGeoJson=>geoJson==true;
 public void Add(string json)
 {
  // Only aggregate allowances scale for worlds; Inspect keeps the per-feature structural guards.
  var budget=GeographicGeometryBudget.ForBytes(maxBytes);
  if(Encoding.UTF8.GetByteCount(json)>maxBytes)throw TooLarge();
  using var document=Parse(json);var root=document.RootElement;var incoming=IsGeo(root);
  if(geoJson is {} prior&&prior!=incoming)throw new ArgumentException("Overpass and GeoJSON cannot be mixed in one geographic source.");
  // Stage the entire addition: a malformed or over-budget tile must not corrupt the retained source.
  var pending=new Dictionary<string,Entry>(elements);var sequence=nextFeature;var work=0;var points=elements.Values.Sum(e=>e.Points);var features=elements.Values.Sum(e=>e.Features);long bytes=elements.Values.Sum(e=>(long)e.Bytes);
  foreach(var item in Items(root,incoming)){
   if(++work>budget.Work)throw TooComplex();
   var key=incoming?"geo-"+sequence++:OsmKey(item);var entry=Inspect(item,incoming,budget.Work,ref work);
   if(pending.TryGetValue(key,out var previous)){if(entry.Points<=previous.Points)continue;points-=previous.Points;features-=previous.Features;bytes-=previous.Bytes;}
   points+=entry.Points;features+=entry.Features;bytes+=entry.Bytes;
   if(pending.Count>=budget.Features&&!pending.ContainsKey(key)||features>budget.Features||points>budget.Points)throw TooComplex();if(bytes>maxBytes)throw TooLarge();pending[key]=entry;
  }
  // Measure the actual UTF-8 representation too, including JSON escaping and the collection envelope.
  Encode(pending.Values,incoming,null);elements=pending;geoJson=incoming;nextFeature=sequence;
 }
 public string Serialize(string? date=null)=>Encode(elements.Values,IsGeoJson,date);
 public string ForSection(GeoBounds bounds){WorldLayoutLimits.ValidateSourceBounds(bounds);return Encode(elements.Values.Where(e=>e.Intersects(bounds)),IsGeoJson,null);}
 public static bool HasFeatures(string json){using var document=Parse(json);var root=document.RootElement;return Items(root,IsGeo(root)).Any();}
 private string Encode(IEnumerable<Entry> entries,bool geo,string? date){var values=entries.Select(e=>e.Json).ToArray();var json=geo?JsonSerializer.Serialize(new{type="FeatureCollection",features=values}):JsonSerializer.Serialize(new{osm3s=new{timestamp_osm_base=date},elements=values});if(Encoding.UTF8.GetByteCount(json)>maxBytes)throw TooLarge();return json;}
 private static JsonDocument Parse(string json){try{return JsonDocument.Parse(json,new(){MaxDepth=48});}catch(JsonException e){throw new ArgumentException("Could not read geographic JSON (maximum nesting: 48).",e);}}
 private static bool IsGeo(JsonElement root){
  if(root.ValueKind!=JsonValueKind.Object)throw new ArgumentException("A GeoJSON FeatureCollection, Feature, or Overpass object is required.");
  var hasElements=root.TryGetProperty("elements",out var elements);var hasFeatures=root.TryGetProperty("features",out var features);var type=root.TryGetProperty("type",out var kind)&&kind.ValueKind==JsonValueKind.String?kind.GetString():null;
  if(hasElements){if(hasFeatures||type is "FeatureCollection" or "Feature")throw new ArgumentException("Overpass and GeoJSON cannot be mixed in one geographic source.");if(elements.ValueKind!=JsonValueKind.Array)throw new ArgumentException("OSM elements must be an array.");return false;}
  if(type=="Feature"&&!hasFeatures)return true;if(type=="FeatureCollection"&&hasFeatures&&features.ValueKind==JsonValueKind.Array)return true;throw new ArgumentException("A GeoJSON FeatureCollection, Feature, or Overpass object is required.");
 }
 private static IEnumerable<JsonElement> Items(JsonElement root,bool geo){if(geo&&root.TryGetProperty("type",out var type)&&type.GetString()=="Feature"){yield return root;yield break;}foreach(var item in root.GetProperty(geo?"features":"elements").EnumerateArray())yield return item;}
 private static string OsmKey(JsonElement item){if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||type.GetString() is not ("node" or "way" or "relation")||!item.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.Number||!id.TryGetInt64(out var number))throw new ArgumentException("The OSM feature has no valid type/id.");return type.GetString()+":"+number.ToString(System.Globalization.CultureInfo.InvariantCulture);}
 private static Entry Inspect(JsonElement item,bool geo,int maxWork,ref int work){
  var minX=double.PositiveInfinity;var minY=double.PositiveInfinity;var maxX=double.NegativeInfinity;var maxY=double.NegativeInfinity;var points=0;var features=1;var visits=work;
  void Visit(){if(++visits>maxWork)throw TooComplex();}
  void Point(double x,double y){if(!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -85 or >85)throw new ArgumentException("Invalid geographic coordinates.");if(++points>250000)throw TooComplex();minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
  void Coordinates(JsonElement value,int depth){Visit();if(value.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Invalid GeoJSON coordinates.");if(depth==0){if(value.GetArrayLength()<2||value[0].ValueKind!=JsonValueKind.Number||value[1].ValueKind!=JsonValueKind.Number||!value[0].TryGetDouble(out var x)||!value[1].TryGetDouble(out var y))throw new ArgumentException("Invalid GeoJSON position.");Point(x,y);return;}foreach(var child in value.EnumerateArray())Coordinates(child,depth-1);}
  void Osm(JsonElement value){Visit();if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Invalid OSM geometry.");if(value.TryGetProperty("lon",out var lon)&&lon.ValueKind==JsonValueKind.Number&&lon.TryGetDouble(out var x)&&value.TryGetProperty("lat",out var lat)&&lat.ValueKind==JsonValueKind.Number&&lat.TryGetDouble(out var y))Point(x,y);if(value.TryGetProperty("geometry",out var geometry)){if(geometry.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Invalid OSM geometry.");foreach(var p in geometry.EnumerateArray())Osm(p);}if(value.TryGetProperty("members",out var members)){if(members.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Invalid OSM members.");foreach(var member in members.EnumerateArray())Osm(member);}}
  if(geo){
   if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||type.GetString()!="Feature"||item.TryGetProperty("elements",out _))throw new ArgumentException("Invalid GeoJSON Feature.");
   if(!item.TryGetProperty("geometry",out var geometry))throw new ArgumentException("GeoJSON Feature requires geometry.");
   if(geometry.ValueKind!=JsonValueKind.Null){
    if(geometry.ValueKind!=JsonValueKind.Object||!geometry.TryGetProperty("type",out var kind)||kind.ValueKind!=JsonValueKind.String||!geometry.TryGetProperty("coordinates",out var coordinates))throw new ArgumentException("Invalid GeoJSON geometry.");
    var depth=kind.GetString() switch{"Point"=>0,"LineString"=>1,"Polygon" or "MultiLineString"=>2,"MultiPolygon"=>3,_=>throw new ArgumentException("Supported GeoJSON geometry: Point, LineString, Polygon, MultiLineString, MultiPolygon.")};
    if(kind.GetString() is "MultiLineString" or "MultiPolygon"){if(coordinates.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Invalid GeoJSON coordinates.");features=Math.Max(1,coordinates.GetArrayLength());if(features>30000)throw TooComplex();}Coordinates(coordinates,depth);
   }
  }else Osm(item);
  work=visits;return new(item.Clone(),minX,minY,maxX,maxY,points,features,Encoding.UTF8.GetByteCount(item.GetRawText()));
 }
 private static ArgumentException TooComplex()=>new("Geographic geometry exceeds the feature, point, or processing limit. Reduce the area or simplify the source.");
 private static ArgumentException TooLarge()=>new("World geographic data is too large. Reduce the area or import multiple worlds.");
}
