namespace SimplePZMapmaker.Core;

/// <summary>Attached buildings (docs/TRANSFER-CONTRACT.md, section 6): a building with AttachedTo touches that building with no free line.
/// Their wall rings may share tiles, their rooms never do. On a shared edge the larger building owns the wall and the
/// smaller one draws nothing, so the edge never carries two walls.</summary>
public static class AttachedBuildings
{
 public static bool Attached(Building a,Building b)=>a.AttachedTo is not null&&a.AttachedTo==b.Id||b.AttachedTo is not null&&b.AttachedTo==a.Id;
 /// <summary>Whether a world tile used by both buildings is allowed: only for an attached pair, and only when it is not a room of both.</summary>
 public static bool MayShare(Building a,Building b,int x,int y)=>Attached(a,b)&&!(BuildingDesigner.Occupies(a,x-a.X,y-a.Y)&&BuildingDesigner.Occupies(b,x-b.X,y-b.Y));
 static int Area(Building b)=>b.Design is null?b.Width*b.Height:b.Design.Floors.Sum(f=>f.Cells.Count(c=>c!='.'));
 /// <summary>The building that owns the wall on edges shared by an attached pair: the larger one, or the attachment target on a tie.</summary>
 public static Building WallOwner(Building attached,Building target)=>Area(attached)>Area(target)?attached:target;
 /// <summary>Edges, keyed by the building that must not draw them, in project tile coordinates with signed level: its attached partner owns the wall there.</summary>
 public static HashSet<(string Building,int X,int Y,int Z,string Edge)> SuppressedEdges(IReadOnlyList<Building> buildings){
  var result=new HashSet<(string,int,int,int,string)>();var byId=buildings.Where(b=>b.Id is not null).GroupBy(b=>b.Id).ToDictionary(g=>g.Key,g=>g.First());
  foreach(var b in buildings){
   if(b.AttachedTo is null||!byId.TryGetValue(b.AttachedTo,out var target)||b.Design is null||target.Design is null)continue;
   var owner=WallOwner(b,target);var other=ReferenceEquals(owner,b)?target:b;var owned=Edges(owner).Where(e=>e.Kind!="open").Select(e=>(e.X,e.Y,e.Z,e.Edge)).ToHashSet();
   foreach(var e in Edges(other))if(owned.Contains((e.X,e.Y,e.Z,e.Edge)))result.Add((other.Id,e.X,e.Y,e.Z,e.Edge));
  }
  return result;
 }
 static IEnumerable<(int X,int Y,int Z,string Edge,string Kind)> Edges(Building b)=>b.Design!.Floors.SelectMany((f,i)=>f.Edges.Select(e=>(b.X+e.X,b.Y+e.Y,i+b.Design.MinLevel,e.Edge,e.Kind)));
 public static void Validate(MapProject p){
  var ids=p.Buildings.Select(b=>b.Id).ToHashSet(StringComparer.Ordinal);
  foreach(var b in p.Buildings.Concat(p.BuildingLibrary))if(b.AttachedTo is {} target&&(target.Length is 0 or >100||target==b.Id||!ids.Contains(target)||p.BuildingLibrary.Contains(b)))throw new ArgumentException($"Building '{b.Name}' is attached to a building that is not on this map.");
 }
 /// <summary>Joins an attached pair after both are placed: the smaller building keeps an exterior entrance off the shared wall,
 /// and with <paramref name="door"/> the wall owner gets one door on the shared ground-level wall. Returns false when no door fits.</summary>
 public static bool Join(Building attached,Building target,bool door){
  if(attached.Design is null||target.Design is null)return false;
  var owner=WallOwner(attached,target);var other=ReferenceEquals(owner,attached)?target:attached;
  var suppressed=SuppressedEdges([attached,target]);
  EnsureEntrance(other,suppressed);
  CloseSharedWindows(owner,other);
  if(!door)return true;
  var ground=owner.Design!.Floors[-owner.Design.MinLevel];var otherGround=other.Design!.MinLevel<=0&&other.Design.Floors.Count>-other.Design.MinLevel?other.Design.Floors[-other.Design.MinLevel]:null;if(otherGround is null)return false;
  var candidates=ground.Edges.Where(e=>e.Kind=="wall").Where(e=>{var north=e.Edge=="n";var a=BuildingDesigner.Cell(owner,ground,e.X,e.Y);var b=north?BuildingDesigner.Cell(owner,ground,e.X,e.Y-1):BuildingDesigner.Cell(owner,ground,e.X-1,e.Y);if((a=='.')==(b=='.'))return false;
   var (ox,oy)=a=='.'?(owner.X+e.X,owner.Y+e.Y):north?(owner.X+e.X,owner.Y+e.Y-1):(owner.X+e.X-1,owner.Y+e.Y);return BuildingDesigner.Cell(other,otherGround,ox-other.X,oy-other.Y)!='.';}).ToList();
  if(candidates.Count==0)return false;
  var before=NeighborhoodPlanner.Copy(owner.Design);var chosen=candidates[candidates.Count/2];chosen.Kind="door";chosen.OpeningTile=null;chosen.OverlayTile=null;chosen.OverlayLevelOffset=0;
  if(chosen.WallTile is {} wall&&wall.LastIndexOf('_') is var split and >0&&int.TryParse(wall[(split+1)..],out var index))chosen.WallTile=$"{wall[..split]}_{index+10}";
  try{BuildingDesigner.Furnish(owner,-owner.Design.MinLevel,new Random(owner.Design.Variant));BuildingDesigner.Validate(owner);return true;}catch(ArgumentException){owner.Design=before;return false;}
 }
 /// <summary>A window of the wall owner that would look into the partner's room becomes a plain wall.</summary>
 static void CloseSharedWindows(Building owner,Building other){
  for(var i=0;i<owner.Design!.Floors.Count;i++){var z=i+owner.Design.MinLevel;var level=z-other.Design!.MinLevel;if(level<0||level>=other.Design.Floors.Count)continue;var f=owner.Design.Floors[i];var g=other.Design.Floors[level];
   foreach(var e in f.Edges.Where(e=>e.Kind=="window")){var north=e.Edge=="n";var a=BuildingDesigner.Cell(owner,f,e.X,e.Y);var (x,y)=a=='.'?(owner.X+e.X,owner.Y+e.Y):north?(owner.X+e.X,owner.Y+e.Y-1):(owner.X+e.X-1,owner.Y+e.Y);
    if(BuildingDesigner.Cell(other,g,x-other.X,y-other.Y)=='.')continue;e.Kind="wall";e.OpeningTile=null;e.OverlayTile=null;e.OverlayLevelOffset=0;if(e.WallTile is {} wall&&wall.LastIndexOf('_') is var split and >0&&int.TryParse(wall[(split+1)..],out var index))e.WallTile=$"{wall[..split]}_{index-8}";}}
 }
 /// <summary>When every exterior door of a building lies on a wall its partner owns, the first other exterior wall at ground level becomes a door.</summary>
 static void EnsureEntrance(Building b,HashSet<(string Building,int X,int Y,int Z,string Edge)> suppressed){
  var f=b.Design!.Floors[-b.Design.MinLevel];bool Outer(BuildingEdge e){var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var o=e.Edge=="n"?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y);return(a=='.')!=(o=='.');}
  bool Shared(BuildingEdge e)=>suppressed.Contains((b.Id,b.X+e.X,b.Y+e.Y,0,e.Edge));
  if(f.Edges.Any(e=>e.Kind is "door" or "open"&&Outer(e)&&!Shared(e)))return;
  var before=NeighborhoodPlanner.Copy(b.Design);
  foreach(var (x,y,edge) in f.Edges.Where(e=>e.Kind is "wall" or "window"&&Outer(e)&&!Shared(e)).OrderBy(e=>e.Edge=="n"?0:1).ThenBy(e=>e.Y).ThenBy(e=>e.X).Select(e=>(e.X,e.Y,e.Edge)).ToList()){
   b.Design=NeighborhoodPlanner.Copy(before);f=b.Design.Floors[-b.Design.MinLevel];var e=f.Edges.Single(e=>e.X==x&&e.Y==y&&e.Edge==edge);
   // A wall set keeps the door frame 10 tiles after the plain wall and 2 after the window frame.
   if(e.WallTile is {} wall&&wall.LastIndexOf('_') is var split and >0&&int.TryParse(wall[(split+1)..],out var index))e.WallTile=$"{wall[..split]}_{index+(e.Kind=="window"?2:10)}";
   e.Kind="door";e.OpeningTile=null;e.OverlayTile=null;e.OverlayLevelOffset=0;
   try{BuildingDesigner.Furnish(b,-b.Design.MinLevel,new Random(b.Design.Variant));BuildingDesigner.Validate(b);return;}catch(ArgumentException){}
  }
  b.Design=before;
 }
}
