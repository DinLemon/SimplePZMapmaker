using System.Text.Json;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class BuildingModuleEndpoints
{
 public sealed record CaptureRequest(Building Building,int X,int Y,int Width,int Height,int MinLevel,int MaxLevel,string Name,string Role);
 public sealed record TransformRequest(Building Module,int QuarterTurns=0,bool Mirror=false);
 public sealed record ComposeRequest(Building Building,Building Module,int X,int Y,int Level,int QuarterTurns=0,bool Mirror=false,bool AutoConnect=true,bool Expand=true);
 static string Pair(GameAssetCatalog assets,string tile,string direction,string role){
  if(role is not("doorwall" or "windowwall"))return JsonSerializer.SerializeToElement(assets.Pair(tile,role)).GetProperty(direction).GetString()!;
  var split=tile.LastIndexOf('_');if(split<0||!int.TryParse(tile[(split+1)..],out var index))throw new ArgumentException("Invalid directional resource: "+tile);
  var owner=assets.Get(tile).ModId;var suffix=direction=="n"?"N":"W";foreach(var candidate in Enumerable.Range(index/16*16,16).OrderBy(v=>Math.Abs(v-index))){var id=tile[..(split+1)]+candidate;try{var asset=assets.Get(id);if(asset.ModId!=owner)continue;if(role=="doorwall"?asset.Properties.ContainsKey("DoorWall"+suffix):asset.Properties.ContainsKey("Window"+suffix)||asset.Properties.ContainsKey("WindowWall"+suffix))return id;}catch(ArgumentException){}}
  throw new ArgumentException("The authored wall opening has no verified directional pair: "+tile);
 }
 static void Validate(Building b,GameAssetCatalog assets,StaticModelCatalog models){AssetRules.Building(b,assets);ModelRules.Building(b,models);}
 public static void MapBuildingModuleEndpoints(this WebApplication app){
  app.MapGet("/api/building/modules",()=>BuildingModuleCatalog.Entries);
  app.MapPost("/api/building/modules/preset/{id}",(string id,GameAssetCatalog assets,StaticModelCatalog models)=>{var b=BuildingModuleCatalog.Create(id);Validate(b,assets,models);return b;});
  app.MapPost("/api/building/modules/capture",(CaptureRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{AssetRules.Building(request.Building,assets,false);ModelRules.Building(request.Building,models);var b=BuildingModules.Capture(request.Building,request.X,request.Y,request.Width,request.Height,request.MinLevel,request.MaxLevel,request.Name,request.Role);Validate(b,assets,models);return b;});
  app.MapPost("/api/building/modules/transform",(TransformRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{Validate(request.Module,assets,models);var b=BuildingModules.Transform(request.Module,request.QuarterTurns,request.Mirror,(tile,direction,role)=>Pair(assets,tile,direction,role));Validate(b,assets,models);return b;});
  app.MapPost("/api/building/modules/compose",(ComposeRequest request,GameAssetCatalog assets,StaticModelCatalog models)=>{Validate(request.Building,assets,models);Validate(request.Module,assets,models);var module=BuildingModules.Transform(request.Module,request.QuarterTurns,request.Mirror,(tile,direction,role)=>Pair(assets,tile,direction,role));var b=BuildingModules.Compose(request.Building,module,request.X,request.Y,request.Level,request.AutoConnect,request.Expand);Validate(b,assets,models);return b;});
 }
}
