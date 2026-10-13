namespace SimplePZMapmaker.Core;
/// <summary>Vanilla30-degree slope pieces, selected from occupied room spans instead of bounding-box padding.</summary>
public static class RoofGeometry
{
 public readonly record struct RoofPiece(int Depth,int TileIndex);
 public static RoofPiece Piece(Building building,BuildingFloor floor,int x,int y){
  bool Covered(int xx,int yy){var code=BuildingDesigner.Cell(building,floor,xx,yy);return code!='.'&&floor.Rooms[code.ToString()]!="balcony";}
  if(!Covered(x,y))throw new ArgumentException("A roof can only be placed above an indoor tile.");
  var west=0;while(Covered(x-west-1,y))west++;var east=0;while(Covered(x+east+1,y))east++;var north=0;while(Covered(x,y-north-1))north++;var south=0;while(Covered(x,y+south+1))south++;
  if(building.Design!.Roof=="hipped"){
   var depth=Math.Min(Math.Min(west,east),Math.Min(north,south));var h=depth%6;var w=west==depth;var e=east==depth;var n=north==depth;var s=south==depth;
   // TileZed BuildingTiles.txt defines actual convex corner and ridge pieces for all six heights.
   var tile=w&&e&&n&&s?112+h:w&&e?109-h:n&&s?96+h:w&&n?93-h:w&&s?80+h:e&&n?77-h:e&&s?64+h:w?16+h:e?13-h:n?29-h:h;
   return new(depth,tile);
  }
  // Local spans allow independent slopes on stepped roofs and protect balconies/open courtyards.
  var acrossX=west+east<=north+south;var first=acrossX?west:north;var second=acrossX?east:south;var shed=building.Design.Roof=="shed";var rise=shed||first<=second;var d=shed?first:Math.Min(first,second);
  return new(d,!shed&&first==second?(acrossX?109-d%6:96+d%6):acrossX?(rise?16+d%6:13-d%6):(rise?29-d%6:d%6));
 }
}
