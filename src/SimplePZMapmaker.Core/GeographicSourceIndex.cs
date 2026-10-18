using System.Text;
using System.Text.Json.Nodes;
namespace SimplePZMapmaker.Core;

/// <summary>A geographic source parsed once and cut per section: each GeoJSON feature or Overpass element keeps its longitude/latitude
/// box, and <see cref="For"/> returns a source of the same kind holding only the items that reach the section bounds plus a margin.
/// Building a world then imports each section from its own neighbourhood instead of walking the whole source every time. Items
/// without a readable box are kept everywhere.</summary>
public sealed class GeographicSourceIndex
{
 readonly string arrayName;readonly JsonObject header;readonly List<(double West,double South,double East,double North,string Json)> items=[];
 public int Count=>items.Count;
 public GeographicSourceIndex(string json){
  var root=JsonNode.Parse(json) as JsonObject??throw new ArgumentException("The geographic source is not a JSON object.");
  arrayName=root["features"] is JsonArray?"features":root["elements"] is JsonArray?"elements":throw new ArgumentException("The geographic source has neither GeoJSON features nor Overpass elements.");
  var list=(JsonArray)root[arrayName]!;foreach(var item in list){if(item is null)continue;var (w,s,e,n)=Box(item);items.Add((w,s,e,n,item.ToJsonString()));}
  root.Remove(arrayName);header=root;
 }
 static (double,double,double,double) Box(JsonNode item){
  double w=double.MaxValue,s=double.MaxValue,e=double.MinValue,n=double.MinValue;void Add(double lon,double lat){w=Math.Min(w,lon);e=Math.Max(e,lon);s=Math.Min(s,lat);n=Math.Max(n,lat);}
  try{
   if(item["bounds"] is JsonObject b){Add((double)b["minlon"]!,(double)b["minlat"]!);Add((double)b["maxlon"]!,(double)b["maxlat"]!);}
   else if(item["lat"] is not null&&item["lon"] is not null)Add((double)item["lon"]!,(double)item["lat"]!);
   else if(item["geometry"] is JsonObject g&&g["coordinates"] is JsonNode c)Walk(c);
   else if(item["geometry"] is JsonArray points)foreach(var p in points)if(p?["lat"] is not null)Add((double)p["lon"]!,(double)p["lat"]!);
  }catch(Exception x) when(x is InvalidOperationException or FormatException or InvalidCastException){return(double.MinValue,double.MinValue,double.MaxValue,double.MaxValue);}
  void Walk(JsonNode node){if(node is JsonArray a){if(a.Count>=2&&a[0] is JsonValue&&a[1] is JsonValue)Add((double)a[0]!,(double)a[1]!);else foreach(var child in a)if(child is not null)Walk(child);}}
  return w==double.MaxValue?(double.MinValue,double.MinValue,double.MaxValue,double.MaxValue):(w,s,e,n);
 }
 /// <summary>The items whose box meets <paramref name="bounds"/> grown by <paramref name="marginMetres"/> on every side.</summary>
 public string For(GeoBounds bounds,double marginMetres=250){
  var dLat=marginMetres/111_320.0;var dLon=marginMetres/(111_320.0*Math.Max(0.01,Math.Cos((bounds.South+bounds.North)/2*Math.PI/180)));
  var west=bounds.West-dLon;var east=bounds.East+dLon;var south=bounds.South-dLat;var north=bounds.North+dLat;
  var text=new StringBuilder(header.ToJsonString()[..^1]);if(header.Count>0)text.Append(',');text.Append('"').Append(arrayName).Append("\":[");var first=true;
  foreach(var i in items)if(i.East>=west&&i.West<=east&&i.North>=south&&i.South<=north){if(!first)text.Append(',');text.Append(i.Json);first=false;}
  return text.Append("]}").ToString();
 }
}
