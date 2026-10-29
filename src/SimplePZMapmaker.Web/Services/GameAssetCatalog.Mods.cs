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
  // Workspace packages are validated later by AgentTilesetStore, not discovered from user mod roots.
  private Dictionary<string,ModDescriptor> agentTilesetMods=new(StringComparer.Ordinal);
  private HashSet<string> knownAgentTilesetIds=new(StringComparer.Ordinal);
  private string[] requestedModIds=[];
  private bool modSelectionLoaded;
 private List<ModDiagnostic> discoveryDiagnostics=[];
 private List<ModDiagnostic> modDiagnostics=[];
 private string[] modRoots=string.IsNullOrWhiteSpace(ModRegistry.WorkshopRoot)?[]:[ModRegistry.WorkshopRoot];
 private string gameVersion="42.21.0";
 public bool HasActiveMods {get {lock(sync)return activeMods.Count>0;}}
 // Registry membership alone does not establish that a tile is active: callers must also check its current catalog owner.
 internal bool IsAgentTileset(string modId){lock(sync)return agentTilesetMods.ContainsKey(modId);}
  private sealed record SavedMods(string[] Roots,string GameVersion,string[] Ids,string[]? WorkspaceIds=null);
  private SavedMods? LoadModSettings(){
   var file=Path.Combine(cache,"mods.json");if(!File.Exists(file)){modSelectionLoaded=true;return null;}if(new FileInfo(file).Length>128*1024)throw new ArgumentException("Mod settings are too large.");
   var saved=JsonSerializer.Deserialize<SavedMods>(File.ReadAllText(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new ArgumentException("Invalid mod settings.");
   if(saved.Ids is null||saved.Ids.Length>512||saved.WorkspaceIds?.Length>512)throw new ArgumentException("Invalid saved mod selection.");
   requestedModIds=saved.Ids.Distinct(StringComparer.Ordinal).ToArray();knownAgentTilesetIds=new(saved.WorkspaceIds??[],StringComparer.Ordinal);
   modSelectionLoaded=true;return saved;
  }
  private void RestoreMods(){
   var saved=LoadModSettings();if(saved is null)return;
   DiscoverMods(saved.Roots,saved.GameVersion);
   var available=new Dictionary<string,ModDescriptor>(discoveredMods,StringComparer.Ordinal);
   foreach(var id in knownAgentTilesetIds)available.Remove(id); // Never activate an unvalidated workspace package from settings.
   var ordered=RestorableMods(available,requestedModIds,out var pending);var merged=MergeMods(gameItems,gameAtlases,ordered);
   discoveredMods=available;activeMods=ordered;items=merged.Items;atlases=merged.Atlases;modDiagnostics=discoveryDiagnostics.Concat(pending).Concat(merged.Diagnostics).ToList();
  }
  private static IReadOnlyList<ModDescriptor> RestorableMods(Dictionary<string,ModDescriptor> available,IEnumerable<string> requested,out List<ModDiagnostic> pending){
   var ordered=new List<ModDescriptor>();var seen=new HashSet<string>(StringComparer.Ordinal);pending=[];
   // Resolve independent selections separately: a deferred workspace ID must not discard working normal mods.
   foreach(var id in requested){try{foreach(var mod in ModRegistry.Order(available,[id]))if(seen.Add(mod.Id))ordered.Add(mod);}
    catch(ArgumentException e){pending.Add(new("restore-deferred",e.Message,id));}}
   return ordered;
  }
  private void SaveModSettings(IEnumerable<string> ids,IEnumerable<string> workspaceIds){
   var bytes=JsonSerializer.SerializeToUtf8Bytes(new{roots=modRoots,gameVersion,ids=ids.Distinct(StringComparer.Ordinal).ToArray(),workspaceIds=workspaceIds.Order(StringComparer.Ordinal).ToArray()});
   if(bytes.Length>128*1024)throw new ArgumentException("Mod settings are too large.");
   Directory.CreateDirectory(cache);var file=Path.Combine(cache,"mods.json");var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 public sealed record Requirement(string ModId,string Name,string? WorkshopId,string[] Dependencies);
 public object ModsStatus(){lock(sync)return new{gameVersion,roots=modRoots,active=activeMods.Select(m=>m.Id).ToArray(),mods=discoveredMods.Values.OrderBy(m=>m.Name).ToArray(),diagnostics=modDiagnostics.ToArray()};}
 public object DiscoverMods(string[]? roots=null,string? version=null)
 {
   lock(sync){var nextRoots=roots??modRoots;var nextVersion=version??gameVersion;var found=ModRegistry.Discover(nextRoots,nextVersion);
    foreach(var mod in agentTilesetMods.Values){if(found.Mods.TryGetValue(mod.Id,out var other)&&!other.Root.Equals(mod.Root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("ModId collides with a validated workspace mod: "+mod.Id);found.Mods[mod.Id]=mod;}
    var activated=ModRegistry.Order(found.Mods,activeMods.Select(m=>m.Id));var merged=MergeMods(gameItems,gameAtlases,activated);
   modRoots=nextRoots;gameVersion=nextVersion;discoveredMods=found.Mods;discoveryDiagnostics=found.Diagnostics;activeMods=activated;items=merged.Items;atlases=merged.Atlases;modDiagnostics=found.Diagnostics.Concat(merged.Diagnostics).ToList();return ModsStatus();}
 }
 public object ActivateMods(string[] ids)
 {
   lock(sync){if(!modSelectionLoaded)LoadModSettings();if(discoveredMods.Count==0)DiscoverMods();var ordered=ModRegistry.Order(discoveredMods,ids);var merged=MergeMods(gameItems,gameAtlases,ordered);
   // Parse every source before committing the new view. A broken mod never erases working assets.
   SaveModSettings(ids,knownAgentTilesetIds);requestedModIds=ids.Distinct(StringComparer.Ordinal).ToArray();
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
    foreach(var registration in mod.TileDefinitions){var token=registration.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";if(!Regex.IsMatch(token,@"^[A-Za-z0-9_-]{1,160}$"))throw new ArgumentException("Invalid tiledef registration: "+mod.Id);var binary=Path.Combine(media,token+".tiles");var text=binary+".txt";if(!File.Exists(binary)&&!File.Exists(text))continue;if(++files>4096)throw new ArgumentException("Too many mod resource files.");IReadOnlyList<TileDefinitionReader.Tile> tiles;if(File.Exists(binary)){using var stream=File.OpenRead(ModRegistry.SafePath(binary));tiles=TileDefinitionReader.Read(stream);}else tiles=TileDefinitionReader.ReadText(ModRegistry.SafePath(text));foreach(var tile in tiles)defs[tile.Id]=tile;}
    foreach(var tile in defs.Values){var p=tile.Properties;var id=tile.Id;if(next.TryGetValue(id,out var previous)&&previous.ModId!=mod.Id)Diagnostic(new("tile-conflict","A mod overrides the tile according to load order: "+id,mod.Id,id));var asset=new Asset(id,Category(id,p),p.GetValueOrDefault("CustomName",id),p.GetValueOrDefault("GroupName",""),p.GetValueOrDefault("Facing",""),p.GetValueOrDefault("SpriteGridPos"),p.ContainsKey("solid")||p.ContainsKey("solidtrans")||p.ContainsKey("BlocksPlacement")||p.ContainsKey("tree")||p.ContainsKey("collideN")||p.ContainsKey("collideW"),p.ContainsKey("solidfloor"),p,null,null,"mod",mod.Id,required);next[id]=asset;if(next.Count>400000)throw new ArgumentException("Too many mod resources.");}
    foreach(var registration in mod.Packs){var token=registration.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";if(!Regex.IsMatch(token,@"^[A-Za-z0-9_ .()-]{1,160}$"))throw new ArgumentException("Invalid pack registration: "+mod.Id);var variants=new[]{token+".pack",token+"2x.pack"};var full=variants.Select(file=>Path.Combine(media,"texturepacks",file)).LastOrDefault(File.Exists);if(full is null)continue;full=ModRegistry.SafePath(full);if(++files>4096||new FileInfo(full).Length>512*1024*1024)throw new ArgumentException("Mod packs are too large.");var fingerprint=Fingerprint(full);using var stream=File.OpenRead(full);
     foreach(var page in TexturePackIndex.Read(stream,Path.GetFileName(full))){var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full+fingerprint+page.Offset)))[..24].ToLowerInvariant();nextAtlases[key]=new(full,page,fingerprint);if(nextAtlases.Count>16384)throw new ArgumentException("Too many mod atlases.");foreach(var sprite in page.Sprites){if(!next.TryGetValue(sprite.Name,out var asset)){Diagnostic(new("unregistered-sprite","A sprite without tiledef cannot be placed: "+sprite.Name,mod.Id,sprite.Name));continue;}if(asset.ModId!=mod.Id)Diagnostic(new("sprite-conflict","A mod overrides the tile image: "+sprite.Name,mod.Id,sprite.Name));next[sprite.Name]=asset with{Page=key,Sprite=sprite,Source="mod",ModId=mod.Id,Requirements=(asset.Requirements??[]).Concat(required).Distinct().ToArray()};}}
    }
   }}
  if(next.Count>400000)throw new ArgumentException("Too many mod resources.");return(next,nextAtlases,diagnostics.Take(2000).ToList());
 }
}
