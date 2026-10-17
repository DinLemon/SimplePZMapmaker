using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core.Export;

/// <summary>One sprite of a custom tileset: a PNG frame (normally 128x256 for Build 42 2x tiles) and its tile properties.</summary>
public sealed record TilesetSprite(byte[] Png,Dictionary<string,string> Properties);
/// <summary>A tile mod: Build 42 reads media/texturepacks/&lt;Tileset&gt;.pack and media/&lt;Tileset&gt;.tiles registered in mod.info.
/// Tiles are named &lt;Tileset&gt;_&lt;index&gt;; FileNumber is the tiledef number (100-8190) that must not clash with other active mods.</summary>
public sealed record TilesetMod(string ModId,string Name,string Tileset,int FileNumber,List<TilesetSprite> Sprites);

/// <summary>Builds a Build 42 tile mod from PNG frames, replacing an external make_fence_tiles step: a PZPK v1 atlas page and a tdef v1 definition.</summary>
public static class TilesetBuilder
{
 public const int Columns=8;
 static readonly Regex Name=new("^[A-Za-z0-9_]{1,64}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
 public static void Validate(TilesetMod mod){
  NativeModPackage.Validate(new(mod.ModId,Name:mod.Name));
  if(!Name.IsMatch(mod.Tileset)||mod.Tileset.EndsWith('_'))throw new ArgumentException("Tileset name: 1-64 letters, digits or '_'; tiles are named <tileset>_<index>.");
  if(mod.FileNumber is <100 or >8190)throw new ArgumentException("Tile definition number: 100-8190, unique among the active mods.");
  if(mod.Sprites is null||mod.Sprites.Count is <1 or >1024)throw new ArgumentException("A tileset holds 1-1,024 sprites.");
  foreach(var s in mod.Sprites)if(s.Properties is null||s.Properties.Count>256||s.Properties.Any(p=>p.Key.Length is <1 or >100||p.Key.Contains('\n')||p.Value is null||p.Value.Length>1000||p.Value.Contains('\n')))throw new ArgumentException("Tile properties: up to 256 single-line keys and values.");
 }
 /// <summary>Writes the tile mod archive: 42/mod.info, 42/media/texturepacks/&lt;tileset&gt;.pack, 42/media/&lt;tileset&gt;.tiles and an empty common/.</summary>
 public static void Write(TilesetMod mod,Stream output){
  Validate(mod);var frames=mod.Sprites.Select(s=>Png.Decode(s.Png)).ToList();
  using var zip=new ZipArchive(output,ZipArchiveMode.Create,true);
  void Entry(string path,byte[] bytes){using var s=zip.CreateEntry(path,CompressionLevel.Fastest).Open();s.Write(bytes);}
  zip.CreateEntry(mod.ModId+"/common/");
  Entry($"{mod.ModId}/42/mod.info",Encoding.UTF8.GetBytes($"name={mod.Name}\nid={mod.ModId}\nversionMin=42.0\ndescription=Tiles {mod.Tileset}_0..{mod.Sprites.Count-1} built with SimplePZMapmaker.\npack={mod.Tileset}\ntiledef={mod.Tileset} {mod.FileNumber}\n"));
  Entry($"{mod.ModId}/42/media/texturepacks/{mod.Tileset}.pack",Pack(mod.Tileset,frames));
  Entry($"{mod.ModId}/42/media/{mod.Tileset}.tiles",Tiles(mod.Tileset,mod.Sprites.Select(s=>s.Properties).ToList()));
 }
 /// <summary>PZPK version 1 with one page: sprites are trimmed to their opaque bounds and shelf-packed into one PNG atlas.</summary>
 public static byte[] Pack(string tileset,IReadOnlyList<Png.Image> frames){
  var trimmed=frames.Select((f,i)=>(Index:i,Frame:f,Box:f.OpaqueBounds())).ToList();
  const int atlasWidth=2048;var placed=new (int X,int Y)[frames.Count];int x=0,y=0,shelf=0;
  foreach(var t in trimmed.OrderByDescending(t=>t.Box.Height)){var (w,h)=(t.Box.Width,t.Box.Height);if(w>atlasWidth)throw new ArgumentException("A sprite is wider than the 2,048-pixel atlas.");if(x+w>atlasWidth){x=0;y+=shelf+1;shelf=0;}placed[t.Index]=(x,y);x+=w+1;shelf=Math.Max(shelf,h);}
  var height=Math.Max(1,y+shelf);if(height>8192)throw new ArgumentException("The sprites do not fit one 2048x8192 atlas page; split the tileset.");
  var atlas=new Png.Image(atlasWidth,height,new byte[atlasWidth*height*4]);
  foreach(var t in trimmed){var (px,py)=placed[t.Index];for(var row=0;row<t.Box.Height;row++)Array.Copy(t.Frame.Rgba,((t.Box.Y+row)*t.Frame.Width+t.Box.X)*4,atlas.Rgba,((py+row)*atlasWidth+px)*4,t.Box.Width*4);}
  var png=Png.Encode(atlas);using var s=new MemoryStream();using var w2=new BinaryWriter(s,Encoding.ASCII);
  void Text(string text){w2.Write(text.Length);w2.Write(Encoding.ASCII.GetBytes(text));}
  w2.Write("PZPK"u8.ToArray());w2.Write(1);w2.Write(1);Text(tileset+"0");w2.Write(frames.Count);w2.Write(1);
  foreach(var t in trimmed){var (px,py)=placed[t.Index];Text($"{tileset}_{t.Index}");foreach(var v in new[]{px,py,t.Box.Width,t.Box.Height,t.Box.X,t.Box.Y,t.Frame.Width,t.Frame.Height})w2.Write(v);}
  w2.Write(png.Length);w2.Write(png);w2.Flush();return s.ToArray();
 }
 /// <summary>tdef version 1 with one tileset of eight columns; the tileset index inside the file is 0.</summary>
 public static byte[] Tiles(string tileset,IReadOnlyList<Dictionary<string,string>> properties){
  using var s=new MemoryStream();using var w=new BinaryWriter(s,Encoding.Latin1);void Line(string text){w.Write(Encoding.Latin1.GetBytes(text));w.Write((byte)'\n');}
  var rows=(properties.Count+Columns-1)/Columns;w.Write("tdef"u8.ToArray());w.Write(1);w.Write(1);Line(tileset);Line(tileset+".png");w.Write(Columns);w.Write(rows);w.Write(0);w.Write(properties.Count);
  foreach(var p in properties){w.Write(p.Count);foreach(var (key,value) in p.OrderBy(p=>p.Key,StringComparer.Ordinal)){Line(key);Line(value);}}
  w.Flush();return s.ToArray();
 }
}

/// <summary>Minimal PNG codec for tile frames: 8-bit RGBA or RGB, non-interlaced, all five scanline filters. Writes RGBA with filter 0.</summary>
public static class Png
{
 public sealed record Image(int Width,int Height,byte[] Rgba)
 {
  /// <summary>The smallest rectangle holding every pixel with alpha above zero (1x1 at the origin for an empty frame).</summary>
  public (int X,int Y,int Width,int Height) OpaqueBounds(){int minX=Width,minY=Height,maxX=-1,maxY=-1;for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)if(Rgba[(y*Width+x)*4+3]!=0){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}return maxX<0?(0,0,1,1):(minX,minY,maxX-minX+1,maxY-minY+1);}
 }
 static readonly byte[] Signature=[137,80,78,71,13,10,26,10];
 public static Image Decode(byte[] png){
  if(png.Length<33||!png.AsSpan(0,8).SequenceEqual(Signature))throw new ArgumentException("A tile frame is not a PNG file.");
  int width=0,height=0,channels=0;using var data=new MemoryStream();var at=8;
  while(at+12<=png.Length){var length=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));var type=Encoding.ASCII.GetString(png,at+4,4);if(length<0||at+12+length>png.Length)throw new ArgumentException("A tile frame PNG is truncated.");var body=png.AsSpan(at+8,length);
   if(type=="IHDR"){width=BinaryPrimitives.ReadInt32BigEndian(body);height=BinaryPrimitives.ReadInt32BigEndian(body[4..]);if(body[8]!=8||body[9] is not(2 or 6)||body[12]!=0)throw new ArgumentException("Tile frames must be 8-bit RGBA or RGB PNGs without interlacing.");channels=body[9]==6?4:3;}
   else if(type=="IDAT")data.Write(body);else if(type=="IEND")break;at+=12+length;}
  if(width is <1 or >4096||height is <1 or >4096||channels==0)throw new ArgumentException("A tile frame PNG must be 1-4096 pixels on each side.");
  data.Position=0;using var inflate=new ZLibStream(data,CompressionMode.Decompress);var stride=width*channels;var raw=new byte[(stride+1)*height];var read=0;while(read<raw.Length){var n=inflate.Read(raw,read,raw.Length-read);if(n==0)throw new ArgumentException("A tile frame PNG has too little image data.");read+=n;}
  var pixels=new byte[stride*height];
  for(var y=0;y<height;y++){var filter=raw[y*(stride+1)];for(var x=0;x<stride;x++){var value=raw[y*(stride+1)+1+x];int a=x>=channels?pixels[y*stride+x-channels]:0,b=y>0?pixels[(y-1)*stride+x]:0,c=x>=channels&&y>0?pixels[(y-1)*stride+x-channels]:0;
    pixels[y*stride+x]=(byte)(filter switch{0=>value,1=>value+a,2=>value+b,3=>value+(a+b)/2,4=>value+Paeth(a,b,c),_=>throw new ArgumentException("A tile frame PNG uses an unknown filter.")});}}
  if(channels==4)return new(width,height,pixels);var rgba=new byte[width*height*4];for(var i=0;i<width*height;i++){rgba[i*4]=pixels[i*3];rgba[i*4+1]=pixels[i*3+1];rgba[i*4+2]=pixels[i*3+2];rgba[i*4+3]=255;}return new(width,height,rgba);
 }
 static int Paeth(int a,int b,int c){var p=a+b-c;var pa=Math.Abs(p-a);var pb=Math.Abs(p-b);var pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
 public static byte[] Encode(Image image){
  using var output=new MemoryStream();output.Write(Signature);
  void Chunk(string type,byte[] body){Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,body.Length);output.Write(length);var typed=Encoding.ASCII.GetBytes(type).Concat(body).ToArray();output.Write(typed);BinaryPrimitives.WriteUInt32BigEndian(length,Crc(typed));output.Write(length);}
  var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,image.Width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),image.Height);header[8]=8;header[9]=6;Chunk("IHDR",header);
  using(var compressed=new MemoryStream()){using(var deflate=new ZLibStream(compressed,CompressionLevel.Optimal,true))for(var y=0;y<image.Height;y++){deflate.WriteByte(0);deflate.Write(image.Rgba,y*image.Width*4,image.Width*4);}Chunk("IDAT",compressed.ToArray());}
  Chunk("IEND",[]);return output.ToArray();
 }
 static readonly uint[] CrcTable=Enumerable.Range(0,256).Select(n=>{var c=(uint)n;for(var k=0;k<8;k++)c=(c&1)!=0?0xEDB88320u^(c>>1):c>>1;return c;}).ToArray();
 static uint Crc(byte[] bytes){var c=0xFFFFFFFFu;foreach(var b in bytes)c=CrcTable[(c^b)&0xFF]^(c>>8);return c^0xFFFFFFFFu;}
}
