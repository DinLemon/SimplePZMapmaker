using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class ExportJobEndpoints
{
 public static IEndpointRouteBuilder MapExportJobEndpoints(this IEndpointRouteBuilder app){
  app.MapPost("/api/export/jobs",(MapProject project,GameAssetCatalog assets,StaticModelCatalog models,ExportJobStore store)=>{AssetRules.Project(project,assets);models.Validate(project);var job=store.StartMap(project);return Results.Accepted($"/api/export/jobs/{job.Id}",job);});
  app.MapPost("/api/world/export/jobs",(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models,ExportJobStore store)=>{WorldEndpoints.Validate(world,assets,models);var job=store.StartWorld(world);return Results.Accepted($"/api/export/jobs/{job.Id}",job);});
  app.MapPost("/api/world/export/jobs/sections/{memberId}",(string memberId,WorldProject world,GameAssetCatalog assets,StaticModelCatalog models,ExportJobStore store)=>{WorldEndpoints.Validate(world,assets,models);var job=store.StartWorld(world,memberId);return Results.Accepted($"/api/export/jobs/{job.Id}",job);});
  app.MapPost("/api/world/workspaces/{id}/export/jobs",async(string id,WorldStore worlds,ExportJobStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var lease=await worlds.PinAsync(id,token);var job=store.StartWorkspace(lease,assets,models);return Results.Accepted($"/api/export/jobs/{job.Id}",job);});
  app.MapPost("/api/world/workspaces/{id}/sections/{memberId}/export/jobs",async(string id,string memberId,WorldStore worlds,ExportJobStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var lease=await worlds.PinAsync(id,token);var job=store.StartWorkspace(lease,assets,models,memberId);return Results.Accepted($"/api/export/jobs/{job.Id}",job);});
  app.MapGet("/api/export/jobs",(ExportJobStore store)=>store.List());app.MapGet("/api/export/jobs/{id}",(string id,ExportJobStore store)=>store.Status(id));app.MapPost("/api/export/jobs/{id}/cancel",(string id,ExportJobStore store)=>store.Cancel(id));app.MapGet("/api/export/jobs/{id}/download",(HttpRequest request,string id,ExportJobStore store,NativeExportService exporter)=>NativeExportService.Options(request) is {} package?exporter.Package(store.OpenArchive(id),"SimplePZMod.zip",package):store.Download(id));return app;
 }
}
