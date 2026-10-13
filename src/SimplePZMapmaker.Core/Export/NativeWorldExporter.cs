using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
namespace SimplePZMapmaker.Core.Export;
public static class NativeWorldExporter
{
 public static byte[] Export(WorldProject world,CancellationToken token=default){
  var bounds=WorldAssembler.Validate(world);var slug="SimplePZWorld_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.Id)))[..12].ToLowerInvariant();var root=slug+"/42/media/maps/"+slug+"/";
  using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
   void Text(string name,string value){var entry=zip.CreateEntry(name,CompressionLevel.Fastest);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(value);}
   long mapTextBytes=0;zip.CreateEntry(slug+"/common/");var zones=new StringBuilder("objects = {\n");var xml=new XElement("world",new XAttribute("version","1.0"),new XAttribute("cellSize",256));var spawns=new List<string>();var attributions=new HashSet<string>();var names=new HashSet<string>();
   foreach(var member in world.Maps){token.ThrowIfCancellationRequested();var p=member.Project;using var input=new MemoryStream(NativeMapExporter.Export(p,true,token));using var source=new ZipArchive(input,ZipArchiveMode.Read);
    foreach(var entry in source.Entries){token.ThrowIfCancellationRequested();var file=Path.GetFileName(entry.FullName);if(file.EndsWith(".lotheader")||file.EndsWith(".lotpack")||file.StartsWith("chunkdata_")&&file.EndsWith(".bin")){if(!names.Add(file))throw new ArgumentException("Дублирующаяся ячейка мира: "+file);var target=zip.CreateEntry(root+file,CompressionLevel.Fastest);using var from=entry.Open();using var to=target.Open();from.CopyTo(to);}
     else if(file=="objects.lua"){if(entry.Length+zones.Length>64_000_000)throw new ArgumentException("Описания зон мира превышают64МБ текста; разделите мир на несколько модов.");using var reader=new StreamReader(entry.Open());var text=reader.ReadToEnd();var first=text.IndexOf('{');var last=text.LastIndexOf('}');zones.Append(text.AsSpan(first+1,last-first-1));}
     else if(file=="worldmap.xml"){if(entry.Length+mapTextBytes>64_000_000)throw new ArgumentException("Контуры игрового атласа превышают64МБ; разделите мир на несколько модов.");mapTextBytes+=entry.Length;using var reader=new StreamReader(entry.Open());var cells=XDocument.Parse(reader.ReadToEnd()).Root!.Elements("cell");foreach(var cell in cells)xml.Add(new XElement(cell));}
    }
    foreach(var point in p.Spawns)spawns.Add($" {{ posX = {p.WorldX*256+point.X}, posY = {p.WorldY*256+point.Y}, posZ = 0 }}");
    foreach(var a in p.Attribution.Concat(p.Buildings.Concat(p.BuildingLibrary).SelectMany(b=>b.Attribution)))attributions.Add(a);
    if(output.Length>256*1024*1024)throw new ArgumentException("Экспорт мира превышает256МБ; разделите его на несколько модов.");
   }
   zones.Append("}\n");Text(root+"objects.lua",zones.ToString());Text(root+"worldmap.xml",new XDocument(xml).ToString());Text(root+"spawnpoints.lua","function SpawnPoints()\n local points = {\n"+string.Join(",\n",spawns)+"\n }\n return { unemployed = points }\nend\n");Text(root+"spawnregions.lua",$"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n");
   var dependencies=world.Maps.SelectMany(m=>ResourceDependencies.ForProject(m.Project)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();var require=dependencies.Length>0?"require="+string.Join(',',dependencies)+"\n":"";
   StaticModelExporter.Write(zip,slug,world.Maps.SelectMany(m=>StaticModelExporter.Gather(m.Project)));
   Text(slug+"/42/mod.info",$"name={world.Name}\nid={slug}\nversionMin=42.0\ndescription=Мир из {world.Maps.Count} участков; новый мир Build42.\n{require}");Text(root+"map.info",$"title={world.Name}\nfixed2x=true\ndescription=Сшитая территория {bounds.Width}×{bounds.Height}; {bounds.Cells} ячеек.\nzoomX={bounds.X*256+bounds.Width/2}\nzoomY={bounds.Y*256+bounds.Height/2}\nzoomS=10\n");
   using(var json=zip.CreateEntry(slug+"/world.pzworld.json",CompressionLevel.Fastest).Open())JsonSerializer.Serialize(json,world,NativeMapExporter.JsonOptions);
   Text(slug+"/ATTRIBUTION.txt",string.Join('\n',attributions.Prepend("Created with SimplePZMapmaker. Sparse world export;256-cell/8-chunk Build42."))+"\n");Text(slug+"/INSTALL.txt",$"Распакуйте {slug} в Zomboid/mods, включите мод и создайте НОВЫЙ мир с картой {world.Name}. Участки объединены в одной карте; пересечения ячеек проверены. Все исходные участки сохранены в world.pzworld.json. Игровые ресурсы не включены. Не подключайте к важному существующему сохранению. Для сервера: Map={slug}, Mods={slug}.\n");
  }if(output.Length>256*1024*1024)throw new ArgumentException("Экспорт мира превышает256МБ; разделите его на несколько модов.");return output.ToArray();
 }
}
