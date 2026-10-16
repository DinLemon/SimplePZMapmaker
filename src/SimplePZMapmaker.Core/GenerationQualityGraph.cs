namespace SimplePZMapmaker.Core;

public static partial class GenerationQuality {
 static readonly (int Dx,int Dy)[] Cardinal=[(1,0),(-1,0),(0,1),(0,-1)];
 readonly record struct GraphEdge(int X,int Y,int Z,string Side);
 sealed class GroundGraph(GenerationQualityArea area){
  public HashSet<WalkPoint> Nodes {get;}=[];public HashSet<WalkPoint> Roads {get;}=[];
  public HashSet<GraphEdge> Edges {get;}=[];public Dictionary<WalkPoint,List<WalkPoint>> Vertical {get;}=[];
  public HashSet<WalkPoint> StreetReachable {get;}=[];public bool Complete {get;set;}=true;
  public bool Inside(int x,int y)=>x>=area.X&&y>=area.Y&&x<area.X+area.Width&&y<area.Y+area.Height;
  public bool Contains(WalkPoint c)=>Nodes.Contains(c);
  public static GraphEdge Between(WalkPoint a,WalkPoint b)=>a.X!=b.X?new(Math.Max(a.X,b.X),a.Y,a.Z,"w"):new(a.X,Math.Max(a.Y,b.Y),a.Z,"n");
  public IEnumerable<WalkPoint> Neighbors(WalkPoint a){foreach(var (dx,dy) in Cardinal){var n=new WalkPoint(a.X+dx,a.Y+dy,a.Z);if(Nodes.Contains(n)&&!Edges.Contains(Between(a,n)))yield return n;}if(Vertical.TryGetValue(a,out var links))foreach(var n in links)yield return n;}
 }
 static GroundGraph BuildGroundGraph(MapProject p,List<Building> buildings,GenerationQualityArea area,CancellationToken token,Func<string,IReadOnlyDictionary<string,string>>? properties,Action<string> partial){
  var g=new GroundGraph(area);var blocked=new HashSet<WalkPoint>();var steps=new HashSet<WalkPoint>();var holes=new HashSet<WalkPoint>();
  void Incomplete(string note){g.Complete=false;partial(note);}
  void Add(WalkPoint c){if(!g.Inside(c.X,c.Y))return;if(g.Nodes.Count>=500000&&!g.Nodes.Contains(c)){Incomplete("Outdoor/infrastructure graph reached 500,000 nodes; street, courtyard and spawn conclusions were suppressed.");return;}g.Nodes.Add(c);}
  void Part(int x,int y,int z,BuildingObjects.Part part){
   var c=new WalkPoint(x+part.Dx,y+part.Dy,z);if(!g.Inside(c.X,c.Y))return;var flags=part.Flags;
   if(properties is not null){var native=properties(part.Tile);if(native.ContainsKey("HoppableN")||native.ContainsKey("WallNTrans")||native.ContainsKey("collideN"))flags|=2;if(native.ContainsKey("HoppableW")||native.ContainsKey("WallWTrans")||native.ContainsKey("collideW"))flags|=4;}
   if((flags&1)!=0)blocked.Add(c);if((flags&2)!=0)g.Edges.Add(new(c.X,c.Y,c.Z,"n"));if((flags&4)!=0)g.Edges.Add(new(c.X,c.Y,c.Z,"w"));
  }
  void Model(StaticModelPlacement m,int ox,int oy,int z){
   if(m is null||m.Footprint is null||m.Footprint.Count>64)throw new ArgumentException("Invalid model footprint for quality audit.");
   if(StaticModelPlacement.BlocksTiles(m))foreach(var cell in StaticModelPlacement.FootprintCells(m))blocked.Add(new(ox+m.X+cell.Dx,oy+m.Y+cell.Dy,z));
   if(m.Profile=="door"&&m.Door is {Locked:true,Open:false} d)g.Edges.Add(new(ox+m.X,oy+m.Y,z,d.Direction));
  }
  for(var y=area.Y;y<area.Y+area.Height;y++){token.ThrowIfCancellationRequested();for(var x=area.X;x<area.X+area.Width;x++){var c=new WalkPoint(x,y,0);if(p.Terrain[y*p.Width+x]!=Terrain.Water)Add(c);if(p.Terrain[y*p.Width+x]==Terrain.Road)g.Roads.Add(c);}}
  long buildingWork=0;long partCount=0;
  foreach(var b in buildings){
   if((buildingWork+=(long)b.Width*b.Height)>2000000||partCount>2000000){Incomplete("Building collision work budget reached; outdoor route conclusions were suppressed.");break;}
   token.ThrowIfCancellationRequested();if(b.Design is null){for(var y=Math.Max(area.Y,b.Y);y<Math.Min(area.Y+area.Height,b.Y+b.Height);y++)for(var x=Math.Max(area.X,b.X);x<Math.Min(area.X+area.Width,b.X+b.Width);x++)blocked.Add(new(x,y,0));continue;}
   var index=-b.Design.MinLevel;if(index<0||index>=b.Design.Floors.Count)continue;var f=b.Design.Floors[index];
   for(var i=0;i<f.Cells.Length;i++)if(f.Cells[i]!='.'){var c=new WalkPoint(b.X+i%b.Width,b.Y+i/b.Width,0);Add(c);g.Roads.Remove(c);}
   foreach(var e in f.Edges)if(e.Kind is "wall" or "window"&&g.Inside(b.X+e.X,b.Y+e.Y))g.Edges.Add(new(b.X+e.X,b.Y+e.Y,0,e.Edge));
   foreach(var o in f.Objects){if(o is null||o.Parts is null||o.Parts.Count>512||o.Style is null||o.Kind is not("stairs" or "asset" or "bed" or "storage" or "counter" or "seat" or "sink"))throw new ArgumentException("Invalid building collision object.");if(o.Kind=="stairs"){for(var step=1;step<=3;step++)steps.Add(new(b.X+o.X+(o.Direction=="w"?step:0),b.Y+o.Y+(o.Direction=="n"?step:0),0));}else{var parts=BuildingObjects.Parts(o);if((partCount+=parts.Length)>2000000){Incomplete("Building collision part budget reached; outdoor route conclusions were suppressed.");break;}foreach(var part in parts)Part(b.X+o.X,b.Y+o.Y,0,part);}}
   if(index>0)foreach(var o in b.Design.Floors[index-1].Objects.Where(o=>o.Kind=="stairs"))for(var step=1;step<=3;step++)holes.Add(new(b.X+o.X+(o.Direction=="w"?step:0),b.Y+o.Y+(o.Direction=="n"?step:0),0));
   foreach(var m in f.StaticModels)Model(m,b.X,b.Y,0);
  }
  foreach(var decoration in p.Decorations){token.ThrowIfCancellationRequested();if(decoration is null||decoration.Parts is null||decoration.Parts.Count>512)throw new ArgumentException("Invalid decoration collision layer.");partCount+=decoration.Parts.Count;if(partCount>2000000){Incomplete("Decoration collision budget reached; outdoor route conclusions were suppressed.");break;}foreach(var part in decoration.Parts)Part(decoration.X,decoration.Y,0,part);}
  foreach(var m in p.StaticModels){token.ThrowIfCancellationRequested();Model(m,0,0,m.Z);}
  InfrastructureGeometry.Geometry infrastructure;
  try{token.ThrowIfCancellationRequested();infrastructure=InfrastructureGeometry.Build(p);token.ThrowIfCancellationRequested();}
  catch(ArgumentException e){Incomplete("Infrastructure geometry could not be audited: "+e.Message);infrastructure=new([],[]);}
  var stairStarts=new List<WalkPoint>();
  foreach(var tile in infrastructure.Tiles){token.ThrowIfCancellationRequested();var c=new WalkPoint(tile.X,tile.Y,tile.Z);if(tile.Id==InfrastructureGeometry.DeckTile){Add(c);g.Roads.Add(c);}else if(tile.Id is "carpentry_02_98" or "carpentry_02_97" or "carpentry_02_96"){steps.Add(c);if(tile.Id=="carpentry_02_98")stairStarts.Add(c);}Part(tile.X,tile.Y,tile.Z,new(0,0,tile.Id,tile.Flags));}
  foreach(var hole in infrastructure.Holes)holes.Add(new(hole.X,hole.Y,hole.Z));
  g.Nodes.ExceptWith(blocked);g.Nodes.ExceptWith(holes);g.Nodes.ExceptWith(steps);
  foreach(var start in stairStarts){
   var low=new WalkPoint(start.X,start.Y+3,start.Z);var high=new WalkPoint(start.X,start.Y-1,start.Z+1);if(!g.Contains(low)||!g.Contains(high))continue;
   var clear=true;for(var j=0;j<4;j++){var a=new WalkPoint(start.X,start.Y-1+j,start.Z);var c=new WalkPoint(start.X,start.Y+j,start.Z);if(blocked.Contains(a)||blocked.Contains(c)||g.Edges.Contains(GroundGraph.Between(a,c)))clear=false;}
   if(!clear)continue;if(!g.Vertical.TryGetValue(low,out var lower))g.Vertical[low]=lower=[];lower.Add(high);if(!g.Vertical.TryGetValue(high,out var upper))g.Vertical[high]=upper=[];upper.Add(low);g.Roads.Add(low);g.Roads.Add(high);
  }
  g.Roads.IntersectWith(g.Nodes);
  if(g.Complete){var q=new Queue<WalkPoint>();foreach(var road in g.Roads)if(g.StreetReachable.Add(road))q.Enqueue(road);var visited=0;while(q.TryDequeue(out var c)){if((++visited&1023)==0)token.ThrowIfCancellationRequested();foreach(var next in g.Neighbors(c))if(g.StreetReachable.Add(next))q.Enqueue(next);}}
  return g;
 }
 static void AuditRoadsAndGameplay(MapProject p,GroundGraph graph,GenerationQualityReport r,CancellationToken token,AddIssue issue,Action<string> partial){
  var scope=r.Area;r.Counts.RoadTiles=graph.Roads.Count;
  if(graph.Complete){
   var unseen=graph.Roads.ToHashSet();var components=new List<(WalkPoint Point,int Count,bool Boundary)>();var q=new Queue<WalkPoint>();
   while(unseen.Count>0){token.ThrowIfCancellationRequested();var first=unseen.First();unseen.Remove(first);q.Enqueue(first);var count=0;var boundary=false;while(q.TryDequeue(out var c)){if((++count&1023)==0)token.ThrowIfCancellationRequested();boundary|=c.X==scope.X||c.Y==scope.Y||c.X==scope.X+scope.Width-1||c.Y==scope.Y+scope.Height-1;foreach(var next in graph.Neighbors(c))if(unseen.Remove(next))q.Enqueue(next);}components.Add((first,count,boundary));}
   r.Counts.RoadComponents=components.Count;
   foreach(var component in components.OrderByDescending(c=>c.Count).Skip(1))issue("road-island","warning","roads",$"Street/deck component has {component.Count} tiles and no four-neighbor road or authored stair link to the largest component inside Area."+(component.Boundary?" It reaches the area boundary; an outside connection may be intentional.":" Add a connecting road or inspect the detached feature."),component.Point.X,component.Point.Y,component.Point.Z);
  }
  if(graph.Roads.Count==0)issue("no-street-network","warning","roads","Area has no passable road or infrastructure deck. Street-route checks are unavailable; this may be intentional in wilderness.",scope.X,scope.Y);
  var zones=p.GameZones.Where(z=>z is not null&&z.X<(long)scope.X+scope.Width&&z.Y<(long)scope.Y+scope.Height&&(long)z.X+z.Width>scope.X&&(long)z.Y+z.Height>scope.Y).ToArray();
  r.Counts.Zones=zones.Length;r.Counts.ZonesByType=zones.GroupBy(z=>z.Type).ToDictionary(g=>g.Key,g=>g.Count());
  var covered=new HashSet<int>();var ordinary=new Dictionary<int,string>();var vehicles=new HashSet<int>();long zoneWork=0;
  foreach(var z in zones){
   token.ThrowIfCancellationRequested();if(z.Width<1||z.Height<1||z.Z!=0){issue("invalid-gameplay-zone","error","gameplay","Gameplay zone needs a positive ground-level rectangle.",z.X,z.Y,z.Z);continue;}
   var touched=(long)(Math.Min((long)scope.X+scope.Width,(long)z.X+z.Width)-Math.Max(scope.X,z.X))*(Math.Min((long)scope.Y+scope.Height,(long)z.Y+z.Height)-Math.Max(scope.Y,z.Y));
   if((zoneWork+=touched)>4000000){partial("Zone tile work capped at 4,000,000; ZoneCoveredTiles counts only fully inspected zones.");break;}
   var conflict=false;var unsafeVehicle=false;
   for(var y=Math.Max(scope.Y,z.Y);y<Math.Min((long)scope.Y+scope.Height,(long)z.Y+z.Height);y++){token.ThrowIfCancellationRequested();for(var x=Math.Max(scope.X,z.X);x<Math.Min((long)scope.X+scope.Width,(long)z.X+z.Width);x++){
    var cell=y*p.Width+x;covered.Add(cell);
    if(z.Vehicle){conflict|=!vehicles.Add(cell);unsafeVehicle|=!graph.Contains(new(x,y,0))||p.Terrain[cell]==Terrain.Water;}
    else if(!ordinary.TryAdd(cell,z.Type)&&ordinary[cell]!=z.Type)conflict=true;
   }}
   if(conflict)issue("gameplay-zone-overlap","warning","gameplay","Zone overlaps another zone in its layer; review which gameplay distribution should apply.",z.X,z.Y,z.Z);
   if(unsafeVehicle&&graph.Complete)issue("vehicle-zone-obstructed","warning","gameplay","Vehicle zone includes water or blocked tiles. Review spawnable parking clearance.",z.X,z.Y,z.Z);
  }
  r.Counts.ZoneCoveredTiles=covered.Count;
  if(zones.Length==0)issue("gameplay-zones-missing","warning","gameplay","No authored gameplay zones intersect Area. Review town, forest, navigation and vehicle distributions before export.",scope.X,scope.Y);
  else if(r.Coverage.BuildingsInArea>0&&zones.All(z=>z.Type is "Forest" or "DeepForest" or "Vegitation"))issue("gameplay-urban-coverage","warning","gameplay","Only vegetation zones are authored around buildings; review urban loot/population and navigation distribution.",scope.X,scope.Y);
  foreach(var spawn in p.Spawns.Where(s=>s is not null&&graph.Inside(s.X,s.Y))){
   token.ThrowIfCancellationRequested();r.Counts.Spawns++;var c=new WalkPoint(spawn.X,spawn.Y,0);
   if(!graph.Complete)continue;
   if(!graph.Contains(c))issue("spawn-blocked","error","gameplay","Player spawn is on water, a collision footprint, a stair hole or unsupported ground.",spawn.X,spawn.Y);
   else if(graph.Roads.Count>0&&!graph.StreetReachable.Contains(c))issue("spawn-no-street-route","warning","gameplay","Player spawn has no structural pedestrian route to a street inside Area.",spawn.X,spawn.Y);
   else if(graph.Roads.Count>0)r.Counts.SpawnRoutes++;
  }
  if(r.Counts.Spawns==0)issue("spawn-missing","warning","gameplay","Area has no player spawn. Add one if this section should support a starting location.",scope.X,scope.Y);
 }
}
