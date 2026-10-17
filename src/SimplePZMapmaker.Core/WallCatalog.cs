using System.Text.Json;
namespace SimplePZMapmaker.Core;

/// <summary>One exterior wall set as TileZed's BuildingEditor draws it: the W and N edges, the NW corner piece that replaces both
/// edges in one square, the SE pillar that closes an outer corner, and the window and door edges.</summary>
public sealed record WallSet(string West,string North,string? NorthWest,string? SouthEast,string? WestWindow,string? NorthWindow,string? WestDoor,string? NorthDoor)
{
 /// <summary>The edge tile of a wall, window or door edge, or null when the set has none.</summary>
 public string? Edge(string kind,bool north)=>kind switch{"door"=>north?NorthDoor:WestDoor,"window"=>north?NorthWindow:WestWindow,_=>north?North:West};
}

/// <summary>The exterior wall sets of TileZed's BuildingTiles.txt (wwwroot/wall-catalog.json, GPL-2.0-or-later). Most sets put the
/// window at +8 and the door at +10 from the west wall, but 42 of them do not, so tiles are looked up here before that rule is used.</summary>
public static class WallCatalog
{
 public static readonly IReadOnlyList<WallSet> Sets=Load();
 static readonly Dictionary<string,WallSet> ByTile=Index();
 static List<WallSet> Load(){using var s=typeof(WallCatalog).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.WallCatalog.json")!;using var d=JsonDocument.Parse(s);
  return d.RootElement.GetProperty("sets").Deserialize<List<WallSet>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;}
 static Dictionary<string,WallSet> Index(){var index=new Dictionary<string,WallSet>(StringComparer.Ordinal);
  foreach(var set in Sets)foreach(var tile in new[]{set.West,set.North,set.WestWindow,set.NorthWindow,set.WestDoor,set.NorthDoor})if(tile is not null)index.TryAdd(tile,set);return index;}
 /// <summary>The set a wall, window or door edge tile belongs to.</summary>
 public static WallSet? Find(string? tile)=>tile is not null&&ByTile.TryGetValue(tile,out var set)?set:null;
 /// <summary>The edge tile of the set whose west wall is <paramref name="prefix"/>_<paramref name="first"/>; outside the catalog, the +8 window and +10 door rule.</summary>
 public static string Tile(string prefix,int first,string kind,bool north)=>
  Find($"{prefix}_{first}") is {} set&&set.West==$"{prefix}_{first}"&&set.Edge(kind,north) is {} tile?tile:$"{prefix}_{first+(kind=="door"?10:kind=="window"?8:0)+(north?1:0)}";
}
