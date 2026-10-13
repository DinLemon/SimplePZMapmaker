using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;

public sealed record ResourceSource(string Id,string Name,string Root,string? RequiredModId,string[] Requirements);
public sealed record ModDescriptor(string Id,string Name,string Root,string InfoPath,string Version,string[] Requires,string[] Layers,string[] Packs,string[] TileDefinitions,string? WorkshopId);
public sealed record ModDiagnostic(string Code,string Message,string? ModId=null,string? Tile=null);

public static class ModRegistry
{
 public const string WorkshopRoot=@"./workshop-content";
 public static string SafePath(string path){var full=Path.GetFullPath(path);if(!full.StartsWith(@"G:\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Ресурсы модов должны находиться на диске G:.");var cursor=full;while(cursor.Length>3){if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Ссылки на внешние папки ресурсов не поддерживаются.");cursor=Path.GetDirectoryName(cursor)!;}return full;}
 public static (Dictionary<string,ModDescriptor> Mods,List<ModDiagnostic> Diagnostics) Discover(IEnumerable<string> roots,string gameVersion)
 {
  if(!System.Version.TryParse(gameVersion,out var game)||game.Major!=42)throw new ArgumentException("Укажите версию игры Build 42, например 42.20.0.");
  var paths=roots.Select(SafePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(paths.Length>16)throw new ArgumentException("Слишком много папок модов.");
  var mods=new Dictionary<string,ModDescriptor>(StringComparer.Ordinal);var diagnostics=new List<ModDiagnostic>();var budget=0;var infos=new List<string>();
  void Walk(string dir,int depth){if(++budget>30000)throw new ArgumentException("Папка модов слишком велика.");if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)return;var info=Path.Combine(dir,"mod.info");if(File.Exists(info))infos.Add(info);if(depth>=6)return;foreach(var child in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))Walk(child,depth+1);}
  foreach(var root in paths)if(Directory.Exists(root))Walk(root,0);else diagnostics.Add(new("missing-root","Папка модов не найдена: "+root));
  var groups=infos.GroupBy(file=>{var d=Path.GetDirectoryName(file)!;var name=Path.GetFileName(d);return name=="common"||System.Version.TryParse(name.Contains('.')?name:name+".0",out _)?Path.GetDirectoryName(d)!:d;},StringComparer.OrdinalIgnoreCase);
  foreach(var group in groups){
   try {
    var candidates=group.Select(file=>(File:file,Fields:ReadInfo(file))).Where(c=>Compatible(c.Fields,game)&&LayerVersion(Path.GetFileName(Path.GetDirectoryName(c.File)!))<=game).OrderByDescending(c=>LayerVersion(Path.GetFileName(Path.GetDirectoryName(c.File)!))).ToArray();
    var selected=candidates.FirstOrDefault(c=>Path.GetFileName(Path.GetDirectoryName(c.File)!)!="common");if(selected.File is null)selected=candidates.FirstOrDefault();
    if(selected.File is null){diagnostics.Add(new("incompatible-version","Нет варианта мода для Build "+gameVersion+": "+group.Key));continue;}
    var root=SafePath(group.Key);var effective=Path.GetDirectoryName(selected.File)!;var id=Value(selected.Fields,"id");if(!Regex.IsMatch(id,@"^[A-Za-z0-9_ .()'-]{1,100}$"))throw new ArgumentException("Недопустимый id мода.");
    var common=Path.Combine(root,"common");var layers=effective==root?new[]{root}:new[]{common,effective}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var workshop=Regex.Match(root,@"[\\/]108600[\\/](\d+)[\\/]");var descriptor=new ModDescriptor(id,Value(selected.Fields,"name",id),root,selected.File,Value(selected.Fields,"modversion"),Values(selected.Fields,"require").SelectMany(v=>v.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).Select(v=>v.TrimStart('\\')).Where(v=>v.Length>0).Distinct().ToArray(),layers,Values(selected.Fields,"pack").ToArray(),Values(selected.Fields,"tiledef").ToArray(),workshop.Success?workshop.Groups[1].Value:null);
    if(!mods.TryAdd(id,descriptor))diagnostics.Add(new("duplicate-mod-id","Повторный id мода; выбран первый найденный: "+id,id));
   }catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException){diagnostics.Add(new("invalid-mod",e.Message));}
  }
  return (mods,diagnostics);
 }
 public static IReadOnlyList<ModDescriptor> Order(Dictionary<string,ModDescriptor> mods,IEnumerable<string> ids)
 {
  var requested=ids.ToArray();if(requested.Length>512)throw new ArgumentException("Слишком много активных модов.");var result=new List<ModDescriptor>();var visiting=new HashSet<string>();var seen=new HashSet<string>();
  void Visit(string id){if(seen.Contains(id))return;if(!mods.TryGetValue(id,out var m))throw new ArgumentException("Не найден мод или зависимость: "+id);if(!visiting.Add(id))throw new ArgumentException("Циклические зависимости модов: "+id);foreach(var dependency in m.Requires)Visit(dependency);visiting.Remove(id);seen.Add(id);result.Add(m);}
  foreach(var id in requested)Visit(id);return result;
 }
 private static Version LayerVersion(string s)=>Version.TryParse(s.Contains('.')?s:s+".0",out var v)?v:new Version(0,0);
 private static bool Compatible(Dictionary<string,List<string>> p,Version game)=> (!Version.TryParse(Value(p,"versionMin"),out var min)||game>=min)&&(!Version.TryParse(Value(p,"versionMax"),out var max)||game<=max);
 private static Dictionary<string,List<string>> ReadInfo(string file){SafePath(file);if(new FileInfo(file).Length>256*1024)throw new ArgumentException("mod.info слишком велик.");var p=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);foreach(var line in File.ReadLines(file)){var split=line.IndexOf('=');if(split<1)continue;var key=line[..split].Trim();if(!p.TryGetValue(key,out var list))p[key]=list=[];list.Add(line[(split+1)..].Trim());}return p;}
 private static IEnumerable<string> Values(Dictionary<string,List<string>> p,string key)=>p.GetValueOrDefault(key)??[];
 private static string Value(Dictionary<string,List<string>> p,string key,string fallback="")=>Values(p,key).FirstOrDefault()??fallback;
}
