using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static partial class AssetRules
{
 public static void WorldInfrastructure(WorldProject world,GameAssetCatalog assets){
  // Core geometry validates explicit ownership even offline. Inspect active provider overrides
  // only when the installation is present, as with other built-in native authoring.
  if(!assets.Ready)return;
  foreach(var id in SimplePZMapmaker.Core.WorldInfrastructure.ResourceIds(world))if(assets.Get(id).Source!="game")throw new ArgumentException("Shared infrastructure requires the original game resource provider: "+id);
 }
}
