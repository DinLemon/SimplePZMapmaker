namespace SimplePZMapmaker.Core;

// A world may retain four map-sized source batches before clipping them to sections.
// Byte, per-feature, scratch-disk and retained-memory guards remain independent.
public readonly record struct GeographicGeometryBudget(int Features,int Points,int Work)
{
 public static GeographicGeometryBudget ForBytes(int bytes){
  if(bytes is <1 or >64_000_000)throw new ArgumentException("Geographic data limit: 64 MB.");
  // Dense world sources include many small address/entrance points. Their count can
  // grow faster than polygon geometry while byte, point, work and retention caps stay fixed.
  var factor=Math.Clamp((bytes+15_999_999)/16_000_000,1,4);return new((factor==1?30_000:60_000)*factor,250_000*factor,1_000_000*factor);
 }
}
