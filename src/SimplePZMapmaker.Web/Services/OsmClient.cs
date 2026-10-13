using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmSource(string Host,string? DataTimestamp,DateTimeOffset RetrievedAtUtc,bool FromCache);
public sealed record OsmDownload(string Data,string Source,List<string> Warnings){public DateTimeOffset RetrievedAtUtc {get;init;}=DateTimeOffset.UtcNow;public string? DataTimestamp {get;init;}public bool FromCache {get;init;}public int Requests {get;init;}=1;public List<OsmSource> Sources {get;init;}=[];}
public sealed partial class OsmClient(string cacheDirectory,HttpMessageHandler? handler=null,TimeSpan? requestInterval=null,TimeSpan? attemptTimeout=null) : IDisposable
{
 private readonly HttpClient client=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
 private readonly SemaphoreSlim gate=new(1,1);
 private DateTimeOffset lastRequest=DateTimeOffset.MinValue;
 private static readonly string[] providers=["https://overpass-api.de/api/interpreter","https://overpass.private.coffee/api/interpreter","https://maps.mail.ru/osm/tools/overpass/api/interpreter"];
 public const int ExtractTimeoutSeconds=240;
 public static GeoBounds SupportedExtractBounds=>new(){South=54.6,North=54.95,West=31.8,East=32.3};
 public static bool CanUseExtract(GeoBounds? b)=>b is not null&&double.IsFinite(b.South)&&double.IsFinite(b.North)&&double.IsFinite(b.West)&&double.IsFinite(b.East)&&b.South>=54.6&&b.North<=54.95&&b.West>=31.8&&b.East<=32.3&&b.South<b.North&&b.West<b.East&&b.North-b.South<=0.08&&b.East-b.West<=0.12;
 // Explicit read-only regional download, never the OSM editing API. Small-area Overpass remains separate.
 public async Task<OsmDownload> FetchExtractDetailed(GeoBounds b,CancellationToken cancellationToken){
  ProjectValidator.ValidateBounds(b);if(!CanUseExtract(b))throw new ArgumentException("Региональная выгрузка сейчас поддерживает только участок Смоленска (54,6–54,95° N; 31,8–32,3° E). Для другого места используйте Overpass или импорт файла.");
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(ExtractTimeoutSeconds));var token=deadline.Token;await gate.WaitAsync(token);
  const string source="https://download.openstreetmap.fr/extracts/russia/central_federal_district/smolensk_oblast-latest.osm.pbf";const int limit=64*1024*1024;
  try{
   Directory.CreateDirectory(cacheDirectory);var file=Path.Combine(cacheDirectory,"region-smolensk-oblast.pbf");var cached=File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7);
   if(!cached){var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{
    using var request=new HttpRequestMessage(HttpMethod.Get,source);request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.4.1 (local-map-editor)");using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>limit)throw new ArgumentException("Региональная выгрузка превышает лимит 64 МБ.");await using(var input=await response.Content.ReadAsStreamAsync(token)){await using var output=File.Create(temporary);var buffer=new byte[81920];long total=0;while(true){var read=await input.ReadAsync(buffer,token);if(read==0)break;total+=read;if(total>limit)throw new ArgumentException("Региональная выгрузка превышает лимит 64 МБ.");await output.WriteAsync(buffer.AsMemory(0,read),token);}}
    // Validate and clip before promoting an incomplete/malformed download to the stable cache.
    var result=PbfExtractReader.Read(temporary,b,token);token.ThrowIfCancellationRequested();File.Move(temporary,file,true);return ExtractResult(result,false);
   }finally{if(File.Exists(temporary))File.Delete(temporary);}}
   return ExtractResult(PbfExtractReader.Read(file,b,token),true);
  }catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new HttpRequestException("Региональная выгрузка не успела загрузиться за 240 секунд. Текущая карта сохранена; повторите позже или импортируйте сохранённый файл.");}
  catch(HttpRequestException e){throw new HttpRequestException("Источник региональной выгрузки сейчас недоступен. Повторите позже либо импортируйте сохранённый файл. Текущая карта сохранена.",e,e.StatusCode);}
  catch(IOException e){throw new HttpRequestException("Не удалось получить или сохранить региональную выгрузку. Проверьте соединение и свободное место в папке проекта. Текущая карта сохранена.",e);}
  finally{gate.Release();}
  static OsmDownload ExtractResult(PbfExtractReader.Result result,bool cached){var warnings=new List<string>{"Источник — ежедневная региональная выгрузка OSM France, дата данных: "+(result.Timestamp??"не указана")+". "+(cached?"Использован локальный кэш выгрузки.":"Первая загрузка региональной выгрузки сохранена в папке проекта.")};if(result.OmittedRelations>0)warnings.Add($"Пропущено неполных или вложенных контуров отношений в региональном источнике: {result.OmittedRelations} (в том числе вне выбранного участка). Они не выдаются за точные водные/земельные полигоны.");if(result.OmittedWays>0)warnings.Add($"Пропущено объектов с отсутствующими узлами в региональном источнике: {result.OmittedWays} (в том числе вне выбранного участка). Неполная геометрия не используется.");return new(result.Data,"download.openstreetmap.fr",warnings);}
 }
 public async Task<string> Fetch(GeoBounds b,CancellationToken token)=>(await FetchDetailed(b,token)).Data;
 public Task<OsmDownload> FetchDetailed(GeoBounds b,CancellationToken token){
  ProjectValidator.ValidateBounds(b);return b.North-b.South>.01||b.East-b.West>.02?FetchTiledDetailed(b,token):FetchSingleDetailed(b,token);
 }
 private Task<OsmDownload> FetchSingleDetailed(GeoBounds b,CancellationToken token,IProgress<OsmProgress>? progress=null){
  ProjectValidator.ValidateBounds(b);var box=FormattableString.Invariant($"{b.South:F6},{b.West:F6},{b.North:F6},{b.East:F6}");
  var query=$"[out:json][timeout:25];(way[highway]({box});way[building]({box});way[natural]({box});way[landuse]({box});way[waterway]({box});relation[natural]({box});relation[landuse]({box});relation[building]({box}););out geom;";
  return Request("osm-v6-"+box,providers,url=>new(HttpMethod.Post,url){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"data",query}})},true,token,progress);
 }
 public async Task<JsonElement> Search(string query,CancellationToken token){
  if(string.IsNullOrWhiteSpace(query)||query.Length>180)throw new ArgumentException("Введите название города или адрес, до180 символов.");
  var url="https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q="+Uri.EscapeDataString(query);
  var data=await Request("search-"+query,[url],uri=>new(HttpMethod.Get,uri),false,token);using var doc=JsonDocument.Parse(data.Data);return doc.RootElement.Clone();
 }
 private static void ValidateJson(string data,bool overpass){using var json=JsonDocument.Parse(data);if(!overpass)return;var root=json.RootElement;if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("elements",out var elements)||elements.ValueKind!=JsonValueKind.Array)throw new HttpRequestException("Сервер OSM вернул неподходящие данные.");if(root.TryGetProperty("remark",out var remark)&&remark.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(remark.GetString()))throw new HttpRequestException("Сервер OSM вернул неполную карту. Пробуем резервный сервер.");}
 private async Task<string> Read(HttpResponseMessage response,bool overpass,CancellationToken token){
  if(response.Content.Headers.ContentLength>16_000_000)throw new ArgumentException("Слишком много геоданных. Уменьшите выбранный участок.");
  await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var chunk=new byte[8192];while(true){var n=await stream.ReadAsync(chunk,token);if(n==0)break;if(buffer.Length+n>16_000_000)throw new ArgumentException("Слишком много геоданных. Уменьшите выбранный участок.");buffer.Write(chunk,0,n);}var data=Encoding.UTF8.GetString(buffer.ToArray());ValidateJson(data,overpass);return data;
 }
 private async Task<OsmDownload> Request(string key,string[] urls,Func<string,HttpRequestMessage> requestFactory,bool overpass,CancellationToken cancellationToken,IProgress<OsmProgress>? progress=null){
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(overpass?110:40));var token=deadline.Token;
  Directory.CreateDirectory(cacheDirectory);var file=Path.Combine(cacheDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json");
  await gate.WaitAsync(token);
  try{
   if(File.Exists(file)&&new FileInfo(file).Length<=16_000_000&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7)){var cached=await File.ReadAllTextAsync(file,token);try{ValidateJson(cached,overpass);var origin=CacheMetadata(file,cached);var date=Timestamp(cached);return new(cached,origin.Host,overpass?["Использованы ранее загруженные данные OSM из локального кэша."]:[]){FromCache=true,Requests=0,RetrievedAtUtc=origin.Retrieved,DataTimestamp=date,Sources=[new(origin.Host,date,origin.Retrieved,true)]};}catch(Exception e) when(e is JsonException or HttpRequestException){/* Incomplete cache must not masquerade as a complete map. */}}
   for(var i=0;i<urls.Length;i++){
    var wait=(requestInterval??TimeSpan.FromSeconds(2))-(DateTimeOffset.UtcNow-lastRequest);if(wait>TimeSpan.Zero)await Task.Delay(wait,token);
    using var attempt=CancellationTokenSource.CreateLinkedTokenSource(token);attempt.CancelAfter(attemptTimeout??TimeSpan.FromSeconds(35));
    try{
     progress?.Report(new("download",i,urls.Length,new Uri(urls[i]).Host,null,i>0?"Повтор на резервном сервере.":null));using var request=requestFactory(urls[i]);request.Headers.UserAgent.ParseAdd("SimplePZMapmaker/0.6.0 (local-map-editor)");lastRequest=DateTimeOffset.UtcNow;
     using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,attempt.Token);
     if(!response.IsSuccessStatusCode){var transient=response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests||(int)response.StatusCode>=500;var error=new HttpRequestException($"Картографический сервис вернул {(int)response.StatusCode}.",null,response.StatusCode);if(!transient||i==urls.Length-1)throw error;continue;}
     var data=await Read(response,overpass,attempt.Token);var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,data,token);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
     var source=new Uri(urls[i]).Host;var retrieved=DateTimeOffset.UtcNow;var meta=file+".source.json";var tempMeta=meta+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(tempMeta,JsonSerializer.Serialize(new CacheSource(source,DataHash(data),retrieved)),token);File.Move(tempMeta,meta,true);}finally{if(File.Exists(tempMeta))File.Delete(tempMeta);}return new(data,source,i>0?[$"Основной сервер OSM не ответил; данные загружены с резервного сервера {source}."]:[]){Requests=i+1,RetrievedAtUtc=retrieved,DataTimestamp=Timestamp(data),Sources=[new(source,Timestamp(data),retrieved,false)]};
    }catch(OperationCanceledException) when(!token.IsCancellationRequested){if(i==urls.Length-1)break;}
    catch(HttpRequestException e) when(e.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests||(int)e.StatusCode>=500){if(i==urls.Length-1)break;}
    catch(JsonException){if(i==urls.Length-1)break;}
   }
   throw new HttpRequestException("Серверы OSM сейчас недоступны или перегружены. Уменьшите участок либо загрузите сохранённый GeoJSON. Текущая карта сохранена.");
  }catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new HttpRequestException("Серверы OSM не успели загрузить карту. Уменьшите участок и повторите. Текущая карта сохранена.");}
  finally{gate.Release();}
 }
 public void Dispose(){client.Dispose();gate.Dispose();}
}
