namespace SimplePZMapmaker.Web.Services;
public static class OsmWorldImportEndpoints
{
 public static IEndpointRouteBuilder MapOsmWorldImportEndpoints(this IEndpointRouteBuilder app){app.MapPost("/api/world/osm/plan",(OsmWorldImportRequest request)=>OsmWorldImportService.Plan(request));app.MapPost("/api/world/osm/jobs",(OsmWorldImportRequest request,OsmWorldImportService service)=>service.Start(request));app.MapGet("/api/world/osm/jobs/{id}",(string id,OsmWorldImportService service)=>service.Status(id));app.MapPost("/api/world/osm/jobs/{id}/cancel",(string id,OsmWorldImportService service)=>service.Cancel(id));app.MapGet("/api/world/osm/jobs/{id}/result",(string id,OsmWorldImportService service)=>Results.File(service.ResultFile(id),"application/json","OSM.pzworld.json"));return app;}
}
