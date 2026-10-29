namespace SimplePZMapmaker.Core;

/// <summary>Feature-owned bridge/deck floors and cardinal stairs; native ground bridges retain water terrain.</summary>
public static class InfrastructureGeometry
{
 public const string DeckTile="blends_street_01_0";
 public const int MaximumTiles=250000;
 public sealed record Tile(int X,int Y,int Z,string Id,byte Flags=0,bool Artwork=false);
 public sealed record Hole(int X,int Y,int Z);
 public sealed record Geometry(List<Tile> Tiles,List<Hole> Holes){internal Dictionary<string,HashSet<(int X,int Y)>>? WorldCells {get;init;}internal HashSet<(int X,int Y)>? RampApproaches {get;init;}}
 public static bool HasAuthoring(LinearFeature f)=>f.Level!=0||f.Access!="none"||f.GuardRails||f.SupportSpacing!=0||f.GroupId is not null;
 public static bool HasDeck(LinearFeature f)=>f.Level!=0||f.Kind=="bridge";
 public static bool HasFeatures(MapProject p)=>p.LinearFeatures.Any(HasDeck)||p.SharedInfrastructure is {Tiles.Count:>0} or {Holes.Count:>0};
 public static Geometry Build(MapProject p)
 {
  var tiles=new Dictionary<(int,int,int,string),Tile>();var holes=new HashSet<Hole>();var floors=new HashSet<(int X,int Y,int Z)>();var rails=new List<(LinearFeature Feature,HashSet<int> Cells)>();var approaches=new HashSet<(int X,int Y)>();
  void Add(int x,int y,int z,string id,byte flags=0){if(x<0||y<0||x>=p.Width||y>=p.Height||!ZLevels.Contains(z))throw new ArgumentException("Infrastructure or its stair access extends outside the map or signed floor range.");if(tiles.Count>=MaximumTiles)throw new ArgumentException("Infrastructure exceeds 250,000 native tiles. Shorten decks or reduce height/supports.");tiles.TryAdd((x,y,z,id),new(x,y,z,id,flags));}
  void Floor(int x,int y,int z){floors.Add((x,y,z));Add(x,y,z,DeckTile);}
  foreach(var f in p.LinearFeatures.Where(HasDeck)){
   LinearFeatureEditor.ValidateFeature(f);
   if(f.Access=="ramps"){
    Terrain Ground(int x,int y){if(x<0||y<0||x>=p.Width||y>=p.Height)throw new ArgumentException("Keep the entire native road ramp inside the map.");return p.Terrain[y*p.Width+x];}
    var plan=RoadRampGeometry.Plan(f,Ground);approaches.UnionWith(plan.Approaches);foreach(var tile in plan.Geometry.Tiles){Add(tile.X,tile.Y,tile.Z,tile.Id,tile.Flags);if(tile.Id==DeckTile)floors.Add((tile.X,tile.Y,tile.Z));}continue;
   }
   var cells=LinearFeatureEditor.Rasterize(f,p.Width,p.Height);if(cells.Count==0||f.Level!=0&&f.Points.Any(v=>v.X<f.Width/2||v.Y<f.Width/2||v.X>p.Width-f.Width/2||v.Y>p.Height-f.Width/2))throw new ArgumentException("Keep the entire elevated deck inside the map; extend a multi-cell map for bridges across cell seams.");
   DeckArtwork.ValidateCoverage(f,(x,y)=>x>=0&&y>=0&&x<p.Width&&y<p.Height&&cells.Contains(y*p.Width+x));
   foreach(var i in cells)Floor(i%p.Width,i/p.Width,f.Level);foreach(var a in f.DeckArtwork){Add(a.X,a.Y,f.Level,a.Tile);tiles[(a.X,a.Y,f.Level,a.Tile)]=new(a.X,a.Y,f.Level,a.Tile,Artwork:true);}if(f.GuardRails)rails.Add((f,cells));
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
  if(p.SharedInfrastructure is {} shared){if(shared.RampApproaches is {} sharedApproaches)approaches.UnionWith(sharedApproaches);foreach(var tile in shared.Tiles){Add(tile.X,tile.Y,tile.Z,tile.Id,tile.Flags);if(tile.Artwork)tiles[(tile.X,tile.Y,tile.Z,tile.Id)]=tile;}foreach(var hole in shared.Holes){holes.Add(hole);tiles.Remove((hole.X,hole.Y,hole.Z,DeckTile));}}
  var artworkCells=new HashSet<(int,int,int)>();foreach(var tile in tiles.Values.Where(t=>t.Artwork))if(holes.Contains(new(tile.X,tile.Y,tile.Z))||!tiles.ContainsKey((tile.X,tile.Y,tile.Z,DeckTile))||!artworkCells.Add((tile.X,tile.Y,tile.Z)))throw new ArgumentException("Deck artwork overlaps a stair opening or conflicting artwork, or has no supporting deck.");
  ValidateRampFloors(tiles.Values,holes);
  return new(tiles.Values.ToList(),holes.ToList()){RampApproaches=approaches};
 }
 internal static void ValidateRampFloors(IEnumerable<Tile> tiles,IEnumerable<Hole> holes){
  var material=tiles.ToList();var slopes=new HashSet<(int X,int Y,int Z)>();
  void Conflict()=>throw new ArgumentException("A native road ramp intersects another floor, stair, support, guard face, artwork or opening. Keep its slope squares and adjoining collision faces clear.");
  foreach(var t in material.Where(t=>RoadRampGeometry.IsRampTile(t.Id)))if(t.Artwork||!slopes.Add((t.X,t.Y,t.Z)))Conflict();
  if(slopes.Count==0)return;
  foreach(var t in material){
   if(slopes.Contains((t.X,t.Y,t.Z))&&!RoadRampGeometry.IsRampTile(t.Id))Conflict();
   // North/west faces are owned by the square below/right of the boundary.
   // Their owner may lie outside a narrow approach while still blocking it.
   if((t.Flags&2)!=0&&slopes.Contains((t.X,t.Y-1,t.Z))||(t.Flags&4)!=0&&slopes.Contains((t.X-1,t.Y,t.Z)))Conflict();
  }
  foreach(var h in holes)if(slopes.Contains((h.X,h.Y,h.Z)))Conflict();
 }
 public static void Validate(MapProject p)
 {
  if(!HasFeatures(p))return;var geometry=Build(p);var mask=TerrainEditMask.Decode(p);var groundBlocked=new HashSet<int>();var occupiedBuildings=new HashSet<int>();
  foreach(var b in p.Buildings)foreach(var i in BuildingDesigner.Occupied(b)){var x=b.X+i%(b.Width+1);var y=b.Y+i/(b.Width+1);if(x>=0&&y>=0&&x<p.Width&&y<p.Height)occupiedBuildings.Add(y*p.Width+x);}
  foreach(var d in p.Decorations)foreach(var part in d.Parts)groundBlocked.Add((d.Y+part.Dy)*p.Width+d.X+part.Dx);
  foreach(var m in p.StaticModels)foreach(var c in StaticModelPlacement.FootprintCells(m))groundBlocked.Add((m.Y+c.Dy)*p.Width+m.X+c.Dx);
  foreach(var s in p.Spawns)groundBlocked.Add(s.Y*p.Width+s.X);
  // A floor can support props and edge fences. Their native overlays are kept by
  // the composer; solid stair/support pieces still reject occupied ground.
  var authoredGround=new HashSet<int>(p.SurfaceTiles.Keys);
  if(geometry.RampApproaches is {Count:>0} rampCells&&p.Fences.Count>0){
   // Gate tiles have zero navigation flags in the composed artwork. Compose their
   // underlying cardinal edges as closed faces as well, so a gate/frame cannot be
   // erased or left colliding with a slope. Posts retain their occupied cells.
   // The extra east/south owner row catches an adjacent face just outside a section.
   var faces=FenceLines.Compose(p.Fences.Select(f=>new FenceLine{Id=f.Id,Style=f.Style,PostSpacing=f.PostSpacing,Points=f.Points}),p.FenceStyles,p.GateStyles,p.Width+1,p.Height+1);
   foreach(var piece in faces.Pieces)if(rampCells.Contains((piece.X,piece.Y))||(piece.Flags&2)!=0&&rampCells.Contains((piece.X,piece.Y-1))||(piece.Flags&4)!=0&&rampCells.Contains((piece.X-1,piece.Y)))throw new ArgumentException("A native road ramp approach intersects an authored fence, gate, post or adjacent collision face. Move the ramp or fence first.");
  }
  foreach(var (x,y) in geometry.RampApproaches??[]){var i=y*p.Width+x;if(occupiedBuildings.Contains(i)||groundBlocked.Contains(i)||p.Terrain[i]==Terrain.Water||authoredGround.Contains(i)||TerrainEditMask.IsEdited(mask,i))throw new ArgumentException("Native road ramp approaches overlap water, a building, ground prop/model, spawn or authored surface. Clear the entire approach before placing ramps.");}
  foreach(var t in geometry.Tiles){if(!t.Artwork&&p.AssetSources.TryGetValue(t.Id,out var provider)&&provider!="game")throw new ArgumentException("Native infrastructure requires its original game tile provider.");var i=t.Y*p.Width+t.X;if(occupiedBuildings.Contains(i)||t.Z==0&&!t.Artwork&&t.Id!=DeckTile&&groundBlocked.Contains(i))throw new ArgumentException("Infrastructure collides with a building, ground prop/model, or spawn. Move the deck, support or stair access.");if(t.Z==0&&t.Id==DeckTile&&authoredGround.Contains(i))throw new ArgumentException("A ground deck overlaps an authored surface. Move the deck or the authored surface.");}
  foreach(var h in geometry.Holes){var i=h.Y*p.Width+h.X;if(occupiedBuildings.Contains(i)||h.Z==0&&(groundBlocked.Contains(i)||p.Terrain[i]==Terrain.Water||p.SurfaceTiles.ContainsKey(i)||TerrainEditMask.IsEdited(mask,i)))throw new ArgumentException("A stair opening overlaps authored ground or occupied geometry. Move its first node.");}
  // An access shaft cannot silently erase another deck at the same floor.
  foreach(var f in p.LinearFeatures.Where(f=>HasDeck(f)&&f.Access=="none")){var cells=LinearFeatureEditor.Rasterize(f,p.Width,p.Height);if(geometry.Holes.Any(h=>h.Z==f.Level&&cells.Contains(h.Y*p.Width+h.X)))throw new ArgumentException("A stair shaft intersects another deck. Move its access or choose another floor.");}
 }
}
