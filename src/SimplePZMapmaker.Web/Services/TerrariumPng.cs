using System.Buffers.Binary;
using System.IO.Compression;
namespace SimplePZMapmaker.Web.Services;
/// <summary>Bounded RGB/RGBA PNG decoding for 256x256 Terrarium DEM tiles.</summary>
public static class TerrariumPng
{
 public static double?[] Decode(byte[] bytes){
  if(bytes.Length is <33 or >1_000_000||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new ArgumentException("The elevation provider returned an invalid PNG.");var offset=8;var channels=0;bool header=false,finished=false;using var compressed=new MemoryStream();
  while(offset+12<=bytes.Length){var length=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4));if(length<0||length>1_000_000||offset+12L+length>bytes.Length)throw new ArgumentException("Invalid elevation PNG chunk.");var type=bytes.AsSpan(offset+4,4);var data=bytes.AsSpan(offset+8,length);var expected=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8+length,4));if(Crc(bytes.AsSpan(offset+4,length+4))!=expected)throw new ArgumentException("Elevation PNG checksum mismatch.");
   if(type.SequenceEqual("IHDR"u8)){if(header||length!=13||BinaryPrimitives.ReadInt32BigEndian(data)!=256||BinaryPrimitives.ReadInt32BigEndian(data[4..])!=256||data[8]!=8||data[9] is not(2 or 6)||data[10]!=0||data[11]!=0||data[12]!=0)throw new ArgumentException("A non-interlaced 256 x 256 RGB8 Terrarium PNG is required.");channels=data[9]==2?3:4;header=true;}
   else if(type.SequenceEqual("IDAT"u8)){if(!header)throw new ArgumentException("The elevation PNG has no header.");compressed.Write(data);if(compressed.Length>1_000_000)throw new ArgumentException("The elevation PNG exceeds the size budget.");}
   else if(type.SequenceEqual("IEND"u8)){if(length!=0)throw new ArgumentException("Invalid PNG end chunk.");finished=true;offset+=12;break;}offset+=12+length;
  }
  if(!finished||!header||offset!=bytes.Length)throw new ArgumentException("The elevation PNG is truncated.");var stride=256*channels;var raw=new byte[(stride+1)*256];compressed.Position=0;try{using var z=new ZLibStream(compressed,CompressionMode.Decompress);z.ReadExactly(raw);if(z.ReadByte()!=-1)throw new ArgumentException("The elevation PNG contains extra pixels.");}catch(InvalidDataException e){throw new ArgumentException("Could not decompress the elevation PNG.",e);}catch(EndOfStreamException e){throw new ArgumentException("The elevation PNG is truncated.",e);}
  var pixels=new byte[stride*256];for(var y=0;y<256;y++){var filter=raw[y*(stride+1)];if(filter>4)throw new ArgumentException("Unknown elevation PNG filter.");for(var x=0;x<stride;x++){var a=x>=channels?pixels[y*stride+x-channels]:0;var b=y>0?pixels[(y-1)*stride+x]:0;var c=x>=channels&&y>0?pixels[(y-1)*stride+x-channels]:0;var predictor=filter switch{0=>0,1=>a,2=>b,3=>(a+b)/2,_=>Paeth(a,b,c)};pixels[y*stride+x]=unchecked((byte)(raw[y*(stride+1)+x+1]+predictor));}}
  var heights=new double?[65536];for(var n=0;n<heights.Length;n++){var i=n*channels;var height=pixels[i]*256+pixels[i+1]+pixels[i+2]/256d-32768;heights[n]=channels==4&&pixels[i+3]==0?null:height;}return heights;
 }
 private static int Paeth(int a,int b,int c){var p=a+b-c;var pa=Math.Abs(p-a);var pb=Math.Abs(p-b);var pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
 private static uint Crc(ReadOnlySpan<byte> bytes){uint crc=uint.MaxValue;foreach(var b in bytes){crc^=b;for(var k=0;k<8;k++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}return crc^uint.MaxValue;}
}
