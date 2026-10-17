using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core.Export;

/// <summary>How an exported Build 42 mod is named and laid out. ModId names the mod folder and mod.info id; MapFolder names
/// media/maps/&lt;folder&gt; (the server's Map= value) and defaults to ModId; Name is the title in the mod list and map selector.
/// CommonMaps puts media/maps under common/ instead of 42/.</summary>
public sealed record NativeModOptions(string? ModId=null,string? MapFolder=null,string? Name=null,bool CommonMaps=false,bool Install=false)
{
 public bool Renames=>ModId is not null||MapFolder is not null||Name is not null||CommonMaps;
}
public static class NativeModPackage
{
 /// <summary>Marks a mod folder written by the editor, so a later install may replace it and never replaces anything else.</summary>
 public const string InstallMarker=".simplepzmapmaker";
 static readonly Regex SafeName=new("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
 public static void Validate(NativeModOptions o){
  if(o.ModId is not null&&!SafeName.IsMatch(o.ModId)||o.MapFolder is not null&&!SafeName.IsMatch(o.MapFolder))throw new ArgumentException("Mod id and map folder: 1-64 letters, digits, '_', '.' or '-', starting with a letter, digit or '_'.");
  if(o.Name is not null&&(string.IsNullOrWhiteSpace(o.Name)||o.Name.Length>80||o.Name.Any(char.IsControl)))throw new ArgumentException("Mod name: 1-80 characters without line breaks.");
 }
 /// <summary>Copies an exported archive under a new mod id, map folder, title and layout. Text files that name the mod or map are rewritten.</summary>
 public static void Repack(Stream source,Stream target,NativeModOptions o){
  Validate(o);using var input=new ZipArchive(source,ZipArchiveMode.Read,true);
  var slug=input.Entries.Select(e=>e.FullName.Split('/')[0]).FirstOrDefault(s=>s.Length>0)??throw new ArgumentException("The export archive is empty.");
  if(input.Entries.Any(e=>e.FullName.Split('/')[0]!=slug))throw new ArgumentException("The export archive must contain one mod folder.");
  var mod=o.ModId??slug;var map=o.MapFolder??o.ModId??slug;var oldMaps=$"{slug}/42/media/maps/{slug}/";var newMaps=$"{mod}/{(o.CommonMaps?"common":"42")}/media/maps/{map}/";
  using var output=new ZipArchive(target,ZipArchiveMode.Create,true);var written=new HashSet<string>(StringComparer.Ordinal);
  foreach(var entry in input.Entries){
   var path=entry.FullName.StartsWith(oldMaps,StringComparison.Ordinal)?newMaps+entry.FullName[oldMaps.Length..]:mod+entry.FullName[slug.Length..];
   if(!written.Add(path))continue;var copy=output.CreateEntry(path,CompressionLevel.Fastest);if(path.EndsWith('/'))continue;
   using var from=entry.Open();using var to=copy.Open();var name=entry.Name;
   if(name is "mod.info" or "map.info" or "spawnregions.lua" or "INSTALL.txt"){using var reader=new StreamReader(from,Encoding.UTF8);var text=reader.ReadToEnd();
    text=name switch{
     "mod.info"=>Regex.Replace(Regex.Replace(text,"^id=.*$","id="+mod,RegexOptions.Multiline),"^name=.*$",o.Name is null?"$0":"name="+o.Name,RegexOptions.Multiline),
     "map.info"=>o.Name is null?text:Regex.Replace(text,"^title=.*$","title="+o.Name,RegexOptions.Multiline),
     "spawnregions.lua"=>text.Replace($"media/maps/{slug}/",$"media/maps/{map}/").Replace($"name = \"{slug}\"",$"name = \"{map}\""),
     _=>text.Replace($"Map={slug}",$"Map={map}").Replace(slug,mod)};
    var bytes=new UTF8Encoding(false).GetBytes(text);to.Write(bytes);}
   else from.CopyTo(to);
  }
  foreach(var folder in new[]{$"{mod}/common/",$"{mod}/common/media/",$"{mod}/common/media/maps/"})if(written.Add(folder))output.CreateEntry(folder);
 }
 /// <summary>Extracts an export archive into a mods folder (normally Zomboid/mods). A folder the editor did not create is never replaced.</summary>
 public static string Install(Stream archive,string modsDirectory){
  using var zip=new ZipArchive(archive,ZipArchiveMode.Read,true);var mod=zip.Entries.Select(e=>e.FullName.Split('/')[0]).FirstOrDefault(s=>s.Length>0)??throw new ArgumentException("The export archive is empty.");
  if(!SafeName.IsMatch(mod))throw new ArgumentException("The export archive has an unsafe mod folder name.");
  var root=Path.GetFullPath(modsDirectory);Directory.CreateDirectory(root);var target=Path.GetFullPath(Path.Combine(root,mod));var staging=target+".installing";
  if(Directory.Exists(target)&&!File.Exists(Path.Combine(target,InstallMarker)))throw new ArgumentException($"{target} already exists and was not created by SimplePZMapmaker. Rename the mod id or remove that folder yourself.");
  if(Directory.Exists(staging))Directory.Delete(staging,true);
  try{
   foreach(var entry in zip.Entries){var relative=entry.FullName[mod.Length..].TrimStart('/');var path=Path.GetFullPath(Path.Combine(staging,relative));if(!path.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&path!=staging)throw new ArgumentException("The export archive contains a path outside its mod folder.");
    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,true);}
   File.WriteAllText(Path.Combine(staging,InstallMarker),"Installed by SimplePZMapmaker. A later install of the same mod id replaces this folder.\n");
   if(Directory.Exists(target))Directory.Delete(target,true);Directory.Move(staging,target);
  }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
  return target;
 }
 /// <summary>Zomboid/mods of the current user, or PZMAP_MODS_DIR when set.</summary>
 public static string DefaultModsDirectory()=>Environment.GetEnvironmentVariable("PZMAP_MODS_DIR") is {Length:>0} configured?configured:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Zomboid","mods");
}
