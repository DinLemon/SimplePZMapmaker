using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core.Export;

/// <summary>Finds native cells that another installed map already uses. A map folder's cells are the names of its X_Y.lotheader files;
/// two maps loaded together with a common cell overwrite each other there. Sources are read only: the game's media/maps, Steam Workshop
/// items of Project Zomboid and the local mods folder.</summary>
public static partial class CellConflicts
{
 public sealed record InstalledMap(string Name,string Source,HashSet<(int X,int Y)> Cells);
 public sealed record Conflict(string Map,string Source,int Count,(int X,int Y) First);
 [GeneratedRegex(@"^(\d{1,5})_(\d{1,5})\.lotheader$")] private static partial Regex LotHeader();

 static bool Cell(string name,out (int X,int Y) cell){var m=LotHeader().Match(name);cell=m.Success?(int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture)):default;return m.Success;}

 /// <summary>Every folder below <paramref name="root"/> (at most eight levels, links skipped) holding lot headers, merged by map folder name.</summary>
 public static List<InstalledMap> Scan(string root,string source){
  var maps=new Dictionary<string,InstalledMap>(StringComparer.OrdinalIgnoreCase);if(!Directory.Exists(root))return [];
  var options=new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=8,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
  foreach(var file in Directory.EnumerateFiles(root,"*.lotheader",options)){
   // Challenge maps (media/maps/challengemaps) are played on their own, never with a world.
   if(!Cell(Path.GetFileName(file),out var cell)||file.Replace('\\','/').Contains("/challengemaps/",StringComparison.OrdinalIgnoreCase))continue;var folder=Path.GetFileName(Path.GetDirectoryName(file))!;
   if(!maps.TryGetValue(folder,out var map))maps[folder]=map=new(folder,source,[]);map.Cells.Add(cell);
  }
  return maps.Values.ToList();
 }
 /// <summary>The installed maps next to a Build 42 installation: vanilla, Workshop items (steamapps/workshop/content/108600) and local mods.</summary>
 public static List<InstalledMap> Installed(string game,string? modsDirectory){
  var result=Scan(Path.Combine(game,"media","maps"),"game");
  result.AddRange(Scan(Path.GetFullPath(Path.Combine(game,"..","..","workshop","content","108600")),"workshop"));
  if(modsDirectory is not null)result.AddRange(Scan(modsDirectory,"mods"));
  return result;
 }
 public static HashSet<(int X,int Y)> Cells(MapProject p){var cells=new HashSet<(int,int)>();for(var y=0;y<p.Height/256;y++)for(var x=0;x<p.Width/256;x++)cells.Add((p.WorldX+x,p.WorldY+y));return cells;}
 public static HashSet<(int X,int Y)> Cells(WorldProject w){var cells=new HashSet<(int,int)>();foreach(var m in w.Maps)cells.UnionWith(Cells(m.Project));return cells;}
 /// <summary>The cells of an exported mod archive.</summary>
 public static HashSet<(int X,int Y)> Cells(ZipArchive zip){var cells=new HashSet<(int,int)>();foreach(var e in zip.Entries)if(Cell(e.Name,out var cell))cells.Add(cell);return cells;}
 /// <summary>Installed maps sharing a cell with <paramref name="cells"/>; a map folder named <paramref name="own"/> (an earlier copy of this map) is ignored.</summary>
 public static List<Conflict> Check(HashSet<(int X,int Y)> cells,IEnumerable<InstalledMap> maps,string? own=null)=>
  maps.Where(m=>own is null||!string.Equals(m.Name,own,StringComparison.OrdinalIgnoreCase)).Select(m=>(Map:m,Shared:m.Cells.Where(cells.Contains).OrderBy(c=>c.Y).ThenBy(c=>c.X).ToList())).Where(x=>x.Shared.Count>0)
   .Select(x=>new Conflict(x.Map.Name,x.Map.Source,x.Shared.Count,x.Shared[0])).OrderBy(c=>c.Source=="game"?0:1).ThenByDescending(c=>c.Count).ToList();
}
