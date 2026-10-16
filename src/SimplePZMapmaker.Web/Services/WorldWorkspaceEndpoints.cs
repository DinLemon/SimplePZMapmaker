using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class WorldWorkspaceEndpoints
{
 public sealed record SaveRequest(WorldProject World,long? ExpectedRevision=null);
 public sealed record SectionRequest(MapProject Project,long ExpectedRevision);
 public sealed record AppendRequest(string MemberId,MapProject Project,long ExpectedRevision);
 public static void MapWorldWorkspaceEndpoints(this WebApplication app){
  app.MapGet("/api/world/workspaces",async(WorldStore store,CancellationToken token)=>await store.ListAsync(token));
  app.MapGet("/api/world/workspaces/{id}",async(string id,int? version,WorldStore store,CancellationToken token)=>{var m=await store.ManifestAsync(id,token,version??0);return m is null?Results.NotFound():Results.Ok(m);});
  app.MapPost("/api/world/workspaces/{id}",async(string id,SaveRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{WorldEndpoints.Validate(request.World,assets,models);return await store.SaveWorkspaceAsync(id,request.World,request.ExpectedRevision,token);});
  app.MapPost("/api/world/workspaces/{id}/sections",async(string id,AppendRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{AssetRules.Project(request.Project,assets);models.Validate(request.Project);return await store.AppendSectionAsync(id,request.MemberId,request.ExpectedRevision,request.Project,token);});
  app.MapGet("/api/world/workspaces/{id}/sections/{memberId}",async(string id,string memberId,int? version,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var snapshot=await store.LoadSectionAsync(id,memberId,token,version??0);AssetRules.Project(snapshot.Member.Project,assets);models.Validate(snapshot.Member.Project);return snapshot;});
  app.MapPost("/api/world/workspaces/{id}/sections/{memberId}",async(string id,string memberId,SectionRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{AssetRules.Project(request.Project,assets);models.Validate(request.Project);return await store.SaveSectionAsync(id,memberId,request.ExpectedRevision,request.Project,token);});
  app.MapPost("/api/world/workspaces/{id}/sections/{memberId}/validate",async(string id,string memberId,SectionRequest request,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{AssetRules.Project(request.Project,assets);models.Validate(request.Project);await store.ValidateSectionAsync(id,memberId,request.ExpectedRevision,request.Project,token);return Results.Ok(new{valid=true,revision=request.ExpectedRevision});});
  app.MapGet("/api/world/workspaces/{id}/export",async(string id,WorldStore store,NativeExportService exporter,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var lease=await store.PinAsync(id,token);return await exporter.Workspace(lease,assets,models,token);});
  app.MapGet("/api/world/workspaces/{id}/sections/{memberId}/export",async(string id,string memberId,WorldStore store,NativeExportService exporter,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var lease=await store.PinAsync(id,token);return await exporter.Workspace(lease,assets,models,token,memberId);});
  app.MapGet("/api/world/workspaces/{id}/world",async(string id,WorldStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{var world=await store.LoadAsync(id,token)??throw new ArgumentException("Section workspace not found.");WorldEndpoints.Validate(world,assets,models);return world;});
 }
}
