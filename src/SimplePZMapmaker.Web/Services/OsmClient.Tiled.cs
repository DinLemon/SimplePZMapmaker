using System.Text.Json;
using System.Text;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmProgress(string Phase,int Completed,int Total,string? Source=null,string? DataTimestamp=null,string? Warning=null);
public sealed partial class OsmClient
{
 public async Task<OsmDownload> FetchTiledDetailed(GeoBounds bounds,CancellationToken token,IProgress<OsmProgress>? progress=null,int maxBytes=16_000_000)
 {
  ProjectValidator.ValidateBounds(bounds);if(maxBytes is <1 or >64_000_000)throw new ArgumentException("Geographic data limit: 64 MB.");
  var rows=Math.Max(1,(int)Math.Ceiling((bounds.North-bounds.South)/.01));var columns=Math.Max(1,(int)Math.Ceiling((bounds.East-bounds.West)/.02));var total=rows*columns;if(total>64)throw new ArgumentException("Too many geographic requests: reduce the selected area.");
  var geometry=new OsmGeometrySet(maxBytes);var warnings=new HashSet<string>();var sources=new HashSet<string>();var metadata=new List<OsmSource>();var dates=new HashSet<string>();var requests=0;var allCached=true;var retrieved=DateTimeOffset.MinValue;var completed=0;
  foreach(var y in Enumerable.Range(0,rows))foreach(var x in Enumerable.Range(0,columns)){
   token.ThrowIfCancellationRequested();var tile=new GeoBounds{West=bounds.West+(bounds.East-bounds.West)*x/columns,East=bounds.West+(bounds.East-bounds.West)*(x+1)/columns,South=bounds.South+(bounds.North-bounds.South)*y/rows,North=bounds.South+(bounds.North-bounds.South)*(y+1)/rows};
   var report=new InlineProgress<OsmProgress>(attempt=>progress?.Report(attempt with{Completed=completed,Total=total}));var data=await FetchSingleDetailed(tile,token,report);geometry.Add(data.Data);sources.Add(data.Source);metadata.AddRange(data.Sources);if(data.DataTimestamp is {} date)dates.Add(date);foreach(var warning in data.Warnings)warnings.Add(warning);requests+=data.Requests;allCached&=data.FromCache;retrieved=data.RetrievedAtUtc>retrieved?data.RetrievedAtUtc:retrieved;progress?.Report(new("downloaded",++completed,total,data.Source,data.DataTimestamp));
  }
  if(dates.Count>1)warnings.Add("Sections were retrieved from OSM snapshots with different dates; source dates were preserved in the report.");warnings.Add($"Geographic data loaded in {total} small areas; duplicate features merged by type/id, and way geometry retained beyond query boundaries.");
  var timestamp=dates.Count==1?dates.Single():null;return new(geometry.Serialize(timestamp),string.Join(", ",sources),warnings.Take(20).ToList()){Requests=requests,FromCache=allCached,RetrievedAtUtc=retrieved,DataTimestamp=timestamp,Sources=metadata};
 }
 private static string? Timestamp(string json){using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("osm3s",out var meta)&&meta.TryGetProperty("timestamp_osm_base",out var date)&&date.ValueKind==JsonValueKind.String&&date.GetString() is {Length:<=100} value?value:null;}
 private sealed record CacheSource(string Host,string Hash,DateTimeOffset RetrievedAtUtc);
 private static string DataHash(string data)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(data)));
 private static (string Host,DateTimeOffset Retrieved) CacheMetadata(string file,string data){var meta=file+".source.json";if(File.Exists(meta)&&new FileInfo(meta).Length<4096){try{var saved=JsonSerializer.Deserialize<CacheSource>(File.ReadAllText(meta));if(saved is not null&&saved.Hash==DataHash(data)&&saved.Host.Length<=200&&Uri.CheckHostName(saved.Host)!=UriHostNameType.Unknown)return(saved.Host,saved.RetrievedAtUtc);}catch(Exception e) when(e is JsonException or IOException){}}return("local-cache (original server not recorded)",File.GetLastWriteTimeUtc(file));}
}
internal sealed class InlineProgress<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
public sealed class OsmGeometrySet(int maxBytes)
{
 private readonly Dictionary<(string Type,long Id),JsonElement> elements=[];private long bytes;
 public void Add(string json){using var document=JsonDocument.Parse(json);foreach(var element in document.RootElement.GetProperty("elements").EnumerateArray()){
   if(!element.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||!element.TryGetProperty("id",out var id)||!id.TryGetInt64(out var number))throw new ArgumentException("The OSM feature has no type/id.");var key=(type.GetString()!,number);var length=Encoding.UTF8.GetByteCount(element.GetRawText());
   if(elements.TryGetValue(key,out var previous)){if(Points(element)<=Points(previous))continue;bytes-=Encoding.UTF8.GetByteCount(previous.GetRawText());}else if(elements.Count>=30000)throw new ArgumentException("Too many OSM features: the limit is 30,000.");bytes+=length;if(bytes>maxBytes-2000)throw new ArgumentException("World geographic data is too large. Reduce the area or import multiple worlds.");elements[key]=element.Clone();
  }}
 public string Serialize(string? date=null)=>JsonSerializer.Serialize(new{osm3s=new{timestamp_osm_base=date},elements=elements.Values.ToArray()});
 public string ForSection(GeoBounds bounds){return JsonSerializer.Serialize(new{elements=elements.Values.Where(e=>Intersects(e,bounds)).ToArray()});}
 private static int Points(JsonElement e){var n=e.TryGetProperty("lon",out _)?1:0;n+=e.TryGetProperty("geometry",out var g)&&g.ValueKind==JsonValueKind.Array?g.GetArrayLength():0;if(e.TryGetProperty("members",out var members)&&members.ValueKind==JsonValueKind.Array)foreach(var member in members.EnumerateArray())n+=Points(member);return n;}
 private static bool Intersects(JsonElement e,GeoBounds b){var minX=double.PositiveInfinity;var minY=double.PositiveInfinity;var maxX=double.NegativeInfinity;var maxY=double.NegativeInfinity;void Walk(JsonElement item){if(item.TryGetProperty("lon",out var nl)&&nl.TryGetDouble(out var nx)&&item.TryGetProperty("lat",out var nt)&&nt.TryGetDouble(out var ny)){minX=Math.Min(minX,nx);maxX=Math.Max(maxX,nx);minY=Math.Min(minY,ny);maxY=Math.Max(maxY,ny);}if(item.TryGetProperty("geometry",out var geometry)&&geometry.ValueKind==JsonValueKind.Array)foreach(var p in geometry.EnumerateArray())if(p.TryGetProperty("lon",out var lon)&&lon.TryGetDouble(out var x)&&p.TryGetProperty("lat",out var lat)&&lat.TryGetDouble(out var y)){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}if(item.TryGetProperty("members",out var members)&&members.ValueKind==JsonValueKind.Array)foreach(var member in members.EnumerateArray())Walk(member);}Walk(e);return minX<=b.East&&maxX>=b.West&&minY<=b.North&&maxY>=b.South;}
}
