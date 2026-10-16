namespace SimplePZMapmaker.Core;

public sealed record GenerationQualityArea(int X,int Y,int Width,int Height);
public sealed record GenerationQualityIssue(string Code,string Severity,string Category,string Message,int X,int Y,int Z,string? BuildingId=null);
public sealed class GenerationQualityCounts {
 public int Buildings {get;set;} public int Rooms {get;set;} public int Windows {get;set;} public int ExteriorEntrances {get;set;}
 public int RoadTiles {get;set;} public int RoadComponents {get;set;} public int Zones {get;set;} public int ZoneCoveredTiles {get;set;}
 public Dictionary<string,int> ZonesByType {get;set;}=[];
 public int Spawns {get;set;} public int SpawnRoutes {get;set;} public int Errors {get;set;} public int Warnings {get;set;}
}
public sealed class GenerationQualityCoverage {
 public GenerationQualityArea RequestedArea {get;set;}=new(0,0,0,0);
 public int TerrainTiles {get;set;} public int BuildingsInArea {get;set;} public int BuildingsAnalyzed {get;set;} public int AccessBuildingsAnalyzed {get;set;}
 public int MaximumBuildings {get;set;} public int MaximumTerrainTiles {get;set;}=262144;
 public int AccessNodes {get;set;} public int MaximumAccessNodes {get;set;}=2000000;
 public int ArchitectureCells {get;set;} public int MaximumArchitectureCells {get;set;}=4000000;
 public bool GroundGraphComplete {get;set;}=true; public int IssuesOmitted {get;set;}
}
public sealed class GenerationQualityReport {
 public string Status {get;set;}="pass"; public bool Complete {get;set;}=true;
 public GenerationQualityArea Area {get;set;}=new(0,0,0,0);
 public GenerationQualityCounts Counts {get;set;}=new(); public GenerationQualityCoverage Coverage {get;set;}=new();
 public List<GenerationQualityIssue> Issues {get;set;}=[]; public List<string> Notes {get;set;}=[];
}

