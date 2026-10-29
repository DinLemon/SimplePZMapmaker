namespace SimplePZMapmaker.Core;

public sealed record GeographicWarpRequest(WorldGeoReference Reference,GeographicWarpBuildOptions Options);
public sealed record GeographicWarpPreview(WorldGeoReference Reference,int Width,int Height,double MinimumDeterminant,string Notice);
public static class GeographicWarpProposal
{
 public static GeographicWarpPreview Create(GeographicWarpRequest request){
  ArgumentNullException.ThrowIfNull(request);WorldGeoReference.Validate(request.Reference);ArgumentNullException.ThrowIfNull(request.Options);
  var reference=NeighborhoodPlanner.Copy(request.Reference);reference.Warp=null;
  var size=GeographicProjection.RequiredTiles(reference);var options=NeighborhoodPlanner.Copy(request.Options);
  // Input coordinates are the one rotated affine frame, never per-section coordinates.
  options.Width=size.Width;options.Height=size.Height;
  if(options.MaxStep==256)options.MaxStep=Math.Max(256,Math.Max(size.Width,size.Height)/16d);
  reference.Warp=GeographicWarpBuilder.Build(options);var mesh=new GeographicWarp(reference.Warp);var actual=GeographicProjection.RequiredTiles(reference);
  return new(reference,actual.Width,actual.Height,mesh.MinimumDeterminant,"One validated world mesh, including roads, parcels, buildings, holes and fences. Protected rectangles keep 1:1 scale. Fields use harmonic displacement or an isolated radial rotation transition, not ARAP. Reimport sources to change geometry; replacing metadata alone does not move an existing world.");
 }
}
