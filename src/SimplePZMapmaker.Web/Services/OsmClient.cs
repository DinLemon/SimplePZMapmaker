using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed class OsmClient(string cacheDirectory) : IDisposable
{
    private readonly HttpClient client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(100)};
    private readonly SemaphoreSlim gate=new(1,1);
    private DateTimeOffset lastRequest=DateTimeOffset.MinValue;
    public async Task<string> Fetch(GeoBounds b,CancellationToken token) {
        ProjectValidator.ValidateBounds(b);
        var box=FormattableString.Invariant($"{b.South:F6},{b.West:F6},{b.North:F6},{b.East:F6}");
        var query=$"[out:json][timeout:65];(way[highway]({box});way[building]({box});way[natural]({box});way[landuse]({box});way[waterway]({box});relation[natural=water]({box}););out geom;";
        return await Request("osm-"+box,()=>client.PostAsync("https://overpass-api.de/api/interpreter",new FormUrlEncodedContent(new Dictionary<string,string>{{"data",query}}),token),token);
    }
    public async Task<JsonElement> Search(string query,CancellationToken token) {
        if(string.IsNullOrWhiteSpace(query)||query.Length>180)throw new ArgumentException("Введите название города или адрес, до 180 символов.");
        var data=await Request("search-"+query,()=>client.GetAsync("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q="+Uri.EscapeDataString(query),HttpCompletionOption.ResponseHeadersRead,token),token);
        using var doc=JsonDocument.Parse(data);return doc.RootElement.Clone();
    }
    private async Task<string> Request(string key,Func<Task<HttpResponseMessage>> send,CancellationToken token) {
        Directory.CreateDirectory(cacheDirectory);var file=Path.Combine(cacheDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json");
        await gate.WaitAsync(token);
        try {
            if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))return await File.ReadAllTextAsync(file,token);
            var wait=TimeSpan.FromSeconds(2)-(DateTimeOffset.UtcNow-lastRequest);if(wait>TimeSpan.Zero)await Task.Delay(wait,token);
            if(client.DefaultRequestHeaders.UserAgent.Count==0)client.DefaultRequestHeaders.UserAgent.ParseAdd("SimplePZMapmaker/0.1 (local-map-editor)");
            lastRequest=DateTimeOffset.UtcNow;
            using var response=await send();
            if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Картографический сервис вернул {(int)response.StatusCode}. Повторите позже или импортируйте GeoJSON.");
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var chunk=new byte[8192];
            while(true){var n=await stream.ReadAsync(chunk,token);if(n==0)break;if(buffer.Length+n>16_000_000)throw new ArgumentException("Слишком много геоданных. Уменьшите выбранный участок.");buffer.Write(chunk,0,n);}
            var data=Encoding.UTF8.GetString(buffer.ToArray());using var json=JsonDocument.Parse(data);
            await File.WriteAllTextAsync(file,data,token);return data;
        }finally{gate.Release();}
    }
    public void Dispose(){client.Dispose();gate.Dispose();}
}
