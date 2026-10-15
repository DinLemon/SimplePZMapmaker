using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class AgentDesignEndpoints {
 public sealed record CreateRequest(long Revision,DesignConstraints Constraints,Building[]? Replacements=null,NeighborhoodConstraints? Neighborhood=null,string Scope="map",string? MemberId=null,string Objective="Checked design");
 public sealed record TaskRequest(long TaskRevision);
 public static void MapAgentDesignEndpoints(this WebApplication app){
  app.MapGet("/api/agent/design/{token}",(string token,AgentDesignStore store)=>store.List(token).Select(id=>McpDesignTools.Summary(store.Read(token,id),token)));
  app.MapPost("/api/agent/design/{token}",(string token,CreateRequest request,McpDesignTools tools)=>tools.Create(token,request.Revision,request.Constraints,request.Replacements,request.Neighborhood,request.Scope,request.MemberId,request.Objective));
  app.MapGet("/api/agent/design/{token}/{id}",(string token,string id,McpDesignTools tools)=>tools.Read(token,id));
  app.MapPost("/api/agent/design/{token}/{id}/check",(string token,string id,TaskRequest request,McpDesignTools tools,CancellationToken cancellation)=>tools.Check(token,id,request.TaskRevision,cancellation));
  app.MapPost("/api/agent/design/{token}/{id}/repair",(string token,string id,TaskRequest request,McpDesignTools tools,CancellationToken cancellation)=>tools.Repair(token,id,request.TaskRevision,cancellation));
  app.MapPost("/api/agent/design/{token}/{id}/commit",(string token,string id,TaskRequest request,McpDesignTools tools)=>tools.Commit(token,id,request.TaskRevision));
 }
}
