using System.Text.Json;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class ServicesGenerationEndpoints {
 public sealed record EnsemblePreviewRequest(MapProject Project,StreetEnsembleOptions Constraints);
 public sealed record EnsembleApplyRequest(MapProject Project,StreetEnsemblePlan Plan);
 static readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
 public static void MapGenerationEndpoints(this WebApplication app){
  app.MapPost("/api/generation/building/preview",(ParametricBuildingOptions options,GameAssetCatalog assets)=>{var result=ParametricBuildings.Create(options);AssetRules.Building(result.Building,assets);foreach(var module in result.Modules)AssetRules.Building(module,assets);return result;});
  app.MapPost("/api/generation/ensemble/preview",(EnsemblePreviewRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{
   var resources=Resources(assets);if(request.Constraints.Preset=="emergency-shelter")foreach(var tent in EmergencyAccommodation.DefaultResources()){try{var placement=assets.PlacementFor(tent.Asset);resources.Add(new("tent",tent.Asset,placement.Parts.Select(p=>new BuildingObjects.Part(p.Dx,p.Dy,p.Tile,p.Flags)).ToList(),placement.ModId,(placement.Requirements??[]).ToList()));}catch(ArgumentException){}}var light=Lamp(models);var plan=StreetEnsembles.Create(request.Project,request.Constraints,resources,light);foreach(var building in plan.Buildings)AssetRules.Building(building,assets);foreach(var d in plan.Decorations)AssetRules.CheckParts(assets,d.Asset,d.Parts);foreach(var m in plan.StaticModels)models.Validate(m);return plan;
  });
  app.MapPost("/api/generation/ensemble/apply",(EnsembleApplyRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{var project=StreetEnsembles.Apply(request.Project,request.Plan);AssetRules.Project(project,assets);models.Validate(project);return project;});
 }
 static IEnumerable<string> Search(GameAssetCatalog assets,string? query,string category){var page=JsonSerializer.SerializeToElement(assets.Search(query,category,0,120),json);return page.GetProperty("items").EnumerateArray().Select(a=>a.GetProperty("id").GetString()!).ToArray();}
 public static List<EnsembleResource> Resources(GameAssetCatalog assets){
  var result=new List<EnsembleResource>();
  void Select(string role,IEnumerable<string> ids,Func<GameAssetCatalog.Asset,bool>? filter=null){foreach(var id in ids.Distinct()){try{var a=assets.Get(id);if(filter is not null&&!filter(a))continue;var placement=assets.PlacementFor(id);if(placement.Parts.Count>8)continue;result.Add(new(role,id,placement.Parts.Select(p=>new BuildingObjects.Part(p.Dx,p.Dy,p.Tile,p.Flags)).ToList(),placement.ModId,(placement.Requirements??[]).ToList()));return;}catch(ArgumentException){}}}
  // Both facings are resolved from the active catalog, then PlacementFor supplies
  // canonical physical flags, complete sprite grids and provider dependencies.
  var fences=Search(assets,null,"fences");Select("fence-n",new[]{"fencing_01_5"}.Concat(fences),a=>(a.Properties.ContainsKey("collideN")||a.Properties.ContainsKey("HoppableN")||a.Properties.ContainsKey("WallNTrans"))&&!(a.Properties.ContainsKey("collideW")||a.Properties.ContainsKey("HoppableW")||a.Properties.ContainsKey("WallWTrans")));Select("fence-w",new[]{"fencing_01_4"}.Concat(fences),a=>(a.Properties.ContainsKey("collideW")||a.Properties.ContainsKey("HoppableW")||a.Properties.ContainsKey("WallWTrans"))&&!(a.Properties.ContainsKey("collideN")||a.Properties.ContainsKey("HoppableN")||a.Properties.ContainsKey("WallNTrans")));
  Select("greenery",new[]{"vegetation_ornamental_01_0"}.Concat(Search(assets,null,"vegetation")),a=>!a.Properties.ContainsKey("tree"));Select("bench",Search(assets,"Bench","furniture"));Select("lamp",new[]{"lighting_outdoor_01_0"}.Concat(Search(assets,null,"lighting")),a=>a.Properties.ContainsKey("streetlight"));return result;
 }
 public static StaticModelPlacement? Lamp(StaticModelCatalog models){
  var page=JsonSerializer.SerializeToElement(models.Search("lamp",0,120),json);var items=page.GetProperty("items").EnumerateArray().Where(a=>a.GetProperty("error").ValueKind==JsonValueKind.Null).Select(a=>a.GetProperty("id").GetString()!).OrderBy(id=>id.Contains("Floor",StringComparison.OrdinalIgnoreCase)?0:id.Contains("Table",StringComparison.OrdinalIgnoreCase)?1:2).ThenBy(id=>id,StringComparer.Ordinal);
  foreach(var id in items.Concat(new[]{"Base.LightBulb"})){try{var light=models.Placement(id);light.Profile="light";light.Light=new(){Radius=10,Rgb=[1,.86,.65]};models.Validate(light);return light;}catch(ArgumentException){}}return null;
 }
}
