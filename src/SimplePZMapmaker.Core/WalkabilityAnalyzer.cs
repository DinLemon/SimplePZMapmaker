namespace SimplePZMapmaker.Core;

public sealed record WalkPoint(int X,int Y,int Z);
public sealed class WalkabilityOptions {
 public int X {get;set;} public int Y {get;set;} public int Width {get;set;}=128;public int Height {get;set;}=128;
 public int MaximumNodes {get;set;}=400000;public WalkPoint? Start {get;set;} public WalkPoint? End {get;set;}
}
public sealed record WalkIssue(string Code,string Severity,string Message,int X,int Y,int Z,string? BuildingId=null,IReadOnlyList<WalkPoint>? Cells=null);
public sealed class WalkabilityReport {
 public bool Complete {get;set;}=true;public int Nodes {get;set;} public int Reachable {get;set;}
 public string Method {get;set;}="Authored pedestrian graph; doors may open unless locked; no jumping, climbing or demolition.";
 public List<string> Notes {get;set;}=[];public List<WalkIssue> Issues {get;set;}=[];public List<WalkPoint> Path {get;set;}=[];
 public WalkabilityOptions Coverage {get;set;}=new();
}

/// <summary>Read-only bounded analysis, including furniture footprints and native stair landings.</summary>
public static class WalkabilityAnalyzer {
 readonly record struct Edge(int X,int Y,int Z,string Side);
 sealed record Room(Building Building,int Floor,char Code,bool Cropped);
 public static WalkabilityReport Building(Building b,CancellationToken token=default,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  var copy=WorldAssembler.CloneBuilding(b);copy.X=copy.Y=2;var width=b.Width+4;var height=b.Height+4;
  var p=new MapProject{Width=width,Height=height,Terrain=new Terrain[width*height],Buildings=[copy]};
  var r=Analyze(p,new(){Width=width,Height=height},token,properties);
  // Building reports use local building coordinates, including exterior approach tiles.
  WalkPoint Local(WalkPoint a)=>new(a.X-2,a.Y-2,a.Z);
  r.Issues=r.Issues.Select(i=>i with{X=i.X-2,Y=i.Y-2,Cells=i.Cells?.Select(Local).ToArray()}).ToList();r.Path=r.Path.Select(Local).ToList();return r;
 }
 public static WalkabilityReport World(WorldProject w,string memberId,WalkabilityOptions options,CancellationToken token=default,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  var m=w.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("Select an existing world section.");
  return Analyze(WorldAssembler.ProjectSection(w,m),options,token,properties);
 }
 public static WalkabilityReport Analyze(MapProject p,WalkabilityOptions o,CancellationToken token=default,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  if(p is null||p.Width is <1 or >2048||p.Height is <1 or >2048||p.Terrain is null||p.Terrain.Length!=(long)p.Width*p.Height||p.Buildings is null||o is null||o.X<0||o.Y<0||o.Width is <1 or >256||o.Height is <1 or >256||(long)o.X+o.Width>p.Width||(long)o.Y+o.Height>p.Height||o.MaximumNodes is <64 or >500000)throw new ArgumentException("Access analysis needs an in-map crop up to 256×256 tiles and a 64–500,000 node budget.");
  var r=new WalkabilityReport{Coverage=o};var nodes=new HashSet<WalkPoint>();var rooms=new Dictionary<WalkPoint,Room>();var edges=new HashSet<Edge>();var guards=new HashSet<Edge>();var blocked=new HashSet<WalkPoint>();var stairs=new List<(WalkPoint Low,WalkPoint High,string? Building)>();var stairSteps=new HashSet<WalkPoint>();var openings=new List<(Edge Edge,string? Building)>();var rootExclusions=new HashSet<WalkPoint>();var limit=false;
  bool Inside(int x,int y)=>x>=o.X&&y>=o.Y&&x<o.X+o.Width&&y<o.Y+o.Height;
  void Note(string text){r.Complete=false;if(!r.Notes.Contains(text))r.Notes.Add(text);}
  void Add(WalkPoint c){if(!Inside(c.X,c.Y))return;if(nodes.Count>=o.MaximumNodes&&!nodes.Contains(c)){if(!limit)Note("Node budget reached; this graph is incomplete. Reduce the crop or floor count.");limit=true;return;}nodes.Add(c);}
  void Issue(string code,string severity,string message,WalkPoint c,string? building=null,IEnumerable<WalkPoint>? cells=null){if(r.Issues.Count<512)r.Issues.Add(new(code,severity,message,c.X,c.Y,c.Z,building,cells?.Take(256).ToArray()));else Note("Issue budget reached; additional issues were omitted.");}
  void Model(StaticModelPlacement m,int ox,int oy,int z){foreach(var cell in StaticModelPlacement.FootprintCells(m)){var c=new WalkPoint(ox+m.X+cell.Dx,oy+m.Y+cell.Dy,z);if(StaticModelPlacement.BlocksTiles(m))blocked.Add(c);}if(m.Profile=="door"&&m.Door is {} d){var e=new Edge(ox+m.X,oy+m.Y,z,d.Direction);openings.Add((e,null));if(!d.Open){guards.Add(e);if(d.Locked)edges.Add(e);}}}
  void Part(int x,int y,int z,BuildingObjects.Part part){if(properties is not null){var native=properties(part.Tile);var flags=part.Flags;if(native.ContainsKey("HoppableN")||native.ContainsKey("WallNTrans"))flags|=2;if(native.ContainsKey("HoppableW")||native.ContainsKey("WallWTrans"))flags|=4;part=part with{Flags=flags};}var c=new WalkPoint(x+part.Dx,y+part.Dy,z);if((part.Flags&1)!=0)blocked.Add(c);foreach(var (flag,side) in new[]{(2,"n"),(4,"w")})if((part.Flags&flag)!=0){var e=new Edge(c.X,c.Y,z,side);edges.Add(e);guards.Add(e);}}
  for(var y=o.Y;y<o.Y+o.Height;y++){token.ThrowIfCancellationRequested();for(var x=o.X;x<o.X+o.Width;x++)if(p.Terrain[y*p.Width+x]!=Terrain.Water)Add(new(x,y,0));}
  foreach(var b in p.Buildings){
   token.ThrowIfCancellationRequested();if(b is null)throw new ArgumentException("A building is missing.");if(b.X+b.Width<=o.X||b.Y+b.Height<=o.Y||b.X>=o.X+o.Width||b.Y>=o.Y+o.Height)continue;
   if(b.Design is null){Note("Legacy buildings have no authored room graph. Open them in the workshop to generate floors before checking access.");continue;}
   if(b.Width is <1 or >64||b.Height is <1 or >64||b.Design.Floors is null||b.Design.Floors.Count is <1 or >64||b.Design.MinLevel<ZLevels.Min||b.Design.MinLevel+b.Design.Floors.Count-1>ZLevels.Max)throw new ArgumentException("Invalid building floor bounds for access analysis.");
   var cropped=b.X<o.X||b.Y<o.Y||b.X+b.Width>o.X+o.Width||b.Y+b.Height>o.Y+o.Height;if(cropped)Note("A building crosses the analysis boundary; its unreachable-room results are omitted. Enlarge or move the crop.");
   for(var index=0;index<b.Design.Floors.Count;index++){
    var f=b.Design.Floors[index];var z=b.Design.MinLevel+index;if(f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Edges is null||f.Objects is null||f.StaticModels is null)throw new ArgumentException("Invalid building floor data for access analysis.");
    for(var i=0;i<f.Cells.Length;i++){if(f.Cells[i]=='.')continue;var c=new WalkPoint(b.X+i%b.Width,b.Y+i/b.Width,z);Add(c);if(Inside(c.X,c.Y)){rooms[c]=new(b,index,f.Cells[i],cropped);rootExclusions.Add(c);}}
    foreach(var e in f.Edges){var key=new Edge(b.X+e.X,b.Y+e.Y,z,e.Edge);if(e.Kind is "wall" or "window"){edges.Add(key);guards.Add(key);}else if(e.Kind=="door"){guards.Add(key);openings.Add((key,b.Id));}else if(e.Kind=="open")openings.Add((key,b.Id));}
    foreach(var obj in f.Objects){if(obj.Kind!="stairs"){foreach(var part in BuildingObjects.Parts(obj))Part(b.X+obj.X,b.Y+obj.Y,z,part);continue;}
     var low=new WalkPoint(b.X+obj.X+(obj.Direction=="w"?4:0),b.Y+obj.Y+(obj.Direction=="n"?4:0),z);var high=new WalkPoint(b.X+obj.X,b.Y+obj.Y,z+1);stairs.Add((low,high,b.Id));
     for(var step=1;step<=3;step++){var x=high.X+(obj.Direction=="w"?step:0);var y=high.Y+(obj.Direction=="n"?step:0);stairSteps.Add(new(x,y,z));nodes.Remove(new(x,y,z+1));}
    }
    foreach(var m in f.StaticModels)Model(m,b.X,b.Y,z);
   }
  }
  foreach(var d in p.Decorations)foreach(var part in d.Parts)Part(d.X,d.Y,0,part);
  foreach(var m in p.StaticModels)Model(m,0,0,m.Z);
  var infrastructure=InfrastructureGeometry.Build(p);var nativeStairStarts=infrastructure.Tiles.Where(t=>t.Id=="carpentry_02_98").Select(t=>(t.X,t.Y,t.Z)).ToHashSet();foreach(var tile in infrastructure.Tiles){var c=new WalkPoint(tile.X,tile.Y,tile.Z);if(tile.Id==InfrastructureGeometry.DeckTile)Add(c);else if(tile.Id is "carpentry_02_98" or "carpentry_02_97" or "carpentry_02_96"){
   stairSteps.Add(c);if(tile.Id=="carpentry_02_98"){var low=new WalkPoint(tile.X,tile.Y+3,tile.Z);var high=new WalkPoint(tile.X,tile.Y-1,tile.Z+1);stairs.Add((low,high,null));}
   else if(Inside(tile.X,tile.Y)&&!nativeStairStarts.Contains((tile.X,tile.Y-(tile.Id=="carpentry_02_97"?1:2),tile.Z)))Note("A native stair run crosses the section boundary. Its landing and access beyond this section are unknown.");
  }Part(tile.X,tile.Y,tile.Z,new(0,0,tile.Id,tile.Flags));}
  foreach(var hole in infrastructure.Holes)nodes.Remove(new(hole.X,hole.Y,hole.Z));
  // Floors added after their lower stair runs must not close those runs again.
  foreach(var stair in stairs){var dx=(stair.Low.X-stair.High.X)/4;var dy=(stair.Low.Y-stair.High.Y)/4;for(var j=1;j<=3;j++)nodes.Remove(new(stair.High.X+dx*j,stair.High.Y+dy*j,stair.High.Z));}
  foreach(var c in blocked)nodes.Remove(c);foreach(var c in stairSteps)nodes.Remove(c);
  Edge Between(WalkPoint a,WalkPoint b)=>a.X!=b.X?new(Math.Max(a.X,b.X),a.Y,a.Z,"w"):new(a.X,Math.Max(a.Y,b.Y),a.Z,"n");
  var vertical=new Dictionary<WalkPoint,List<WalkPoint>>();
  foreach(var s in stairs){
   // Only transitions touching the requested crop affect its coverage. Other stairs belong to another analysis.
   if(!Enumerable.Range(0,5).Any(j=>Inside(s.High.X+(s.Low.X-s.High.X)/4*j,s.High.Y+(s.Low.Y-s.High.Y)/4*j)))continue;
   if(!Inside(s.Low.X,s.Low.Y)||!Inside(s.High.X,s.High.Y)){Note("A stair transition crosses the analysis crop. Access outside the crop is unknown.");continue;}
   var dx=(s.Low.X-s.High.X)/4;var dy=(s.Low.Y-s.High.Y)/4;var clear=nodes.Contains(s.Low)&&nodes.Contains(s.High);
   for(var j=1;j<=4;j++){var a=new WalkPoint(s.High.X+dx*(j-1),s.High.Y+dy*(j-1),s.Low.Z);var c=new WalkPoint(s.High.X+dx*j,s.High.Y+dy*j,s.Low.Z);if(blocked.Contains(a)||blocked.Contains(c)||edges.Contains(Between(a,c)))clear=false;}
   if(!clear){Issue("broken-stair","error","Stairs need an unobstructed lower landing, run and upper anchor.",new(s.High.X,s.High.Y,s.Low.Z),s.Building);continue;}
   if(!vertical.TryGetValue(s.Low,out var lower))vertical[s.Low]=lower=[];lower.Add(s.High);if(!vertical.TryGetValue(s.High,out var upper))vertical[s.High]=upper=[];upper.Add(s.Low);
  }
  foreach(var (e,bid) in openings){var a=new WalkPoint(e.X,e.Y,e.Z);var b=new WalkPoint(e.X-(e.Side=="w"?1:0),e.Y-(e.Side=="n"?1:0),e.Z);if(!Inside(a.X,a.Y)||!Inside(b.X,b.Y))continue;if(blocked.Contains(a)||blocked.Contains(b)||!nodes.Contains(a)||!nodes.Contains(b))Issue("blocked-opening","error","Door or passage has a blocked or unsupported approach.",a,bid,[a,b]);}
  IEnumerable<WalkPoint> Neighbors(WalkPoint a){foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var n=new WalkPoint(a.X+dx,a.Y+dy,a.Z);if(nodes.Contains(n)&&!edges.Contains(Between(a,n)))yield return n;}if(vertical.TryGetValue(a,out var links))foreach(var n in links)yield return n;}
  var reachable=new HashSet<WalkPoint>();var queue=new Queue<WalkPoint>();
  foreach(var c in nodes)if(c.Z==0&&!rootExclusions.Contains(c)&&(c.X==o.X||c.Y==o.Y||c.X==o.X+o.Width-1||c.Y==o.Y+o.Height-1)){reachable.Add(c);queue.Enqueue(c);}
  while(queue.TryDequeue(out var c)){if((reachable.Count&1023)==0)token.ThrowIfCancellationRequested();foreach(var n in Neighbors(c))if(reachable.Add(n))queue.Enqueue(n);}
  if(!limit)foreach(var group in rooms.Where(k=>!k.Value.Cropped).GroupBy(k=>(k.Value.Building.Id,k.Value.Floor,k.Value.Code)))if(group.All(k=>!nodes.Contains(k.Key))&&group.Any(k=>blocked.Contains(k.Key))){var first=group.First();Issue("blocked-room","error","The entire room is occupied by blocking object footprints.",first.Key,first.Value.Building.Id,group.Select(k=>k.Key));}
  var unseen=rooms.Where(k=>!k.Value.Cropped&&nodes.Contains(k.Key)&&!reachable.Contains(k.Key)).Select(k=>k.Key).ToHashSet();
  while(unseen.Count>0){var first=unseen.First();var room=rooms[first];var cells=new List<WalkPoint>{first};queue.Enqueue(first);unseen.Remove(first);while(queue.TryDequeue(out var c))foreach(var n in Neighbors(c))if(unseen.Contains(n)&&rooms[n].Building.Id==room.Building.Id&&rooms[n].Floor==room.Floor&&rooms[n].Code==room.Code){unseen.Remove(n);cells.Add(n);queue.Enqueue(n);}Issue("unreachable-room","error","Area has no pedestrian route to dry outdoor ground in this crop.",first,room.Building.Id,cells);}
  foreach(var group in nodes.Where(c=>c.Z>0).GroupBy(c=>(c.Z,Id:rooms.GetValueOrDefault(c)?.Building.Id))){var unsafeCells=new List<WalkPoint>();foreach(var c in group)foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var n=new WalkPoint(c.X+dx,c.Y+dy,c.Z);if(!Inside(n.X,n.Y))continue;if(nodes.Contains(n)||blocked.Contains(n)||stairSteps.Contains(n)||vertical.GetValueOrDefault(c)?.Count>0)continue;if(!guards.Contains(Between(c,n)))unsafeCells.Add(c);}if(unsafeCells.Count>0)Issue("unsafe-edge","warning","Upper floor has an unguarded edge. Add a wall, railing or supported landing.",unsafeCells[0],group.Key.Id,unsafeCells.Distinct());}
  if(o.Start is {} start&&o.End is {} end){if(!nodes.Contains(start)||!nodes.Contains(end))r.Notes.Add("A route endpoint is blocked, unsupported, or outside the crop.");else{var parents=new Dictionary<WalkPoint,WalkPoint?>{{start,null}};queue.Enqueue(start);while(queue.TryDequeue(out var c)&&!parents.ContainsKey(end)){token.ThrowIfCancellationRequested();foreach(var n in Neighbors(c))if(parents.TryAdd(n,c))queue.Enqueue(n);}if(parents.ContainsKey(end)){var path=new List<WalkPoint>();for(WalkPoint? c=end;c is not null;c=parents[c])path.Add(c);path.Reverse();if(path.Count>8192){Note("Route exceeds the display budget; the first 8192 points are shown.");path=path.Take(8192).ToList();}r.Path=path;}else r.Notes.Add("No pedestrian route connects the selected endpoints inside this crop.");}}
  r.Nodes=nodes.Count;r.Reachable=reachable.Count;if(limit)r.Issues.RemoveAll(i=>i.Code=="unreachable-room");return r;
 }
}
