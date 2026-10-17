using System.Text.Json;
namespace SimplePZMapmaker.Core;

/// <summary>The 25 building templates of TileZed's BuildingTemplates.txt (wwwroot/building-templates.json, GPL-2.0-or-later): Bank, Barn,
/// Diner, Police, Spiffo, Gigamart and others. A template gives the exterior wall set, door and window, and per room (by internal name)
/// the floor and the interior wall with its door wall. pz:style=tilezed:&lt;Name&gt; applies one to an imported building.</summary>
public static class BuildingTemplateStyles
{
 public sealed record Room(string? Floor,string? WallW,string? WallN,string? DoorWallW,string? DoorWallN);
 public sealed record Template(string Exterior,string? DoorW,string? DoorN,string? WindowW,string? WindowN,Dictionary<string,Room> Rooms);
 public static readonly IReadOnlyDictionary<string,Template> All=Load();
 static Dictionary<string,Template> Load(){using var s=typeof(BuildingTemplateStyles).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.BuildingTemplates.json")!;using var d=JsonDocument.Parse(s);
  return new(d.RootElement.GetProperty("templates").Deserialize<Dictionary<string,Template>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!,StringComparer.OrdinalIgnoreCase);}
 public const string Prefix="tilezed:";
 public static Template? Find(string? style)=>style is not null&&style.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)&&All.TryGetValue(style[Prefix.Length..],out var t)?t:null;

 /// <summary>Applies a template to every floor: exterior edges take its wall set (through the wall catalog) with its door and window,
 /// walls between two rooms take the interior wall of the room on their south or east side, and each room takes the floor of the
 /// template room with the same internal name, or of the template's first room.</summary>
 public static void Apply(Building b,Template t){
  if(b.Design is not {} d)return;var split=t.Exterior.LastIndexOf('_');var prefix=t.Exterior[..split];var first=int.Parse(t.Exterior[(split+1)..],System.Globalization.CultureInfo.InvariantCulture);
  var fallback=t.Rooms.Values.FirstOrDefault(r=>r.Floor is not null);
  Room? RoomFor(BuildingFloor f,char code)=>f.Rooms.TryGetValue(code.ToString(),out var name)&&t.Rooms.TryGetValue(name,out var room)?room:fallback;
  foreach(var f in d.Floors){
   foreach(var e in f.Edges){
    if(e.Kind=="open")continue;var north=e.Edge=="n";var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var other=north?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y);
    if(a=='.'&&other=='.'||(a=='.'?other:a) is var inside&&f.Rooms.GetValueOrDefault(inside.ToString())=="balcony")continue;
    if(a=='.'||other=='.'){
     e.WallTile=WallCatalog.Tile(prefix,first,e.Kind,north);
     if(e.Kind=="door"&&(north?t.DoorN:t.DoorW) is {} door)e.OpeningTile=door;else if(e.Kind=="window"&&(north?t.WindowN:t.WindowW) is {} window)e.OpeningTile=window;
    }else if(RoomFor(f,a) is {} room){
     if(e.Kind=="wall"&&(north?room.WallN:room.WallW) is {} wall)e.WallTile=wall;else if(e.Kind=="door"&&(north?room.DoorWallN:room.DoorWallW) is {} doorWall){e.WallTile=doorWall;if((north?t.DoorN:t.DoorW) is {} door)e.OpeningTile=door;}
    }
   }
   for(var i=0;i<f.Cells.Length;i++)if(f.Cells[i]!='.'&&RoomFor(f,f.Cells[i])?.Floor is {} floor&&f.Rooms.GetValueOrDefault(f.Cells[i].ToString())!="balcony")f.FloorTiles[i]=floor;
  }
 }
}
