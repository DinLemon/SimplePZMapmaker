namespace SimplePZMapmaker.Web.Services;
public static class RecoveryVersionEndpoints {
 public static void MapRecoveryVersionEndpoints(this WebApplication app){
  app.MapGet("/api/recovery/{id}/versions",(string id,RecoveryStore store)=>store.Versions(id));
  app.MapGet("/api/recovery/{id}/versions/{version:int}",async(string id,int version,RecoveryStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var p=await store.LoadAsync(id,token,version);if(p is null)return Results.NotFound();AssetRules.Project(p,assets);models.Validate(p);return Results.Ok(p);});
  app.MapGet("/api/world/recovery/{id}/versions",(string id,WorldStore store)=>store.Versions(id));
  app.MapGet("/api/world/recovery/{id}/versions/{version:int}",async(string id,int version,WorldStore store,McpPlanningTools tools,CancellationToken token)=>{var w=await store.LoadAsync(id,token,version);if(w is null)return Results.NotFound();tools.ValidateWorld(w);return Results.Ok(w);});
 }
}