/// <summary>Read-only structural and gameplay authoring audit. No normalization or generation is performed.</summary>
public static partial class GenerationQuality {
 public const int MaximumIssues=2048;
 public static GenerationQualityReport Analyze(MapProject p,GenerationQualityArea? area=null,int maximumBuildings=128,CancellationToken token=default,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  token.ThrowIfCancellationRequested();
  if(p is null||p.Width is <1 or >2048||p.Height is <1 or >2048||p.Terrain is null||p.Terrain.Length!=(long)p.Width*p.Height||p.Buildings is null||p.Buildings.Count>20000||p.GameZones is null||p.GameZones.Count>4096||p.Spawns is null||p.Spawns.Count>4096||p.Decorations is null||p.Decorations.Count>200000||p.StaticModels is null||p.StaticModels.Count>20000||p.LinearFeatures is null||p.LinearFeatures.Count>LinearFeatureEditor.MaximumFeatures||p.SharedInfrastructure is {} shared&&(shared.Tiles is null||shared.Holes is null||shared.Tiles.Count>InfrastructureGeometry.MaximumTiles||shared.Holes.Count>InfrastructureGeometry.MaximumTiles))throw new ArgumentException("Quality audit requires a bounded map with terrain and authored layers.");
  if(maximumBuildings is <1 or >512)throw new ArgumentException("MaximumBuildings must be 1–512.");
  var requested=area??new(0,0,p.Width,p.Height);
  if(requested.X<0||requested.Y<0||requested.Width<1||requested.Height<1||(long)requested.X+requested.Width>p.Width||(long)requested.Y+requested.Height>p.Height)throw new ArgumentException("Quality area must be inside the map.");
  // The explicit cap is rectangular and reported. A partial report never claims the whole map passed.
  var width=Math.Min(requested.Width,512);var height=Math.Min(requested.Height,512);
  var r=new GenerationQualityReport{Area=new(requested.X,requested.Y,width,height),Coverage=new(){RequestedArea=requested,MaximumBuildings=maximumBuildings}};
  void Partial(string note){r.Complete=false;if(!r.Notes.Contains(note))r.Notes.Add(note);}
  void Issue(string code,string severity,string category,string message,int x,int y,int z=0,string? id=null){
   if(severity=="error")r.Counts.Errors++;else if(severity=="warning")r.Counts.Warnings++;
   if(r.Issues.Count<MaximumIssues)r.Issues.Add(new(code,severity,category,message,x,y,z,id));else{r.Coverage.IssuesOmitted++;Partial("Issue cap reached; inspect a smaller area for all issue locations.");}
  }
  if(r.Area!=requested)Partial("Terrain/access area capped at 512 tiles per side and 262,144 tiles; Area reports the inspected rectangle. Request additional areas for the remainder.");
  var scope=r.Area;
  bool Intersects(Building b)=>b.X<(long)scope.X+scope.Width&&b.Y<(long)scope.Y+scope.Height&&(long)b.X+b.Width>scope.X&&(long)b.Y+b.Height>scope.Y;
  if(p.Buildings.Any(b=>b is null))throw new ArgumentException("A building is missing.");
  var buildings=p.Buildings.Where(Intersects).ToArray();r.Coverage.BuildingsInArea=buildings.Length;
  if(buildings.Length>maximumBuildings)Partial($"Building audit capped at {maximumBuildings} of {buildings.Length} intersecting buildings; increase MaximumBuildings or use smaller areas.");
  var selected=buildings.Take(maximumBuildings).ToArray();var valid=new List<Building>();
  foreach(var b in buildings){token.ThrowIfCancellationRequested();if(b.Width is <1 or >64||b.Height is <1 or >64||b.Design is {} d&&(d.Floors is null||d.Floors.Count is <1 or >64||!ZLevels.Contains(d.MinLevel)||d.MinLevel+d.Floors.Count-1>ZLevels.Max||d.Floors.Any(f=>f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Edges is null||f.Edges.Count>8450||f.Edges.Any(e=>e is null||e.Edge is not("n" or "w")||e.X<0||e.Y<0||e.X>b.Width||e.Y>b.Height)||f.Rooms is null||f.Objects is null||f.Objects.Count>512||f.StaticModels is null||f.StaticModels.Count>512))){Issue("invalid-building","error","architecture","Building floor data is malformed; repair its dimensions or floor layers before access analysis.",b.X,b.Y,0,b.Id);Partial("Malformed buildings were excluded from access geometry.");r.Coverage.GroundGraphComplete=false;continue;}valid.Add(b);}
  var graph=BuildGroundGraph(p,valid,scope,token,properties,Partial);r.Coverage.GroundGraphComplete&=graph.Complete;
  r.Coverage.TerrainTiles=scope.Width*scope.Height;
  AuditRoadsAndGameplay(p,graph,r,token,Issue,Partial);
  foreach(var b in selected){
   token.ThrowIfCancellationRequested();if(!valid.Contains(b))continue;
   var architectureCells=b.Design?.Floors.Sum(f=>f.Cells.Length)??0;
   if(r.Coverage.ArchitectureCells+architectureCells>r.Coverage.MaximumArchitectureCells){Partial("Architecture cell budget reached; additional detailed building audits were not run.");continue;}
   r.Coverage.ArchitectureCells+=architectureCells;r.Coverage.BuildingsAnalyzed++;r.Counts.Buildings++;
   if(b.Design is null){Partial("Legacy buildings have no authored floors: room, facade and vertical access audits require workshop floor authoring.");Issue("legacy-floor-coverage","warning","coverage","Generate authored floors to inspect rooms and vertical access.",b.X,b.Y,0,b.Id);continue;}
   AuditArchitecture(b,r,graph,token,Issue);
   // Local access isolates the full building from crop boundaries. The external graph separately checks its real approaches.
   var estimated=(long)(b.Width+4)*(b.Height+4)+b.Design.Floors.Sum(f=>(long)f.Cells.Count(c=>c!='.'));
   if(r.Coverage.AccessNodes+estimated>r.Coverage.MaximumAccessNodes){Partial("Aggregate access node budget reached; additional building access graphs were not run.");continue;}
   var access=WalkabilityAnalyzer.Building(b,token,properties);r.Coverage.AccessNodes+=access.Nodes;r.Coverage.AccessBuildingsAnalyzed++;
   if(!access.Complete){Partial($"Building {b.Id} has incomplete access coverage.");foreach(var note in access.Notes)Partial(note);}
   foreach(var i in access.Issues)Issue(i.Code,i.Severity,"access",i.Message,b.X+i.X,b.Y+i.Y,i.Z,b.Id);
   foreach(var level in access.Issues.Where(i=>i.Code is "unreachable-room" or "blocked-room").GroupBy(i=>i.Z))if(level.Key<0)Issue("basement-access","error","access","Basement rooms have no usable route through the authored stairs to an outdoor approach.",b.X+level.First().X,b.Y+level.First().Y,level.Key,b.Id);
  }
  r.Notes.Add("Access is structural: unlocked doors may open; windows, locked model doors, collision footprints, railings and native stair landings constrain walking. No climbing, demolition or gameplay simulation.");
  r.Notes.Add("Building interior access uses a complete local building graph; street, courtyard and spawn checks use real scoped outdoor geometry. Routes beyond Area are unknown; road components are components inside Area.");
  r.Status=r.Counts.Errors>0?"error":r.Counts.Warnings>0||!r.Complete?"warning":"pass";return r;
 }
 delegate void AddIssue(string code,string severity,string category,string message,int x,int y,int z=0,string? id=null);
 static (char A,char B) Sides(Building b,BuildingFloor f,BuildingEdge e)=>(BuildingDesigner.Cell(b,f,e.X,e.Y),BuildingDesigner.Cell(b,f,e.X-(e.Edge=="w"?1:0),e.Y-(e.Edge=="n"?1:0)));
 static bool Exterior((char A,char B) pair)=>(pair.A=='.')!=(pair.B=='.');
 static void AuditArchitecture(Building b,GenerationQualityReport r,GroundGraph graph,CancellationToken token,AddIssue issue){
  var d=b.Design!;var residential=b.Kind is "house" or "apartments" or "hotel";var wet=false;var entries=0;
  for(var index=0;index<d.Floors.Count;index++){
   token.ThrowIfCancellationRequested();var f=d.Floors[index];var z=d.MinLevel+index;
   var windows=f.Edges.Where(e=>e.Kind=="window").ToArray();r.Counts.Windows+=windows.Length;
   foreach(var e in windows){var pair=Sides(b,f,e);if(!Exterior(pair)){
    var authored=e.Edited&&pair.A!='.'&&pair.B!='.';
    issue(authored?"authored-interior-glazing":"interior-window",authored?"info":"error","architecture",authored?"Authored interior glazing; check privacy and intentional room separation.":"Window is not on an exterior facade. Replace it with a doorway/wall or explicitly mark intentional interior glazing.",b.X+e.X,b.Y+e.Y,z,b.Id);
    if(pair.A!='.'&&pair.B!='.'&&pair.A!=pair.B&&!authored)issue("window-room-partition","error","architecture","Window crosses two different room codes; a room partition needs an intentional opening or authored interior glazing.",b.X+e.X,b.Y+e.Y,z,b.Id);
   }}
   if(index>0&&z>0){var previous=d.Floors[index-1];if(f.Cells.Select(c=>c!='.').SequenceEqual(previous.Cells.Select(c=>c!='.'))){
    var prior=previous.Edges.Where(e=>e.Kind=="window"&&Exterior(Sides(b,previous,e))).Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();
    var shifted=windows.FirstOrDefault(e=>Exterior(Sides(b,f,e))&&!prior.Contains((e.X,e.Y,e.Edge)));
    if(prior.Count>0&&shifted is not null)issue("facade-axis-variation","warning","architecture","Repeated floor footprint has a window outside the previous facade axes. Confirm the asymmetry is intentional.",b.X+shifted.X,b.Y+shifted.Y,z,b.Id);
   }}
   foreach(var group in Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]!='.').GroupBy(i=>f.Cells[i])){
    r.Counts.Rooms++;var code=group.Key;var name=f.Rooms.GetValueOrDefault(code.ToString(),"unknown");var first=group.First();var x=b.X+first%b.Width;var y=b.Y+first/b.Width;
    if(name is "bathroom" or "kitchen")wet=true;
    var exteriorLight=windows.Any(e=>Exterior(Sides(b,f,e))&&(Sides(b,f,e).A==code||Sides(b,f,e).B==code));
    if(residential&&z>=0&&name is "bedroom" or "livingroom" or "kitchen" or "hotelroom"&&!exteriorLight)issue("residential-daylight","warning","architecture",$"{name} has no exterior window. Review daylight and ventilation; borrowed/artificial light may be intentional.",x,y,z,b.Id);
    if(residential&&z>=0&&name=="bathroom"&&!exteriorLight)issue("residential-ventilation","warning","architecture","Bathroom has no exterior window. Review an intentional mechanical or borrowed-air ventilation solution.",x,y,z,b.Id);
    var passages=f.Edges.Count(e=>e.Kind is "door" or "open"&&(Sides(b,f,e).A==code||Sides(b,f,e).B==code));
    if(residential&&name is "hall" or "corridor"&&passages<2)issue("residential-circulation","warning","architecture","Circulation room has fewer than two authored passages. Review movement between entry, rooms and stairs.",x,y,z,b.Id);
   }
   if(z!=0)continue;
   foreach(var e in f.Edges.Where(e=>e.Kind is "door" or "open"&&Exterior(Sides(b,f,e)))){
    entries++;r.Counts.ExteriorEntrances++;var pair=Sides(b,f,e);var a=new WalkPoint(b.X+e.X,b.Y+e.Y,0);var other=new WalkPoint(a.X-(e.Edge=="w"?1:0),a.Y-(e.Edge=="n"?1:0),0);var outside=pair.A=='.'?a:other;
    if(!graph.Contains(outside)){if(graph.Inside(outside.X,outside.Y))issue("entrance-blocked-approach","error","access","Ground entrance opens onto water, a blocking footprint or unsupported ground.",outside.X,outside.Y,0,b.Id);else{r.Complete=false;r.Notes.Add($"Entrance for {b.Id} lies outside Area; its street route is unknown.");}}
    else if(graph.Complete&&graph.Roads.Count>0&&!graph.StreetReachable.Contains(outside))issue("entrance-no-street-route","warning","access","Ground entrance has no pedestrian route to a street inside Area. Check fences, courtyard gates and collision footprints.",outside.X,outside.Y,0,b.Id);
   }
   // A hole in a footprint enclosed by the building is a courtyard, rather than an arbitrary grass patch.
   var exterior=new HashSet<int>();var q=new Queue<int>();
   for(var i=0;i<f.Cells.Length;i++)if(f.Cells[i]=='.'&&(i%b.Width==0||i%b.Width==b.Width-1||i/b.Width==0||i/b.Width==b.Height-1)){exterior.Add(i);q.Enqueue(i);}
   while(q.TryDequeue(out var cell))foreach(var (dx,dy) in Cardinal){var nx=cell%b.Width+dx;var ny=cell/b.Width+dy;var next=ny*b.Width+nx;if(nx>=0&&ny>=0&&nx<b.Width&&ny<b.Height&&f.Cells[next]=='.'&&exterior.Add(next))q.Enqueue(next);}
   var enclosed=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]=='.'&&!exterior.Contains(i)).ToArray();
   if(enclosed.Length>0&&graph.Complete&&graph.Roads.Count>0&&!enclosed.Any(i=>graph.StreetReachable.Contains(new(b.X+i%b.Width,b.Y+i/b.Width,0))))issue("courtyard-isolated","warning","access","Enclosed courtyard has no passable route to a street. Add or unblock a courtyard door/gate.",b.X+enclosed[0]%b.Width,b.Y+enclosed[0]/b.Width,0,b.Id);
  }
  if(entries==0)issue("missing-ground-entrance","error","access","Building has no exterior ground-level door or open passage.",b.X,b.Y,0,b.Id);
  if(residential&&!wet)issue("residential-wet-room","warning","architecture","Residential building has no authored kitchen or bathroom room. Review dwelling facilities; detached annexes may be intentional.",b.X,b.Y,0,b.Id);
 }
}
