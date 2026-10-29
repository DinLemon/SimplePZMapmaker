using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Cli;

/// <summary>A headless world build request (format SimplePZScene, version 1). Paths are relative to the scene file.</summary>
public sealed class Scene
{
 public string Format {get;set;}="";
 public int Version {get;set;}
 public string Name {get;set;}="";
 public string? Source {get;set;}
 public List<string> Sources {get;set;}=[];
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
 public bool AlignStreetGrid {get;set;}
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
 public string? OvertureBuildings {get;set;}
 public WorldGeoReference Reference {get;set;}=new();
 public int SectionSize {get;set;}=1024;
 public int Seed {get;set;}=42;
 public bool SkipEmptySections {get;set;}=true;
 public NativeModOptions? Export {get;set;}
}
public sealed record SceneBuild(WorldProject World,string SourceJson,List<string> Warnings,TransferReport Report);

/// <summary>Imports a GeoJSON/OSM scene into one geographic world the same way the editor's world import does:
/// every section imports the whole source, crossing fences and buildings are promoted to the world, and the seam check runs last.</summary>
public static class SceneBuilder
{
 public static Scene Read(string path){
  var scene=JsonSerializer.Deserialize<Scene>(File.ReadAllText(path),NativeMapExporter.JsonOptions)??throw new ArgumentException("The scene file is empty.");
  if(scene.Format!="SimplePZScene"||scene.Version!=1)throw new ArgumentException("Expected a scene with \"format\": \"SimplePZScene\" and \"version\": 1.");
  if(string.IsNullOrWhiteSpace(scene.Name)||scene.Name.Length>80||scene.Name.Any(char.IsControl))throw new ArgumentException("Scene name: 1-80 characters.");
  if(scene.Source is null&&scene.Sources.Count==0)throw new ArgumentException("The scene needs \"source\" or \"sources\" (GeoJSON or Overpass JSON files).");
  if(scene.SectionSize is <256 or >2048||scene.SectionSize%256!=0)throw new ArgumentException("Section size: 256-2048 tiles in multiples of 256.");
  ValidateKnoxOptions(scene);
  if(scene.Export is {} export)NativeModPackage.Validate(export);
  return scene;
 }
 /// <summary>Compose canonical source identities in listed order. A later complete feature replaces
 /// an equal identity; geometric overlap alone is never treated as identity.</summary>
 public static string MergeSources(IEnumerable<string> files){
  string? combined=null;
  foreach(var file in files){var next=ReadSource(file);combined=GeographicImporter.CombineSources(combined is null?[next]:[combined,next],64_000_000).GeoJson;}
  return combined??throw new ArgumentException("Choose at least one scene source.");
 }
 static string ReadSource(string path){
  // Check the open file before allocating its text; deny concurrent writes while reading.
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length>64_000_000)throw new ArgumentException("A scene source exceeds 64 MB.");
  using var reader=new StreamReader(stream);var json=reader.ReadToEnd();
  if(Encoding.UTF8.GetByteCount(json)>64_000_000)throw new ArgumentException("A scene source exceeds 64 MB of UTF8.");
  return json;
 }
 static void ValidateKnoxOptions(Scene scene){
  if(scene.OvertureBuildings is {} path&&(string.IsNullOrWhiteSpace(path)||path.Length>4096||path.Any(char.IsControl)||Path.IsPathRooted(path)))throw new ArgumentException("overtureBuildings must be a relative saved GeoJSON path of 1-4096 characters without control characters.");
  if(scene.AlignStreetGrid&&scene.Reference.Warp is not null)throw new ArgumentException("alignStreetGrid cannot be combined with reference.warp: its precomputed deformation uses the manual rotation. Align the source before constructing a warp.");
 }
 /// <summary>Contract section 2: section identity is SHA-256(world name | X,Y), so rebuilds keep it.</summary>
 public static string SectionId(string world,int x,int y)=>Hash($"{world}|{x},{y}");
 static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

 public static SceneBuild Build(Scene scene,string sceneDirectory,Action<WorldProject>? validate=null,Action<string>? progress=null,CancellationToken token=default){
  token.ThrowIfCancellationRequested();ValidateKnoxOptions(scene);WorldGeoReference.Validate(scene.Reference);
  var files=(scene.Source is null?scene.Sources:[scene.Source,..scene.Sources]).Select(f=>Path.GetFullPath(Path.Combine(sceneDirectory,f))).ToList();
  var json=files.Count==1?ReadSource(files[0]):MergeSources(files);
  var warnings=new HashSet<string>(StringComparer.Ordinal);
  var reference=JsonSerializer.Deserialize<WorldGeoReference>(JsonSerializer.Serialize(scene.Reference,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
  if(scene.OvertureBuildings is {} overture){
   var supplement=GeographicImporter.MergeOvertureBuildings(json,ReadSource(Path.GetFullPath(Path.Combine(sceneDirectory,overture))),reference.Bounds,token);
   json=supplement.Json;foreach(var warning in supplement.Warnings)warnings.Add(warning);
   warnings.Add($"Overture buildings: candidates {supplement.Candidates}, added {supplement.Added}, existing {supplement.Existing}, too small {supplement.TooSmall}, outside {supplement.Outside}.");
  }
  if(scene.AlignStreetGrid){
   var grid=GeographicImporter.SuggestStreetGrid(json,reference.Bounds,token);
   if(grid.RotationDegrees is {} angle)reference.RotationDegrees=angle;
   else warnings.Add($"Street grid: keeping manual rotation {reference.RotationDegrees} degrees. {grid.Reason}");
  }
  var size=GeographicProjection.RequiredTiles(reference);
  var columns=Math.Max(1,(int)Math.Ceiling(size.Width/(double)scene.SectionSize));var rows=Math.Max(1,(int)Math.Ceiling(size.Height/(double)scene.SectionSize));var step=scene.SectionSize/256;
  if(columns*rows>128||columns*rows*step*step>8192)throw new ArgumentException($"The area needs {columns}x{rows} sections; a world holds up to 128 sections and 8,192 native cells. Increase metresPerTile or the section size.");
  var index=new GeographicSourceIndex(json);   // each section imports only its neighbourhood of the source
  var world=new WorldProject{Name=scene.Name,Id=Hash("world|"+scene.Name),GeoReference=reference};var skipped=0;
  for(var y=0;y<rows;y++)for(var x=0;x<columns;x++){
   token.ThrowIfCancellationRequested();var cellX=reference.OriginCellX+x*step;var cellY=reference.OriginCellY+y*step;var projection=new GeographicProjection(reference,cellX,cellY);
   var project=MapGenerator.Generate(new(){Kind="blank",Size=scene.SectionSize,Seed=scene.Seed,Name=$"{scene.Name[..Math.Min(60,scene.Name.Length)]} {x+1}/{y+1}"});project.WorldX=cellX;project.WorldY=cellY;project.WorldSection=true;project.Version=Math.Max(project.Version,6);
   var bounds=projection.SectionBounds(scene.SectionSize,scene.SectionSize);var intersects=bounds.West<bounds.East&&bounds.South<bounds.North;
   var sectionJson=intersects?index.For(bounds):null;
   if(sectionJson is not null&&SimplePZMapmaker.Web.Services.OsmGeometrySet.HasFeatures(sectionJson)){var result=GeographicImporter.Import(sectionJson,project,bounds,token,projection:projection,regionalProfile:reference.RegionalProfile??"default");project=result.Project;foreach(var w in result.Warnings)warnings.Add(w);}else{project.Bounds=intersects?bounds:null;project.MetresPerTile=reference.MetresPerTile;}
   if(scene.SkipEmptySections&&project.Buildings.Count==0&&project.Fences.Count==0&&project.LinearFeatures.Count==0){skipped++;continue;}
   world.Maps.Add(new(){Id=SectionId(scene.Name,cellX,cellY),Project=project});progress?.Invoke($"section {x+1}/{y+1}: {project.Buildings.Count} buildings, {project.Fences.Count} fence lines");
  }
  if(world.Maps.Count==0)throw new ArgumentException("No section received buildings, fences or paths. Check the source and the reference bounds.");
  if(world.Maps.All(m=>m.Project.Spawns.Count==0)){var first=world.Maps[0].Project;first.Spawns=[MapGenerator.FindSafeSpawn(first)];}
  if(skipped>0)warnings.Add($"Empty sections left out of the world: {skipped}.");
  var notes=new List<string>();GeographicImporter.PromoteSharedFences(world,notes);GeographicImporter.ImportWorldBuildings(json,world,scene.Seed,notes,token);foreach(var n in notes)warnings.Add(n);
  if(validate is null)WorldAssembler.Validate(world);else validate(world);
  return new(world,json,warnings.ToList(),GeographicImporter.TransferCheck(json,world));
 }
}
