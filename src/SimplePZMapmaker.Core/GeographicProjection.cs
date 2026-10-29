namespace SimplePZMapmaker.Core;

public sealed class WorldGeoReference
{
 public GeoBounds Bounds {get;set;}=new();
 public double MetresPerTile {get;set;}=1;
 public int OriginCellX {get;set;}=100;
 public int OriginCellY {get;set;}=100;
 /// <summary>Rotation of the local metric plane (east, south) before it is laid on the tile grid; positive turns map content clockwise on screen. Lets a street grid that is not north-aligned become axis-aligned while source coordinates stay real lon/lat (docs/TRANSFER-CONTRACT.md, section 1).</summary>
 public double RotationDegrees {get;set;}
 /// <summary>Optional single world-wide deformation, in tile units relative to the affine origin cell.</summary>
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
 public GeographicWarpDefinition? Warp {get;set;}
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
 public string? RegionalProfile {get;set;}
 public static void Validate(WorldGeoReference reference){if(reference?.RegionalProfile is not(null or "default" or "ru-rural"))throw new ArgumentException("Unknown regional road profile.");if(reference is null)throw new ArgumentException("The world has no geographic reference.");WorldLayoutLimits.ValidateSourceBounds(reference.Bounds);if(!double.IsFinite(reference.MetresPerTile)||reference.MetresPerTile is <.5 or >20||reference.OriginCellX is <0 or >1000||reference.OriginCellY is <0 or >1000)throw new ArgumentException("Invalid world geographic reference: scale must be 0.5-20 meters per tile.");if(!double.IsFinite(reference.RotationDegrees)||reference.RotationDegrees is < -45 or >45)throw new ArgumentException("Invalid world geographic reference: rotation must be between -45 and 45 degrees.");if(reference.Warp is {} definition){var mesh=new GeographicWarp(definition);var b=reference.Bounds;var w=(b.East-b.West)*Math.Cos((b.North+b.South)/2*Math.PI/180)*GeographicProjection.MetresPerDegree/reference.MetresPerTile;var h=(b.North-b.South)*GeographicProjection.MetresPerDegree/reference.MetresPerTile;var a=reference.RotationDegrees*Math.PI/180;if(mesh.SourceWidth+1e-8<Math.Abs(w*Math.Cos(a))+Math.Abs(h*Math.Sin(a))||mesh.SourceHeight+1e-8<Math.Abs(w*Math.Sin(a))+Math.Abs(h*Math.Cos(a)))throw new ArgumentException("Geographic warp source lattice must cover the entire affine world footprint.");}}
}
public sealed partial class GeographicProjection
{
 public const double MetresPerDegree=111320;
 public WorldGeoReference Reference {get;}
 public int CellX {get;}
 public int CellY {get;}
 private readonly double cos,sin,rotationCos,centreX,centreY,offsetX,offsetY,tilesPerDegreeX,tilesPerDegreeY,affineOriginX,affineOriginY;
 private readonly GeographicWarp? warp;
 private double SectionX=>(CellX-Reference.OriginCellX)*256;
 private double SectionY=>(CellY-Reference.OriginCellY)*256;
 public GeographicProjection(WorldGeoReference reference,int cellX,int cellY){
  WorldGeoReference.Validate(reference);Reference=reference;CellX=cellX;CellY=cellY;var b=reference.Bounds;
  cos=Math.Cos((b.North+b.South)/2*Math.PI/180);var angle=reference.RotationDegrees*Math.PI/180;sin=Math.Sin(angle);rotationCos=Math.Cos(angle);
  tilesPerDegreeX=cos*MetresPerDegree/reference.MetresPerTile;tilesPerDegreeY=MetresPerDegree/reference.MetresPerTile;centreX=(b.West+b.East)/2;centreY=(b.North+b.South)/2;
  // The rotated bounds' minimum corner lands on the origin cell; with no rotation this is the bounds' north-west corner, as before.
  var corners=new[]{(b.West,b.North),(b.East,b.North),(b.West,b.South),(b.East,b.South)}.Select(Rotated).ToArray();
  affineOriginX=corners.Min(c=>c.X);affineOriginY=corners.Min(c=>c.Y);
  offsetX=affineOriginX+(cellX-reference.OriginCellX)*256;offsetY=affineOriginY+(cellY-reference.OriginCellY)*256;
  if(reference.Warp is {} definition)warp=new GeographicWarp(definition);
 }
 private (double X,double Y) Rotated((double X,double Y) coordinate){var e=(coordinate.X-centreX)*tilesPerDegreeX;var n=(centreY-coordinate.Y)*tilesPerDegreeY;return(e*rotationCos-n*sin,e*sin+n*rotationCos);}
 // Without rotation the original north-west-anchored formulas are kept verbatim so existing worlds project bit-identically.
 private (double X,double Y) AffineProject((double X,double Y) coordinate){if(Reference.RotationDegrees==0)return((coordinate.X-Reference.Bounds.West)*cos*MetresPerDegree/Reference.MetresPerTile-(CellX-Reference.OriginCellX)*256,(Reference.Bounds.North-coordinate.Y)*MetresPerDegree/Reference.MetresPerTile-(CellY-Reference.OriginCellY)*256);var r=Rotated(coordinate);return(r.X-offsetX,r.Y-offsetY);}
 private (double Longitude,double Latitude) AffineGeographic(double x,double y){if(Reference.RotationDegrees==0)return(Reference.Bounds.West+(x+(CellX-Reference.OriginCellX)*256)*Reference.MetresPerTile/(cos*MetresPerDegree),Reference.Bounds.North-(y+(CellY-Reference.OriginCellY)*256)*Reference.MetresPerTile/MetresPerDegree);var rx=x+offsetX;var ry=y+offsetY;var e=rx*rotationCos+ry*sin;var n=-rx*sin+ry*rotationCos;return(centreX+e/tilesPerDegreeX,centreY-n/tilesPerDegreeY);}
 private GeographicWarpPoint AffineWorldProject((double X,double Y) coordinate){if(Reference.RotationDegrees==0)return new((coordinate.X-Reference.Bounds.West)*cos*MetresPerDegree/Reference.MetresPerTile,(Reference.Bounds.North-coordinate.Y)*MetresPerDegree/Reference.MetresPerTile);var p=Rotated(coordinate);return new(p.X-affineOriginX,p.Y-affineOriginY);}
 private (double Longitude,double Latitude) AffineWorldGeographic(GeographicWarpPoint p){if(Reference.RotationDegrees==0)return(Reference.Bounds.West+p.X*Reference.MetresPerTile/(cos*MetresPerDegree),Reference.Bounds.North-p.Y*Reference.MetresPerTile/MetresPerDegree);var rx=p.X+affineOriginX;var ry=p.Y+affineOriginY;return(centreX+(rx*rotationCos+ry*sin)/tilesPerDegreeX,centreY-(-rx*sin+ry*rotationCos)/tilesPerDegreeY);}
 public (double X,double Y) Project((double X,double Y) coordinate){if(warp is null)return AffineProject(coordinate);var q=warp.Forward(AffineWorldProject(coordinate));return(q.X-SectionX,q.Y-SectionY);}
 public (double Longitude,double Latitude) Geographic(double x,double y){if(warp is null)return AffineGeographic(x,y);return AffineWorldGeographic(warp.Inverse(new(x+SectionX,y+SectionY)));}
 /// <summary>Common subdivision for roads, parcels, house outlines and fences. Closed rings must repeat their first point.</summary>
 public IReadOnlyList<(double X,double Y)> ProjectPath(IEnumerable<(double X,double Y)> coordinates){ArgumentNullException.ThrowIfNull(coordinates);var input=coordinates.Take(GeographicWarp.MaxPathPoints+1).ToArray();if(input.Length>GeographicWarp.MaxPathPoints)throw new ArgumentException("Projection path exceeds 100000 points.");if(warp is null)return input.Select(Project).ToArray();return warp.ForwardPath(input.Select(AffineWorldProject)).Select(p=>(p.X-SectionX,p.Y-SectionY)).ToArray();}
 /// <summary>Maps each ring independently; the exterior/hole ordering, closure and winding are retained.</summary>
 public IReadOnlyList<IReadOnlyList<(double X,double Y)>> ProjectRings(IEnumerable<IEnumerable<(double X,double Y)>> rings){ArgumentNullException.ThrowIfNull(rings);var output=new List<IReadOnlyList<(double X,double Y)>>();var total=0;foreach(var ring in rings){if(output.Count>=256)throw new ArgumentException("Projection exceeds 256 rings.");var p=ProjectPath(ring);total+=p.Count;if(total>GeographicWarp.MaxPathPoints)throw new ArgumentException("Projection rings exceed 100000 output points.");output.Add(p);}return output;}
 private List<GeographicWarpPoint> AffineFootprint(){var b=Reference.Bounds;return new[]{(b.West,b.North),(b.East,b.North),(b.East,b.South),(b.West,b.South)}.Select(AffineWorldProject).ToList();}
 /// <summary>Conservative source bounds from every intersecting mesh triangle, clipped to the reference footprint.
 /// An empty warped section returns zero width and height; callers must skip it before source intake.</summary>
 public GeoBounds SectionBounds(int width,int height){if(warp is not null){if(width<1||height<1||width>GeographicWarp.MaxCoordinate||height>GeographicWarp.MaxCoordinate)throw new ArgumentException("Invalid warp section dimensions.");var rectangle=new GeographicWarpPoint[]{new(SectionX,SectionY),new(SectionX+width,SectionY),new(SectionX+width,SectionY+height),new(SectionX,SectionY+height)};var footprint=AffineFootprint();var points=warp.MapRegion(rectangle,true).SelectMany(p=>GeographicWarp.Clip(p,footprint)).Select(AffineWorldGeographic).ToArray();var b=Reference.Bounds;if(points.Length==0)return new(){West=b.West,East=b.West,South=b.North,North=b.North};return new(){West=Math.Max(b.West,points.Min(p=>p.Longitude)),East=Math.Min(b.East,points.Max(p=>p.Longitude)),South=Math.Max(b.South,points.Min(p=>p.Latitude)),North=Math.Min(b.North,points.Max(p=>p.Latitude))};}var corners=new[]{Geographic(0,0),Geographic(width,0),Geographic(0,height),Geographic(width,height)};return new(){West=Math.Max(Reference.Bounds.West,corners.Min(c=>c.Longitude)),East=Math.Min(Reference.Bounds.East,corners.Max(c=>c.Longitude)),South=Math.Max(Reference.Bounds.South,corners.Min(c=>c.Latitude)),North=Math.Min(Reference.Bounds.North,corners.Max(c=>c.Latitude))};}
 public static (int Width,int Height) RequiredTiles(WorldGeoReference reference){WorldGeoReference.Validate(reference);var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);if(projection.warp is {} mesh){var points=mesh.MapRegion(projection.AffineFootprint(),false).SelectMany(p=>p).ToArray();return((int)Math.Ceiling(points.Max(p=>p.X)),(int)Math.Ceiling(points.Max(p=>p.Y)));}var b=reference.Bounds;var corners=new[]{(b.West,b.North),(b.East,b.North),(b.West,b.South),(b.East,b.South)}.Select(projection.Project).ToArray();return((int)Math.Ceiling(corners.Max(c=>c.X)),(int)Math.Ceiling(corners.Max(c=>c.Y)));}
}
