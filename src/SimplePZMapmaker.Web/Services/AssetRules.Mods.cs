using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

public static partial class AssetRules
{
 private static void ValidateProjectStructure(MapProject p)
 {
  ArgumentNullException.ThrowIfNull(p);var version=p.Version;var upgrade=version is >=1 and <6&&(p.RequiredMods?.Count>0||p.AssetSources?.Count>0||p.StaticModels?.Count>0||p.Buildings?.Concat(p.BuildingLibrary??[]).Any(b=>b?.RequiredMods?.Count>0||b?.AssetSources?.Count>0||b?.Design?.Floors?.Any(f=>f?.StaticModels?.Count>0)==true)==true);
  if(upgrade)p.Version=6;try{ProjectValidator.Validate(p);}finally{p.Version=version;}
 }
 public static void StampDependencies(Building b,GameAssetCatalog assets)
 {
  if(!assets.HasActiveMods&&b.AssetSources.Count==0&&b.RequiredMods.Count==0&&b.Design?.Floors.Any(f=>f.StaticModels.Any(m=>m.Profile!="pickup"))!=true)return;
  var explicitTiles=b.Design?.Floors.SelectMany(f=>f.FloorTiles.Values.Concat(f.FloorTile is null?[]:new[]{f.FloorTile}).Concat(f.Edges.SelectMany(e=>new[]{e.WallTile,e.OpeningTile}.OfType<string>())).Concat(f.Objects.SelectMany(o=>o.Kind=="asset"?o.Parts.Select(p=>p.Tile):[])))??[];
  // Compose through the native implementation, so generated walls, roofs and furniture
  // carry the same provider as the actual exported building.
  var ids=NativeScene.BuildingResourceIds(b).Concat(explicitTiles).Distinct().ToArray();
  b.AssetSources=StampSources(ids,b.AssetSources,assets);b.RequiredMods=assets.ResolveRequirements(ids).Select(m=>m.ModId).Concat(b.Design?.Floors.SelectMany(f=>f.StaticModels).SelectMany(m=>m.RequiredMods)??[]).Distinct(StringComparer.Ordinal).ToList();
 }
 public static void StampDependencies(MapProject p,GameAssetCatalog assets)
 {
  if(!assets.HasActiveMods&&p.AssetSources.Count==0&&p.RequiredMods.Count==0&&p.StaticModels.All(m=>m.Profile=="pickup")&&p.Buildings.Concat(p.BuildingLibrary).All(b=>b.AssetSources.Count==0&&b.RequiredMods.Count==0&&b.Design?.Floors.Any(f=>f.StaticModels.Any(m=>m.Profile!="pickup"))!=true))return;
  foreach(var b in p.Buildings.Concat(p.BuildingLibrary))StampDependencies(b,assets);
  var ids=NativeScene.ResourceIds(p).Concat(p.SurfaceTiles.Values).Concat(p.Decorations.SelectMany(d=>d.Parts.Select(part=>part.Tile).Append(d.Asset))).Distinct().ToArray();p.AssetSources=StampSources(ids,p.AssetSources,assets);
  p.RequiredMods=assets.ResolveRequirements(ids).Select(m=>m.ModId).Concat(p.Buildings.SelectMany(b=>b.RequiredMods)).Concat(p.StaticModels.SelectMany(m=>m.RequiredMods)).Distinct(StringComparer.Ordinal).ToList();if(p.RequiredMods.Count>0||p.AssetSources.Count>0)p.Version=Math.Max(p.Version,6);
 }
 public static void ValidateDependencies(MapProject p,GameAssetCatalog assets)=>StampDependencies(p,assets);
 private static Dictionary<string,string> StampSources(IEnumerable<string> ids,Dictionary<string,string> saved,GameAssetCatalog assets)
 {
  var next=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var id in ids){var provider=assets.Get(id).ModId??"game";if(saved.TryGetValue(id,out var original)&&original!=provider)throw new ArgumentException($"Tile {id} has a different source: expected {original}, active {provider}. Enable the original mod.");next[id]=provider;}return next;
 }
}
