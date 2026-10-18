using System.Text;
namespace SimplePZMapmaker.Core.Export;

/// <summary>spawnpoints.lua like the vanilla maps: players start inside houses, and a profession with a workplace on the map can also
/// start there (vanilla Muldraugh adds the police station for police officers and doctors' houses for doctors). A house is a building
/// with a bedroom; its point is a free square near the middle of the living room, kitchen or bedroom on the ground floor. Every
/// Build 42 profession gets a key, since the game only falls back to unemployed in some screens. Without houses the authored
/// spawn points are used.</summary>
public static class SpawnPlanner
{
 public sealed record Point(int X,int Y,int Z);
 /// <summary>The profession keys of the vanilla Build 42 spawnpoints.lua.</summary>
 public static readonly string[] Professions=["unemployed","burgerflipper","burglar","carpenter","chef","constructionworker","doctor","electrician","engineer","farmer","fireofficer","fisherman","fitnessInstructor","lumberjack","mechanics","metalworker","nurse","parkranger","policeofficer","rancher","repairman","securityguard","smither","tailor","veteran"];
 static readonly Dictionary<string,string[]> Workplaces=new(StringComparer.Ordinal){
  ["doctor"]=["medicaloffice","medicalstorage","dentist"],["nurse"]=["medicaloffice","medicalstorage"],["policeofficer"]=["policeoffice","policestorage"],["fireofficer"]=["firestorage"],
  ["chef"]=["restaurantkitchen","cafekitchen","bakerykitchen"],["burgerflipper"]=["burgerkitchen"],["mechanics"]=["mechanic"],["farmer"]=["farmstorage","barn"],["rancher"]=["barn"],
  ["carpenter"]=["toolstore"],["securityguard"]=["bank"],["electrician"]=["electronicstore"]};
 const int MaximumPerList=256;

 /// <summary>House points and workplace points per profession, in world tiles, for buildings offset by (dx, dy).</summary>
 public sealed class Plan
 {
  public List<Point> Homes {get;}=[];
  public List<Point> Authored {get;}=[];
  public Dictionary<string,List<Point>> Work {get;}=new(StringComparer.Ordinal);
  public void Add(IEnumerable<Building> buildings,int dx,int dy){
   foreach(var b in buildings.OrderBy(b=>b.Y).ThenBy(b=>b.X)){
    if(b.Design is not {} d||d.MinLevel>0||-d.MinLevel>=d.Floors.Count)continue;var floor=d.Floors[-d.MinLevel];
    if(floor.Rooms.ContainsValue("bedroom")||d.Floors.Any(f=>f.Rooms.ContainsValue("bedroom"))){if(Inside(b,floor,["livingroom","kitchen","bedroom"],dx,dy) is {} home&&Homes.Count<MaximumPerList&&!Homes.Contains(home))Homes.Add(home);}
    foreach(var (profession,rooms) in Workplaces)if(Inside(b,floor,rooms,dx,dy) is {} work){if(!Work.TryGetValue(profession,out var list))Work[profession]=list=[];if(list.Count<MaximumPerList&&!list.Contains(work))list.Add(work);}
   }
  }
  public void AddAuthored(MapProject p){foreach(var s in p.Spawns)if(new Point(p.WorldX*256+s.X,p.WorldY*256+s.Y,0) is var point&&!Authored.Contains(point))Authored.Add(point);}
  public int Count=>Homes.Count+Authored.Count;
  /// <summary>The spawnpoints.lua text.</summary>
  public string Lua(){
   var everyone=Homes.Count>0?Homes:Authored;var text=new StringBuilder("function SpawnPoints()\n");
   void List(string name,IEnumerable<Point> points){text.Append("    local ").Append(name).Append(" = {\n");foreach(var p in points)text.Append("        { posX = ").Append(p.X).Append(", posY = ").Append(p.Y).Append(", posZ = ").Append(p.Z).Append(" },\n");text.Append("    }\n");}
   List("houses",everyone);foreach(var (profession,points) in Work.OrderBy(w=>w.Key,StringComparer.Ordinal))List(profession+"_work",everyone.Concat(points));
   text.Append("    return {\n");foreach(var profession in Professions)text.Append("        ").Append(profession).Append(" = ").Append(Work.ContainsKey(profession)?profession+"_work":"houses").Append(",\n");
   return text.Append("    }\nend\n").ToString();
  }
 }

 /// <summary>A free ground-floor square near the middle of the first of <paramref name="rooms"/> the building has.</summary>
 static Point? Inside(Building b,BuildingFloor floor,string[] rooms,int dx,int dy){
  var taken=floor.Objects.SelectMany(BuildingObjects.Cells).ToHashSet();
  foreach(var name in rooms){
   var keys=floor.Rooms.Where(r=>r.Value==name&&r.Key.Length==1).Select(r=>r.Key[0]).ToHashSet();if(keys.Count==0)continue;
   var cells=Enumerable.Range(0,b.Width*b.Height).Where(i=>i<floor.Cells.Length&&keys.Contains(floor.Cells[i])).Select(i=>(X:i%b.Width,Y:i/b.Width)).ToList();if(cells.Count==0)continue;
   var cx=cells.Average(c=>c.X);var cy=cells.Average(c=>c.Y);
   foreach(var c in cells.OrderBy(c=>(c.X-cx)*(c.X-cx)+(c.Y-cy)*(c.Y-cy)).ThenBy(c=>c.Y).ThenBy(c=>c.X))if(!taken.Contains(c))return new(dx+b.X+c.X,dy+b.Y+c.Y,0);
  }
  return null;
 }
 /// <summary>spawnpoints.lua for one map.</summary>
 public static string Lua(MapProject p){var plan=new Plan();plan.Add(p.Buildings,p.WorldX*256,p.WorldY*256);plan.AddAuthored(p);return plan.Lua();}
}
