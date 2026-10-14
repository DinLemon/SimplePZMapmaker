namespace SimplePZMapmaker.Web.Services;
public static class AgentGameplayEndpoints {
 public sealed record PlanRequest(long Revision,AgentZoneStore.Entry[] Entries,string Scope="map",string Objective="Gameplay zones");
 public static void MapAgentGameplayEndpoints(this WebApplication app){
  app.MapPost("/api/agent/zones/{token}/plan",(string token,PlanRequest request,McpGameplayTools tools)=>tools.CreateZones(token,request.Revision,request.Entries,request.Scope,request.Objective));
  app.MapGet("/api/agent/zones/{token}/plan/{id}",(string token,string id,AgentZoneStore store)=>store.Read(token,id));
  app.MapPost("/api/agent/zones/{token}/plan/{id}/commit",(string token,string id,AgentPlanningEndpoints.Revision request,McpGameplayTools tools)=>tools.Commit(token,id,request.Value));
 }
}
