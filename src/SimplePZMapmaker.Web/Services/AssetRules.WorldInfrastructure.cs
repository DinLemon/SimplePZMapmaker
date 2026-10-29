using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static partial class AssetRules
{
 public static void StampDeckArtwork(IEnumerable<LinearFeature> features,GameAssetCatalog assets){foreach(var f in features.Where(f=>f.DeckArtwork.Count>0)){var ids=f.DeckArtwork.Select(a=>a.Tile).Distinct().ToArray();foreach(var id in ids)CheckDeckArtwork(assets,id);f.AssetSources=StampSources(ids,f.AssetSources,assets);}}
 public static void WorldInfrastructure(WorldProject world,GameAssetCatalog assets){
  // Core geometry validates explicit ownership even offline. Inspect active provider overrides
  // only when the installation is present, as with other built-in native authoring.
  if(!assets.Ready)return;
  StampDeckArtwork(world.LinearFeatures,assets);var artwork=world.LinearFeatures.SelectMany(f=>f.DeckArtwork).Select(a=>a.Tile).ToHashSet(StringComparer.Ordinal);foreach(var id in artwork)CheckDeckArtwork(assets,id);
  foreach(var id in SimplePZMapmaker.Core.WorldInfrastructure.ResourceIds(world).Where(id=>!artwork.Contains(id)))if(assets.Get(id).Source!="game")throw new ArgumentException("Shared infrastructure requires the original game resource provider: "+id);
 }
}
