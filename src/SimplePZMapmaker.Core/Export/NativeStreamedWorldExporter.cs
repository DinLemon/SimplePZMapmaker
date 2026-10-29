using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
  ValidateLayout(source.Sections,token);
  var sections=memberId is null?source.Sections:source.Sections.Where(s=>s.Id==memberId).ToArray();if(sections.Count==0)throw new ArgumentException("Section not found.");
  var world=source.Metadata;var slug=(memberId is null?"SimplePZWorld_":"SimplePZSection_")+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(world.Id+(memberId is null?"":":"+memberId))))[..12];var root=slug+"/42/media/maps/"+slug+"/";
  context?.Begin(sections.Count,sections.Sum(s=>s.Width/256*(s.Height/256)));using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
   var minX=sections.Min(s=>s.WorldX);var minY=sections.Min(s=>s.WorldY);var width=(sections.Max(s=>s.WorldX+s.Width/256)-minX)*256;var height=(sections.Max(s=>s.WorldY+s.Height/256)-minY)*256;
   var metadata=new NativeExportMetadata(zip,slug,NativeMetadataKind.StreamedWorld,minX,minY,width,height);
   void Text(string name,string value)=>metadata.Text(name,value);
   zip.CreateEntry(slug+"/common/");var dependencies=new SortedSet<string>(StringComparer.Ordinal);var models=new List<StaticModelExporter.PlacedStaticModel>();var modelIds=new HashSet<(string,int,int,int)>();
   foreach(var section in sections){
    token.ThrowIfCancellationRequested();context?.Section(section.Id);
    await WriteSection(section);
    context?.SectionDone();
   }
   async Task WriteSection(StreamedWorldSection section){
    var authored=await source.ReadSectionAsync(section.Id,token);var p=await source.ProjectSectionAsync(section.Id,authored,token);
    NativeMapExporter.WriteCells(p,zip,root,token,context);
    metadata.AddSection(p,token);
    foreach(var dependency in ResourceDependencies.ForProject(p))dependencies.Add(dependency);
    foreach(var model in StaticModelExporter.Gather(p).Where(m=>m.X>=p.WorldX*256&&m.Y>=p.WorldY*256&&m.X<p.WorldX*256+p.Width&&m.Y<p.WorldY*256+p.Height)){if(modelIds.Add((model.Placement.Id,model.X,model.Y,model.Z)))models.Add(model);if(models.Count>100000)throw new ArgumentException("World static models exceed 100,000 placements.");}
    using(var entry=zip.CreateEntry(slug+"/source/sections/"+section.Id+".pzmap.json",CompressionLevel.Fastest).Open())await source.CopySectionSourceAsync(section.Id,entry,token);
    if(memberId is not null){using(var entry=zip.CreateEntry(slug+"/project.pzmap.json",CompressionLevel.Fastest).Open())await source.CopySectionSourceAsync(section.Id,entry,token);Text(slug+"/section-world-context.json",JsonSerializer.Serialize(new{format="SimplePZSectionContext",version=1,worldId=world.Id,worldName=world.Name,memberId,sections=source.Sections,world.Buildings,world.LinearFeatures,world.Fences,world.FenceStyles,world.GateStyles,world.Decorations,world.GeoReference},NativeMapExporter.JsonOptions));}
   }
   metadata.WriteZones();metadata.WriteAtlas();metadata.WriteSpawns(requireSpawn:memberId is null);
   StaticModelExporter.Write(zip,slug,models);metadata.WriteModInfo(world.Name,$"Streamed world assembled from {sections.Count} sections.",dependencies);
   metadata.WriteMapInfo(world.Name,$"Stitched area {width} x {height}.",10);
   using(var index=zip.CreateEntry(slug+"/source/world.pzworld.index.json",CompressionLevel.Fastest).Open())await JsonSerializer.SerializeAsync(index,source.AuthoringIndex,NativeMapExporter.JsonOptions,token);using(var metadataSource=zip.CreateEntry(slug+"/source/world.metadata.json",CompressionLevel.Fastest).Open())await source.CopyMetadataSourceAsync(metadataSource,token);
   Text(slug+"/source/README.txt","The index pins original immutable content hashes. world.metadata.json preserves shared buildings, paths, fences, decorations, geographic evidence and elevation. Section IDs map to sections/<id>.pzmap.json. Reconstruct SimplePZWorld using metadata Version/Id/Name/Buildings/LinearFeatures/Fences/FenceStyles/GateStyles/Decorations/GeoReference/Elevation and Maps=[{id,project:section JSON}]. A selected section export contains only its section source and retains the complete layout as context; export all sections to reconstruct the complete world.\n");
   metadata.WriteElevation(world.Elevation);
   metadata.WriteAttribution("Created with SimplePZMapmaker. Sparse streamed Build 42 world.");Text(slug+"/INSTALL.txt",$"Extract {slug} into Zomboid/mods, enable the mod, and create a NEW world with the {world.Name} map. Sections share one native map. Original authoring data is reconstructible from source/world.pzworld.index.json, source/world.metadata.json and source/sections/*.pzmap.json. Game resources are not included. For a server: Map={slug}, Mods={slug}.\n{NativeZombieIntensity.ExportNotice}\n");
  }token.ThrowIfCancellationRequested();context?.Complete();
 }
 private static void ValidateLayout(IReadOnlyList<StreamedWorldSection> sections,CancellationToken token){
  if(sections is null||sections.Count is <1 or >WorldLayoutLimits.MaxSections)throw new ArgumentException("Streamed worlds require 1-65,536 sections.");
  var ids=new HashSet<string>(StringComparer.Ordinal);var cells=new HashSet<(int,int)>();
  foreach(var s in sections){
   token.ThrowIfCancellationRequested();
   if(s is null||!WorldAssembler.SafeId(s.Id)||!ids.Add(s.Id)||string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>80||s.Name.Any(char.IsControl)||s.WorldX is <0 or >1000||s.WorldY is <0 or >1000||s.Width is <256 or >2048||s.Height is <256 or >2048||s.Width%256!=0||s.Height%256!=0)throw new ArgumentException("Invalid streamed world section layout.");
   for(var y=s.WorldY;y<s.WorldY+s.Height/256;y++)for(var x=s.WorldX;x<s.WorldX+s.Width/256;x++)if(!cells.Add((x,y))||cells.Count>WorldLayoutLimits.MaxWorldCells)throw new ArgumentException("Streamed world cells overlap or exceed the 65,536-cell budget.");
  }
 }
}
