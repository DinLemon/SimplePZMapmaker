namespace SimplePZMapmaker.Core;
/// <summary>Vanilla30-degree slope pieces, selected from occupied room spans instead of bounding-box padding.</summary>
public static class RoofGeometry
{
 public readonly record struct RoofPiece(int Depth,int TileIndex,bool Flat=false);
 /// <summary>The highest occupied level, including exported ceilings and slope pieces.</summary>
 public static int MaxLevel(Building building){
  var design=building.Design??throw new ArgumentException("A roof requires a building design.");
  var maximum=design.MinLevel+design.Floors.Count-1;
  for(var i=0;i<design.Floors.Count;i++)foreach(var edge in design.Floors[i].Edges)if(edge.OverlayTile is not null)maximum=Math.Max(maximum,design.MinLevel+i+edge.OverlayLevelOffset);
  if(design.Roof=="none")return maximum;
  for(var index=0;index<design.Floors.Count;index++){
   var z=index+design.MinLevel;var floor=design.Floors[index];
   if(z==-1)continue;
   for(var y=0;y<building.Height;y++)for(var x=0;x<building.Width;x++){
    var code=BuildingDesigner.Cell(building,floor,x,y);
    if(code=='.'||floor.Rooms[code.ToString()]=="balcony"||index+1<design.Floors.Count&&BuildingDesigner.Cell(building,design.Floors[index+1],x,y)!='.')continue;
    var roofLevel=z+1;
    if(index==design.Floors.Count-1&&design.Roof!="flat")roofLevel+=Piece(building,floor,x,y).Depth/6;
    maximum=Math.Max(maximum,roofLevel);
   }
  }
  return maximum;
 }
 public static void Validate(Building building){
  if(MaxLevel(building)>ZLevels.Max)throw new ArgumentException($"Building '{building.Name}': the automatic roof exceeds z={ZLevels.Max}. Choose a flat roof, fewer floors, or 'No automatic roof'.");
 }
 /// <summary>Generated/imported plans retain all authored floors while respecting roof headroom.</summary>
 public static bool SelectSafeAutomaticRoof(Building building){
  if(MaxLevel(building)<=ZLevels.Max)return false;
  building.Design!.Roof=building.Design.MinLevel+building.Design.Floors.Count-1>=ZLevels.Max?"none":"flat";
  return true;
 }
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
  // A stepped footprint needs separate rectangular wings: changing a ridge's direction per
  // square creates a crooked gable on L/T houses. Courtyards remain empty and thin wings flat.
  if(floor.Cells.Contains('.')||floor.Rooms.Values.Contains("balcony")){
   var part=RoofPartitions.At(building,floor,x,y);
   if(part.Width<2||part.Height<2)return new(0,0,true);
   west=x-part.X;east=part.X+part.Width-1-x;north=y-part.Y;south=part.Y+part.Height-1-y;
  }
  var acrossX=west+east<=north+south;var first=acrossX?west:north;var second=acrossX?east:south;var shed=building.Design.Roof=="shed";var rise=shed||first<=second;var d=shed?first:Math.Min(first,second);
  return new(d,!shed&&first==second?(acrossX?109-d%6:96+d%6):acrossX?(rise?16+d%6:13-d%6):(rise?29-d%6:d%6));
 }
}
