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
  app.MapPost("/api/world/buildings",(WorldBuildingRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var world=WorldAssembler.PutBuilding(request.World,request.Building);Validate(world,assets,models);return Results.Ok(new{world});
  });
  app.MapPost("/api/world/buildings/promote",(WorldPromoteRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var world=WorldAssembler.PromoteBuilding(request.World,request.MemberId,request.BuildingId,request.X,request.Y);Validate(world,assets,models);return Results.Ok(new{world});
  });
  app.MapPost("/api/world/buildings/remove",(WorldRemoveBuildingRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var world=WorldAssembler.Clone(request.World);if(world.Buildings.RemoveAll(b=>b.Id==request.BuildingId)!=1)throw new ArgumentException("Общее здание не найдено.");Validate(world,assets,models);return Results.Ok(new{world});
  });
  app.MapPost("/api/world/seams/repair",(WorldSeamRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var result=WorldSeams.Repair(request.World,request.Mode);Validate(result.World,assets,models);return Results.Ok(result);
  });
  app.MapPost("/api/world/elevation/import",(WorldElevationRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var world=WorldAssembler.Clone(request.World);if(world.GeoReference is null)throw new ArgumentException("Для DEM нужна географическая привязка мира. Импортируйте территорию OSM с общим масштабом.");
   world.Elevation=ElevationSurvey.ParseAscii(request.Text,request.CoordinateSystem,request.Source,request.License);Validate(world,assets,models);var values=world.Elevation.Values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();
   var measured=0;var sampled=0;foreach(var m in world.Maps){var projection=new GeographicProjection(world.GeoReference,m.Project.WorldX,m.Project.WorldY);for(var y=0;y<m.Project.Height;y+=16)for(var x=0;x<m.Project.Width;x+=16){var g=projection.Geographic(x+.5,y+.5);sampled++;if(world.Elevation.Sample(g.Longitude,g.Latitude).HasValue)measured++;}}
   if(measured==0)throw new ArgumentException("DEM не покрывает ни одной проверочной точки этого мира. Проверьте систему координат и границы данных.");
   return Results.Ok(new{world,minimum=values.Min(),maximum=values.Max(),measured,sampled,nativeSlopes=false,note="Реальные высоты сохраняются как географический слой и elevation.asc. Нативная карта PZ остаётся плоской; высоты не подменяются этажами зданий."});
  });
  app.MapPost("/api/world/elevation/download",async(WorldProject input,TerrainElevationClient client,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   Validate(input,assets,models);if(input.GeoReference is null)throw new ArgumentException("Сначала импортируйте территорию OSM с общей географической привязкой.");var world=WorldAssembler.Clone(input);world.Elevation=await client.Fetch(input.GeoReference.Bounds,token);Validate(world,assets,models);var heights=world.Elevation.Values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return Results.Ok(new{world,minimum=heights.Min(),maximum=heights.Max(),nativeSlopes=false,note="DEM загружен из Terrain Tiles / AWS Open Data. Нативная карта PZ остаётся плоской; обзор высот и исходные измерения сохраняются в мире и elevation.asc."});
  });
  app.MapGet("/api/world/recovery/{id}",async(string id,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   var world=await store.LoadAsync(id,token);if(world is null)return Results.NotFound();Validate(world,assets,models);return Results.Ok(world);
  });
  app.MapPost("/api/world/recovery/{id}",async(string id,WorldProject world,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{
   Validate(world,assets,models);await store.SaveAsync(id,world,token);return Results.Ok(new{saved=true});
  });
  app.MapPost("/api/world/recovery/{id}/clear",async(string id,WorldStore store,CancellationToken token)=>{await store.ClearAsync(id,token);return Results.Ok(new{cleared=true});});return app;
 }
 private static WorldAssembler.Bounds Validate(WorldProject world,GameAssetCatalog assets,StaticModelCatalog models){var bounds=WorldAssembler.Validate(world);foreach(var member in world.Maps){AssetRules.Project(member.Project,assets);models.Validate(member.Project);}foreach(var building in world.Buildings){AssetRules.Building(building,assets);ModelRules.Building(building,models);}return bounds;}
}
public sealed record WorldMoveRequest(WorldProject World,string MemberId,int CellX,int CellY);
public sealed record WorldBuildingRequest(WorldProject World,Building Building);
public sealed record WorldPromoteRequest(WorldProject World,string MemberId,string BuildingId,int X,int Y);
public sealed record WorldRemoveBuildingRequest(WorldProject World,string BuildingId);
public sealed record WorldSeamRequest(WorldProject World,string Mode);
public sealed record WorldElevationRequest(WorldProject World,string Text,string CoordinateSystem,string Source,string License);
