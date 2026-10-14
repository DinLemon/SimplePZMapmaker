using System.IO.Compression;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class NativeVerticalRangeTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return("native export preserves all 64 inclusive floor levels and rooms at both extremes",()=>{
   var p=Project();using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   using var header=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith(".lotheader")).Open());
   Check(new string(header.ReadChars(4))=="LOTH"&&header.ReadInt32()==1,"Native header version changed");var tileCount=header.ReadInt32();for(var i=0;i<tileCount;i++)Line(header);
   Check(header.ReadInt32()==8&&header.ReadInt32()==8&&header.ReadInt32()==-32&&header.ReadInt32()==31,"Native range was clamped or included invalid z=32");
   var roomCount=header.ReadInt32();var levels=new HashSet<int>();for(var i=0;i<roomCount;i++){Line(header);levels.Add(header.ReadInt32());var rectangles=header.ReadInt32();for(var j=0;j<rectangles*4;j++)header.ReadInt32();var objects=header.ReadInt32();for(var j=0;j<objects*3;j++)header.ReadInt32();}
   Check(levels.SetEquals(Enumerable.Range(-32,64)),"A signed floor's rooms are missing");
   using var stream=new MemoryStream();zip.Entries.Single(e=>e.FullName.EndsWith(".lotpack")).Open().CopyTo(stream);stream.Position=0;using var pack=new BinaryReader(stream);Check(new string(pack.ReadChars(4))=="LOTP"&&pack.ReadInt32()==1&&pack.ReadInt32()==1024,"Native pack version changed");var offsets=Enumerable.Range(0,1024).Select(_=>pack.ReadInt64()).ToArray();var extremeRooms=new HashSet<int>();
   for(var chunk=0;chunk<1024;chunk++){stream.Position=offsets[chunk];var squares=0;while(squares<4096){var n=pack.ReadInt32();if(n==-1){var skip=pack.ReadInt32();Check(skip>0,"Invalid skipped squares");squares+=skip;}else{Check(n>=2,"Empty serialized square");var room=pack.ReadInt32();if(room>=0&&(squares/64==0||squares/64==63))extremeRooms.Add(squares/64);for(var i=1;i<n;i++)Check(pack.ReadInt32() is var tile&&tile>=0&&tile<tileCount,"Invalid tile index");squares++;}}Check(squares==4096&&stream.Position==(chunk<1023?offsets[chunk+1]:stream.Length),"Native chunk does not contain exactly 64 floors");}
   Check(extremeRooms.SetEquals([0,63]),"Native chunk lost bottom or top floor room references");
  });
  yield return("native scene and streamed viewport expose -32 and 31 and reject out-of-range levels",()=>{
   var p=Project();var viewport=NativeViewportScene.FromProject(p);foreach(var z in new[]{-32,31}){var scene=NativeScene.Cell(p,38,38,12,12,z);Check(scene.Sprites.Any(s=>s.Z==z),"Native scene omitted extreme floor");var chunk=viewport.Read(32,32,32,1,z);Check(chunk.Sprites.Any(s=>s.Z==z),"Streamed native view omitted extreme floor");}
   foreach(var z in new[]{-33,32}){Reject(()=>NativeScene.Cell(p,38,38,12,12,z));Reject(()=>viewport.Read(32,32,32,1,z));}
  });
  yield return("lighting geometry spans 64 floors and keeps both endpoint rooms and controlled power",()=>{
   var p=Project();foreach(var z in new[]{-32,31}){var scene=NativeLightingGeometry.FromProject(p,38,38,12,z,true,_=>new Dictionary<string,string>{{"solidfloor",""}});Check(scene.MinZ==-32&&scene.MaxZ==31,"Lighting input lost native range");Check(scene.Squares.Any(s=>s.Z==z&&s.RoomId>=0)&&scene.RoomLights.Any(l=>l.Z==z&&l.Active),"Extreme lighting room is missing");Check(scene.Squares.Count<=NativeLightingGeometry.MaximumSquares,"Lighting budget exceeded");}
  });
 }
 static MapProject Project(){var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=8,Height=8};b.Design=BuildingDesigner.Generate(b,32,42,"rect",32);Check(b.Design.Roof=="none","Full-height generation did not explicitly omit an out-of-range roof");p.Buildings.Add(b);return p;}
 static void Line(BinaryReader reader){while(reader.ReadByte()!=10){}}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected floor range rejection");}
}
