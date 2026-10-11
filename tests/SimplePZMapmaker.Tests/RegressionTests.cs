using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;

public static class RegressionTests
{
    public static IEnumerable<(string,Action)> Cases() {
        yield return ("perimeter and wall clearance are consistent",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=249,Y=20,Width=6,Height=6};if(!MapGenerator.CanPlace(p,b))throw new Exception("Final usable perimeter rejected");p.Buildings.Add(b);ProjectValidator.Validate(p);
            p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(new(){X=20,Y=20,Width=12,Height=10});p.Buildings.Add(new(){X=32,Y=20,Width=12,Height=10});Reject(()=>ProjectValidator.Validate(p));
        });
        yield return ("null import document rejected cleanly",()=>Reject(()=>GeographicImporter.Import(null!,MapGenerator.Generate(new(){Kind="blank"}))));
        yield return ("null geographic bounds rejected cleanly",()=>Reject(()=>ProjectValidator.ValidateBounds(null!)));
        yield return ("OSM relation stitches ways and retains inner island",()=>{
            var outer=new[]{new[]{30.0,50.0},new[]{30.004,50.0},new[]{30.004,50.004},new[]{30.0,50.004},new[]{30.0,50.0}};
            var inner=new[]{new[]{30.001,50.001},new[]{30.003,50.001},new[]{30.003,50.003},new[]{30.001,50.003},new[]{30.001,50.001}};
            object Member(string role,IEnumerable<double[]> points)=>new {type="way",role,geometry=points.Select(v=>new{lon=v[0],lat=v[1]})};
            var json=JsonSerializer.Serialize(new {elements=new[]{new {type="relation",tags=new Dictionary<string,string>{{"natural","water"},{"type","multipolygon"}},members=new[]{Member("outer",outer.Take(3)),Member("outer",outer.Skip(2)),Member("inner",inner)}}}});
            var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}));
            if(result.Project.Terrain[128*256+128]==Terrain.Water||!result.Project.Terrain.Contains(Terrain.Water))throw new Exception("Island lost or open outer ways discarded");
        });
        yield return ("large polygon rasterization stays bounded",()=>{
            var ring=Enumerable.Range(0,12000).Select(i=>{var angle=i*Math.PI*2/12000;return new[]{30+Math.Cos(angle)*.01,50+Math.Sin(angle)*.01};}).ToList();ring.Add(ring[0]);
            var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",properties=new{natural="wood"},geometry=new{type="Polygon",coordinates=new[]{ring}}}}});
            var timer=Stopwatch.StartNew();var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank",Size=1024}));
            if(timer.Elapsed.TotalSeconds>4||!result.Project.Terrain.Contains(Terrain.Forest))throw new Exception($"Rasterization took {timer.Elapsed.TotalSeconds:F2}s");
        });
        yield return ("native navigation matches water tree and door frame semantics",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(new(){X=40,Y=40,Width=12,Height=10});p.Terrain[10*256+10]=Terrain.Water;
            var tree=Enumerable.Range(0,65536).First(i=>i/256>100&&(((uint)((i%256)*73856093)^((uint)(i/256)*19349663)^(uint)p.Seed)%19==0));p.Terrain[tree]=Terrain.Forest;
            using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var reader=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith("chunkdata_100_100.bin")).Open());reader.ReadBytes(2);
            var flags=new byte[65536];for(var cy=0;cy<32;cy++)for(var cx=0;cx<32;cx++){var kind=reader.ReadByte();var chunk=kind==2?reader.ReadBytes(64):Enumerable.Repeat(kind switch{0=>(byte)0,1=>(byte)1,3=>(byte)8,4=>(byte)16,_=>(byte)32},64).ToArray();for(var y=0;y<8;y++)for(var x=0;x<8;x++)flags[(cy*8+y)*256+cx*8+x]=chunk[y*8+x];}
            if(flags[10*256+10]!=8||flags[tree]!=0||(flags[40*256+43]&2)==0||(flags[42*256+46]&4)==0)throw new Exception("Water/tree/door flags differ from engine navigation semantics");
        });
    }
    static void Reject(Action f){try{f();}catch(ArgumentException){return;}throw new Exception("Expected clean input rejection");}
}
