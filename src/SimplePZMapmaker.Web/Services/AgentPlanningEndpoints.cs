using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;
public static class AgentPlanningEndpoints {
 public sealed record PlanRequest(long Revision,NeighborhoodConstraints Constraints);
 public sealed record WorldPublish(long Revision,WorldProject World);
 public sealed record WorldPreview(WorldProject World,string MemberId,int X,int Y,int? Level,int CellX,int CellY);
 public static void MapAgentPlanningEndpoints(this WebApplication app){
  app.MapPost("/api/agent/world/session",(WorldProject world,AgentWorldStore store,McpPlanningTools tools)=>{tools.ValidateWorld(world);return store.Create(world);});
  app.MapPost("/api/world/preview",(WorldPreview request,McpPlanningTools tools)=>{tools.ValidateWorld(request.World);return NativeScene.WorldCell(request.World,request.MemberId,request.X,request.Y,request.Level,request.CellX,request.CellY);});
  app.MapGet("/api/agent/world/session/{token}",(string token,AgentWorldStore store)=>store.Read(token));
  app.MapGet("/api/agent/world/session/{token}/status",(string token,AgentWorldStore store)=>store.Status(token));
  app.MapPost("/api/agent/world/session/{token}/publish",(string token,WorldPublish request,AgentWorldStore store,McpPlanningTools tools)=>{tools.ValidateWorld(request.World);return store.Publish(token,request.Revision,request.World);});
  app.MapPost("/api/agent/world/session/{token}/close",(string token,AgentWorldStore store,AgentPlanningStore plans,AgentZoneStore zones,AgentDesignStore designs)=>{store.Close(token);plans.Close(token);zones.Close(token);designs.Close(token);return new{closed=true};});
  app.MapPost("/api/agent/session/{token}/plan",(string token,PlanRequest request,McpPlanningTools tools)=>tools.CreateMapPlan(token,request.Revision,request.Constraints));
  app.MapPost("/api/agent/world/session/{token}/plan",(string token,PlanRequest request,McpPlanningTools tools)=>tools.CreateWorldPlan(token,request.Revision,request.Constraints));
  app.MapGet("/api/agent/plan/{token}/{id}",(string token,string id,AgentPlanningStore plans)=>plans.Read(token,id));
  app.MapPost("/api/agent/session/{token}/plan/{id}/commit",(string token,string id,Revision request,McpPlanningTools tools)=>tools.CommitMapPlan(token,id,request.Value));
  app.MapPost("/api/agent/world/session/{token}/plan/{id}/commit",(string token,string id,Revision request,McpPlanningTools tools)=>tools.CommitWorldPlan(token,id,request.Value));
  app.MapGet("/api/agent/world/export/{token}/{revision:long}",async(string token,long revision,AgentWorldStore store,McpPlanningTools tools,NativeExportService exporter,CancellationToken cancellation)=>{var s=store.Read(token);if(s.Revision!=revision)throw new AgentRevisionConflict(s.Revision);tools.ValidateWorld(s.World);return await exporter.World(s.World,cancellation);});
 }
 public sealed record Revision(long Value);
}
