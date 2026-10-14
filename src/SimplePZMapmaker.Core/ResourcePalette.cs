using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core;

public sealed class ResourcePalette
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Name {get;set;}="My palette";
 public List<PaletteResource> Resources {get;set;}=[];
 public static void Validate(IReadOnlyList<ResourcePalette>? palettes)
 {
  if(palettes is null||palettes.Count>16)throw new ArgumentException("A project can contain up to 16 resource palettes.");
  var ids=new HashSet<string>(StringComparer.Ordinal);
  foreach(var palette in palettes){
   if(palette is null||palette.Id is null||!Regex.IsMatch(palette.Id,"^[a-f0-9]{32}$")||!ids.Add(palette.Id)||string.IsNullOrWhiteSpace(palette.Name)||palette.Name.Length>100||palette.Name.Any(char.IsControl)||palette.Resources is null||palette.Resources.Count is <1 or >64)throw new ArgumentException("Invalid resource palette: unique ID, name and 1-64 resource references are required.");
   var entries=new HashSet<string>(StringComparer.Ordinal);
   foreach(var entry in palette.Resources){
    if(entry is null||entry.Role is not("floor" or "wall" or "door" or "window" or "object" or "roof")||!VisualStyles.TileId(entry.Id)||!ResourceDependencies.SafeModId(entry.SourceId)||entry.RequiredMods is null||entry.RequiredMods.Count>64||entry.RequiredMods.Any(m=>!ResourceDependencies.SafeModId(m))||entry.RequiredMods.Distinct(StringComparer.Ordinal).Count()!=entry.RequiredMods.Count||!entries.Add(entry.Role+":"+entry.Id))throw new ArgumentException("Invalid or duplicate palette resource reference.");
   }
  }
 }
}
public sealed class PaletteResource
{
 public string Role {get;set;}="floor";
 public string Id {get;set;}="";
 public string SourceId {get;set;}="game";
 public List<string> RequiredMods {get;set;}=[];
}
