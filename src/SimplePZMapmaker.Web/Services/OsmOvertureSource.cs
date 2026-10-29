using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmOvertureRequest(OsmWorldImportRequest Import,string Json);
public sealed record OsmOvertureResult(OsmSavedSource Source,int Candidates,int Added,int Existing,int TooSmall,int Outside,IReadOnlyList<string> Warnings);

public static class OsmOvertureSource
{
    public static async Task<OsmOvertureResult> PrepareAsync(OsmOvertureRequest request,OsmClient client,CancellationToken token=default,OsmImportTimeouts? timeouts=null)
    {
        if(request is null||request.Import is null||string.IsNullOrWhiteSpace(request.Json))throw new ArgumentException("Choose an Overture buildings GeoJSON file.");
        _=OsmWorldImportService.Plan(request.Import);
        if(request.Import.PreserveEdits)throw new ArgumentException("Add a building supplement while preparing a new source, before refreshing a world with preserved edits.");
        if(request.Json.Length>64_000_000||System.Text.Encoding.UTF8.GetByteCount(request.Json)>64_000_000)throw new ArgumentException("The Overture buildings file exceeds 64 MB.");
        var timing=timeouts??new();timing.Validate();using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timing.SourceMaximum);
        var input=request.Import;
        var baseline=await client.FetchForImport(input.Bounds,input.SourceMode,input.SourceId,deadline.Token,maxBytes:64_000_000,world:true);
        deadline.CancelAfter(timing.ProcessingMaximum);
        var merged=GeographicImporter.MergeOvertureBuildings(baseline.Data,request.Json,input.Bounds,deadline.Token);
        var dates=$"Baseline data date: {baseline.DataTimestamp??"not specified"}. Overture retains its supplied per-feature metadata; no single source date is established for this composition.";
        var saved=await client.Sources.SaveAsync(new(merged.Json,"OSM/GeoJSON baseline + Overture Maps buildings",new[]{dates}.Concat(baseline.Warnings).Concat(merged.Warnings).Distinct().Take(20).ToList()),input.Bounds,deadline.Token);
        return new(saved,merged.Candidates,merged.Added,merged.Existing,merged.TooSmall,merged.Outside,merged.Warnings);
    }
}
