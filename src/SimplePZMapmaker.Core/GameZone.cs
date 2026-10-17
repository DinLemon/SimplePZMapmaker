using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core;
public sealed class GameZone
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Name {get;set;}="";
 public string Type {get;set;}="TownZone";
 public int X {get;set;} public int Y {get;set;} public int Z {get;set;}
 public int Width {get;set;}=8; public int Height {get;set;}=8;
 public List<ZonePoint>? Polygon {get;set;}
 public Dictionary<string,string> SourceTags {get;set;}=[];
 public string? VehicleDistribution {get;set;}
 public string VehicleSourceId {get;set;}="game";
 public List<string> RequiredMods {get;set;}=[];
 public string Direction {get;set;}="N";
 public bool FaceDirection {get;set;}=true;
 public bool Vehicle=>Type is "Vehicle" or "ParkingStall";
 /// <summary>Zones of one layer may not overlap: terrain zones, vehicle spawns and ZombiesType definitions are separate layers.</summary>
 public string Layer=>Vehicle?"vehicle":Type=="ZombiesType"?"zombies":"terrain";
 public static string[] Types {get;}=["TownZone","Forest","DeepForest","Vegitation","Farm","TrailerPark","Nav","Vehicle","ParkingStall","ZombiesType"];
 public static void Validate(GameZone z,int width,int height)
 {
  if(z is null||!Regex.IsMatch(z.Id??"",@"^[a-fA-F0-9]{32}$")||z.Name is null||z.Name.Length>100||z.Name.Any(char.IsControl)||!Types.Contains(z.Type)||z.X<0||z.Y<0||z.Z!=0||z.Width<1||z.Height<1||(long)z.X+z.Width>width||(long)z.Y+z.Height>height)throw new ArgumentException("Game zones need a valid ID/type, ground level and a rectangle inside the map.");
  if(z.Type=="ZombiesType"&&string.IsNullOrWhiteSpace(z.Name))throw new ArgumentException("A ZombiesType zone needs the name of its zombie definition.");
  ZoneGeometry.Validate(z.X,z.Y,z.Width,z.Height,z.Polygon,width,height);ZoneGeometry.ValidateSourceTags(z.SourceTags);ResourceDependencies.Validate(z.RequiredMods,[]);
  if(z.Vehicle){if(!Regex.IsMatch(z.VehicleDistribution??"",@"^[A-Za-z][A-Za-z0-9_]{0,119}$")||!ResourceDependencies.SafeModId(z.VehicleSourceId)||z.Direction is not ("N" or "S" or "E" or "W"))throw new ArgumentException("Vehicle zones need a registered distribution and N/S/E/W direction.");}
  else if(z.VehicleDistribution is not null||z.RequiredMods.Count>0)throw new ArgumentException("Vehicle distribution settings belong to a vehicle zone.");
 }
}
public static class GameZones
{
 public static MapProject ApplyProposal(MapProject project,GameZone[] zones){
  if(zones is null||zones.Length is <1 or >64||zones.Any(z=>z is null)||zones.Select(z=>z.Id).Distinct().Count()!=zones.Length)throw new ArgumentException("A zone proposal needs 1–64 distinct zones.");
  var copy=NeighborhoodPlanner.Copy(project);foreach(var z in zones){GameZone.Validate(z,copy.Width,copy.Height);var next=NeighborhoodPlanner.Copy(z);var index=copy.GameZones.FindIndex(old=>old.Id==next.Id);if(index<0)copy.GameZones.Add(next);else copy.GameZones[index]=next;}ProjectValidator.Validate(copy);return copy;
 }
 public static void ValidateProject(MapProject p)
 {
  if(p.GameZones is null||p.GameZones.Count>4096||p.GameZones.Select(z=>z?.Id).Distinct().Count()!=p.GameZones.Count)throw new ArgumentException("A map can contain up to 4,096 distinct authored game zones.");
  foreach(var zone in p.GameZones)GameZone.Validate(zone,p.Width,p.Height);
  for(var i=0;i<p.GameZones.Count;i++)for(var j=0;j<i;j++){var a=p.GameZones[i];var b=p.GameZones[j];if(a.Layer==b.Layer&&ZoneGeometry.Intersects(a,b))throw new ArgumentException("Authored zones of the same layer cannot overlap. Vehicle zones may overlap terrain zones.");}
  foreach(var zone in p.GameZones.Where(z=>z.Vehicle)){for(var y=zone.Y;y<zone.Y+zone.Height;y++)for(var x=zone.X;x<zone.X+zone.Width;x++)if(ZoneGeometry.Covers(zone,x,y)&&p.Terrain[y*p.Width+x]==Terrain.Water)throw new ArgumentException("A vehicle spawn zone intersects water.");foreach(var b in p.Buildings.Where(b=>b.X<zone.X+zone.Width&&b.Y<zone.Y+zone.Height&&b.X+b.Width>zone.X&&b.Y+b.Height>zone.Y))for(var y=Math.Max(zone.Y,b.Y);y<Math.Min(zone.Y+zone.Height,b.Y+b.Height);y++)for(var x=Math.Max(zone.X,b.X);x<Math.Min(zone.X+zone.Width,b.X+b.Width);x++)if(ZoneGeometry.Covers(zone,x,y)&&BuildingDesigner.OccupiesAbove(b,x-b.X,y-b.Y))throw new ArgumentException("A vehicle spawn zone intersects a building.");}
 }
}
