namespace SimplePZMapmaker.Web.Services;
public static class AssetsModEndpoints
{
 public sealed record DiscoverRequest(string[]? Roots=null,string? GameVersion=null);
 public sealed record ActivateRequest(string[] Ids);
 public static void MapAssetsModEndpoints(this WebApplication app){app.MapGet("/api/assets/mods",(GameAssetCatalog assets)=>assets.ModsStatus());app.MapPost("/api/assets/mods/discover",(GameAssetCatalog assets,DiscoverRequest request)=>assets.DiscoverMods(request.Roots,request.GameVersion));app.MapPost("/api/assets/mods/activate",(GameAssetCatalog assets,ActivateRequest request)=>assets.ActivateMods(request.Ids));}
}
