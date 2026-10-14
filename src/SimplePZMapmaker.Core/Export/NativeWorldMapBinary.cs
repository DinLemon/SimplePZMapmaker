using System.Text;
using System.Xml.Linq;
namespace SimplePZMapmaker.Core.Export;
/// <summary>Original writer for the Build 42 IGMB v2 atlas companion, using the same geometry as worldmap.xml.</summary>
public static class NativeWorldMapBinary {
 public static byte[] Write(XElement world){
  var cells=world.Elements("cell").ToArray();if(cells.Length==0)throw new ArgumentException("The native atlas needs at least one cell.");
  int Number(XElement element,string name)=>int.TryParse((string?)element.Attribute(name),out var n)?n:throw new ArgumentException("Invalid atlas coordinate.");
  var coordinates=cells.ToDictionary(c=>(X:Number(c,"x"),Y:Number(c,"y")));var minX=coordinates.Keys.Min(p=>p.X);var minY=coordinates.Keys.Min(p=>p.Y);var maxX=coordinates.Keys.Max(p=>p.X);var maxY=coordinates.Keys.Max(p=>p.Y);if(minX<0||minY<0||maxX>1007||maxY>1007)throw new ArgumentException("The native atlas exceeds supported cell coordinates.");
  var strings=cells.SelectMany(c=>c.Elements("feature")).SelectMany(f=>f.Elements("geometry").Select(g=>(string?)g.Attribute("type")??"Polygon").Concat(f.Element("properties")?.Elements("property").SelectMany(p=>new[]{(string?)p.Attribute("name")??"",(string?)p.Attribute("value")??""})??[])).Distinct(StringComparer.Ordinal).ToArray();if(strings.Length>32767||strings.Any(s=>Encoding.UTF8.GetByteCount(s)>1022))throw new ArgumentException("The native atlas string table exceeds the game reader's limits.");var index=strings.Select((s,i)=>(s,i)).ToDictionary(v=>v.s,v=>(short)v.i,StringComparer.Ordinal);
  using var output=new MemoryStream();using var writer=new BinaryWriter(output,Encoding.UTF8,true);writer.Write("IGMB"u8);writer.Write(2);writer.Write(256);writer.Write(maxX-minX+1);writer.Write(maxY-minY+1);writer.Write(strings.Length);foreach(var value in strings){var bytes=Encoding.UTF8.GetBytes(value);writer.Write((short)bytes.Length);writer.Write(bytes);}
  for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++){
   if(!coordinates.TryGetValue((x,y),out var cell)){writer.Write(-1);continue;}writer.Write(x);writer.Write(y);var features=cell.Elements("feature").ToArray();writer.Write(features.Length);var totalPointShorts=0;
   foreach(var feature in features){var geometry=feature.Element("geometry")??throw new ArgumentException("Atlas feature has no geometry.");writer.Write(index[(string?)geometry.Attribute("type")??"Polygon"]);var rings=geometry.Elements("coordinates").ToArray();if(rings.Length is <1 or >255)throw new ArgumentException("Invalid atlas ring count.");writer.Write((byte)rings.Length);
    foreach(var ring in rings){var points=ring.Elements("point").ToArray();if(points.Length is <3 or >32767||(long)totalPointShorts+points.Length*2>32767)throw new ArgumentException("In-game atlas cell has too many polygon points for the Build 42 reader. Simplify the building outlines before exporting.");writer.Write((short)points.Length);foreach(var point in points){var px=Number(point,"x");var py=Number(point,"y");if(px is <0 or >256||py is <0 or >256)throw new ArgumentException("Atlas point is outside its native cell.");writer.Write((short)px);writer.Write((short)py);}totalPointShorts+=points.Length*2;}
    var properties=feature.Element("properties")?.Elements("property").ToArray()??[];if(properties.Length>255)throw new ArgumentException("Too many atlas feature properties.");writer.Write((byte)properties.Length);foreach(var property in properties){writer.Write(index[(string?)property.Attribute("name")??""]);writer.Write(index[(string?)property.Attribute("value")??""]);}
   }
  }
  writer.Flush();return output.ToArray();
 }
}
