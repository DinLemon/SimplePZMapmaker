using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static partial class AssetRules
{
 public static void WorldInfrastructure(WorldProject world,GameAssetCatalog assets){foreach(var id in SimplePZMapmaker.Core.WorldInfrastructure.ResourceIds(world))if(assets.Get(id).Source!="game")throw new ArgumentException("Shared infrastructure requires the original game resource provider: "+id);}
}
