namespace SimplePZMapmaker.Core;

/// <summary>Transforms the complete original layout, including stairs, wall edges, and furniture.</summary>
internal static class SettlementBuildingOrientation
{
 internal static IEnumerable<(int X,int Y)> Entrances(Building b)
 {
  var floor=b.Design!.Floors[-b.Design.MinLevel];foreach(var e in floor.Edges.Where(e=>e.Kind is "door" or "open"))
  {
   var a=BuildingDesigner.Cell(b,floor,e.X,e.Y);var nx=e.X-(e.Edge=="w"?1:0);var ny=e.Y-(e.Edge=="n"?1:0);var other=BuildingDesigner.Cell(b,floor,nx,ny);
   if((a=='.')==(other=='.'))continue;yield return a=='.'?(b.X+e.X,b.Y+e.Y):(b.X+nx,b.Y+ny);
  }
 }
 internal static void Face(Building b,string desired)
 {
  var floor=b.Design!.Floors[-b.Design.MinLevel];var entrance=floor.Edges.First(e=>e.Kind=="door"&&(BuildingDesigner.Cell(b,floor,e.X,e.Y)=='.')!=(BuildingDesigner.Cell(b,floor,e.X-(e.Edge=="w"?1:0),e.Y-(e.Edge=="n"?1:0))=='.'));
  var side=entrance.Edge=="n"?(BuildingDesigner.Cell(b,floor,entrance.X,entrance.Y)=='.'?"s":"n"):(BuildingDesigner.Cell(b,floor,entrance.X,entrance.Y)=='.'?"e":"w");
  var transpose=(side is "n" or "s")!=(desired is "n" or "s");if(transpose)side=side switch{"n"=>"w","s"=>"e","w"=>"n",_=>"s"};
  var flipX=side!=desired&&desired is "w" or "e";var flipY=side!=desired&&desired is "n" or "s";
  var oldWidth=b.Width;var oldHeight=b.Height;var width=transpose?oldHeight:oldWidth;var height=transpose?oldWidth:oldHeight;
  (int X,int Y) Cell(int x,int y){if(transpose)(x,y)=(y,x);return(flipX?width-1-x:x,flipY?height-1-y:y);}
  foreach(var f in b.Design.Floors)
  {
   var cells=new char[width*height];for(var y=0;y<oldHeight;y++)for(var x=0;x<oldWidth;x++){var c=Cell(x,y);cells[c.Y*width+c.X]=f.Cells[y*oldWidth+x];}f.Cells=new(cells);
   f.FloorTiles=f.FloorTiles.ToDictionary(k=>{var c=Cell(k.Key%oldWidth,k.Key/oldWidth);return c.Y*width+c.X;},k=>k.Value);
   foreach(var e in f.Edges){var x=e.X;var y=e.Y;var direction=e.Edge;if(transpose){(x,y)=(y,x);direction=direction=="n"?"w":"n";}e.X=flipX?width-x-(direction=="n"?1:0):x;e.Y=flipY?height-y-(direction=="w"?1:0):y;e.Edge=direction;}
   foreach(var o in f.Objects){var points=BuildingObjects.Cells(o).Select(c=>Cell(c.X,c.Y)).ToArray();o.X=points.Min(c=>c.X);o.Y=points.Min(c=>c.Y);if(o.Kind=="furniture")o.Direction=FurnitureCatalog.Transform(o.Direction,transpose,flipX,flipY);else if(transpose)o.Direction=o.Direction=="n"?"w":"n";
   }
  }
  b.Width=width;b.Height=height;
  RoofGeometry.SelectSafeAutomaticRoof(b);BuildingDesigner.Validate(b);
 }
}
