using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;
public static class McpEndpoints {
 public sealed record PublishRequest(long Revision,MapProject Project);
 public static void MapAgentEndpoints(this WebApplication app){
  app.MapPost("/api/agent/session",(MapProject p,AgentProjectStore store,GameAssetCatalog assets,StaticModelCatalog models)=>{AssetRules.Project(p,assets);models.Validate(p);return store.Create(p);});
  app.MapGet("/api/agent/session/{token}",(string token,AgentProjectStore store)=>store.Read(token));
  app.MapGet("/api/agent/session/{token}/status",(string token,AgentProjectStore store)=>store.Status(token));
  app.MapPost("/api/agent/session/{token}/publish",(string token,PublishRequest request,AgentProjectStore store,GameAssetCatalog assets,StaticModelCatalog models)=>{AssetRules.Project(request.Project,assets);models.Validate(request.Project);return store.Publish(token,request.Revision,request.Project);});
  app.MapPost("/api/agent/session/{token}/close",(string token,AgentProjectStore store,AgentPlanningStore plans,AgentZoneStore zones,AgentDesignStore designs)=>{store.Close(token);plans.Close(token);zones.Close(token);designs.Close(token);return new{closed=true};});
  app.MapGet("/api/agent/export/{token}/{revision:long}",async(string token,long revision,AgentProjectStore store,GameAssetCatalog assets,StaticModelCatalog models,NativeExportService exporter,CancellationToken cancellation)=>{var s=store.Read(token);if(s.Revision!=revision)throw new AgentRevisionConflict(s.Revision);AssetRules.Project(s.Project,assets);models.Validate(s.Project);return await exporter.Map(s.Project,cancellation);});
  app.MapMcp("/mcp");
 }
}
