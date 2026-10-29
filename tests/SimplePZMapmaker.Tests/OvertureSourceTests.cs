using System.Net;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class OvertureSourceTests
{
    public static IEnumerable<(string,Action)> Cases()
    {
        yield return ("Overture source preparation retains the baseline and saves an offline merged source",()=>{
            var root=Path.Combine(Path.GetTempPath(),"pz-overture-"+Guid.NewGuid().ToString("N"));
            try {
                using var client=new OsmClient(root,new Offline());var bounds=new GeoBounds{West=30,East=30.003,South=50,North=50.002};
                const string baseline="{\"elements\":[{\"type\":\"way\",\"id\":1,\"tags\":{\"highway\":\"residential\"},\"geometry\":[{\"lon\":30.0001,\"lat\":50.001},{\"lon\":30.0029,\"lat\":50.001}]}]}";
                var saved=client.Sources.SaveAsync(new(baseline,"fixture",[]){DataTimestamp="2026-01-02T00:00:00Z"},bounds).GetAwaiter().GetResult();
                var input=new OsmWorldImportRequest(bounds,SectionSize:256,SourceMode:"file",SourceId:saved.Id);
                var extra=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",properties=new{id="overture-fixture",@class="house",num_floors=2},geometry=new{type="Polygon",coordinates=new[]{new[]{new[]{30.0002,50.0002},new[]{30.0004,50.0002},new[]{30.0004,50.0004},new[]{30.0002,50.0004},new[]{30.0002,50.0002}}}}}}});
                var result=OsmOvertureSource.PrepareAsync(new(input,extra),client).GetAwaiter().GetResult();
                Check(result.Added==1&&result.Source.Id!=saved.Id,"Supplement was not composed into its own saved source");
                Check(result.Source.DataTimestamp is null&&result.Source.Warnings.Any(w=>w.Contains("2026-01-02")),"A baseline date was presented as the date of a different combined source");
                var composed=client.Sources.ReadAsync(result.Source.Id,bounds,64_000_000).GetAwaiter().GetResult();
                Check(composed.Data.Contains("residential")&&composed.Data.Contains("Overture"),"Baseline road or supplement attribution disappeared");
                Check(client.Sources.ReadAsync(saved.Id,bounds,64_000_000).GetAwaiter().GetResult().Data==baseline,"Baseline was overwritten");
            } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        });
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class Offline:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("Saved supplement import accessed the network");}
}
