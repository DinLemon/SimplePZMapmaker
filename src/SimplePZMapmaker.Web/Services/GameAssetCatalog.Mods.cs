using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;

public sealed partial class GameAssetCatalog
{
 private Dictionary<string,Asset> gameItems=[];
 private Dictionary<string,Atlas> gameAtlases=[];
 private Dictionary<string,ModDescriptor> discoveredMods=new(StringComparer.Ordinal);
 private IReadOnlyList<ModDescriptor> activeMods=[];
 private List<ModDiagnostic> discoveryDiagnostics=[];
 private List<ModDiagnostic> modDiagnostics=[];
 private string[] modRoots=[ModRegistry.WorkshopRoot];
 private string gameVersion="42.21.0";
 public bool HasActiveMods {get {lock(sync)return activeMods.Count>0;}}
 private sealed record SavedMods(string[] Roots,string GameVersion,string[] Ids);
 private void RestoreMods(){var file=Path.Combine(cache,"mods.json");if(!File.Exists(file))return;if(new FileInfo(file).Length>128*1024)throw new ArgumentException("Настройки модов слишком велики.");var saved=JsonSerializer.Deserialize<SavedMods>(File.ReadAllText(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new ArgumentException("Настройки модов повреждены.");DiscoverMods(saved.Roots,saved.GameVersion);ActivateMods(saved.Ids);}
 public sealed record Requirement(string ModId,string Name,string? WorkshopId,string[] Dependencies);
 public object ModsStatus(){lock(sync)return new{gameVersion,roots=modRoots,active=activeMods.Select(m=>m.Id).ToArray(),mods=discoveredMods.Values.OrderBy(m=>m.Name).ToArray(),diagnostics=modDiagnostics.ToArray()};}
 public object DiscoverMods(string[]? roots=null,string? version=null)
 {
  lock(sync){var nextRoots=roots??modRoots;var nextVersion=version??gameVersion;var found=ModRegistry.Discover(nextRoots,nextVersion);var activated=ModRegistry.Order(found.Mods,activeMods.Select(m=>m.Id));var merged=MergeMods(gameItems,gameAtlases,activated);
   modRoots=nextRoots;gameVersion=nextVersion;discoveredMods=found.Mods;discoveryDiagnostics=found.Diagnostics;activeMods=activated;items=merged.Items;atlases=merged.Atlases;modDiagnostics=found.Diagnostics.Concat(merged.Diagnostics).ToList();return ModsStatus();}
 }
 public object ActivateMods(string[] ids)
 {
  lock(sync){if(discoveredMods.Count==0)DiscoverMods();var ordered=ModRegistry.Order(discoveredMods,ids);var merged=MergeMods(gameItems,gameAtlases,ordered);
   // Parse every source before committing the new view. A broken mod never erases working assets.
   Directory.CreateDirectory(cache);var file=Path.Combine(cache,"mods.json");var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(new{roots=modRoots,gameVersion,ids=ids.Distinct().ToArray()}));File.Move(temporary,file,true);
   activeMods=ordered;items=merged.Items;atlases=merged.Atlases;modDiagnostics=discoveryDiagnostics.Concat(merged.Diagnostics).ToList();return ModsStatus();}
 }
 public IReadOnlyList<ResourceSource> ActiveSources(){lock(sync){var sources=new List<ResourceSource>{new("game","Project Zomboid",GamePath,null,[])};foreach(var mod in activeMods)foreach(var layer in mod.Layers)sources.Add(new("mod:"+mod.Id+":"+Path.GetFileName(layer),mod.Name,layer,mod.Id,RequirementsFor(mod.Id)));return sources;}}
 public IReadOnlyList<Requirement> ResolveRequirements(IEnumerable<string> tileIds)
 {
  lock(sync){var required=tileIds.Distinct().Select(Get).SelectMany(a=>a.Requirements??[]).Distinct().ToHashSet();return activeMods.Where(m=>required.Contains(m.Id)).Select(m=>new Requirement(m.Id,m.Name,m.WorkshopId,m.Requires)).ToArray();}
 }
 private string[] RequirementsFor(string id)=>RequirementsFor(id,activeMods);
 private static string[] RequirementsFor(string id,IReadOnlyList<ModDescriptor> mods){var found=new HashSet<string>();void Visit(string value){if(!found.Add(value))return;foreach(var d in mods.Single(m=>m.Id==value).Requires)Visit(d);}Visit(id);return mods.Where(m=>found.Contains(m.Id)).Select(m=>m.Id).ToArray();}
 private (Dictionary<string,Asset> Items,Dictionary<string,Atlas> Atlases,List<ModDiagnostic> Diagnostics) MergeMods(Dictionary<string,Asset> basis,Dictionary<string,Atlas> basisAtlases,IReadOnlyList<ModDescriptor> mods)
 {
  var next=new Dictionary<string,Asset>(basis,StringComparer.Ordinal);var nextAtlases=new Dictionary<string,Atlas>(basisAtlases);var diagnostics=new List<ModDiagnostic>();var files=0; void Diagnostic(ModDiagnostic diagnostic){if(diagnostics.Count<2000)diagnostics.Add(diagnostic);}
  foreach(var mod in mods){var required=RequirementsFor(mod.Id,mods);foreach(var layer in mod.Layers){var media=Path.Combine(layer,"media");if(!Directory.Exists(media))continue;ModRegistry.SafePath(media);
    var defs=new Dictionary<string,TileDefinitionReader.Tile>();
    // Only registered tiledefs are loaded; helper/patch files are not standalone definitions.
    foreach(var registration in mod.TileDefinitions){var token=registration.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";if(!Regex.IsMatch(token,@"^[A-Za-z0-9_-]{1,160}$"))throw new ArgumentException("Неверная регистрация tiledef: "+mod.Id);var binary=Path.Combine(media,token+".tiles");var text=binary+".txt";if(!File.Exists(binary)&&!File.Exists(text))continue;if(++files>4096)throw new ArgumentException("Слишком много файлов ресурсов модов.");IReadOnlyList<TileDefinitionReader.Tile> tiles;if(File.Exists(binary)){using var stream=File.OpenRead(ModRegistry.SafePath(binary));tiles=TileDefinitionReader.Read(stream);}else tiles=TileDefinitionReader.ReadText(ModRegistry.SafePath(text));foreach(var tile in tiles)defs[tile.Id]=tile;}
    foreach(var tile in defs.Values){var p=tile.Properties;var id=tile.Id;if(next.TryGetValue(id,out var previous)&&previous.ModId!=mod.Id)Diagnostic(new("tile-conflict","Тайл заменён модом по порядку загрузки: "+id,mod.Id,id));var asset=new Asset(id,Category(id,p),p.GetValueOrDefault("CustomName",id),p.GetValueOrDefault("GroupName",""),p.GetValueOrDefault("Facing",""),p.GetValueOrDefault("SpriteGridPos"),p.ContainsKey("solid")||p.ContainsKey("solidtrans")||p.ContainsKey("BlocksPlacement")||p.ContainsKey("tree")||p.ContainsKey("collideN")||p.ContainsKey("collideW"),p.ContainsKey("solidfloor"),p,null,null,"mod",mod.Id,required);next[id]=asset;if(next.Count>400000)throw new ArgumentException("Слишком много ресурсов модов.");}
    foreach(var registration in mod.Packs){var token=registration.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";if(!Regex.IsMatch(token,@"^[A-Za-z0-9_ .()-]{1,160}$"))throw new ArgumentException("Неверная регистрация pack: "+mod.Id);var variants=new[]{token+".pack",token+"2x.pack"};var full=variants.Select(file=>Path.Combine(media,"texturepacks",file)).LastOrDefault(File.Exists);if(full is null)continue;full=ModRegistry.SafePath(full);if(++files>4096||new FileInfo(full).Length>512*1024*1024)throw new ArgumentException("Пакеты модов слишком велики.");var fingerprint=Fingerprint(full);using var stream=File.OpenRead(full);
     foreach(var page in TexturePackIndex.Read(stream,Path.GetFileName(full))){var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full+fingerprint+page.Offset)))[..24].ToLowerInvariant();nextAtlases[key]=new(full,page,fingerprint);if(nextAtlases.Count>16384)throw new ArgumentException("Слишком много атласов модов.");foreach(var sprite in page.Sprites){if(!next.TryGetValue(sprite.Name,out var asset)){Diagnostic(new("unregistered-sprite","Спрайт без tiledef не доступен для размещения: "+sprite.Name,mod.Id,sprite.Name));continue;}if(asset.ModId!=mod.Id)Diagnostic(new("sprite-conflict","Изображение тайла заменено модом: "+sprite.Name,mod.Id,sprite.Name));next[sprite.Name]=asset with{Page=key,Sprite=sprite,Source="mod",ModId=mod.Id,Requirements=(asset.Requirements??[]).Concat(required).Distinct().ToArray()};}}
    }
   }}
  if(next.Count>400000)throw new ArgumentException("Слишком много ресурсов модов.");return(next,nextAtlases,diagnostics.Take(2000).ToList());
 }
}
