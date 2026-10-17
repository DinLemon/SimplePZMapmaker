namespace SimplePZMapmaker.Core;

public sealed class WorldGeoReference
{
 public GeoBounds Bounds {get;set;}=new();
 public double MetresPerTile {get;set;}=1;
 public int OriginCellX {get;set;}=100;
 public int OriginCellY {get;set;}=100;
 /// <summary>Rotation of the local metric plane (east, south) before it is laid on the tile grid; positive turns map content clockwise on screen. Lets a street grid that is not north-aligned become axis-aligned while source coordinates stay real lon/lat (docs/TRANSFER-CONTRACT.md, section 1).</summary>
 public double RotationDegrees {get;set;}
 public static void Validate(WorldGeoReference reference){if(reference is null)throw new ArgumentException("The world has no geographic reference.");ProjectValidator.ValidateBounds(reference.Bounds);if(!double.IsFinite(reference.MetresPerTile)||reference.MetresPerTile is <.5 or >20||reference.OriginCellX is <0 or >1000||reference.OriginCellY is <0 or >1000)throw new ArgumentException("Invalid world geographic reference: scale must be 0.5-20 meters per tile.");if(!double.IsFinite(reference.RotationDegrees)||reference.RotationDegrees is < -45 or >45)throw new ArgumentException("Invalid world geographic reference: rotation must be between -45 and 45 degrees.");}
}
public sealed class GeographicProjection
{
 public const double MetresPerDegree=111320;
 public WorldGeoReference Reference {get;}
 public int CellX {get;}
 public int CellY {get;}
 private readonly double cos,sin,rotationCos,centreX,centreY,offsetX,offsetY,tilesPerDegreeX,tilesPerDegreeY;
 public GeographicProjection(WorldGeoReference reference,int cellX,int cellY){
  WorldGeoReference.Validate(reference);Reference=reference;CellX=cellX;CellY=cellY;var b=reference.Bounds;
  cos=Math.Cos((b.North+b.South)/2*Math.PI/180);var angle=reference.RotationDegrees*Math.PI/180;sin=Math.Sin(angle);rotationCos=Math.Cos(angle);
  tilesPerDegreeX=cos*MetresPerDegree/reference.MetresPerTile;tilesPerDegreeY=MetresPerDegree/reference.MetresPerTile;centreX=(b.West+b.East)/2;centreY=(b.North+b.South)/2;
  // The rotated bounds' minimum corner lands on the origin cell; with no rotation this is the bounds' north-west corner, as before.
  var corners=new[]{(b.West,b.North),(b.East,b.North),(b.West,b.South),(b.East,b.South)}.Select(Rotated).ToArray();
  offsetX=corners.Min(c=>c.X)+(cellX-reference.OriginCellX)*256;offsetY=corners.Min(c=>c.Y)+(cellY-reference.OriginCellY)*256;
 }
 private (double X,double Y) Rotated((double X,double Y) coordinate){var e=(coordinate.X-centreX)*tilesPerDegreeX;var n=(centreY-coordinate.Y)*tilesPerDegreeY;return(e*rotationCos-n*sin,e*sin+n*rotationCos);}
 // Without rotation the original north-west-anchored formulas are kept verbatim so existing worlds project bit-identically.
 public (double X,double Y) Project((double X,double Y) coordinate){if(Reference.RotationDegrees==0)return((coordinate.X-Reference.Bounds.West)*cos*MetresPerDegree/Reference.MetresPerTile-(CellX-Reference.OriginCellX)*256,(Reference.Bounds.North-coordinate.Y)*MetresPerDegree/Reference.MetresPerTile-(CellY-Reference.OriginCellY)*256);var r=Rotated(coordinate);return(r.X-offsetX,r.Y-offsetY);}
 public (double Longitude,double Latitude) Geographic(double x,double y){if(Reference.RotationDegrees==0)return(Reference.Bounds.West+(x+(CellX-Reference.OriginCellX)*256)*Reference.MetresPerTile/(cos*MetresPerDegree),Reference.Bounds.North-(y+(CellY-Reference.OriginCellY)*256)*Reference.MetresPerTile/MetresPerDegree);var rx=x+offsetX;var ry=y+offsetY;var e=rx*rotationCos+ry*sin;var n=-rx*sin+ry*rotationCos;return(centreX+e/tilesPerDegreeX,centreY-n/tilesPerDegreeY);}
 /// <summary>The source area a section must read: the bounding box of its corners, clipped to the reference bounds. With rotation it is larger than the section, so importers still clip by tile footprint.</summary>
 public GeoBounds SectionBounds(int width,int height){var corners=new[]{Geographic(0,0),Geographic(width,0),Geographic(0,height),Geographic(width,height)};return new(){West=Math.Max(Reference.Bounds.West,corners.Min(c=>c.Longitude)),East=Math.Min(Reference.Bounds.East,corners.Max(c=>c.Longitude)),South=Math.Max(Reference.Bounds.South,corners.Min(c=>c.Latitude)),North=Math.Min(Reference.Bounds.North,corners.Max(c=>c.Latitude))};}
 public static (int Width,int Height) RequiredTiles(WorldGeoReference reference){WorldGeoReference.Validate(reference);var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);var b=reference.Bounds;var corners=new[]{(b.West,b.North),(b.East,b.North),(b.West,b.South),(b.East,b.South)}.Select(projection.Project).ToArray();return((int)Math.Ceiling(corners.Max(c=>c.X)),(int)Math.Ceiling(corners.Max(c=>c.Y)));}
}
