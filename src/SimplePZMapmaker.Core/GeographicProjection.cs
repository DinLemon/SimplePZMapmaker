namespace SimplePZMapmaker.Core;

public sealed class WorldGeoReference
{
 public GeoBounds Bounds {get;set;}=new();
 public double MetresPerTile {get;set;}=1;
 public int OriginCellX {get;set;}=100;
 public int OriginCellY {get;set;}=100;
 public static void Validate(WorldGeoReference reference){if(reference is null)throw new ArgumentException("The world has no geographic reference.");ProjectValidator.ValidateBounds(reference.Bounds);if(!double.IsFinite(reference.MetresPerTile)||reference.MetresPerTile is <.5 or >20||reference.OriginCellX is <0 or >1000||reference.OriginCellY is <0 or >1000)throw new ArgumentException("Invalid world geographic reference: scale must be 0.5-20 meters per tile.");}
}
public sealed class GeographicProjection
{
 public const double MetresPerDegree=111320;
 public WorldGeoReference Reference {get;}
 public int CellX {get;}
 public int CellY {get;}
 private readonly double cos;
 public GeographicProjection(WorldGeoReference reference,int cellX,int cellY){WorldGeoReference.Validate(reference);Reference=reference;CellX=cellX;CellY=cellY;cos=Math.Cos((reference.Bounds.North+reference.Bounds.South)/2*Math.PI/180);}
 public (double X,double Y) Project((double X,double Y) coordinate)=>((coordinate.X-Reference.Bounds.West)*cos*MetresPerDegree/Reference.MetresPerTile-(CellX-Reference.OriginCellX)*256,(Reference.Bounds.North-coordinate.Y)*MetresPerDegree/Reference.MetresPerTile-(CellY-Reference.OriginCellY)*256);
 public (double Longitude,double Latitude) Geographic(double x,double y)=>(Reference.Bounds.West+(x+(CellX-Reference.OriginCellX)*256)*Reference.MetresPerTile/(cos*MetresPerDegree),Reference.Bounds.North-(y+(CellY-Reference.OriginCellY)*256)*Reference.MetresPerTile/MetresPerDegree);
 public GeoBounds SectionBounds(int width,int height){var top=Geographic(0,0);var bottom=Geographic(width,height);return new(){West=Math.Max(Reference.Bounds.West,top.Longitude),East=Math.Min(Reference.Bounds.East,bottom.Longitude),South=Math.Max(Reference.Bounds.South,bottom.Latitude),North=Math.Min(Reference.Bounds.North,top.Latitude)};}
 public static (int Width,int Height) RequiredTiles(WorldGeoReference reference){WorldGeoReference.Validate(reference);var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);var corner=projection.Project((reference.Bounds.East,reference.Bounds.South));return((int)Math.Ceiling(corner.X),(int)Math.Ceiling(corner.Y));}
}
