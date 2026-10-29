namespace SimplePZMapmaker.Web.Services;
public static class OsmWorldImportEndpoints
{
 public static IEndpointRouteBuilder MapOsmWorldImportEndpoints(this IEndpointRouteBuilder app){
  app.MapGet("/api/world/osm/limits",()=>new{
   normalSectionLimit=SimplePZMapmaker.Core.WorldLayoutLimits.NormalSections,normalCellLimit=SimplePZMapmaker.Core.WorldLayoutLimits.NormalImportCells,
   maxSections=SimplePZMapmaker.Core.WorldLayoutLimits.MaxSections,maxCells=SimplePZMapmaker.Core.WorldLayoutLimits.MaxWorldCells,
   assemblyMode="sections",maxResidentCells=SimplePZMapmaker.Core.WorldLayoutLimits.MaxResidentCells,sourceBudgetBytes=64_000_000,
   terrainBytesPerCell=SimplePZMapmaker.Core.WorldLayoutLimits.TerrainBytesPerCell,baseRasterBytesPerCell=SimplePZMapmaker.Core.WorldLayoutLimits.BaseRasterBytesPerCell,
   baseRasterMemoryEstimateFormula="cells * (256 * 256 + 32 * 32 * 4)",
   aggregateBytesExplanation="Aggregate base raster bytes estimate disk/work across all sections, not peak RAM or serialized file size.",
   residentRasterEstimateFormula="min(cells, maxResidentCells) * baseRasterBytesPerCell",
   memoryEstimateNote="Sections are composed sequentially and written incrementally. Assembly retains the current section; the seam pass retains a bounded window of neighbor sections. Raster estimates exclude source geometry, building metadata, other object graphs, temporary copies and serialization. These need separate memory; the selected-source budget remains 64 MB. Portable JSON and per-section limits still apply."
  });
  app.MapPost("/api/world/osm/alignment",async(OsmWorldImportRequest request,OsmClient client,CancellationToken token)=>await OsmStreetGrid.AnalyzeAsync(request,client,token));
  app.MapPost("/api/world/osm/plan",(OsmWorldImportRequest request)=>OsmWorldImportService.Plan(request));app.MapPost("/api/world/osm/jobs",(OsmWorldImportRequest request,OsmWorldImportService service)=>service.Start(request));app.MapGet("/api/world/osm/jobs/{id}",(string id,OsmWorldImportService service)=>service.Status(id));app.MapPost("/api/world/osm/jobs/{id}/cancel",(string id,OsmWorldImportService service)=>service.Cancel(id));app.MapGet("/api/world/osm/jobs/{id}/result",(string id,OsmWorldImportService service)=>Results.File(service.ResultFile(id),"application/json",service.ResultFile(id).EndsWith(".pzworld.index.json",StringComparison.Ordinal)?"OSM.pzworld.index.json":"OSM.pzworld.json"));return app;}
}
