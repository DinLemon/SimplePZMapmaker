using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

public static class WorldEndpoints
{
 public static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder app){
  app.MapPost("/api/world/validate",(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var bounds=Validate(world,assets,models);return Results.Ok(new{valid=true,world,bounds,warnings=WorldAssembler.SeamWarnings(world)});
  });
  app.MapPost("/api/world/move",(WorldMoveRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var world=WorldAssembler.Move(request.World,request.MemberId,request.CellX,request.CellY);var bounds=Validate(world,assets,models);
   return Results.Ok(new{world,bounds,warnings=WorldAssembler.SeamWarnings(world)});
  });
  app.MapPost("/api/world/export",(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   Validate(world,assets,models);return Results.File(NativeWorldExporter.Export(world,token),"application/zip","SimplePZWorld.zip");
  });
  app.MapGet("/api/world/recovery/{id}",async(string id,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   var world=await store.LoadAsync(id,token);if(world is null)return Results.NotFound();Validate(world,assets,models);return Results.Ok(world);
  });
  app.MapPost("/api/world/recovery/{id}",async(string id,WorldProject world,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   Validate(world,assets,models);await store.SaveAsync(id,world,token);return Results.Ok(new{saved=true});
  });
  app.MapPost("/api/world/recovery/{id}/clear",async(string id,WorldStore store,CancellationToken token)=>{await store.ClearAsync(id,token);return Results.Ok(new{cleared=true});});return app;
 }
 private static WorldAssembler.Bounds Validate(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models){var bounds=WorldAssembler.Validate(world);foreach(var member in world.Maps){AssetRules.Project(member.Project,assets);models.Validate(member.Project);}return bounds;}
}
public sealed record WorldMoveRequest(WorldProject World,string MemberId,int CellX,int CellY);
