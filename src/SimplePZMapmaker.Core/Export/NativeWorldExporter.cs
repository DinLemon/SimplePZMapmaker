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
   foreach(var member in world.Maps){token.ThrowIfCancellationRequested();var p=WorldAssembler.ProjectSection(world,member);using var input=new MemoryStream(NativeMapExporter.ExportValidatedWorldSection(p,token));using var source=new ZipArchive(input,ZipArchiveMode.Read);
    foreach(var entry in source.Entries){token.ThrowIfCancellationRequested();var file=Path.GetFileName(entry.FullName);if(file.EndsWith(".lotheader")||file.EndsWith(".lotpack")||file.StartsWith("chunkdata_")&&file.EndsWith(".bin")){if(!names.Add(file))throw new ArgumentException("Duplicate world cell: "+file);var target=zip.CreateEntry(root+file,CompressionLevel.Fastest);using var from=entry.Open();using var to=target.Open();from.CopyTo(to);}
     else if(file=="objects.lua"){if(entry.Length+zones.Length>64_000_000)throw new ArgumentException("World zone descriptions exceed 64 MB of text; split the world into multiple mods.");using var reader=new StreamReader(entry.Open());var text=reader.ReadToEnd();var first=text.IndexOf('{');var last=text.LastIndexOf('}');zones.Append(text.AsSpan(first+1,last-first-1));}
     else if(file=="worldmap.xml"){if(entry.Length+mapTextBytes>64_000_000)throw new ArgumentException("In-game atlas outlines exceed 64 MB; split the world into multiple mods.");mapTextBytes+=entry.Length;using var reader=new StreamReader(entry.Open());var cells=XDocument.Parse(reader.ReadToEnd()).Root!.Elements("cell");foreach(var cell in cells)xml.Add(new XElement(cell));}
    }
    foreach(var point in p.Spawns)spawns.Add($" {{ posX = {p.WorldX*256+point.X}, posY = {p.WorldY*256+point.Y}, posZ = 0 }}");
    foreach(var a in p.Attribution.Concat(p.Buildings.Concat(p.BuildingLibrary).SelectMany(b=>b.Attribution)))attributions.Add(a);
    if(output.Length>256*1024*1024)throw new ArgumentException("World export exceeds 256 MB; split it into multiple mods.");
   }
   zones.Append("}\n");Text(root+"objects.lua",zones.ToString());Text(root+"worldmap.xml",new XDocument(xml).ToString());using(var binary=zip.CreateEntry(root+"worldmap.xml.bin",CompressionLevel.Fastest).Open()){var bytes=NativeWorldMapBinary.Write(xml);binary.Write(bytes);}Text(root+"spawnpoints.lua","function SpawnPoints()\n local points = {\n"+string.Join(",\n",spawns)+"\n }\n return { unemployed = points }\nend\n");Text(root+"spawnregions.lua",$"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n");
   var dependencies=world.Maps.SelectMany(m=>ResourceDependencies.ForProject(m.Project)).Concat(world.Buildings.SelectMany(b=>b.RequiredMods)).Concat(world.Buildings.SelectMany(b=>b.Design?.Floors.SelectMany(f=>f.StaticModels.SelectMany(m=>m.RequiredMods))??[])).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();var require=dependencies.Length>0?"require="+string.Join(',',dependencies)+"\n":"";
   StaticModelExporter.Write(zip,slug,world.Maps.SelectMany(m=>StaticModelExporter.Gather(m.Project)).Concat(WorldAssembler.GatherModels(world)));
   Text(slug+"/42/mod.info",$"name={world.Name}\nid={slug}\nversionMin=42.0\ndescription=World assembled from {world.Maps.Count} sections; new Build 42 world.\n{require}");Text(root+"map.info",$"title={world.Name}\nfixed2x=true\ndescription=Stitched area {bounds.Width} x {bounds.Height}; {bounds.Cells} cells.\nzoomX={bounds.X*256+bounds.Width/2}\nzoomY={bounds.Y*256+bounds.Height/2}\nzoomS=10\n");
   using(var json=zip.CreateEntry(slug+"/world.pzworld.json",CompressionLevel.Fastest).Open())JsonSerializer.Serialize(json,world,NativeMapExporter.JsonOptions);
   if(world.Elevation is {} survey){Text(slug+"/elevation.asc",survey.ToAscii());attributions.Add("Elevation: "+survey.Source);attributions.Add(survey.License);}
   Text(slug+"/ATTRIBUTION.txt",string.Join('\n',attributions.Prepend("Created with SimplePZMapmaker. Sparse world export;256-cell/8-chunk Build42."))+"\n");Text(slug+"/INSTALL.txt",$"Extract {slug} into Zomboid/mods, enable the mod, and create a NEW world with the {world.Name} map. Sections are combined into one map; cell overlaps were checked. All original sections are preserved in world.pzworld.json. Game resources are not included. Do not add this to an important existing save. For a server: Map={slug}, Mods={slug}.\n");
  }if(output.Length>256*1024*1024)throw new ArgumentException("World export exceeds 256 MB; split it into multiple mods.");return output.ToArray();
 }
}
