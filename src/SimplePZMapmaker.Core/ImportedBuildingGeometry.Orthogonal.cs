namespace SimplePZMapmaker.Core;

public static partial class ImportedBuildingGeometry
{
 /// <summary>Align the dominant source wall to the nearest native axis, then
 /// constrain each entire wall to one X/Y coordinate. Keep concavities and
 /// courtyard rings; callers retain the untouched geographic evidence.</summary>
 public static List<List<(double X,double Y)>>? Orthogonalize(List<List<(double X,double Y)>> rings){
  if(rings.Count==0||rings.Any(r=>r.Count<4||r[0]!=r[^1]))return null;
  var outer=rings[0];var anchor=outer[0];double crossSum=0,cx=0,cy=0,longest=0,angle=0;
  for(var i=1;i<outer.Count;i++){
   var a=(X:outer[i-1].X-anchor.X,Y:outer[i-1].Y-anchor.Y);var b=(X:outer[i].X-anchor.X,Y:outer[i].Y-anchor.Y);
   var cross=a.X*b.Y-b.X*a.Y;crossSum+=cross;cx+=(a.X+b.X)*cross;cy+=(a.Y+b.Y)*cross;
   var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;if(length>longest){longest=length;angle=Math.Atan2(dy,dx);}
  }
  if(Math.Abs(crossSum)<1e-9||longest<1e-9)return null;
  cx=anchor.X+cx/(3*crossSum);cy=anchor.Y+cy/(3*crossSum);
  angle-=Math.Floor((angle+Math.PI/4)/(Math.PI/2))*(Math.PI/2);
  var cos=Math.Cos(angle);var sin=Math.Sin(angle);var result=new List<List<(double X,double Y)>>();
  foreach(var ring in rings){
   var points=ring.Take(ring.Count-1).Select(p=>(X:cx+(p.X-cx)*cos+(p.Y-cy)*sin,Y:cy-(p.X-cx)*sin+(p.Y-cy)*cos)).ToArray();
   var xGroups=Enumerable.Range(0,points.Length).ToArray();var yGroups=xGroups.ToArray();
   int Find(int[] groups,int i){while(groups[i]!=i){groups[i]=groups[groups[i]];i=groups[i];}return i;}
   void Join(int[] groups,int a,int b)=>groups[Find(groups,a)]=Find(groups,b);
   for(var i=0;i<points.Length;i++){var j=(i+1)%points.Length;Join(Math.Abs(points[j].X-points[i].X)<Math.Abs(points[j].Y-points[i].Y)?xGroups:yGroups,i,j);}
   double[] Coordinates(int[] groups,bool x){var sums=new double[points.Length];var counts=new int[points.Length];for(var i=0;i<points.Length;i++){var root=Find(groups,i);sums[root]+=x?points[i].X:points[i].Y;counts[root]++;}return Enumerable.Range(0,points.Length).Select(i=>sums[Find(groups,i)]/counts[Find(groups,i)]).ToArray();}
   var xs=Coordinates(xGroups,true);var ys=Coordinates(yGroups,false);
   var straight=Enumerable.Range(0,points.Length).Select(i=>(X:xs[i],Y:ys[i])).ToList();
   // One linear pass removes duplicate/collinear source samples, including
   // runs that wrap across the ring's closing vertex.
   var compact=new List<(double X,double Y)>();
   bool Collinear((double X,double Y) a,(double X,double Y) b,(double X,double Y) c)=>Math.Abs((b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X))<1e-8;
   foreach(var point in straight){while(compact.Count>=2&&Collinear(compact[^2],compact[^1],point))compact.RemoveAt(compact.Count-1);if(compact.Count==0||compact[^1]!=point)compact.Add(point);}
   while(compact.Count>=3&&Collinear(compact[^2],compact[^1],compact[0]))compact.RemoveAt(compact.Count-1);
   while(compact.Count>=3&&Collinear(compact[^1],compact[0],compact[1]))compact.RemoveAt(0);
   if(compact.Count<4)return null;
   compact.Add(compact[0]);result.Add(compact);
  }
  return result;
 }
}
