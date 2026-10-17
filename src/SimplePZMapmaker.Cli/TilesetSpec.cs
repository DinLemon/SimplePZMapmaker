using System.Text.Json;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
namespace SimplePZMapmaker.Cli;

/// <summary>A tile mod request (format SimplePZTileset, version 1). Each tile is a PNG frame; copyFrom takes the properties of an
/// installed tile (walls, fences, doors and their flags) and properties then adds or overrides keys.</summary>
public sealed class TilesetSpec
{
 public sealed class Tile {public string Png {get;set;}="";public string? CopyFrom {get;set;}public Dictionary<string,string> Properties {get;set;}=[];}
 public string Format {get;set;}="";
 public int Version {get;set;}
 public string ModId {get;set;}="";
 public string Name {get;set;}="";
 public string Tileset {get;set;}="";
 public int FileNumber {get;set;}
 public List<Tile> Tiles {get;set;}=[];

 public static TilesetMod Read(string path,GameAssetCatalog? assets){
  var spec=JsonSerializer.Deserialize<TilesetSpec>(File.ReadAllText(path),NativeMapExporter.JsonOptions)??throw new ArgumentException("The tileset file is empty.");
  if(spec.Format!="SimplePZTileset"||spec.Version!=1)throw new ArgumentException("Expected a tileset with \"format\": \"SimplePZTileset\" and \"version\": 1.");
  var folder=Path.GetDirectoryName(Path.GetFullPath(path))!;var sprites=new List<TilesetSprite>();
  foreach(var tile in spec.Tiles){
   var properties=new Dictionary<string,string>(StringComparer.Ordinal);
   if(tile.CopyFrom is {} source){if(assets is null)throw new ArgumentException($"copyFrom {source} needs --game <Build 42 installation> to read the original tile properties.");foreach(var (key,value) in assets.Get(source).Properties)properties[key]=value;}
   foreach(var (key,value) in tile.Properties)properties[key]=value;
   sprites.Add(new(File.ReadAllBytes(Path.GetFullPath(Path.Combine(folder,tile.Png))),properties));
  }
  var mod=new TilesetMod(spec.ModId,spec.Name,spec.Tileset,spec.FileNumber,sprites);TilesetBuilder.Validate(mod);return mod;
 }
}
