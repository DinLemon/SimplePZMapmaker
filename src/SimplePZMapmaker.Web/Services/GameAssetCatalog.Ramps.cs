using System.Security.Cryptography;
using System.Text;

namespace SimplePZMapmaker.Web.Services;

public sealed partial class GameAssetCatalog
{
 // B42 ramp properties live in a separate binary tdef, not newtiledefinitions.tiles.txt.
 // Read only local game resources, before the ordinary mod merge applies load order.
 private static void LoadNativeRamps(string root,Dictionary<string,Asset> next,Dictionary<string,Atlas> nextAtlases)
 {
  var definitions=Path.Combine(root,"media","tiledefinitions_b42chunkcaching.tiles");
  if(!File.Exists(definitions))return;
  using(var stream=File.OpenRead(ModRegistry.SafePath(definitions)))foreach(var tile in TileDefinitionReader.Read(stream)){
   if(!tile.Id.StartsWith("ramps_01_",StringComparison.Ordinal)||tile.Properties.Count==0)continue;
   var p=tile.Properties;
   next.TryAdd(tile.Id,new(tile.Id,Category(tile.Id,p),p.GetValueOrDefault("CustomName",tile.Id),p.GetValueOrDefault("GroupName",""),p.GetValueOrDefault("Facing",""),p.GetValueOrDefault("SpriteGridPos"),p.ContainsKey("solid")||p.ContainsKey("solidtrans")||p.ContainsKey("BlocksPlacement")||p.ContainsKey("tree")||p.ContainsKey("collideN")||p.ContainsKey("collideW"),p.ContainsKey("solidfloor"),p,null,null));
  }
  var pack=Path.Combine(root,"media","texturepacks","B42ChunkCaching2x.pack");
  if(!File.Exists(pack))return;
  pack=ModRegistry.SafePath(pack);
  if(new FileInfo(pack).Length>512L*1024*1024)throw new ArgumentException("The native ramp texture pack is too large.");
  var fingerprint=Fingerprint(pack);using var data=File.OpenRead(pack);
  foreach(var page in TexturePackIndex.Read(data,"B42ChunkCaching2x.pack")){
   var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pack+fingerprint+page.Offset)))[..24].ToLowerInvariant();
   nextAtlases[key]=new(pack,page,fingerprint);
   // This auxiliary pack must not replace ordinary game images with unrelated sprites.
   foreach(var sprite in page.Sprites)if(sprite.Name.StartsWith("ramps_01_",StringComparison.Ordinal)&&next.TryGetValue(sprite.Name,out var asset))next[sprite.Name]=asset with{Page=key,Sprite=sprite};
  }
 }
}
