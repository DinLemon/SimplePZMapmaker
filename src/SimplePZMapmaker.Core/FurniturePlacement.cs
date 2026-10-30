namespace SimplePZMapmaker.Core;

/// <summary>Room-local occupancy and circulation shared by catalogue and generic furnishing.</summary>
internal sealed class FurniturePlacement
{
 readonly Building building;readonly BuildingFloor floor;readonly char code;readonly HashSet<int> passages,occupied,stairs,fronts=[];readonly HashSet<(int,int,string)> walls,windows;
 internal FurniturePlacement(Building b,BuildingFloor f,int z,char room,HashSet<int> doorways){
  building=b;floor=f;code=room;passages=doorways;
  occupied=f.Objects.SelectMany(BuildingObjects.Cells).Select(p=>Key(p.X,p.Y)).Concat(f.StaticModels.SelectMany(m=>StaticModelPlacement.FootprintCells(m).Select(p=>Key(m.X+p.Dx,m.Y+p.Dy)))).ToHashSet();
  walls=f.Edges.Where(e=>e.Kind!="open").Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();
  windows=f.Edges.Where(e=>e.Kind=="window").Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();
  stairs=b.Design!.Floors.Where((_,i)=>i==z||i==z-1).SelectMany(floor=>floor.Objects).Where(o=>o.Kind=="stairs").SelectMany(BuildingObjects.Cells).Select(p=>Key(p.X,p.Y)).ToHashSet();
 }
 int Key(int x,int y)=>y*building.Width+x;
 bool In(int x,int y)=>BuildingDesigner.Cell(building,floor,x,y)==code;
 bool Crosses((int X,int Y) a,(int X,int Y) b)=>walls.Contains(a.X!=b.X?(Math.Max(a.X,b.X),a.Y,"w"):(a.X,Math.Max(a.Y,b.Y),"n"));
 IEnumerable<(int X,int Y)> Neighbours((int X,int Y) p){foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var next=(X:p.X+dx,Y:p.Y+dy);if(In(next.X,next.Y)&&!Crosses(p,next))yield return next;}}
 internal bool Place(BuildingObject item,bool againstWall){
  if(floor.Objects.Count>=512)return false;var squares=BuildingObjects.Cells(item).ToHashSet();
  if(squares.Any(p=>!In(p.X,p.Y)||occupied.Contains(Key(p.X,p.Y))||fronts.Contains(Key(p.X,p.Y))||passages.Contains(Key(p.X,p.Y))||stairs.Contains(Key(p.X,p.Y))))return false;
  // A multi-tile object must not straddle an authored partition within one room code.
  if(squares.Any(p=>new[]{(X:p.X+1,Y:p.Y),(X:p.X,Y:p.Y+1)}.Any(n=>squares.Contains(n)&&Crosses(p,n))))return false;
  var (bx,by)=item.Direction switch{"n"=>(0,-1),"s"=>(0,1),"w"=>(-1,0),_=>(1,0)};
  // Automatic wall furniture leaves windows and their trim clear. Manual placement remains available.
  if(againstWall&&squares.Any(p=>windows.Contains(bx!=0?(p.X+(bx>0?1:0),p.Y,"w"):(p.X,p.Y+(by>0?1:0),"n"))))return false;
  if(againstWall&&squares.Any(p=>!squares.Contains((p.X+bx,p.Y+by))&&In(p.X+bx,p.Y+by)&&!Crosses(p,(p.X+bx,p.Y+by))))return false;
  var front=squares.Select(p=>(X:p.X-bx,Y:p.Y-by)).Where(p=>!squares.Contains(p)).ToArray();
  if(front.Any(p=>!In(p.X,p.Y)||occupied.Contains(Key(p.X,p.Y))||stairs.Contains(Key(p.X,p.Y))||squares.Any(s=>Math.Abs(s.X-p.X)+Math.Abs(s.Y-p.Y)==1&&Crosses(s,p))))return false;
  // Removing the footprint may not separate any of its neighbouring free squares.
  // Checking the boundary is sufficient and avoids re-flooding every existing room component.
  var boundary=squares.SelectMany(Neighbours).Where(p=>!squares.Contains(p)&&!occupied.Contains(Key(p.X,p.Y))).ToHashSet();
  if(boundary.Count>1){var queue=new Queue<(int X,int Y)>();var seen=new HashSet<(int,int)>();var first=boundary.First();queue.Enqueue(first);seen.Add(first);var remaining=boundary.Count;
   while(queue.TryDequeue(out var current)){if(boundary.Contains(current)&&--remaining==0)break;foreach(var next in Neighbours(current))if(!squares.Contains(next)&&!occupied.Contains(Key(next.X,next.Y))&&seen.Add(next))queue.Enqueue(next);}
   if(remaining>0)return false;
  }
  floor.Objects.Add(item);foreach(var p in squares)occupied.Add(Key(p.X,p.Y));foreach(var p in front)fronts.Add(Key(p.X,p.Y));return true;
 }
}
