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
  "bedroom" or "hotelroom" or "motelroom"=>["bed","dresser"],"kitchen" or "restaurantkitchen" or "cafekitchen"=>["counter","stove","fridge","sink","counter"],
  "bathroom"=>["toilet","sink","bath"],"livingroom"=>["sofa","tv","armchair"],_=>[]};
 public static bool Furnishes(string room)=>RolesFor(room).Length>0;

 /// <summary>Places a room's furniture with its back to a wall: every square of the item lies in the room, away from doorways, stairs and
 /// other objects, the squares on its back side are outside the room, and the square in front of it stays free.</summary>
 public static void Furnish(Building b,BuildingFloor f,int z,char code,string room,HashSet<int> passages,Random rng){
  var taken=f.Objects.SelectMany(BuildingObjects.Cells).Select(p=>p.Y*b.Width+p.X).ToHashSet();var fronts=new HashSet<int>();
  bool In(int x,int y)=>BuildingDesigner.Cell(b,f,x,y)==code;
  var cells=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==code).ToList();
  foreach(var role in RolesFor(room)){
   var choices=Roles[role];var item=choices[rng.Next(choices.Length)];var placed=false;
   foreach(var i in cells.OrderBy(_=>rng.Next())){
    foreach(var direction in Directions.OrderBy(_=>rng.Next())){
     var parts=Parts(item,direction);var x0=i%b.Width;var y0=i/b.Width;var squares=parts.Select(p=>(X:x0+p.Dx,Y:y0+p.Dy)).ToArray();
     if(squares.Any(s=>!In(s.X,s.Y)||taken.Contains(s.Y*b.Width+s.X)||fronts.Contains(s.Y*b.Width+s.X)||passages.Contains(s.Y*b.Width+s.X)||BuildingDesigner.StairSpace(b,z,s.X,s.Y)))continue;
     var (bx,by)=direction switch{"n"=>(0,-1),"s"=>(0,1),"w"=>(-1,0),_=>(1,0)};
     if(squares.Any(s=>!squares.Contains((s.X+bx,s.Y+by))&&In(s.X+bx,s.Y+by)))continue;   // back against a wall
     var front=squares.Select(s=>(X:s.X-bx,Y:s.Y-by)).Where(s=>!squares.Contains(s)).ToArray();
     if(front.Any(s=>!In(s.X,s.Y)||taken.Contains(s.Y*b.Width+s.X)))continue;            // room to use it
     f.Objects.Add(new(){X=x0,Y=y0,Kind="furniture",Direction=direction,Asset=item});
     foreach(var s in squares)taken.Add(s.Y*b.Width+s.X);foreach(var s in front)fronts.Add(s.Y*b.Width+s.X);placed=true;break;
    }
    if(placed)break;
   }
  }
 }
}
