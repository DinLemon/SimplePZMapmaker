using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
static class CrossCellTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("cross-cell multi-floor basement building keeps every sprite and clips room rectangles",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Spawns=[new(){X=20,Y=20}];var b=HousingTemplates.Create("village-izba");b.X=250;b.Y=250;p.Buildings.Add(b);ProjectValidator.Validate(p);
   var expected=NativeScene.Building(b).Sprites.Where(s=>!s.Tile.StartsWith("blends_")).Select(s=>$"{b.X+s.X-2}:{b.Y+s.Y-2}:{s.Z}:{s.Tile}").Order().ToArray();var actual=new List<string>();
   for(var cy=0;cy<2;cy++)for(var cx=0;cx<2;cx++){var x=Math.Max(0,b.X-cx*256);var y=Math.Max(0,b.Y-cy*256);var width=Math.Min(256-x,b.X+b.Width+1-cx*256-x);var height=Math.Min(256-y,b.Y+b.Height+1-cy*256-y);if(width<1||height<1)continue;actual.AddRange(NativeScene.Cell(p,x,y,width,height,cellX:cx,cellY:cy).Sprites.Where(s=>!s.Tile.StartsWith("blends_")).Select(s=>$"{cx*256+s.X}:{cy*256+s.Y}:{s.Z}:{s.Tile}"));}
   Check(expected.SequenceEqual(actual.Order()),"Native cell seam changed sprites");
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));var area=0;var buildingCells=0;
   foreach(var e in zip.Entries.Where(e=>e.Name.EndsWith(".lotheader"))){using var reader=new BinaryReader(e.Open());reader.ReadBytes(8);var tiles=reader.ReadInt32();for(var i=0;i<tiles;i++)Line(reader);reader.ReadBytes(16);var rooms=reader.ReadInt32();for(var i=0;i<rooms;i++){Line(reader);reader.ReadInt32();var rects=reader.ReadInt32();for(var j=0;j<rects;j++){var x=reader.ReadInt32();var y=reader.ReadInt32();var w=reader.ReadInt32();var h=reader.ReadInt32();Check(x>=0&&y>=0&&w>0&&h>0&&x+w<=256&&y+h<=256,"Room escaped native cell");area+=w*h;}Check(reader.ReadInt32()==0,"Unexpected objects");}if(reader.ReadInt32()>0)buildingCells++;}
   Check(buildingCells==4&&area==b.Design!.Floors.Sum(f=>f.Cells.Count(c=>c!='.')),"Room clipping lost or duplicated area");
  });
  yield return ("cross-cell legacy building exports without cell index aliasing",()=>{var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Spawns=[new(){X=20,Y=20}];p.Buildings.Add(new(){X=250,Y=250,Width=12,Height=10});ProjectValidator.Validate(p);using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));Check(zip.Entries.Count(e=>e.Name.EndsWith(".lotheader"))==4,"Missing native cells");});
 }
 static void Line(BinaryReader r){while(r.ReadByte()!=10){}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
