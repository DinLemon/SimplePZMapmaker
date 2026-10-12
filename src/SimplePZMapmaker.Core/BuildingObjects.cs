using System.Text.Json;
namespace SimplePZMapmaker.Core;
public static class BuildingObjects
{
 public sealed record Part(int Dx,int Dy,string Tile);
 private static readonly Dictionary<string,Dictionary<string,Part[]>> Catalog=Load();
 private static Dictionary<string,Dictionary<string,Part[]>> Load(){using var stream=typeof(BuildingObjects).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.BuildingObjects.json")!;return JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,Part[]>>>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;}
 public static Part[] Parts(BuildingObject o)=>Catalog[o.Kind][o.Direction];
 public static IEnumerable<(int X,int Y)> Cells(BuildingObject o)=>o.Kind=="stairs"?Enumerable.Range(0,5).Select(i=>(o.X+(o.Direction=="w"?i:0),o.Y+(o.Direction=="n"?i:0))):Parts(o).Select(p=>(o.X+p.Dx,o.Y+p.Dy));
}
