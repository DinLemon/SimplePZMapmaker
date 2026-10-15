using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class WorldInfrastructureEndpoints
{
 public sealed record PutRequest(WorldProject World,LinearFeature Feature);
 public sealed record RemoveRequest(WorldProject World,string Id);
 public static IEndpointRouteBuilder MapWorldInfrastructure(this IEndpointRouteBuilder app){
  app.MapPost("/api/world/linear/put",(PutRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{var world=WorldInfrastructure.Put(request.World,request.Feature);Validate(world,assets,models);return Results.Ok(new{world});});
  app.MapPost("/api/world/linear/remove",(RemoveRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{var world=WorldInfrastructure.Remove(request.World,request.Id);Validate(world,assets,models);return Results.Ok(new{world});});
  app.MapPost("/api/world/linear/preview",(PutRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{var world=WorldInfrastructure.Put(request.World,request.Feature);Validate(world,assets,models);var geometry=WorldInfrastructure.Build(world);var sections=WorldInfrastructure.Rasterize(request.Feature).Select(c=>WorldAssembler.MemberAt(world,c.X,c.Y)!.Id).Distinct().ToArray();return Results.Ok(new{world,tiles=geometry.Tiles.Count,holes=geometry.Holes.Count,sections});});
  return app;
 }
 static void Validate(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models){WorldEndpoints.Validate(world,assets,models);}
}
