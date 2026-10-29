namespace SimplePZMapmaker.Core;

/// <summary>Shared ground occupancy and pedestrian reservations for additive parcel generators.</summary>
public sealed class ParcelPlacement
{
 readonly MapProject project;
 readonly byte[] edits;
 readonly HashSet<(int X,int Y)> occupied=[];
 readonly HashSet<(int X,int Y)> passages=[];
 public ParcelPlacement(MapProject project)
 {
  this.project=project;edits=TerrainEditMask.Decode(project);
  foreach(var b in project.Buildings)Add(b);
  foreach(var d in project.Decorations)Add(d);
  foreach(var m in project.StaticModels)foreach(var c in StaticModelPlacement.FootprintCells(m))Block(m.X+c.Dx,m.Y+c.Dy);
  foreach(var s in project.Spawns)Block(s.X,s.Y);
  foreach(var zone in project.GameZones)if(ParkingLayout.RearAisle(zone) is {} aisle)
   for(var y=Math.Max(0,aisle.Y);y<Math.Min(project.Height,aisle.Y+aisle.Height);y++)for(var x=Math.Max(0,aisle.X);x<Math.Min(project.Width,aisle.X+aisle.Width);x++)passages.Add((x,y));
  foreach(var i in SiteModel.LockedCells(project))Block(i%project.Width,i/project.Width);
  foreach(var f in project.LinearFeatures.Where(f=>f.Level!=0||f.Kind is "river" or "railway" or "bridge"))foreach(var i in LinearFeatureEditor.Rasterize(f,project.Width,project.Height))Block(i%project.Width,i/project.Width);
  if(InfrastructureGeometry.HasFeatures(project)){var infrastructure=InfrastructureGeometry.Build(project);foreach(var t in infrastructure.Tiles)Block(t.X,t.Y);foreach(var h in infrastructure.Holes)Block(h.X,h.Y);}
  if(project.Fences.Count>0)foreach(var piece in FenceLines.Compose(project.Fences,project.FenceStyles,project.GateStyles,project.Width,project.Height).Pieces)Block(piece.X,piece.Y);
 }
 public bool Inside(int x,int y)=>x>=0&&y>=0&&x<project.Width&&y<project.Height;
 public bool Occupied(int x,int y)=>occupied.Contains((x,y));
 public bool Passage(int x,int y)=>passages.Contains((x,y));
 public void Block(int x,int y){if(Inside(x,y))occupied.Add((x,y));}
 public void ReservePassage(IEnumerable<(int X,int Y)> cells){foreach(var cell in cells)if(Inside(cell.X,cell.Y))passages.Add(cell);}
 public void Add(Building b){foreach(var i in BuildingDesigner.Occupied(b))Block(b.X+i%(b.Width+1),b.Y+i/(b.Width+1));}
 public void Add(WorldDecoration d){foreach(var p in d.Parts)Block(d.X+p.Dx,d.Y+p.Dy);}
 public bool Free(int x,int y,bool allowRoad=false,bool forPassage=false)
 {
  if(!Inside(x,y)||Occupied(x,y)||!forPassage&&Passage(x,y))return false;
  var i=y*project.Width+x;
  return project.Terrain[i]!=Terrain.Water&&(allowRoad||project.Terrain[i]!=Terrain.Road)&&!project.SurfaceTiles.ContainsKey(i)&&!TerrainEditMask.IsEdited(edits,i)&&!(project.Generation?.ProtectedAreas.Any(a=>a.Contains(x,y))??false);
 }
 public bool CanPlace(IEnumerable<(int X,int Y)> cells,bool allowRoad=false)=>cells.All(c=>Free(c.X,c.Y,allowRoad));
 /// <summary>Reserve the actual exterior entrance route before adding optional yard furniture.</summary>
 public void ReserveEntrances(int reach=32)
 {
  foreach(var b in project.Buildings.Where(b=>b.Design is not null))foreach(var entrance in SettlementBuildingOrientation.Entrances(b))
  {
   if(!Inside(entrance.X,entrance.Y))continue;
   var queue=new Queue<(int X,int Y)>();var previous=new Dictionary<(int X,int Y),(int X,int Y)>();
   queue.Enqueue(entrance);previous[entrance]=entrance;
   while(queue.TryDequeue(out var at)&&previous.Count<4096)
   {
    if(project.Terrain[at.Y*project.Width+at.X]==Terrain.Road){while(true){passages.Add(at);if(at==entrance)break;at=previous[at];}break;}
    foreach(var (dx,dy) in new[]{(0,-1),(1,0),(0,1),(-1,0)}){var next=(X:at.X+dx,Y:at.Y+dy);if(Math.Abs(next.X-entrance.X)+Math.Abs(next.Y-entrance.Y)>reach||previous.ContainsKey(next)||!Free(next.X,next.Y,true,true))continue;previous[next]=at;queue.Enqueue(next);}
   }
   // A doorway stays usable even when the road is outside the bounded search.
   passages.Add(entrance);
  }
 }
}
