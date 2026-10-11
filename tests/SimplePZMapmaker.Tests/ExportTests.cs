using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

public static class ExportTests
{
    public static IEnumerable<(string,Action)> Cases() {
        yield return ("in-game map declares native 256 cell coordinates",()=>{
            using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(MapGenerator.Generate(new(){Kind="blank"}))));
            var xml=XDocument.Load(zip.Entries.Single(e=>e.FullName.EndsWith("worldmap.xml")).Open());
            if((string?)xml.Root?.Attribute("cellSize")!="256"||(string?)xml.Root?.Attribute("version")!="1.0")throw new Exception("Worldmap uses legacy 300 cells");
        });
        yield return ("native B42 archive headers rooms offsets and chunks",()=> {
            var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=110;p.WorldY=120;
            p.Buildings.Add(new(){Id="house1",X=40,Y=40,Width=12,Height=10});
            p.Terrain[10*256+10]=Terrain.Water;
            using var stream=new MemoryStream(NativeMapExporter.Export(p));using var zip=new ZipArchive(stream);
            ZipArchiveEntry Entry(string suffix)=>zip.Entries.Single(e=>e.FullName.EndsWith(suffix));
            using var header=new BinaryReader(Entry("110_120.lotheader").Open());
            if(Encoding.ASCII.GetString(header.ReadBytes(4))!="LOTH"||header.ReadInt32()!=1)throw new Exception("Wrong B42 header");
            var count=header.ReadInt32();var tiles=new List<string>();for(var i=0;i<count;i++){var s=new StringBuilder();byte b;while((b=header.ReadByte())!=10)s.Append((char)b);tiles.Add(s.ToString());}
            if(!tiles.Contains("fixtures_doors_01_1")||!tiles.Contains("floors_interior_tilesandwood_01_40"))throw new Exception("Door/floor missing");
            if(header.ReadInt32()!=8||header.ReadInt32()!=8||header.ReadInt32()!=0||header.ReadInt32()!=1)throw new Exception("Wrong chunk/levels");
            var rooms=header.ReadInt32();if(rooms<3)throw new Exception("House has no proper rooms");
            for(var i=0;i<rooms;i++){while(header.ReadByte()!=10){}header.ReadInt32();var rects=header.ReadInt32();for(var j=0;j<rects*4;j++)header.ReadInt32();var objs=header.ReadInt32();for(var j=0;j<objs*3;j++)header.ReadInt32();}
            if(header.ReadInt32()!=1||header.ReadInt32()!=rooms)throw new Exception("Room building references missing");for(var i=0;i<rooms;i++)if(header.ReadInt32()!=i)throw new Exception("Bad room id");
            if(header.ReadBytes(1024).Length!=1024||header.BaseStream.ReadByte()!=-1)throw new Exception("Wrong zombie data size");
            using var packStream=new MemoryStream();Entry("world_110_120.lotpack").Open().CopyTo(packStream);packStream.Position=0;using var pack=new BinaryReader(packStream);
            if(Encoding.ASCII.GetString(pack.ReadBytes(4))!="LOTP"||pack.ReadInt32()!=1||pack.ReadInt32()!=1024)throw new Exception("Wrong pack header");
            var offsets=Enumerable.Range(0,1024).Select(_=>pack.ReadInt64()).ToArray();
            for(var chunk=0;chunk<1024;chunk++) {
                packStream.Position=offsets[chunk];var squares=0;
                while(squares<128){var entries=pack.ReadInt32();if(entries==-1){squares+=pack.ReadInt32();continue;}if(entries is <2 or >20)throw new Exception("Bad square");var room=pack.ReadInt32();if(room < -1||room>=rooms)throw new Exception("Bad room");for(var i=1;i<entries;i++)if(pack.ReadInt32() is var id&&(id<0||id>=tiles.Count))throw new Exception("Bad tile reference");squares++;}
                if(squares!=128||(chunk<1023&&packStream.Position!=offsets[chunk+1]))throw new Exception("Chunk length mismatch");
            }
            using var spawnReader=new StreamReader(Entry("spawnpoints.lua").Open());var spawn=spawnReader.ReadToEnd();
            if(!spawn.Contains("posX = 28288")||!spawn.Contains("posY = 30848"))throw new Exception("Spawn offset wrong");
            using var chunkReader=new BinaryReader(Entry("chunkdata_110_120.bin").Open());if(chunkReader.ReadByte()!=0||chunkReader.ReadByte()!=1)throw new Exception("Chunkdata version endian wrong");
            for(var i=0;i<1024;i++){var t=chunkReader.ReadByte();if(t==2&&chunkReader.ReadBytes(64).Length!=64)throw new Exception("Truncated navigation");else if(t>5)throw new Exception("Bad navigation");}
            if(chunkReader.BaseStream.ReadByte()!=-1)throw new Exception("Trailing navigation bytes");
            if(zip.Entries.Any(e=>e.FullName.Contains("..")||e.FullName.StartsWith('/')))throw new Exception("Unsafe ZIP");
        });
        yield return ("export rejects invalid projects",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank"});p.Spawns.Clear();try{NativeMapExporter.Export(p);}catch(ArgumentException){return;}throw new Exception("Invalid map exported");
        });
        yield return ("export includes all four cells and original attribution",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Attribution.Add("Map data © OpenStreetMap contributors — ODbL");
            using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));if(zip.Entries.Count(e=>e.FullName.EndsWith(".lotheader"))!=4)throw new Exception("Missing cells");
            using var r=new StreamReader(zip.Entries.Single(e=>e.FullName.EndsWith("ATTRIBUTION.txt")).Open());if(!r.ReadToEnd().Contains("OpenStreetMap"))throw new Exception("Lost attribution");
        });
    }
}
