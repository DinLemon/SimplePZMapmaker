using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record OsmStreetGridResult(StreetGridSuggestion Alignment,string? SourceId,string Source,IReadOnlyList<string> Warnings);

public static class OsmStreetGrid
{
    public static async Task<OsmStreetGridResult> AnalyzeAsync(OsmWorldImportRequest request,OsmClient client,CancellationToken token=default,OsmImportTimeouts? timeouts=null)
    {
        _=OsmWorldImportService.Plan(request);
        if(request.PreserveEdits||request.Warp is not null)throw new ArgumentException("Street-grid alignment requires a new geographic frame without deformation. Keep an existing world's original rotation when preserving edits.");
        var timing=timeouts??new();timing.Validate();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timing.SourceMaximum);
        var download=await client.FetchForImport(request.Bounds,request.SourceMode,request.SourceId,deadline.Token,maxBytes:64_000_000,world:true);
        deadline.CancelAfter(timing.ProcessingMaximum);
        var alignment=GeographicImporter.SuggestStreetGrid(download.Data,request.Bounds,deadline.Token);
        return new(alignment,download.SourceId,download.Source,download.Warnings);
    }
}
