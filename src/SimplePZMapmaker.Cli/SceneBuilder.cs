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
  if(scene.Export is {} export)NativeModPackage.Validate(export);
  return scene;
 }
 /// <summary>Concatenates the features of several FeatureCollections; a feature id seen twice is kept once.</summary>
 public static string MergeSources(IEnumerable<string> files){
  var features=new JsonArray();var seen=new HashSet<string>(StringComparer.Ordinal);
  foreach(var file in files){var root=JsonNode.Parse(File.ReadAllText(file))??throw new ArgumentException(file+" is empty.");
   if(root["features"] is not JsonArray list)throw new ArgumentException(file+" is not a GeoJSON FeatureCollection; pass a single Overpass JSON as \"source\".");
   foreach(var feature in list.ToList()){var id=feature?["id"]?.ToString();if(id is not null&&!seen.Add(id))continue;list.Remove(feature);features.Add(feature);}}
  return new JsonObject{["type"]="FeatureCollection",["pzTransfer"]=1,["features"]=features}.ToJsonString();
 }
 /// <summary>Contract section 2: section identity is SHA-256(world name | X,Y), so rebuilds keep it.</summary>
 public static string SectionId(string world,int x,int y)=>Hash($"{world}|{x},{y}");
 static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

 public static SceneBuild Build(Scene scene,string sceneDirectory,Action<WorldProject>? validate=null,Action<string>? progress=null,CancellationToken token=default){
  var files=(scene.Source is null?scene.Sources:[scene.Source,..scene.Sources]).Select(f=>Path.GetFullPath(Path.Combine(sceneDirectory,f))).ToList();
  var json=files.Count==1?File.ReadAllText(files[0]):MergeSources(files);
  var reference=scene.Reference;WorldGeoReference.Validate(reference);var size=GeographicProjection.RequiredTiles(reference);
  var columns=Math.Max(1,(int)Math.Ceiling(size.Width/(double)scene.SectionSize));var rows=Math.Max(1,(int)Math.Ceiling(size.Height/(double)scene.SectionSize));var step=scene.SectionSize/256;
  if(columns*rows>128||columns*rows*step*step>8192)throw new ArgumentException($"The area needs {columns}x{rows} sections; a world holds up to 128 sections and 8,192 native cells. Increase metresPerTile or the section size.");
  var index=new GeographicSourceIndex(json);   // each section imports only its neighbourhood of the source
  var world=new WorldProject{Name=scene.Name,Id=Hash("world|"+scene.Name),GeoReference=reference};var warnings=new HashSet<string>(StringComparer.Ordinal);var skipped=0;
  for(var y=0;y<rows;y++)for(var x=0;x<columns;x++){
   token.ThrowIfCancellationRequested();var cellX=reference.OriginCellX+x*step;var cellY=reference.OriginCellY+y*step;var projection=new GeographicProjection(reference,cellX,cellY);
   var project=MapGenerator.Generate(new(){Kind="blank",Size=scene.SectionSize,Seed=scene.Seed,Name=$"{scene.Name[..Math.Min(60,scene.Name.Length)]} {x+1}/{y+1}"});project.WorldX=cellX;project.WorldY=cellY;project.WorldSection=true;project.Version=Math.Max(project.Version,6);
   var bounds=projection.SectionBounds(scene.SectionSize,scene.SectionSize);var result=GeographicImporter.Import(index.For(bounds),project,bounds,token,projection:projection);project=result.Project;foreach(var w in result.Warnings)warnings.Add(w);
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
