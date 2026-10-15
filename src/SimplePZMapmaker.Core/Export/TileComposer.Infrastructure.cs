namespace SimplePZMapmaker.Core.Export;
internal sealed partial class TileComposer
{
 private void ComposeInfrastructure(InfrastructureGeometry.Geometry? prepared=null){
  if(!InfrastructureGeometry.HasFeatures(project))return;var geometry=prepared??InfrastructureGeometry.Build(project);
  foreach(var tile in geometry.Tiles.OrderBy(t=>t.Id==InfrastructureGeometry.DeckTile?0:1)){if(!InCell(tile.X-ox,tile.Y-oy))continue;MinLevel=Math.Min(MinLevel,tile.Z);MaxLevel=Math.Max(MaxLevel,tile.Z);if(tile.Id==InfrastructureGeometry.DeckTile)Set(tile.X-ox,tile.Y-oy,tile.Z,[tile.Id]);else AddAt(tile.X-ox,tile.Y-oy,tile.Z,tile.Id,tile.Flags);}
  foreach(var hole in geometry.Holes){var x=hole.X-ox;var y=hole.Y-oy;if(!InCell(x,y))continue;
   // This empty native square owns the stair shaft, including openings through level-zero terrain.
   Set(x,y,hole.Z,[]);
  }
 }
}
