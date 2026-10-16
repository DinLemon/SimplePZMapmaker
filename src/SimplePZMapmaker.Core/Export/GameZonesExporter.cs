using System.Globalization;
using System.Text;
namespace SimplePZMapmaker.Core.Export;
public static class GameZonesExporter
{
 public static string ObjectsLua(MapProject p,CancellationToken token=default)
 {
  var output=new StringBuilder("objects = {\n");var decomposed=p.GameZones.Select(z=>(Zone:z,Rectangles:ZoneGeometry.Rectangles(z,token))).ToArray();var authored=decomposed.Where(z=>!z.Zone.Vehicle).SelectMany(z=>z.Rectangles).ToArray();var index=new Dictionary<(int,int),List<ZoneRectangle>>();
  foreach(var z in authored)for(var y=z.Y/8;y<=(z.Y+z.Height-1)/8;y++)for(var x=z.X/8;x<=(z.X+z.Width-1)/8;x++){if(!index.TryGetValue((x,y),out var list))index[(x,y)]=list=[];list.Add(z);}
  void Add(string name,string type,int x,int y,int width,int height,string properties="")=>output.Append(Culture($" {{ name=\"{name}\", type=\"{type}\", x={p.WorldX*256+x}, y={p.WorldY*256+y}, z=0, width={width}, height={height}{properties} }},\n"));
  for(var cy=0;cy<p.Height/8;cy++)for(var cx=0;cx<p.Width/8;cx++){
   if(cx==0)token.ThrowIfCancellationRequested();var terrain=p.Terrain[cy*8*p.Width+cx*8];if(terrain==Terrain.Water)continue;var type=terrain switch{Terrain.Forest=>"Forest",Terrain.Grass=>"Vegitation",_=>"TownZone"};
   var rectangles=new List<(int X,int Y,int W,int H)>{(cx*8,cy*8,8,8)};
   if(index.TryGetValue((cx,cy),out var zones))foreach(var z in zones){var next=new List<(int,int,int,int)>();foreach(var r in rectangles){var x0=Math.Max(r.X,z.X);var x1=Math.Min(r.X+r.W,z.X+z.Width);var y0=Math.Max(r.Y,z.Y);var y1=Math.Min(r.Y+r.H,z.Y+z.Height);if(x0>=x1||y0>=y1){next.Add(r);continue;}if(r.Y<y0)next.Add((r.X,r.Y,r.W,y0-r.Y));if(y1<r.Y+r.H)next.Add((r.X,y1,r.W,r.Y+r.H-y1));if(r.X<x0)next.Add((r.X,y0,x0-r.X,y1-y0));if(x1<r.X+r.W)next.Add((x1,y0,r.X+r.W-x1,y1-y0));}rectangles=next;}
   foreach(var r in rectangles)Add("",type,r.X,r.Y,r.W,r.H);
  }
  foreach(var entry in decomposed){var z=entry.Zone;token.ThrowIfCancellationRequested();var name=z.Vehicle?z.VehicleDistribution!:Escape(z.Name);var properties=z.Vehicle?", properties={Direction=\""+z.Direction+"\", FaceDirection="+(z.FaceDirection?"true":"false")+"}":"";foreach(var rectangle in entry.Rectangles)Add(name,z.Type,rectangle.X,rectangle.Y,rectangle.Width,rectangle.Height,properties);}
  output.Append("}\n");if(output.Length>32*1024*1024)throw new ArgumentException("The game-zone description exceeds 32 MB.");return output.ToString();
 }
 private static string Escape(string text)=>text.Replace("\\","\\\\").Replace("\"","\\\"");
 private static string Culture(FormattableString value)=>value.ToString(CultureInfo.InvariantCulture);
}
