namespace SimplePZMapmaker.Core;
public sealed record ZonePoint(int X,int Y);
public sealed record ZoneRectangle(int X,int Y,int Width,int Height);
/// <summary>Integer tile-edge polygons, rasterized using tile centres. One simple ring; no holes.</summary>
public static class ZoneGeometry
{
 public static void Validate(int x,int y,int width,int height,IReadOnlyList<ZonePoint>? polygon,int mapWidth,int mapHeight)
 {
  if(x<0||y<0||width<1||height<1||(long)x+width>mapWidth||(long)y+height>mapHeight)throw new ArgumentException("Zone bounds must lie inside the map.");
  if(polygon is null)return;
  if(polygon.Count is <3 or >128||(long)width*height>4194304||polygon.Any(p=>p is null||p.X<0||p.Y<0||p.X>mapWidth||p.Y>mapHeight)||polygon.Distinct().Count()!=polygon.Count)throw new ArgumentException("A polygon needs 3–128 distinct bounded integer vertices and a bounding area up to 4,194,304 tiles.");
  if(polygon.Min(p=>p.X)!=x||polygon.Min(p=>p.Y)!=y||polygon.Max(p=>p.X)!=(long)x+width||polygon.Max(p=>p.Y)!=(long)y+height)throw new ArgumentException("Polygon bounds must exactly enclose its vertices.");
  long area=0;for(var i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=(long)a.X*b.Y-(long)a.Y*b.X;}
  if(area==0)throw new ArgumentException("Polygon area is empty.");
  for(var i=0;i<polygon.Count;i++){var a=polygon[(i+polygon.Count-1)%polygon.Count];var b=polygon[i];var c=polygon[(i+1)%polygon.Count];if(Cross(a,b,c)==0&&(long)(b.X-a.X)*(c.X-b.X)+(long)(b.Y-a.Y)*(c.Y-b.Y)<0)throw new ArgumentException("Polygon edges must not double back.");for(var j=i+1;j<polygon.Count;j++){if(j==i+1||i==0&&j==polygon.Count-1)continue;if(Intersect(polygon[i],polygon[(i+1)%polygon.Count],polygon[j],polygon[(j+1)%polygon.Count]))throw new ArgumentException("Polygon edges must not self-intersect.");}}
  for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++)if(Covers(x,y,width,height,polygon,xx,yy))return;throw new ArgumentException("Polygon coverage contains no tile centres. Enlarge the shape.");
 }
 static long Cross(ZonePoint a,ZonePoint b,ZonePoint c)=>(long)(b.X-a.X)*(c.Y-a.Y)-(long)(b.Y-a.Y)*(c.X-a.X);
 static bool On(ZonePoint a,ZonePoint b,ZonePoint c)=>c.X>=Math.Min(a.X,b.X)&&c.X<=Math.Max(a.X,b.X)&&c.Y>=Math.Min(a.Y,b.Y)&&c.Y<=Math.Max(a.Y,b.Y);
 static bool Intersect(ZonePoint a,ZonePoint b,ZonePoint c,ZonePoint d){var x=Cross(a,b,c);var y=Cross(a,b,d);var z=Cross(c,d,a);var w=Cross(c,d,b);return x==0&&On(a,b,c)||y==0&&On(a,b,d)||z==0&&On(c,d,a)||w==0&&On(c,d,b)||Math.Sign(x)!=Math.Sign(y)&&Math.Sign(z)!=Math.Sign(w);}
 public static bool Covers(int left,int top,int width,int height,IReadOnlyList<ZonePoint>? polygon,int x,int y)
 {
  if(x<left||y<top||x>=(long)left+width||y>=(long)top+height)return false;if(polygon is null)return true;var px=x+.5;var py=y+.5;var inside=false;for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++){var a=polygon[i];var b=polygon[j];if((a.Y>py)!=(b.Y>py)&&px<(b.X-a.X)*(py-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;}return inside;
 }
 public static bool Covers(GameZone zone,int x,int y)=>Covers(zone.X,zone.Y,zone.Width,zone.Height,zone.Polygon,x,y);
 public static bool Intersects(GameZone a,GameZone b){for(var y=Math.Max(a.Y,b.Y);y<Math.Min(a.Y+a.Height,b.Y+b.Height);y++)for(var x=Math.Max(a.X,b.X);x<Math.Min(a.X+a.Width,b.X+b.Width);x++)if(Covers(a,x,y)&&Covers(b,x,y))return true;return false;}
 public static IReadOnlyList<ZoneRectangle> Rectangles(GameZone zone,CancellationToken token=default)=>Rectangles(zone.X,zone.Y,zone.Width,zone.Height,zone.Polygon,token);
 public static IReadOnlyList<ZoneRectangle> Rectangles(int x,int y,int width,int height,IReadOnlyList<ZonePoint>? polygon,CancellationToken token=default)
 {
  if(polygon is null)return[new(x,y,width,height)];var output=new List<ZoneRectangle>();var previous=new Dictionary<(int,int),int>();for(var yy=y;yy<y+height;yy++){token.ThrowIfCancellationRequested();var current=new Dictionary<(int,int),int>();for(var xx=x;xx<x+width;){if(!Covers(x,y,width,height,polygon,xx,yy)){xx++;continue;}var start=xx++;while(xx<x+width&&Covers(x,y,width,height,polygon,xx,yy))xx++;var key=(start,xx-start);if(previous.TryGetValue(key,out var index)){output[index]=output[index] with{Height=output[index].Height+1};current[key]=index;}else{current[key]=output.Count;output.Add(new(start,yy,xx-start,1));if(output.Count>65536)throw new ArgumentException("Polygon decomposition exceeds 65,536 native rectangles.");}}previous=current;}return output;
 }
 public static void ValidateSourceTags(Dictionary<string,string> tags){if(tags is null||tags.Count>32||tags.Any(t=>string.IsNullOrWhiteSpace(t.Key)||t.Key.Length>128||t.Value is null||t.Value.Length>2048||t.Key.Any(char.IsControl)||t.Value.Any(char.IsControl)))throw new ArgumentException("Zone source evidence must contain up to 32 bounded text tags.");}
}
