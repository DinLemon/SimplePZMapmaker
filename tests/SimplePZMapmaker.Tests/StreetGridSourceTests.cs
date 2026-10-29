using System.Net;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class StreetGridSourceTests
{
    public static IEnumerable<(string,Action)> Cases()
    {
        yield return ("street grid source analysis reuses a saved source without network or world mutation", () => {
            var root=Path.Combine(Path.GetTempPath(),"pz-grid-"+Guid.NewGuid().ToString("N"));
            try {
                using var client=new OsmClient(root,new Offline());
                var bounds=new GeoBounds{West=30,East=30.004,South=50,North=50.004};
                var json="{\"elements\":[{\"type\":\"way\",\"id\":1,\"tags\":{\"highway\":\"residential\"},\"geometry\":[{\"lon\":30.0001,\"lat\":50.001},{\"lon\":30.0039,\"lat\":50.001}]}]}";
                var saved=client.Sources.SaveAsync(new(json,"fixture",[]),bounds).GetAwaiter().GetResult();
                var request=new OsmWorldImportRequest(bounds,SectionSize:512,SourceMode:"file",SourceId:saved.Id);
                var before=JsonSerializer.Serialize(request);
                var result=OsmStreetGrid.AnalyzeAsync(request,client).GetAwaiter().GetResult();
                Check(result.SourceId==saved.Id&&result.Alignment.RotationDegrees==0,"Saved source or measured angle differs");
                Check(JsonSerializer.Serialize(request)==before,"Analysis mutated input frame");
                Check(client.Sources.ReadAsync(saved.Id,bounds,64_000_000).GetAwaiter().GetResult().Data==json,"Analysis changed source geometry");
                Reject(()=>OsmStreetGrid.AnalyzeAsync(request with{PreserveEdits=true},client).GetAwaiter().GetResult());
            } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        });
    }
    private sealed class Offline:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("Analysis contacted an online provider despite an explicit saved source");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Locked frame accepted for rotation");}
}
