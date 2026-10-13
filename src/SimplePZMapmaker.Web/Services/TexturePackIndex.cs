using System.Buffers.Binary;
using System.Text;
namespace SimplePZMapmaker.Web.Services;
public static class TexturePackIndex
{
 public sealed record Sprite(string Name,int X,int Y,int Width,int Height,int OffsetX,int OffsetY,int FrameWidth,int FrameHeight);
 public sealed record Page(string Pack,string Name,long Offset,int Length,int Width,int Height,List<Sprite> Sprites);
 public static IReadOnlyList<Page> Read(Stream stream,string pack){
  try{
   using var r=new BinaryReader(stream,Encoding.ASCII,true);
   var magic=r.ReadBytes(4);if(magic.Length!=4)throw new EndOfStreamException();var version=0;int count;
   if(magic.SequenceEqual("PZPK"u8.ToArray())){version=r.ReadInt32();if(version!=1)throw new ArgumentException("PNG pack versions 0 and 1 are supported.");count=r.ReadInt32();}else count=BinaryPrimitives.ReadInt32LittleEndian(magic);
   Bound(count,0,4096);var result=new List<Page>();var total=0;
   string Text(){var size=Bound(r.ReadInt32(),0,512);var bytes=r.ReadBytes(size);if(bytes.Length!=size)throw new EndOfStreamException();return Encoding.ASCII.GetString(bytes);}
   for(var i=0;i<count;i++){
    var name=Text();var entries=Bound(r.ReadInt32(),0,100000);total+=entries;if(total>200000)throw new ArgumentException("Too many sprites in the pack.");r.ReadInt32();var sprites=new List<Sprite>();
    for(var j=0;j<entries;j++)sprites.Add(new(Text(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32()));
    var length=version==1?Bound(r.ReadInt32(),24,32*1024*1024):0;var offset=stream.Position;
    if(version==0){stream.Position=offset+8;while(true){var sizeBytes=r.ReadBytes(4);if(sizeBytes.Length!=4)throw new EndOfStreamException();var size=Bound(BinaryPrimitives.ReadInt32BigEndian(sizeBytes),0,32*1024*1024);var type=r.ReadBytes(4);if(type.Length!=4||size+4L>stream.Length-stream.Position)throw new EndOfStreamException();stream.Position+=size+4;if(stream.Position-offset>32*1024*1024)throw new ArgumentException("The PNG atlas is too large.");if(type.SequenceEqual("IEND"u8.ToArray()))break;}length=checked((int)(stream.Position-offset));if(!r.ReadBytes(4).SequenceEqual(new byte[]{239,190,173,222}))throw new ArgumentException("The legacy pack terminator is missing.");stream.Position=offset;}
    if(length>stream.Length-offset)throw new EndOfStreamException();var header=r.ReadBytes(24);
    if(!header.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new ArgumentException("The atlas does not contain PNG data.");
    var width=Bound(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)),1,8192);var height=Bound(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)),1,8192);
    if((long)width*height>32*1024*1024)throw new ArgumentException("The PNG atlas is too large.");
    foreach(var s in sprites)if(s.Width<=0||s.Height<=0||s.X<0||s.Y<0||(long)s.X+s.Width>width||(long)s.Y+s.Height>height||s.FrameWidth is <1 or >8192||s.FrameHeight is <1 or >8192||s.OffsetX is < -2048 or >8192||s.OffsetY is < -2048 or >8192)throw new ArgumentException("Sprite coordinates extend beyond the PNG atlas.");
    result.Add(new(pack,name,offset,length,width,height,sprites));stream.Position=offset+length+(version==0?4:0);
   }
   if(stream.Position!=stream.Length)throw new ArgumentException("The pack contains trailing data.");return result;
  }catch(Exception e) when(e is EndOfStreamException or IOException or OverflowException){throw new ArgumentException("Invalid or truncated texture pack.",e);}
 }
 private static int Bound(int value,int min,int max)=>value>=min&&value<=max?value:throw new ArgumentException("Pack data exceeds the size limit.");
}
