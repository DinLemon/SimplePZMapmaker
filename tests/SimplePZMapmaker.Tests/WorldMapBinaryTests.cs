using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Tests;
public static class WorldMapBinaryTests
{
 public static IEnumerable<(string Name,Action Run)> Cases(){
  yield return ("native map and stitched world publish readable IGMB atlas companions",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=256});p.Buildings.Add(new(){X=20,Y=20,Width=12,Height=10});
   ReadArchive(NativeMapExporter.Export(p),1);
   var q=MapGenerator.Generate(new(){Kind="blank",Size=256});q.WorldX=102;
   ReadArchive(NativeWorldExporter.Export(new(){Maps=[new(){Project=p},new(){Project=q}]}),2);
  });
  yield return ("atlas rejects overflowing reader point offsets before export",()=>{
   var polygon=new XElement("geometry",new XAttribute("type","Polygon"),new XElement("coordinates",Enumerable.Range(0,16384).Select(_=>new XElement("point",new XAttribute("x",1),new XAttribute("y",2)))));
   var world=new XElement("world",new XElement("cell",new XAttribute("x",100),new XAttribute("y",100),new XElement("feature",polygon)));
   try{NativeWorldMapBinary.Write(world);}catch(ArgumentException){return;}throw new Exception("Atlas exceeded signed-short offset limits.");
  });
 }
 static void ReadArchive(byte[] bytes,int expectedCells){
  using var zip=new ZipArchive(new MemoryStream(bytes));var binary=zip.Entries.Single(e=>e.Name=="worldmap.xml.bin");var xml=zip.Entries.Single(e=>e.Name=="worldmap.xml");using var text=xml.Open();var source=XDocument.Load(text);var expectedFeatures=source.Descendants("feature").Count();var expectedPoints=source.Descendants("point").Select(p=>((int)p.Attribute("x")!,(int)p.Attribute("y")!)).ToArray();
  using var stream=binary.Open();using var r=new BinaryReader(stream,Encoding.UTF8);if(Encoding.ASCII.GetString(r.ReadBytes(4))!="IGMB"||r.ReadInt32()!=2||r.ReadInt32()!=256)throw new Exception("Unsupported atlas header.");
  var width=r.ReadInt32();var height=r.ReadInt32();var count=r.ReadInt32();var strings=new List<string>();for(var i=0;i<count;i++)strings.Add(Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt16())));
  var cells=0;var features=0;var points=new List<(int,int)>();for(var i=0;i<width*height;i++){var x=r.ReadInt32();if(x==-1)continue;r.ReadInt32();cells++;var n=r.ReadInt32();features+=n;for(var f=0;f<n;f++){if(strings[r.ReadInt16()]!="Polygon")throw new Exception("Unexpected geometry type.");var rings=r.ReadByte();for(var ring=0;ring<rings;ring++){var vertices=r.ReadInt16();for(var j=0;j<vertices;j++)points.Add((r.ReadInt16(),r.ReadInt16()));}var properties=r.ReadByte();for(var j=0;j<properties;j++){_ = strings[r.ReadInt16()];_ = strings[r.ReadInt16()];}}}
  if(cells!=expectedCells||features!=expectedFeatures||!points.SequenceEqual(expectedPoints)||r.BaseStream.ReadByte()!=-1)throw new Exception("Binary atlas changed XML geometry or trailing bytes.");
 }
}
