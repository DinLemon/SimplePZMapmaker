using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed partial class GameAssetCatalog
{
 public IReadOnlyList<RegionalSprite> RegionalSprites(){lock(sync)return items.Values.OrderBy(a=>a.Id,StringComparer.Ordinal).Select(a=>new RegionalSprite(a.Id,new Dictionary<string,string>(a.Properties),a.ModId,a.Requirements?.ToArray())).ToArray();}
}
