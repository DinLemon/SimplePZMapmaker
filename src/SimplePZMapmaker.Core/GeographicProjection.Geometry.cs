namespace SimplePZMapmaker.Core;

public sealed partial class GeographicProjection
{
 public bool TryGeographic(double x,double y,out (double Longitude,double Latitude) result){
  result=default;if(!double.IsFinite(x)||!double.IsFinite(y))return false;
  if(warp is null){result=AffineGeographic(x,y);return true;}
  if(!warp.TryInverse(new(x+SectionX,y+SectionY),out var source))return false;
  result=AffineWorldGeographic(source);return true;
 }
 /// <summary>Clip fetched features to the bounded mesh before subdivision. Source WGS84 evidence
 /// remains separate: clipping may remove holes or split a line into several independent paths.</summary>
 public List<List<(double X,double Y)>> ProjectGeometry(IEnumerable<IEnumerable<(double X,double Y)>> rings,bool polygon)=>ProjectGeometry(rings,polygon,out _);
 public List<List<(double X,double Y)>> ProjectGeometry(IEnumerable<IEnumerable<(double X,double Y)>> rings,bool polygon,out List<int> originalRingIndices)
 {
  var sourceIndices=new List<int>();originalRingIndices=sourceIndices;
  if(warp is null){var affine=rings.Select(r=>r.Select(Project).ToList()).ToList();sourceIndices.AddRange(Enumerable.Range(0,affine.Count));return affine;}
  var rectangle=new GeographicWarpPoint[]{new(0,0),new(warp.SourceWidth,0),new(warp.SourceWidth,warp.SourceHeight),new(0,warp.SourceHeight)};
  var result=new List<List<(double X,double Y)>>();var count=0;var ringIndex=0;
  void Add(IReadOnlyList<GeographicWarpPoint> path){
   var mapped=warp.ForwardPath(path);count+=mapped.Count;
   if(result.Count>=GeographicEvidence.MaximumRings||count>GeographicWarp.MaxPathPoints)throw new ArgumentException("Clipped geographic feature exceeds projection budgets.");
   result.Add(mapped.Select(p=>(p.X-SectionX,p.Y-SectionY)).ToList());
   sourceIndices.Add(ringIndex);
  }
  foreach(var ring in rings){
   var input=ring.Take(GeographicWarp.MaxPathPoints+1).Select(AffineWorldProject).ToArray();
   if(input.Length>GeographicWarp.MaxPathPoints)throw new ArgumentException("Projection input exceeds 100000 points.");
   if(polygon){var clipped=GeographicWarp.Clip(input,rectangle).DistinctConsecutive().ToList();
    if(clipped.Count>1&&clipped[0]==clipped[^1])clipped.RemoveAt(clipped.Count-1);
    if(clipped.Count<3){if(ringIndex==0)return [];ringIndex++;continue;}
    clipped.Add(clipped[0]);Add(clipped);
   }else if(input.Length==1){if(Inside(input[0]))Add(input);}
   else{List<GeographicWarpPoint> path=[];
    for(var i=1;i<input.Length;i++){
     var a=input[i-1];var b=input[i];var dx=b.X-a.X;var dy=b.Y-a.Y;double begin=0,end=1;
     bool Cut(double p,double q){if(p==0)return q>=0;var t=q/p;if(p<0){if(t>end)return false;begin=Math.Max(begin,t);}else{if(t<begin)return false;end=Math.Min(end,t);}return true;}
     if(!Cut(-dx,a.X)||!Cut(dx,warp.SourceWidth-a.X)||!Cut(-dy,a.Y)||!Cut(dy,warp.SourceHeight-a.Y)){Flush();continue;}
     var first=new GeographicWarpPoint(Math.Clamp(a.X+begin*dx,0,warp.SourceWidth),Math.Clamp(a.Y+begin*dy,0,warp.SourceHeight));
     var last=new GeographicWarpPoint(Math.Clamp(a.X+end*dx,0,warp.SourceWidth),Math.Clamp(a.Y+end*dy,0,warp.SourceHeight));
     if(first==last){Flush();continue;}if(path.Count>0&&path[^1]!=first)Flush();if(path.Count==0)path.Add(first);path.Add(last);if(end<1)Flush();
    }
    Flush();void Flush(){if(path.Count>=2)Add(path);path=[];}
   }
   ringIndex++;
  }
  return result;
  bool Inside(GeographicWarpPoint p)=>p.X>=0&&p.Y>=0&&p.X<=warp.SourceWidth&&p.Y<=warp.SourceHeight;
 }
}

internal static class ConsecutiveGeographicPoints
{
 public static IEnumerable<GeographicWarpPoint> DistinctConsecutive(this IEnumerable<GeographicWarpPoint> points){GeographicWarpPoint? previous=null;foreach(var p in points){if(previous!=p)yield return p;previous=p;}}
}
