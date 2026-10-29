using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record OsmSourceUpload(string Name,GeoBounds Bounds,string? Json=null,string? PbfBase64=null);
public sealed record OsmSavedSource(string Id,GeoBounds Bounds,string Source,string? DataTimestamp,int Features,string Format,int Bytes,DateTimeOffset RetrievedAtUtc,List<string> Warnings);

// Saved sources are inert geographic JSON. User supplied names never become file paths.
public sealed class OsmSourceFiles(string directory)
{
 public const int MaximumJsonBytes=64_000_000;
 public const int MaximumPbfBytes=64*1024*1024;
 private const long MaximumStoredBytes=256_000_000;
 private readonly SemaphoreSlim writes=new(1,1);
 private static readonly JsonSerializerOptions options=new(){PropertyNameCaseInsensitive=true};
 public static bool SafeId(string? id)=>id is {Length:32}&&id.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 private string DataFile(string id)=>Path.Combine(directory,id+".data.json");
 private string MetadataFile(string id)=>Path.Combine(directory,id+".source.json");
 private static void CheckId(string id){if(!SafeId(id))throw new ArgumentException("Invalid geographic source identifier.");}
 public async Task<OsmSavedSource> ImportAsync(OsmSourceUpload upload,CancellationToken token=default)
 {
  if(upload is null||string.IsNullOrWhiteSpace(upload.Name)||upload.Name.Length>200||upload.Name.Any(char.IsControl))throw new ArgumentException("Choose a geographic source file.");
  WorldLayoutLimits.ValidateSourceBounds(upload.Bounds);
  if((upload.Json is null)==(upload.PbfBase64 is null))throw new ArgumentException("Choose one GeoJSON, Overpass JSON or regional .osm.pbf file.");
  var warnings=new List<string>{"Local source: verify that the file covers the selected area. No OSM server was contacted."};
  string data;string? date=null;
  if(upload.PbfBase64 is {} encoded){
   if(encoded.Length>4*((MaximumPbfBytes+2)/3))throw new ArgumentException("The local PBF file exceeds 64 MiB. Use a smaller regional extract.");
   byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException){throw new ArgumentException("Could not read the local PBF file.");}
   if(bytes.Length>MaximumPbfBytes)throw new ArgumentException("The local PBF file exceeds 64 MiB.");
   token.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".pbf.tmp");
   try{await File.WriteAllBytesAsync(temporary,bytes,token);var result=PbfExtractReader.Read(temporary,upload.Bounds,token);data=result.Data;date=result.Timestamp;
    if(result.OmittedRelations>0||result.OmittedWays>0)warnings.Add($"PBF omitted incomplete geometry: {result.OmittedRelations} relations, {result.OmittedWays} ways (including outside the selected area).");
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }else data=upload.Json!;
  token.ThrowIfCancellationRequested();if(data.Length>MaximumJsonBytes||Encoding.UTF8.GetByteCount(data)>MaximumJsonBytes)throw new ArgumentException("The geographic source exceeds 64 MB.");
  using(var document=JsonDocument.Parse(data,new(){MaxDepth=32})){var root=document.RootElement;if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String&&type.GetString()=="Feature")data=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{root}});}
  var info=Inspect(data,token);if(info.Count==0)throw new ArgumentException("The geographic file contains no supported features. The current map was preserved.");
  var name=upload.Name.Replace('\\','/').Split('/')[^1];return await SaveAsync(new(data,"local file: "+name,warnings){Requests=0,DataTimestamp=date??info.Date},upload.Bounds,token);
 }
 public async Task<OsmSavedSource> SaveAsync(OsmDownload download,GeoBounds bounds,CancellationToken token=default)
 {
  WorldLayoutLimits.ValidateSourceBounds(bounds);var info=Inspect(download.Data,token);
  var id=Identity(bounds,download.Source,download.Data);
  var saved=new OsmSavedSource(id,new(){North=bounds.North,South=bounds.South,West=bounds.West,East=bounds.East},download.Source,download.DataTimestamp??info.Date,info.Count,info.Format,Encoding.UTF8.GetByteCount(download.Data),download.RetrievedAtUtc,download.Warnings.Take(20).ToList());
  await writes.WaitAsync(token);try{
   Directory.CreateDirectory(directory);var meta=MetadataFile(id);var file=DataFile(id);
   var existed=File.Exists(file);try{
   if(!existed){
    var prior=List().OrderBy(s=>s.RetrievedAtUtc).ToList();long total=prior.Sum(s=>(long)s.Bytes);while(prior.Count>=32||total+saved.Bytes>MaximumStoredBytes){var old=prior[0];prior.RemoveAt(0);total-=old.Bytes;File.Delete(DataFile(old.Id));File.Delete(MetadataFile(old.Id));}
    await AtomicWrite(file,download.Data,token);
   }
   else await AtomicWrite(file,download.Data,token);
   await AtomicWrite(meta,JsonSerializer.Serialize(saved),token);return saved;
   }catch{if(!existed&&File.Exists(file))File.Delete(file);throw;}
  }finally{writes.Release();}
 }
 private static async Task AtomicWrite(string file,string data,CancellationToken token){var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,data,new UTF8Encoding(false),token);token.ThrowIfCancellationRequested();File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 public OsmSavedSource Describe(string id)
 {
  CheckId(id);var meta=MetadataFile(id);if(!File.Exists(meta)||new FileInfo(meta).Length>65536)throw new ArgumentException("The saved source is missing or has expired. Choose the file again.");
  try{var saved=JsonSerializer.Deserialize<OsmSavedSource>(File.ReadAllText(meta,Encoding.UTF8),options);if(saved is null||saved.Id!=id||saved.Bytes is <1 or >MaximumJsonBytes||saved.Bounds is null||saved.Warnings is null||saved.Source is null||saved.Source.Length>300)throw new ArgumentException("Invalid saved source metadata.");WorldLayoutLimits.ValidateSourceBounds(saved.Bounds);return saved;}catch(JsonException){throw new ArgumentException("Invalid saved source metadata.");}
 }
 public string DownloadFile(string id){var saved=Describe(id);var file=DataFile(id);if(!File.Exists(file)||new FileInfo(file).Length>MaximumJsonBytes+3||new FileInfo(file).Length<saved.Bytes)throw new ArgumentException("The saved source is missing or incomplete. Choose the file again.");return file;}
 public async Task<OsmDownload> ReadAsync(string id,GeoBounds bounds,int maxBytes,CancellationToken token=default)
 {
  WorldLayoutLimits.ValidateSourceBounds(bounds);var saved=Describe(id);if(!Contains(saved.Bounds,bounds))throw new ArgumentException("The saved source was prepared for a different area. Choose the file again for the new bounds.");
  if(saved.Bytes>maxBytes)throw new ArgumentException($"The saved source exceeds this import's {maxBytes/1_000_000} MB limit. Use smaller sections or a smaller source file.");
  var data=await File.ReadAllTextAsync(DownloadFile(id),Encoding.UTF8,token);var info=Inspect(data,token,maxBytes);if(Encoding.UTF8.GetByteCount(data)!=saved.Bytes||info.Count!=saved.Features||Identity(saved.Bounds,saved.Source,data)!=id)throw new ArgumentException("The saved source is incomplete. Choose the file again.");
  return new(data,saved.Source,[..saved.Warnings,"Used a saved geographic source; no OSM download was needed."]){SourceId=id,Requests=0,FromCache=true,RetrievedAtUtc=saved.RetrievedAtUtc,DataTimestamp=saved.DataTimestamp,Sources=[new(saved.Source,saved.DataTimestamp,saved.RetrievedAtUtc,true)]};
 }
 public async Task<OsmDownload?> LastDownloadedAsync(GeoBounds bounds,int maxBytes,CancellationToken token)
 {
  foreach(var saved in List().Where(s=>!s.Source.StartsWith("local file:",StringComparison.Ordinal)&&Contains(s.Bounds,bounds)&&s.Bytes<=maxBytes).OrderByDescending(s=>s.RetrievedAtUtc)){
   token.ThrowIfCancellationRequested();try{return await ReadAsync(saved.Id,bounds,maxBytes,token);}catch(Exception e) when(e is ArgumentException or IOException or JsonException){/* A damaged snapshot cannot replace valid geographic data. */}
  }return null;
 }
 private List<OsmSavedSource> List(){var result=new List<OsmSavedSource>();if(!Directory.Exists(directory))return result;foreach(var file in Directory.EnumerateFiles(directory,"*.source.json")){var id=Path.GetFileName(file).Split('.')[0];if(!SafeId(id))continue;try{var saved=Describe(id);if(File.Exists(DataFile(id)))result.Add(saved);}catch(Exception e) when(e is ArgumentException or IOException or JsonException){}}return result;}
 private static string Identity(GeoBounds bounds,string source,string data)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bounds)+"\n"+source+"\n"+data)))[..32];
 private static bool Contains(GeoBounds outer,GeoBounds inner)=>inner.West>=outer.West-1e-9&&inner.East<=outer.East+1e-9&&inner.South>=outer.South-1e-9&&inner.North<=outer.North+1e-9;
 private static (string Format,int Count,string? Date) Inspect(string data,CancellationToken token,int maxBytes=MaximumJsonBytes)
 {
  var budget=GeographicGeometryBudget.ForBytes(maxBytes);
  token.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(data)||data.Length>MaximumJsonBytes||Encoding.UTF8.GetByteCount(data)>MaximumJsonBytes)throw new ArgumentException("The geographic source is empty or exceeds 64 MB.");
  using var document=JsonDocument.Parse(data,new(){MaxDepth=32});var root=document.RootElement;if(root.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Choose GeoJSON FeatureCollection or Overpass JSON with complete geometry.");
  if(root.TryGetProperty("remark",out var remark)&&remark.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(remark.GetString()))throw new ArgumentException("The saved Overpass response is incomplete. Download a complete source file.");
  string format;JsonElement items;
  if(root.TryGetProperty("elements",out items)&&items.ValueKind==JsonValueKind.Array)format="overpass";
  else if(root.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String&&type.GetString()=="FeatureCollection"&&root.TryGetProperty("features",out items)&&items.ValueKind==JsonValueKind.Array)format="geojson";
  else throw new ArgumentException("Choose GeoJSON FeatureCollection or Overpass JSON with complete geometry.");
  if(items.GetArrayLength()>budget.Features)throw new ArgumentException($"The geographic source exceeds {budget.Features:N0} features.");
  var points=0;var work=0;void Walk(JsonElement value){if((++work&1023)==0)token.ThrowIfCancellationRequested();if(work>budget.Work*3)throw new ArgumentException("The geographic source is too complex.");if(value.ValueKind==JsonValueKind.Object){if(value.TryGetProperty("lon",out var lon)&&value.TryGetProperty("lat",out var lat)){if(!lon.TryGetDouble(out var x)||!lat.TryGetDouble(out var y)||!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -85 or >85)throw new ArgumentException("Invalid source coordinates.");if(++points>budget.Points)throw new ArgumentException($"The geographic source exceeds {budget.Points:N0} points.");}foreach(var property in value.EnumerateObject())Walk(property.Value);}else if(value.ValueKind==JsonValueKind.Array){if(format=="geojson"&&value.GetArrayLength()>=2&&value[0].ValueKind==JsonValueKind.Number&&value[1].ValueKind==JsonValueKind.Number){if(!value[0].TryGetDouble(out var x)||!value[1].TryGetDouble(out var y)||!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -85 or >85)throw new ArgumentException("Invalid source coordinates.");if(++points>budget.Points)throw new ArgumentException($"The geographic source exceeds {budget.Points:N0} points.");}else foreach(var child in value.EnumerateArray())Walk(child);}}
  if(format=="geojson"){foreach(var feature in items.EnumerateArray()){token.ThrowIfCancellationRequested();if(feature.ValueKind!=JsonValueKind.Object||!feature.TryGetProperty("geometry",out var geometry)||geometry.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Invalid GeoJSON feature geometry.");Walk(geometry);}}else Walk(items);string? date=null;if(root.TryGetProperty("osm3s",out var meta)&&meta.ValueKind==JsonValueKind.Object&&meta.TryGetProperty("timestamp_osm_base",out var stamp)&&stamp.ValueKind==JsonValueKind.String)date=stamp.GetString();else if(root.TryGetProperty("timestamp",out stamp)&&stamp.ValueKind==JsonValueKind.String)date=stamp.GetString();
  return(format,items.GetArrayLength(),date is {Length:<=100}?date:null);
 }
}
