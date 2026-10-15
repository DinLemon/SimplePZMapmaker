namespace SimplePZMapmaker.Core;

/// <summary>Feature-owned native floors and cardinal stair runs; elevated strokes never repaint ground terrain.</summary>
public static class InfrastructureGeometry
{
 public const string DeckTile="blends_street_01_0";
 public const int MaximumTiles=250000;
 public sealed record Tile(int X,int Y,int Z,string Id,byte Flags=0);
 public sealed record Hole(int X,int Y,int Z);
 public sealed record Geometry(List<Tile> Tiles,List<Hole> Holes){internal Dictionary<string,HashSet<(int X,int Y)>>? WorldCells {get;init;}}
 public static bool HasAuthoring(LinearFeature f)=>f.Level!=0||f.Access!="none"||f.GuardRails||f.SupportSpacing!=0||f.GroupId is not null;
 public static bool HasFeatures(MapProject p)=>p.LinearFeatures.Any(f=>f.Level!=0)||p.SharedInfrastructure is {Tiles.Count:>0} or {Holes.Count:>0};
 public static Geometry Build(MapProject p)
 {
  var tiles=new Dictionary<(int,int,int,string),Tile>();var holes=new HashSet<Hole>();var floors=new HashSet<(int X,int Y,int Z)>();var rails=new List<(LinearFeature Feature,HashSet<int> Cells)>();
  void Add(int x,int y,int z,string id,byte flags=0){if(x<0||y<0||x>=p.Width||y>=p.Height||!ZLevels.Contains(z))throw new ArgumentException("Infrastructure or its stair access extends outside the map or signed floor range.");if(tiles.Count>=MaximumTiles)throw new ArgumentException("Infrastructure exceeds 250,000 native tiles. Shorten decks or reduce height/supports.");tiles.TryAdd((x,y,z,id),new(x,y,z,id,flags));}
  void Floor(int x,int y,int z){floors.Add((x,y,z));Add(x,y,z,DeckTile);}
  foreach(var f in p.LinearFeatures.Where(f=>f.Level!=0)){
   LinearFeatureEditor.ValidateFeature(f);var cells=LinearFeatureEditor.Rasterize(f,p.Width,p.Height);if(cells.Count==0||f.Points.Any(v=>v.X<f.Width/2||v.Y<f.Width/2||v.X>p.Width-f.Width/2||v.Y>p.Height-f.Width/2))throw new ArgumentException("Keep the entire elevated deck inside the map; extend a multi-cell map for bridges across cell seams.");
   foreach(var i in cells)Floor(i%p.Width,i/p.Width,f.Level);if(f.GuardRails)rails.Add((f,cells));
   if(f.Access=="stairs"){
    var x=(int)Math.Floor(f.Points[0].X);var anchorY=(int)Math.Floor(f.Points[0].Y)-(f.Level<0?4*-f.Level:0);var low=Math.Min(0,f.Level);var high=Math.Max(0,f.Level);
    for(var z=low;z<high;z++){var y=anchorY+4*(high-z-1);Floor(x,y,z+1);Floor(x,y+4,z);for(var step=1;step<=3;step++){Add(x,y+step,z,$"carpentry_02_{99-step}");holes.Add(new(x,y+step,z+1));}}
    var groundY=f.Level>0?anchorY+4*f.Level:anchorY;var index=groundY*p.Width+x;if(p.Terrain[index]==Terrain.Water)throw new ArgumentException("Pedestrian stairs need a dry ground landing. Move the first node or add a separate dry access feature.");
   }
   if(f.SupportSpacing>0){var horizontal=Math.Abs(f.Points[^1].X-f.Points[0].X)>=Math.Abs(f.Points[^1].Y-f.Points[0].Y);foreach(var i in cells.Order()){var x=i%p.Width;var y=i/p.Width;if((horizontal?x:y)%f.SupportSpacing!=0)continue;var edge=horizontal?!cells.Contains(i-p.Width):!cells.Contains(i-1);if(!edge||p.Terrain[i] is Terrain.Road or Terrain.Pavement)continue;for(var z=0;z<f.Level;z++){Add(x,y,z,"walls_exterior_house_01_0",4);Add(x,y,z,"walls_exterior_house_01_1",2);}}}
  }
  foreach(var (f,cells) in rails)foreach(var i in cells){var x=i%p.Width;var y=i/p.Width;foreach(var (dx,dy,id,flags) in new[]{(0,-1,"fencing_01_1",(byte)2),(-1,0,"fencing_01_2",(byte)4),(0,1,"fencing_01_1",(byte)2),(1,0,"fencing_01_2",(byte)4)}){
   if(floors.Contains((x+dx,y+dy,f.Level)))continue;var rx=x+(dx==1?1:0);var ry=y+(dy==1?1:0);
   // Leave the access shaft and its step/landing transitions open to pedestrians.
   if(holes.Any(h=>h.X==rx&&Math.Abs(h.Y-ry)<=1&&h.Z==f.Level)||f.Access=="stairs"&&rx==(int)Math.Floor(f.Points[0].X)&&Math.Abs(ry-f.Points[0].Y)<=Math.Ceiling(f.Width/2)+1)continue;
   if(f.Level<0){for(var z=f.Level;z<0;z++)Add(rx,ry,z,flags==2?"walls_exterior_house_01_1":"walls_exterior_house_01_0",flags);}else Add(rx,ry,f.Level,id,flags);
  }}
  foreach(var hole in holes){tiles.Remove((hole.X,hole.Y,hole.Z,DeckTile));floors.Remove((hole.X,hole.Y,hole.Z));}
  if(p.SharedInfrastructure is {} shared){foreach(var tile in shared.Tiles)Add(tile.X,tile.Y,tile.Z,tile.Id,tile.Flags);foreach(var hole in shared.Holes){holes.Add(hole);tiles.Remove((hole.X,hole.Y,hole.Z,DeckTile));}}
  return new(tiles.Values.ToList(),holes.ToList());
 }
 public static void Validate(MapProject p)
 {
  if(!HasFeatures(p))return;var geometry=Build(p);var mask=TerrainEditMask.Decode(p);var groundBlocked=new HashSet<int>();var occupiedBuildings=new HashSet<int>();
  foreach(var b in p.Buildings)foreach(var i in BuildingDesigner.Occupied(b)){var x=b.X+i%(b.Width+1);var y=b.Y+i/(b.Width+1);occupiedBuildings.Add(y*p.Width+x);}
  foreach(var d in p.Decorations)foreach(var part in d.Parts)groundBlocked.Add((d.Y+part.Dy)*p.Width+d.X+part.Dx);
  foreach(var m in p.StaticModels)foreach(var c in StaticModelPlacement.FootprintCells(m))groundBlocked.Add((m.Y+c.Dy)*p.Width+m.X+c.Dx);
  foreach(var s in p.Spawns)groundBlocked.Add(s.Y*p.Width+s.X);
  foreach(var t in geometry.Tiles){if(p.AssetSources.TryGetValue(t.Id,out var provider)&&provider!="game")throw new ArgumentException("Native infrastructure requires its original game tile provider.");var i=t.Y*p.Width+t.X;if(occupiedBuildings.Contains(i)||t.Z==0&&groundBlocked.Contains(i))throw new ArgumentException("Infrastructure collides with a building, ground prop/model, or spawn. Move the deck, support or stair access.");}
  foreach(var h in geometry.Holes){var i=h.Y*p.Width+h.X;if(occupiedBuildings.Contains(i)||h.Z==0&&(groundBlocked.Contains(i)||p.Terrain[i]==Terrain.Water||p.SurfaceTiles.ContainsKey(i)||TerrainEditMask.IsEdited(mask,i)))throw new ArgumentException("A stair opening overlaps authored ground or occupied geometry. Move its first node.");}
  // An access shaft cannot silently erase another deck at the same floor.
  foreach(var f in p.LinearFeatures.Where(f=>f.Level!=0&&f.Access=="none")){var cells=LinearFeatureEditor.Rasterize(f,p.Width,p.Height);if(geometry.Holes.Any(h=>h.Z==f.Level&&cells.Contains(h.Y*p.Width+h.X)))throw new ArgumentException("A stair shaft intersects another deck. Move its access or choose another floor.");}
 }
}
