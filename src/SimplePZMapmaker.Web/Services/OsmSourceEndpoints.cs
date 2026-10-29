namespace SimplePZMapmaker.Web.Services;

public static class OsmSourceEndpoints
{
 public static IEndpointRouteBuilder MapOsmSourceEndpoints(this IEndpointRouteBuilder app)
 {
  app.MapPost("/api/osm/regions/plan",async(SimplePZMapmaker.Core.GeoBounds bounds,OsmClient client,CancellationToken token)=>await client.GlobalSources.PlanAsync(bounds,token));
  app.MapPost("/api/osm/sources",async(OsmSourceUpload request,OsmClient client,CancellationToken token)=>await client.Sources.ImportAsync(request,token));
  app.MapPost("/api/world/osm/supplement/overture",async(OsmOvertureRequest request,OsmClient client,CancellationToken token)=>await OsmOvertureSource.PrepareAsync(request,client,token));
  app.MapGet("/api/osm/sources/{id}",(string id,OsmClient client)=>client.Sources.Describe(id));
  app.MapGet("/api/osm/sources/{id}/download",(string id,OsmClient client)=>Results.File(client.Sources.DownloadFile(id),"application/json","geographic-source-"+id+".json"));
  return app;
 }
}
