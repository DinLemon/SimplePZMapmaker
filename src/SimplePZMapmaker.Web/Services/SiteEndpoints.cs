using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class SiteEndpoints{
 public static void MapSiteEndpoints(this WebApplication app)=>app.MapPost("/api/sites/edit",(SiteEditRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{var result=SiteEditor.Apply(request);GenerationResourceValidation.Map(result.Project,assets,models);return result;});
}
