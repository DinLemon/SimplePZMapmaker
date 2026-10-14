using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class LinearEditingEndpoints
{
 public sealed record PutRequest(MapProject Project,LinearFeature Feature);
 public sealed record RemoveRequest(MapProject Project,string Id);
 public static void MapLinearEditingEndpoints(this WebApplication app){
  app.MapPost("/api/linear/put",(PutRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   AssetRules.Project(request.Project,assets);models.Validate(request.Project);
   var candidate=LinearFeatureEditor.Put(request.Project,request.Feature);AssetRules.Project(candidate,assets);models.Validate(candidate);return candidate;
  });
  app.MapPost("/api/linear/remove",(RemoveRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   AssetRules.Project(request.Project,assets);models.Validate(request.Project);
   var candidate=LinearFeatureEditor.Remove(request.Project,request.Id);AssetRules.Project(candidate,assets);models.Validate(candidate);return candidate;
  });
 }
}
