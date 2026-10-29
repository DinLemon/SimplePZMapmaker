using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SimplePZMapmaker.Core.Export;
public static class NativeWorldExporter
{
 public static void ExportSectionToStream(WorldProject world,string memberId,Stream output,CancellationToken token=default)=>ExportSectionWithProgress(world,memberId,output,null,token);
 public static void ExportSectionWithProgress(WorldProject world,string memberId,Stream output,NativeExportContext? context,CancellationToken token=default){WorldAssembler.Validate(world);var member=world.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("Section not found.");var p=WorldAssembler.ProjectSection(world,member);context?.Begin(1,p.Width/256*(p.Height/256));context?.Section(memberId);var sharedContext=new{format="SimplePZSectionContext",version=1,worldId=world.Id,worldName=world.Name,memberId,sections=world.Maps.Select(m=>new{m.Id,m.Project.WorldX,m.Project.WorldY,m.Project.Width,m.Project.Height}),buildings=world.Buildings.Where(b=>b.X<p.WorldX*256+p.Width&&b.Y<p.WorldY*256+p.Height&&b.X+b.Width>=p.WorldX*256&&b.Y+b.Height>=p.WorldY*256),world.GeoReference,world.LinearFeatures,world.Fences,world.FenceStyles,world.GateStyles,decorations=WorldAssembler.DecorationsFor(world.Decorations,member.Project).Select(d=>world.Decorations.Single(s=>s.Id==d.Id)),note="Native cells include clipped shared buildings. project.pzmap.json retains only the independently editable local section; shared buildings remain authoritative in the original world workspace. Export the complete world for a single combined mod."};NativeMapExporter.WriteValidatedWorldSection(p,output,token,member.Project,sharedContext,"SimplePZSection_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.Id+":"+memberId)))[..12].ToLowerInvariant(),context);context?.SectionDone();context?.Complete();}
 public static byte[] Export(WorldProject world,CancellationToken token=default){
  using var output=new MemoryStream();ExportToStream(world,output,token);return output.ToArray();
 }
 public static void ExportToStream(WorldProject world,Stream output,CancellationToken token=default)=>ExportWithProgress(world,output,null,token);
 public static void ExportWithProgress(WorldProject world,Stream output,NativeExportContext? context,CancellationToken token=default){
  var bounds=WorldAssembler.Validate(world);context?.Begin(world.Maps.Count,world.Maps.Sum(m=>m.Project.Width/256*(m.Project.Height/256)));var slug="SimplePZWorld_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.Id)))[..12].ToLowerInvariant();var root=slug+"/42/media/maps/"+slug+"/";
  if(!output.CanWrite||!output.CanSeek)throw new ArgumentException("World export requires a writable, seekable destination.");using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
   var metadata=new NativeExportMetadata(zip,slug,NativeMetadataKind.World,bounds.X,bounds.Y,bounds.Width,bounds.Height);
   void Text(string name,string value)=>metadata.Text(name,value);
   zip.CreateEntry(slug+"/common/");
   var sharedGeometry=world.LinearFeatures.Count>0?WorldInfrastructure.Build(world):null;
   foreach(var member in world.Maps){token.ThrowIfCancellationRequested();var p=WorldAssembler.ProjectSection(world,member,sharedGeometry);context?.Section(member.Id);
    NativeMapExporter.WriteCells(p,zip,root,token,context);context?.SectionDone();
    metadata.AddSection(p,token);
    if(output.Length>256*1024*1024)throw new ArgumentException("World export exceeds 256 MB; split it into multiple mods.");
   }
   metadata.WriteZones();metadata.WriteAtlas();metadata.WriteSpawns();
   var dependencies=world.Maps.SelectMany(m=>ResourceDependencies.ForProject(m.Project)).Concat(world.LinearFeatures.SelectMany(f=>f.AssetSources.Values).Where(id=>id!="game")).Concat(world.Buildings.SelectMany(b=>b.RequiredMods)).Concat(world.Buildings.SelectMany(b=>b.Design?.Floors.SelectMany(f=>f.StaticModels.SelectMany(m=>m.RequiredMods.Concat(m.LootRequiredMods)))??[])).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
   StaticModelExporter.Write(zip,slug,world.Maps.SelectMany(m=>StaticModelExporter.Gather(m.Project)).Concat(WorldAssembler.GatherModels(world)));
   metadata.WriteModInfo(world.Name,$"World assembled from {world.Maps.Count} sections; new Build 42 world.",dependencies);metadata.WriteMapInfo(world.Name,$"Stitched area {bounds.Width} x {bounds.Height}; {bounds.Cells} cells.",10);
   using(var json=zip.CreateEntry(slug+"/world.pzworld.json",CompressionLevel.Fastest).Open())JsonSerializer.Serialize(json,world,NativeMapExporter.JsonOptions);
   metadata.WriteElevation(world.Elevation);
   metadata.WriteAttribution("Created with SimplePZMapmaker. Sparse world export;256-cell/8-chunk Build42.");Text(slug+"/INSTALL.txt",$"Extract {slug} into Zomboid/mods, enable the mod, and create a NEW world with the {world.Name} map. Sections are combined into one map; cell overlaps were checked. All original sections are preserved in world.pzworld.json. Game resources are not included. Do not add this to an important existing save. For a server: Map={slug}, Mods={slug}.\n{NativeZombieIntensity.ExportNotice}\n");
  }context?.Complete();if(output.Length>256*1024*1024)throw new ArgumentException("World export exceeds 256 MB; split it into multiple mods.");
 }
}
