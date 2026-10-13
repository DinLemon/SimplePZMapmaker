using System.Text.Json;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core;
public static class VisualStyles
{
 public sealed class Style {
  public string Name {get;set;}=""; public string Material {get;set;}="wood";public string Wall {get;set;}="";public int WallBase {get;set;}
  public string Floor {get;set;}="";public string WetFloor {get;set;}="";public string Roof {get;set;}="";public int DoorBase {get;set;}public int WindowBase {get;set;}
  public int[] BedN {get;set;}=[];public int[] BedW {get;set;}=[];public int ChairN {get;set;}public int ChairW {get;set;}public int StorageN {get;set;}public int StorageW {get;set;}public int CounterN {get;set;}public int CounterW {get;set;}public int SinkN {get;set;}public int SinkW {get;set;}
 }
 public static IReadOnlyDictionary<string,Style> All {get;}=Load();
 private static Dictionary<string,Style> Load(){using var s=typeof(VisualStyles).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.VisualStyles.json")!;return JsonSerializer.Deserialize<Dictionary<string,Style>>(s,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;}
 public static bool TileId(string? id)=>id is not null&&id.Length<=100&&Regex.IsMatch(id,@"^[a-zA-Z0-9_]+_[0-9]{1,6}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
 public static BuildingObjects.Part[] Parts(BuildingObject o){var s=All[o.Style];var n=o.Direction=="n";if(o.Kind=="bed"){var pair=n?s.BedN:s.BedW;return [new(0,0,"furniture_bedding_01_"+pair[0]),new(n?0:1,n?1:0,"furniture_bedding_01_"+pair[1])];}return [new(0,0,o.Kind switch{"seat"=>"furniture_seating_indoor_01_"+(n?s.ChairN:s.ChairW),"storage"=>"furniture_storage_01_"+(n?s.StorageN:s.StorageW),"counter"=>"fixtures_counters_01_"+(n?s.CounterN:s.CounterW),"sink"=>"fixtures_sinks_01_"+(n?s.SinkN:s.SinkW),_=>throw new ArgumentException("Unknown appearance object.")})];}
 public static void Apply(Building b,string id="auto"){if(b.Design is null)return;if(id=="auto"){var choices=b.Kind is "warehouse" or "industrial" or "garage"?new[]{"industrial","commercial"}:b.Kind is "school" or "clinic" or "shop"?new[]{"commercial","modern","brick"}:new[]{"cottage","brick","stone","modern"};id=choices[(int)(unchecked((uint)b.Design.Variant)%choices.Length)];}if(id.Length>0&&!All.ContainsKey(id))throw new ArgumentException("Unknown appearance style.");b.Design.Style=id;if(id.Length>0)b.Design.Material=All[id].Material;foreach(var f in b.Design.Floors)foreach(var o in f.Objects.Where(o=>o.Kind is not("stairs" or "asset")))o.Style=id;}
 public static void ValidateParts(string? asset,List<BuildingObjects.Part>? parts){if(!TileId(asset)||parts is null||parts.Count is <1 or >32||parts.Any(p=>p is null||p.Dx is <0 or >7||p.Dy is <0 or >7||p.Flags>7||!TileId(p.Tile))||parts.Select(p=>(p.Dx,p.Dy)).Distinct().Count()!=parts.Count)throw new ArgumentException("Invalid complete game tile group.");}
}
