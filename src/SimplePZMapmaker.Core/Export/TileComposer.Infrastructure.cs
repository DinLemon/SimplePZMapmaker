namespace SimplePZMapmaker.Core.Export;
internal sealed partial class TileComposer
{
 private void ComposeInfrastructure(InfrastructureGeometry.Geometry? prepared=null){
  if(!InfrastructureGeometry.HasFeatures(project))return;var geometry=prepared??InfrastructureGeometry.Build(project);
  foreach(var tile in geometry.Tiles.OrderBy(t=>t.Id==InfrastructureGeometry.DeckTile?0:1)){
   var x=tile.X-ox;var y=tile.Y-oy;if(!InCell(x,y))continue;MinLevel=Math.Min(MinLevel,tile.Z);MaxLevel=Math.Max(MaxLevel,tile.Z);
   if(RoadRampGeometry.IsRampTile(tile.Id)){
    // Native getFloor() returns the first solidfloor object. Slopes replace the
    // ordinary floor rather than being appended behind it as decorative artwork.
    Set(x,y,tile.Z,[tile.Id]);if(tile.Z==0)Navigation[y*256+x]&=unchecked((byte)~8);
   }else if(tile.Id==InfrastructureGeometry.DeckTile){Set(x,y,tile.Z,tile.Z==0?[tile.Id,..groundOverlays.GetValueOrDefault(y*256+x)??[]]:[tile.Id]);if(tile.Z==0)Navigation[y*256+x]&=unchecked((byte)~8);}
   else AddAt(x,y,tile.Z,tile.Id,tile.Flags);
  }
  foreach(var hole in geometry.Holes){var x=hole.X-ox;var y=hole.Y-oy;if(!InCell(x,y))continue;
   // This empty native square owns the stair shaft, including openings through level-zero terrain.
   Set(x,y,hole.Z,[]);
  }
 }
}
