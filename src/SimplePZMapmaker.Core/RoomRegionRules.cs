namespace SimplePZMapmaker.Core;
public sealed class RoomRegionRule
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Name {get;set;}="Room rule";
 public int X {get;set;}public int Y {get;set;}public int Width {get;set;}=8;public int Height {get;set;}=8;
 public List<ZonePoint>? Polygon {get;set;}public int Level {get;set;}
 public string RoomName {get;set;}="storage";public string SourceId {get;set;}="game";public List<string> RequiredMods {get;set;}=[];
 public string Selection {get;set;}="intersects";public Dictionary<string,string> SourceTags {get;set;}=[];
}
public sealed record RoomRegionChange(string RuleId,string BuildingId,int Level,string Code,string PreviousName,string RoomName,int TileCount);
public static class RoomRegionRules
{
 public static void ValidateProject(MapProject p)=>Validate(p,p.RoomRegionRules);
 static void Validate(MapProject p,IReadOnlyList<RoomRegionRule> rules)
 {
  if(rules is null||rules.Count>256||rules.Any(r=>r is null)||rules.Select(r=>r.Id).Distinct().Count()!=rules.Count)throw new ArgumentException("A map supports up to 256 distinct room rules.");
  foreach(var r in rules){ZombieDensityRegions.ValidateIdentity(r.Id,r.Name);ZoneGeometry.Validate(r.X,r.Y,r.Width,r.Height,r.Polygon,p.Width,p.Height);ZoneGeometry.ValidateSourceTags(r.SourceTags);if(r.Level is <-32 or >31||r.Selection is not ("intersects" or "contained")||!BuildingDesigner.AllRoomNames.Contains(r.RoomName)||r.SourceId!="game"||r.RequiredMods is null||r.RequiredMods.Count>0)throw new ArgumentException("Choose an existing supported game room name and whole-room selection rule. Custom loot-zone types are unsupported.");}
 }
 public static RoomRegionChange[] Preview(MapProject p,RoomRegionRule[] rules)
 {
  Validate(p,rules);var changes=new Dictionary<(string,int,string),RoomRegionChange>();foreach(var r in rules)foreach(var b in p.Buildings){if(b.X>=r.X+r.Width||b.Y>=r.Y+r.Height||b.X+b.Width<=r.X||b.Y+b.Height<=r.Y)continue;if(!Enumerable.Range(Math.Max(b.Y,r.Y),Math.Min(b.Y+b.Height,r.Y+r.Height)-Math.Max(b.Y,r.Y)).Any(y=>Enumerable.Range(Math.Max(b.X,r.X),Math.Min(b.X+b.Width,r.X+r.Width)-Math.Max(b.X,r.X)).Any(x=>ZoneGeometry.Covers(r.X,r.Y,r.Width,r.Height,r.Polygon,x,y))))continue;if(b.Design is null)throw new ArgumentException("Room rules require explicit building floor designs; design the building before reviewing its rooms.");var level=r.Level-b.Design.MinLevel;if(level<0||level>=b.Design.Floors.Count)continue;var f=b.Design.Floors[level];foreach(var room in f.Rooms.OrderBy(k=>k.Key,StringComparer.Ordinal)){var points=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==room.Key[0]).ToArray();if(points.Length==0)continue;var hits=points.Count(i=>ZoneGeometry.Covers(r.X,r.Y,r.Width,r.Height,r.Polygon,b.X+i%b.Width,b.Y+i/b.Width));if(hits==0||r.Selection=="contained"&&hits!=points.Length)continue;var key=(b.Id,r.Level,room.Key);if(changes.ContainsKey(key))throw new ArgumentException("Room rules select the same whole room; review one unambiguous mapping.");changes[key]=new(r.Id,b.Id,r.Level,room.Key,room.Value,r.RoomName,points.Length);}}
  return changes.Values.OrderBy(r=>r.BuildingId,StringComparer.Ordinal).ThenBy(r=>r.Level).ThenBy(r=>r.Code,StringComparer.Ordinal).ToArray();
 }
 public static MapProject ApplyProposal(MapProject p,RoomRegionRule[] rules,RoomRegionChange[] reviewed)
 {
  var changes=Preview(p,rules);if(reviewed is null||!changes.SequenceEqual(reviewed))throw new ArgumentException("Whole-room review is missing or stale. Preview again before applying.");var copy=NeighborhoodPlanner.Copy(p);foreach(var change in changes){var b=copy.Buildings.Single(b=>b.Id==change.BuildingId);b.Design!.Floors[change.Level-b.Design.MinLevel].Rooms[change.Code]=change.RoomName;b.Modified=true;}copy.RoomRegionRules=NeighborhoodPlanner.Copy(rules.ToList());return copy;
 }
}
