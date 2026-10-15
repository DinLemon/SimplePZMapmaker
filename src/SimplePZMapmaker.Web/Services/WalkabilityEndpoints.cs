using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed class AccessCheckRequest {
 public MapProject? Project {get;set;} public WorldProject? World {get;set;} public string? MemberId {get;set;}
 public WalkabilityOptions Options {get;set;}=new();public string Revision {get;set;}="";
}
public static class WalkabilityEndpoints {
 public static void MapWalkabilityEndpoints(this WebApplication app){
  app.MapPost("/api/access/check",(AccessCheckRequest request,GameAssetCatalog assets,CancellationToken token)=>{
   if((request.Project is null)==(request.World is null)||request.Revision is null||request.Revision.Length>100)throw new ArgumentException("Choose one map or world section for access analysis.");
   var report=request.Project is {} p?WalkabilityAnalyzer.Analyze(p,request.Options,token,assets.Ready?id=>assets.Get(id).Properties:null):WalkabilityAnalyzer.World(request.World!,request.MemberId??"",request.Options,token,assets.Ready?id=>assets.Get(id).Properties:null);return new{request.Revision,report};
  });
  app.MapPost("/api/access/building",(Building building,GameAssetCatalog assets,CancellationToken token)=>WalkabilityAnalyzer.Building(building,token,assets.Ready?id=>assets.Get(id).Properties:null));
 }
}
