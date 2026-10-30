using System.Text.Json;
namespace SimplePZMapmaker.Core;

/// <summary>Furniture from TileZed's BuildingFurniture.txt (wwwroot/furniture-catalog.json, GPL-2.0-or-later): beds, dressers, counters,
/// stoves, fridges, sinks, toilets, baths, sofas, armchairs and TVs, each with its W, N, E and S layouts. The orientation is the wall the
/// item backs onto, so an "n" bed has its head at the north wall. A "furniture" building object names one item in Asset.</summary>
public static class FurnitureCatalog
{
 public static readonly IReadOnlyDictionary<string,Dictionary<string,BuildingObjects.Part[]>> Items;
 public static readonly IReadOnlyDictionary<string,string[]> Roles;
 static FurnitureCatalog(){
  using var s=typeof(FurnitureCatalog).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.FurnitureCatalog.json")!;using var d=JsonDocument.Parse(s);var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
  Items=d.RootElement.GetProperty("furniture").Deserialize<Dictionary<string,Dictionary<string,BuildingObjects.Part[]>>>(options)!;
  Roles=d.RootElement.GetProperty("roles").Deserialize<Dictionary<string,string[]>>(options)!;
 }
 public static readonly string[] Directions=["n","w","e","s"];
 public static BuildingObjects.Part[] Parts(string item,string direction)=>Items[item][direction];
 /// <summary>The wall an item backs onto after the building is transposed (x and y swapped) and then mirrored.</summary>
 public static string Transform(string direction,bool transpose,bool flipX,bool flipY){
  if(transpose)direction=direction switch{"n"=>"w","w"=>"n","s"=>"e",_=>"s"};
  if(flipX)direction=direction switch{"w"=>"e","e"=>"w",_=>direction};if(flipY)direction=direction switch{"n"=>"s","s"=>"n",_=>direction};return direction;}
 /// <summary>The wall an item backs onto after a mirror in x and then <paramref name="quarterTurns"/> clockwise quarter turns.</summary>
 public static string Rotate(string direction,bool mirror,int quarterTurns){
  if(mirror)direction=Transform(direction,false,true,false);string[] order=["n","e","s","w"];return order[(Array.IndexOf(order,direction)+quarterTurns)%4];}
 public static bool Known(string? item,string direction)=>item is not null&&Items.TryGetValue(item,out var layouts)&&layouts.ContainsKey(direction);

 /// <summary>What each room gets, in placement order; a role that finds no wall space is left out.</summary>
 static string[] RolesFor(string room)=>room switch{
  "bedroom" or "hotelroom" or "motelroom"=>["bed","dresser"],
  var name when name.EndsWith("kitchen",StringComparison.Ordinal)=>["stove","sink","fridge","counter","counter"],
  "bathroom"=>["toilet","sink","bath"],"publicbathroom"=>["toilet","sink"],"livingroom"=>["sofa","tv","armchair"],_=>[]};
 public static bool Furnishes(string room)=>RolesFor(room).Length>0;

 /// <summary>Places a room's furniture with its back to a wall: every square of the item lies in the room, away from doorways, stairs and
 /// other objects, the squares on its back side are outside the room, and the square in front of it stays free.</summary>
 public static void Furnish(Building b,BuildingFloor f,int z,char code,string room,HashSet<int> passages,Random rng){
  var placement=new FurniturePlacement(b,f,z,code,passages);
  // Shuffle once, then try every suitable variant: a large first choice must not leave a small room empty.
  var cells=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==code).ToArray();rng.Shuffle(cells);
  var selected=new Dictionary<string,string>();
  foreach(var role in RolesFor(room)){
   var choices=Roles[role].ToArray();rng.Shuffle(choices);if(selected.TryGetValue(role,out var matching))choices=[matching,..choices.Where(c=>c!=matching)];var directions=Directions.ToArray();rng.Shuffle(directions);var placed=false;
   foreach(var item in choices){foreach(var i in cells){foreach(var direction in directions){
     if(placement.Place(new(){X=i%b.Width,Y=i/b.Width,Kind="furniture",Direction=direction,Asset=item},true)){selected[role]=item;placed=true;break;}
    }if(placed)break;}
    if(placed)break;
   }
  }
 }
}
