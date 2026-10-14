using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class WorldWorkspaceEndpoints
{
 public sealed record SaveRequest(WorldProject World,long? ExpectedRevision=null);
 public sealed record SectionRequest(MapProject Project,long ExpectedRevision);
 public static void MapWorldWorkspaceEndpoints(this WebApplication app){
  app.MapGet("/api/world/workspaces",async(WorldStore store,CancellationToken token)=>await store.ListAsync(token));
  app.MapGet("/api/world/workspaces/{id}",async(string id,WorldStore store,CancellationToken token)=>{var m=await store.ManifestAsync(id,token);return m is null?Results.NotFound():Results.Ok(m);});
  app.MapPost("/api/world/workspaces/{id}",async(string id,SaveRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{WorldEndpoints.Validate(request.World,assets,models);return await store.SaveWorkspaceAsync(id,request.World,request.ExpectedRevision,token);});
  app.MapGet("/api/world/workspaces/{id}/sections/{memberId}",async(string id,string memberId,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var snapshot=await store.LoadSectionAsync(id,memberId,token);AssetRules.Project(snapshot.Member.Project,assets);models.Validate(snapshot.Member.Project);return snapshot;});
  app.MapPost("/api/world/workspaces/{id}/sections/{memberId}",async(string id,string memberId,SectionRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{AssetRules.Project(request.Project,assets);models.Validate(request.Project);return await store.SaveSectionAsync(id,memberId,request.ExpectedRevision,request.Project,token);});
  app.MapGet("/api/world/workspaces/{id}/export",async(string id,WorldStore store,NativeExportService exporter,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var world=await store.LoadAsync(id,token)??throw new ArgumentException("Section workspace not found.");WorldEndpoints.Validate(world,assets,models);return await exporter.World(world,token);});
  app.MapGet("/api/world/workspaces/{id}/sections/{memberId}/export",async(string id,string memberId,WorldStore store,NativeExportService exporter,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var world=await store.LoadAsync(id,token)??throw new ArgumentException("Section workspace not found.");WorldEndpoints.Validate(world,assets,models);return await exporter.Section(world,memberId,token);});
  app.MapGet("/api/world/workspaces/{id}/world",async(string id,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var world=await store.LoadAsync(id,token)??throw new ArgumentException("Section workspace not found.");WorldEndpoints.Validate(world,assets,models);return world;});
 }
}
