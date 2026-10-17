using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
namespace SimplePZMapmaker.Core.Export;

public sealed record StreamedWorldSection(string Id,string Name,int WorldX,int WorldY,int Width,int Height);
public interface IStreamedWorldExportSource
{
 WorldProject Metadata {get;}
 IReadOnlyList<StreamedWorldSection> Sections {get;}
 object AuthoringIndex {get;}
 Task<MapProject> ReadSectionAsync(string id,CancellationToken token);
 Task<MapProject> ProjectSectionAsync(string id,MapProject authored,CancellationToken token);
 Task CopySectionSourceAsync(string id,Stream destination,CancellationToken token);
 Task CopyMetadataSourceAsync(Stream destination,CancellationToken token);
}
/// <summary>One authored/projected section at a time; no complete world raster or nested section ZIPs.</summary>
public static class NativeStreamedWorldExporter
{
 public static async Task ExportAsync(IStreamedWorldExportSource source,Stream output,NativeExportContext? context=null,CancellationToken token=default,string? memberId=null){
  if(!output.CanWrite||!output.CanSeek)throw new ArgumentException("World export requires a writable, seekable destination.");
  var sections=memberId is null?source.Sections:source.Sections.Where(s=>s.Id==memberId).ToArray();if(sections.Count==0)throw new ArgumentException("Section not found.");
  var world=source.Metadata;var slug=(memberId is null?"SimplePZWorld_":"SimplePZSection_")+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(world.Id+(memberId is null?"":":"+memberId))))[..12];var root=slug+"/42/media/maps/"+slug+"/";
  context?.Begin(sections.Count,sections.Sum(s=>s.Width/256*(s.Height/256)));using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
   void Text(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Fastest).Open(),new UTF8Encoding(false));writer.Write(value);}
   zip.CreateEntry(slug+"/common/");var zones=new StringBuilder("objects = {\n");var atlas=new XElement("world",new XAttribute("version","1.0"),new XAttribute("cellSize",256));long atlasBytes=0,attributionBytes=0;var spawns=new List<string>();var attributions=new HashSet<string>(StringComparer.Ordinal);var dependencies=new SortedSet<string>(StringComparer.Ordinal);var models=new List<StaticModelExporter.PlacedStaticModel>();var modelIds=new HashSet<(string,int,int,int)>();
   foreach(var section in sections){
    token.ThrowIfCancellationRequested();context?.Section(section.Id);
    await WriteSection(section);
    context?.SectionDone();
   }
   async Task WriteSection(StreamedWorldSection section){
    var authored=await source.ReadSectionAsync(section.Id,token);var p=await source.ProjectSectionAsync(section.Id,authored,token);
    NativeMapExporter.WriteCells(p,zip,root,token,context);
    var text=GameZonesExporter.ObjectsLua(p,token);if(text.Length+zones.Length>64_000_000)throw new ArgumentException("World zone descriptions exceed 64 MB. Export selected sections.");var first=text.IndexOf('{');var last=text.LastIndexOf('}');zones.Append(text.AsSpan(first+1,last-first-1));
    var cellAtlas=NativeMapExporter.WorldMap(p);atlasBytes+=Encoding.UTF8.GetByteCount(cellAtlas.ToString());if(atlasBytes>64_000_000)throw new ArgumentException("World atlas descriptions exceed 64 MB. Export selected sections.");foreach(var cell in cellAtlas.Elements("cell"))atlas.Add(new XElement(cell));
    foreach(var point in p.Spawns)spawns.Add($" {{ posX = {p.WorldX*256+point.X}, posY = {p.WorldY*256+point.Y}, posZ = 0 }}");
    foreach(var a in p.Attribution.Concat(p.Buildings.Concat(p.BuildingLibrary).SelectMany(b=>b.Attribution)))if(attributions.Add(a)){attributionBytes+=Encoding.UTF8.GetByteCount(a);if(attributionBytes>16_000_000)throw new ArgumentException("World attribution descriptions exceed 16 MB. Export selected sections.");}
    foreach(var dependency in ResourceDependencies.ForProject(p))dependencies.Add(dependency);
    foreach(var model in StaticModelExporter.Gather(p).Where(m=>m.X>=p.WorldX*256&&m.Y>=p.WorldY*256&&m.X<p.WorldX*256+p.Width&&m.Y<p.WorldY*256+p.Height)){if(modelIds.Add((model.Placement.Id,model.X,model.Y,model.Z)))models.Add(model);if(models.Count>100000)throw new ArgumentException("World static models exceed 100,000 placements.");}
    using(var entry=zip.CreateEntry(slug+"/source/sections/"+section.Id+".pzmap.json",CompressionLevel.Fastest).Open())await source.CopySectionSourceAsync(section.Id,entry,token);
    if(memberId is not null){using(var entry=zip.CreateEntry(slug+"/project.pzmap.json",CompressionLevel.Fastest).Open())await source.CopySectionSourceAsync(section.Id,entry,token);Text(slug+"/section-world-context.json",JsonSerializer.Serialize(new{format="SimplePZSectionContext",version=1,worldId=world.Id,worldName=world.Name,memberId,sections=source.Sections,world.Buildings,world.LinearFeatures,world.Fences,world.FenceStyles,world.GateStyles,world.Decorations,world.GeoReference},NativeMapExporter.JsonOptions));}
   }
   zones.Append("}\n");Text(root+"objects.lua",zones.ToString());Text(root+"worldmap.xml",new XDocument(atlas).ToString());using(var binary=zip.CreateEntry(root+"worldmap.xml.bin",CompressionLevel.Fastest).Open()){var bytes=NativeWorldMapBinary.Write(atlas);binary.Write(bytes);}
   if(spawns.Count==0&&memberId is null)throw new ArgumentException("The world needs at least one dry-land spawn.");Text(root+"spawnpoints.lua","function SpawnPoints()\n local points = {\n"+string.Join(",\n",spawns)+"\n }\n return { unemployed = points }\nend\n");Text(root+"spawnregions.lua",$"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n");
   StaticModelExporter.Write(zip,slug,models);var require=dependencies.Count>0?"require="+string.Join(',',dependencies)+"\n":"";Text(slug+"/42/mod.info",$"name={world.Name}\nid={slug}\nversionMin=42.0\ndescription=Streamed world assembled from {sections.Count} sections.\n{require}");
   var minX=sections.Min(s=>s.WorldX);var minY=sections.Min(s=>s.WorldY);var width=(sections.Max(s=>s.WorldX+s.Width/256)-minX)*256;var height=(sections.Max(s=>s.WorldY+s.Height/256)-minY)*256;Text(root+"map.info",$"title={world.Name}\nfixed2x=true\ndescription=Stitched area {width} x {height}.\nzoomX={minX*256+width/2}\nzoomY={minY*256+height/2}\nzoomS=10\n");
   using(var index=zip.CreateEntry(slug+"/source/world.pzworld.index.json",CompressionLevel.Fastest).Open())await JsonSerializer.SerializeAsync(index,source.AuthoringIndex,NativeMapExporter.JsonOptions,token);using(var metadata=zip.CreateEntry(slug+"/source/world.metadata.json",CompressionLevel.Fastest).Open())await source.CopyMetadataSourceAsync(metadata,token);
   Text(slug+"/source/README.txt","The index pins original immutable content hashes. world.metadata.json preserves shared buildings, paths, fences, decorations, geographic evidence and elevation. Section IDs map to sections/<id>.pzmap.json. Reconstruct SimplePZWorld using metadata Version/Id/Name/Buildings/LinearFeatures/Fences/FenceStyles/GateStyles/Decorations/GeoReference/Elevation and Maps=[{id,project:section JSON}]. A selected section export contains only its section source and retains the complete layout as context; export all sections to reconstruct the complete world.\n");
   if(world.Elevation is {} survey){Text(slug+"/elevation.asc",survey.ToAscii());attributions.Add("Elevation: "+survey.Source);attributions.Add(survey.License);}
   Text(slug+"/ATTRIBUTION.txt",string.Join('\n',attributions.Prepend("Created with SimplePZMapmaker. Sparse streamed Build 42 world."))+"\n");Text(slug+"/INSTALL.txt",$"Extract {slug} into Zomboid/mods, enable the mod, and create a NEW world with the {world.Name} map. Sections share one native map. Original authoring data is reconstructible from source/world.pzworld.index.json, source/world.metadata.json and source/sections/*.pzmap.json. Game resources are not included. For a server: Map={slug}, Mods={slug}.\n");
  }token.ThrowIfCancellationRequested();context?.Complete();
 }
}
