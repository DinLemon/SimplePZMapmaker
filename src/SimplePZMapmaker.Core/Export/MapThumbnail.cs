namespace SimplePZMapmaker.Core.Export;

/// <summary>media/maps/&lt;map&gt;/thumb.png, the 256x256 picture the game shows for a map (vanilla Riverside has one): terrain colours
/// seen from above with building footprints, scaled to fit the map or world and centred.</summary>
public sealed class MapThumbnail
{
 const int Size=256;
 static readonly (byte R,byte G,byte B)[] Colours=[(96,128,64),(46,84,44),(72,72,72),(52,96,148),(206,192,142),(124,98,66),(136,136,136)];
 static readonly (byte R,byte G,byte B) Background=(24,24,24),Roof=(150,62,52);
 readonly byte[] rgba=new byte[Size*Size*4];readonly int left,top;readonly double scale;readonly int offsetX,offsetY;
 /// <summary>A picture of the world area from tile (<paramref name="left"/>, <paramref name="top"/>), <paramref name="width"/> by <paramref name="height"/> tiles.</summary>
 public MapThumbnail(int left,int top,int width,int height){
  this.left=left;this.top=top;scale=Math.Max(width,height)/(double)Size;offsetX=(int)((Size-width/scale)/2);offsetY=(int)((Size-height/scale)/2);
  for(var i=0;i<Size*Size;i++){rgba[i*4]=Background.R;rgba[i*4+1]=Background.G;rgba[i*4+2]=Background.B;rgba[i*4+3]=255;}
 }
 /// <summary>Paints one map or section: the terrain at the centre of each pixel's tiles, then the building footprints.</summary>
 public void Add(MapProject p){
  var ox=p.WorldX*256-left;var oy=p.WorldY*256-top;
  for(var py=0;py<Size;py++)for(var px=0;px<Size;px++){
   var x=(int)((px-offsetX+0.5)*scale)-ox;var y=(int)((py-offsetY+0.5)*scale)-oy;if(x<0||y<0||x>=p.Width||y>=p.Height)continue;
   var c=Colours[Math.Min(Colours.Length-1,(int)p.Terrain[y*p.Width+x])];Set(px,py,c);
  }
  foreach(var b in p.Buildings){
   int X(double tile)=>(int)Math.Floor((ox+tile)/scale)+offsetX;int Y(double tile)=>(int)Math.Floor((oy+tile)/scale)+offsetY;
   for(var py=Math.Max(0,Y(b.Y));py<=Math.Min(Size-1,Y(b.Y+b.Height-1));py++)for(var px=Math.Max(0,X(b.X));px<=Math.Min(Size-1,X(b.X+b.Width-1));px++){
    var x=(int)((px-offsetX+0.5)*scale)-ox-b.X;var y=(int)((py-offsetY+0.5)*scale)-oy-b.Y;
    if(scale>=2||BuildingDesigner.Occupies(b,Math.Clamp(x,0,b.Width-1),Math.Clamp(y,0,b.Height-1)))Set(px,py,Roof);
   }
  }
 }
 void Set(int x,int y,(byte R,byte G,byte B) c){var i=(y*Size+x)*4;rgba[i]=c.R;rgba[i+1]=c.G;rgba[i+2]=c.B;}
 public byte[] Png()=>Export.Png.Encode(new(Size,Size,rgba));
 /// <summary>The thumbnail of a single map.</summary>
 public static byte[] Png(MapProject p){var t=new MapThumbnail(p.WorldX*256,p.WorldY*256,p.Width,p.Height);t.Add(p);return t.Png();}
}
