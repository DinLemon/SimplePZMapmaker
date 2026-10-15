namespace SimplePZMapmaker.Core;
public sealed record AccessRepairResult(MapProject Project,string Change,int X,int Y,int Z);
/// <summary>One bounded, optional edit to an isolated candidate. Never moves an object or unlocks a door.</summary>
public static class AccessRepair {
 public static AccessRepairResult? Step(MapProject candidate,WalkabilityOptions crop,bool allowDoors=true,bool allowStairs=true,bool allowEditedEdges=false,CancellationToken token=default,Func<MapProject,MapProject>? project=null,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  var original=WalkabilityAnalyzer.Analyze(project?.Invoke(candidate)??candidate,crop,token,properties);if(!original.Complete||!original.Issues.Any(i=>i.Severity=="error"))return null;var attempts=0;
  bool In(int x,int y)=>x>=crop.X&&y>=crop.Y&&x<crop.X+crop.Width&&y<crop.Y+crop.Height;
  bool Improves(MapProject p)=>WalkabilityAnalyzer.Analyze(project?.Invoke(p)??p,crop,token,properties) is {} report&&report.Complete&&report.Reachable>original.Reachable;
  foreach(var issue in original.Issues.Where(i=>i.Code=="unreachable-room"&&i.BuildingId is not null)){
   // Shared world buildings are visible to the check but remain outside local ownership.
   var b=candidate.Buildings.SingleOrDefault(b=>b.Id==issue.BuildingId);if(b?.Design is null)continue;var fi=issue.Z-b.Design.MinLevel;var cells=(issue.Cells??[]).Select(c=>(c.X-b.X,c.Y-b.Y)).ToHashSet();
   if(allowDoors)foreach(var e in b.Design.Floors[fi].Edges.Where(e=>e.Kind=="wall"&&(!e.Edited||allowEditedEdges)&&In(b.X+e.X,b.Y+e.Y))){
    var ax=e.X;var ay=e.Y;var bx=e.X-(e.Edge=="w"?1:0);var by=e.Y-(e.Edge=="n"?1:0);if(!cells.Contains((ax,ay))&&!cells.Contains((bx,by)))continue;
    // Ground exterior and same-floor interior openings only. Upper exterior doors need a separately authored landing.
    if(issue.Z!=0&&(BuildingDesigner.Cell(b,b.Design.Floors[fi],ax,ay)=='.'||BuildingDesigner.Cell(b,b.Design.Floors[fi],bx,by)=='.'))continue;
    token.ThrowIfCancellationRequested();if(++attempts>64)return null;var next=NeighborhoodPlanner.Copy(candidate);var nb=next.Buildings.Single(a=>a.Id==b.Id);var edge=nb.Design!.Floors[fi].Edges.Single(a=>a.X==e.X&&a.Y==e.Y&&a.Edge==e.Edge);edge.Kind="door";edge.WallTile=null;edge.OpeningTile=null;edge.Edited=true;nb.Modified=true;
    try{BuildingDesigner.Validate(nb,false);if(Improves(next))return new(next,"Added a door to connect an inaccessible area.",b.X+e.X,b.Y+e.Y,issue.Z);}catch(ArgumentException){}
   }
   if(!allowStairs||fi==0)continue;var lower=b.Design.Floors[fi-1];var upper=b.Design.Floors[fi];
   for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)foreach(var direction in new[]{"n","w"}){
    if(!In(b.X+x,b.Y+y)||!In(b.X+x+(direction=="w"?4:0),b.Y+y+(direction=="n"?4:0)))continue;
    if(Enumerable.Range(0,5).Any(j=>BuildingDesigner.Cell(b,lower,x+(direction=="w"?j:0),y+(direction=="n"?j:0))=='.'||BuildingDesigner.Cell(b,upper,x+(direction=="w"?j:0),y+(direction=="n"?j:0))=='.'))continue;
    token.ThrowIfCancellationRequested();if(++attempts>64)return null;var next=NeighborhoodPlanner.Copy(candidate);var nb=next.Buildings.Single(a=>a.Id==b.Id);nb.Design!.Floors[fi-1].Objects.Add(new(){Kind="stairs",X=x,Y=y,Direction=direction});nb.Modified=true;
    try{BuildingDesigner.Validate(nb,false);if(Improves(next))return new(next,"Added stairs with supported lower and upper landings.",b.X+x,b.Y+y,issue.Z-1);}catch(ArgumentException){}
   }
  }
  return null;
 }
}
