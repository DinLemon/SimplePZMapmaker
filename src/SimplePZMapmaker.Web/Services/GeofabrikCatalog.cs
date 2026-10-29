using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record GeographicRegion(string Id,string Name,string? Parent,string Url,List<List<List<GeographicCoordinate>>> Polygons)
{
 public double Area=>Polygons.Sum(p=>Math.Abs(SignedArea(p[0]))-p.Skip(1).Sum(r=>Math.Abs(SignedArea(r))));
 private static double SignedArea(List<GeographicCoordinate> ring){double sum=0;for(var i=1;i<ring.Count;i++)sum+=ring[i-1].Longitude*ring[i].Latitude-ring[i].Longitude*ring[i-1].Latitude;return sum/2;}
}
public readonly record struct GeographicCoordinate(double Longitude,double Latitude);

// Official catalogue only. All geometry is WGS84; a successful plan must cover the whole request.
public sealed class GeofabrikCatalog
{
 public const string CatalogueUrl="https://download.geofabrik.de/index-v1.json";
 public IReadOnlyList<GeographicRegion> Regions {get;}
 public GeofabrikCatalog(string json)
 {
  try{
  if(System.Text.Encoding.UTF8.GetByteCount(json)>32_000_000)throw new ArgumentException("The geographic region catalogue exceeds 32 MB.");
  using var document=JsonDocument.Parse(json,new(){MaxDepth=24});var root=document.RootElement;
  if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("features",out var features)||features.ValueKind!=JsonValueKind.Array||features.GetArrayLength()>10000)throw new ArgumentException("Invalid geographic region catalogue.");
  var regions=new List<GeographicRegion>();var ids=new HashSet<string>();var points=0;
  foreach(var feature in features.EnumerateArray()){
   var props=feature.GetProperty("properties");var id=props.GetProperty("id").GetString()??"";var name=props.GetProperty("name").GetString()??"";var parent=props.TryGetProperty("parent",out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
   if(!ValidId(id)||!ids.Add(id)||name.Length is <1 or >200)throw new ArgumentException("Invalid geographic region identity.");
   if(!props.TryGetProperty("urls",out var urls)||!urls.TryGetProperty("pbf",out var link)||link.ValueKind!=JsonValueKind.String)continue;
   var url=link.GetString()!;ValidateUrl(url);var geometry=feature.GetProperty("geometry");var type=geometry.GetProperty("type").GetString();var coordinates=geometry.GetProperty("coordinates");
   List<GeographicCoordinate> Ring(JsonElement source){if(source.ValueKind!=JsonValueKind.Array||source.GetArrayLength()<4)throw new ArgumentException("Invalid region boundary.");var result=new List<GeographicCoordinate>();foreach(var value in source.EnumerateArray()){if(++points>2_000_000||value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()<2||!value[0].TryGetDouble(out var x)||!value[1].TryGetDouble(out var y)||!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -90 or >90)throw new ArgumentException("Invalid region coordinates.");result.Add(new(x,y));}if(result[0]!=result[^1])throw new ArgumentException("An extract boundary is not closed.");return result;}
   List<List<GeographicCoordinate>> Polygon(JsonElement source){if(source.ValueKind!=JsonValueKind.Array||source.GetArrayLength() is <1 or >10000)throw new ArgumentException("Invalid region polygon.");return source.EnumerateArray().Select(Ring).ToList();}
   if(coordinates.ValueKind!=JsonValueKind.Array||coordinates.GetArrayLength() is <1 or >10000)throw new ArgumentException("Invalid region geometry.");
   var polygons=type switch{"Polygon"=>new List<List<List<GeographicCoordinate>>>{Polygon(coordinates)},"MultiPolygon"=>coordinates.EnumerateArray().Select(Polygon).ToList(),_=>throw new ArgumentException("Unsupported region boundary geometry.")};
   regions.Add(new(id,name,parent,url,polygons));
  }
  if(regions.Count==0)throw new ArgumentException("The geographic region catalogue is empty.");Regions=regions;
  }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){throw new ArgumentException("Invalid geographic region catalogue structure.",nameof(json),e);}
 }
 public static bool ValidId(string id)=>id.Length is >=1 and <=100&&id.Split('/').All(segment=>segment.Length>0&&segment.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'));
 public static Uri ValidateUrl(string url){if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="download.geofabrik.de"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||!uri.AbsolutePath.EndsWith("-latest.osm.pbf",StringComparison.Ordinal)||uri.AbsolutePath.Contains('%')||uri.AbsolutePath.Contains("..",StringComparison.Ordinal))throw new ArgumentException("The extract URL must be an official public Geofabrik PBF download.");return uri;}
 internal static Uri ValidateRedirect(Uri original,Uri destination)
 {
  ValidateUrl(original.AbsoluteUri);var prefix=original.AbsolutePath[..^"-latest.osm.pbf".Length]+"-";const string suffix=".osm.pbf";var path=destination.AbsolutePath;
  if(destination.Scheme!=original.Scheme||destination.Host!=original.Host||destination.Port!=original.Port||destination.UserInfo.Length>0||destination.Query.Length>0||destination.Fragment.Length>0||!path.StartsWith(prefix,StringComparison.Ordinal)||!path.EndsWith(suffix,StringComparison.Ordinal)||path.Length!=prefix.Length+6+suffix.Length||!path.AsSpan(prefix.Length,6).ToString().All(char.IsAsciiDigit))throw new ArgumentException("The extract redirect must name a dated snapshot of the selected region on the official origin.");
  return destination;
 }
 public List<GeographicRegion> Select(GeoBounds bounds)
 {
  WorldLayoutLimits.ValidateSourceBounds(bounds);var candidates=Regions.Where(r=>Intersects(r,bounds)).OrderBy(r=>r.Area).ThenBy(r=>r.Id,StringComparer.Ordinal).ToList();
  // Prefer complete, most detailed coverage. Ancestors are unnecessary when their children cover the area.
  var chosen=new List<GeographicRegion>();foreach(var candidate in candidates){
   if(chosen.Any(r=>Ancestor(r,candidate)))continue;
   chosen.RemoveAll(r=>Ancestor(candidate,r));chosen.Add(candidate);
   if(chosen.Count<=16&&Covers(chosen,bounds))return chosen;
  }
  throw new ArgumentException("No bounded set of regional extracts covers the complete selected area. Choose a smaller area or import a prepared geographic file.");
  bool Ancestor(GeographicRegion ancestor,GeographicRegion child){var visited=new HashSet<string>();var parent=child.Parent;while(parent is not null&&visited.Add(parent)){if(parent==ancestor.Id)return true;parent=Regions.FirstOrDefault(r=>r.Id==parent)?.Parent;}return false;}
 }
 private static bool InsideRing(double x,double y,List<GeographicCoordinate> ring){var inside=false;for(int i=0,j=ring.Count-1;i<ring.Count;j=i++){var a=ring[i];var b=ring[j];if((a.Latitude>y)!=(b.Latitude>y)&&x<(b.Longitude-a.Longitude)*(y-a.Latitude)/(b.Latitude-a.Latitude)+a.Longitude)inside=!inside;}return inside;}
 private static bool Inside(GeographicRegion region,double x,double y)=>region.Polygons.Any(p=>InsideRing(x,y,p[0])&&!p.Skip(1).Any(r=>InsideRing(x,y,r)));
 private static bool Intersects(GeographicRegion region,GeoBounds b){
  if(Inside(region,(b.West+b.East)/2,(b.South+b.North)/2)||Inside(region,b.West,b.South)||Inside(region,b.East,b.North)||Inside(region,b.West,b.North)||Inside(region,b.East,b.South))return true;
  return region.Polygons.SelectMany(p=>p).Any(r=>r.Any(p=>p.Longitude>=b.West&&p.Longitude<=b.East&&p.Latitude>=b.South&&p.Latitude<=b.North)||r.Zip(r.Skip(1)).Any(e=>SegmentBox(e.First,e.Second,b)));
 }
 private static bool SegmentBox(GeographicCoordinate a,GeographicCoordinate b,GeoBounds box){var dx=b.Longitude-a.Longitude;var dy=b.Latitude-a.Latitude;double start=0,end=1;bool Clip(double direction,double distance){if(direction==0)return distance>=0;var value=distance/direction;if(direction<0){if(value>end)return false;start=Math.Max(start,value);}else{if(value<start)return false;end=Math.Min(end,value);}return true;}return Clip(-dx,a.Longitude-box.West)&&Clip(dx,box.East-a.Longitude)&&Clip(-dy,a.Latitude-box.South)&&Clip(dy,box.North-a.Latitude);}
 private readonly record struct Edge(GeographicCoordinate A,GeographicCoordinate B);
 public static bool Covers(IEnumerable<GeographicRegion> regions,GeoBounds bounds)
 {
  // Sweep all topology changes inside the requested rectangle. Testing corners alone misses holes and concave borders.
  var list=regions.ToList();var edges=list.SelectMany(r=>r.Polygons).SelectMany(p=>p).SelectMany(r=>r.Zip(r.Skip(1)).Select(e=>new Edge(e.First,e.Second))).Where(e=>SegmentBox(e.A,e.B,bounds)).ToList();
  if(edges.Count>10000)throw new ArgumentException("Region boundaries are too complex for this area.");
  var levels=new SortedSet<double>{bounds.South,bounds.North};void Level(double y){if(y>bounds.South&&y<bounds.North)levels.Add(y);}
  foreach(var edge in edges){Level(edge.A.Latitude);Level(edge.B.Latitude);var dx=edge.B.Longitude-edge.A.Longitude;if(dx!=0)foreach(var x in new[]{bounds.West,bounds.East}){var t=(x-edge.A.Longitude)/dx;if(t>0&&t<1)Level(edge.A.Latitude+t*(edge.B.Latitude-edge.A.Latitude));}}
  var relevant=edges.Where(e=>SegmentBox(e.A,e.B,bounds)).ToList();if((long)relevant.Count*relevant.Count>4_000_000)throw new ArgumentException("Region overlap geometry exceeds the planning budget.");
  for(var i=0;i<relevant.Count;i++)for(var j=i+1;j<relevant.Count;j++){var a=relevant[i];var b=relevant[j];var ax=a.B.Longitude-a.A.Longitude;var ay=a.B.Latitude-a.A.Latitude;var bx=b.B.Longitude-b.A.Longitude;var by=b.B.Latitude-b.A.Latitude;var cross=ax*by-ay*bx;if(Math.Abs(cross)<1e-20)continue;var cx=b.A.Longitude-a.A.Longitude;var cy=b.A.Latitude-a.A.Latitude;var t=(cx*by-cy*bx)/cross;var u=(cx*ay-cy*ax)/cross;if(t>0&&t<1&&u>0&&u<1)Level(a.A.Latitude+t*ay);}
  var ys=levels.ToArray();for(var i=1;i<ys.Length;i++){
   var y=(ys[i-1]+ys[i])/2;var xs=new SortedSet<double>{bounds.West,bounds.East};foreach(var edge in edges){if((edge.A.Latitude>y)==(edge.B.Latitude>y))continue;var x=edge.A.Longitude+(y-edge.A.Latitude)*(edge.B.Longitude-edge.A.Longitude)/(edge.B.Latitude-edge.A.Latitude);if(x>bounds.West&&x<bounds.East)xs.Add(x);}
   var cuts=xs.ToArray();for(var x=1;x<cuts.Length;x++)if(!list.Any(r=>Inside(r,(cuts[x-1]+cuts[x])/2,y)))return false;
  }return true;
 }
}
