using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class GeographicWarpEndpoints
{
 public static void MapGeographicWarpEndpoints(this WebApplication app){
  app.MapPost("/api/geographic/warp/preview",(GeographicWarpRequest request)=>GeographicWarpProposal.Create(request));
 }
}
