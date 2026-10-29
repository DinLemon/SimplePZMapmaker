using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Cli;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class ServerCheckSourceTests
{
 const string Root="Fixture/source/", Left="11111111111111111111111111111111", Right="22222222222222222222222222222222";
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("server-check source expanded index retains bounded probes across 129 linked sections",()=>{
   var f=Streamed();f.Projects=Enumerable.Range(0,129).Select(i=>Project(10+i,20,"expanded"+i)).ToArray();f.Rebuild();
   using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);
   Check(points.Count(p=>p.ExpectBuilding) is >0 and <=12,"Expanded layout lost bounded building probes");Inside(points,Enumerable.Range(0,129).Select(i=>((10+i)*256,20*256,256,256)).ToArray());
  });
  yield return ("server-check source legacy project retains building and zone probes",()=>{
   var p=Project(10,20,"local");using var bytes=Archive([( "Fixture/project.pzmap.json",Json(p))]);using var zip=new ZipArchive(bytes);
   var points=ServerCheck.Points(zip);Has(points,2580,5140,"local");Has(points,2596,5156,"forest (zone check)",false);Has(points,2636,5140,"open ground (zone check)",false);
   Check(points.Any(x=>!x.ExpectBuilding&&x.Name=="control without a building"),"Missing empty control");
  });
  yield return ("server-check source legacy world includes absolute shared buildings",()=>{
   var p=Project(10,20,"local");var world=new WorldProject{Maps=[new(){Id=Left,Project=p}],Buildings=[House(2600,5200,"shared")]};
   using var bytes=Archive([("Fixture/world.pzworld.json",Json(world))]);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);Has(points,2580,5140,"local");Has(points,2600,5200,"shared");
  });
  yield return ("server-check source streams multiple sections with immutable identities",()=>{
   var f=Streamed();var original=f.Entries.Select(e=>e.Data.ToArray()).ToArray();using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);
   var points=ServerCheck.Points(zip);Has(points,2580,5140,"left");Has(points,2836,5140,"right");Inside(points,[(2560,5120,256,256),(2816,5120,256,256)]);
   Check(f.Index.Sections[0].Id==Left&&f.Index.Sections[1].Id==Right,"Section identities changed");for(var i=0;i<zip.Entries.Count;i++){using var s=zip.Entries[i].Open();using var copy=new MemoryStream();s.CopyTo(copy);Check(copy.ToArray().SequenceEqual(original[i]),"Source bytes changed");}
  });
  yield return ("server-check source samples sections and shared seam within a small probe budget",()=>{
   var f=Streamed();for(var i=0;i<20;i++)f.Projects[0].Buildings.Add(House(40+i*8,40,"extra"+i));f.Metadata.Buildings=[House(2814,5190,"seam")];f.Rebuild();
   using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip,3);Check(points.Count(x=>x.ExpectBuilding)==3,"Building budget changed");Check(points.Any(x=>x.ExpectBuilding&&x.Name=="seam"),"Shared seam starved by local buildings");Check(points.Any(x=>x.ExpectBuilding&&x.X<2816&&x.Name!="seam")&&points.Any(x=>x.ExpectBuilding&&x.X>=2816),"Sections were not sampled");
  });
  yield return ("server-check source spreads a bounded sample across thirty exported sections",()=>{
   var f=Streamed();f.Projects=Enumerable.Range(10,30).Select(x=>Project(x,20,"section"+x)).ToArray();f.Rebuild();using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);
   Check(points.Count(p=>p.ExpectBuilding)==12,"Thirty sections exceeded the building budget");Has(points,2580,5140,"section10");Has(points,10004,5140,"section39");
  });
  yield return ("server-check source selected subset clips shared seam and ignores outside buildings",()=>{
   var f=Streamed();f.Metadata.Buildings=[House(2814,5190,"seam"),House(2600,5200,"outside")];f.Rebuild(selected:1);
   // A selected streamed export also contains the legacy standalone source. The index must win.
   f.Entries.Insert(0,("Fixture/project.pzmap.json",Json(f.Projects[1])));
   using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);Has(points,2816,5190,"seam");Has(points,2836,5140,"right");Check(points.All(x=>x.Name!="outside"&&x.Name!="left"),"Probed a context-only section");Inside(points,[(2816,5120,256,256)]);
  });
  yield return ("server-check source control stays inside an exported section at its north edge",()=>{
   var f=Streamed();f.Projects[0].Buildings=[House(20,1,"north")];f.Rebuild(selected:0);using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);Inside(points,[(2560,5120,256,256)]);Check(points.Any(x=>x.Name=="control without a building"&&!x.ExpectBuilding),"No in-section empty control");
  });
  yield return ("server-check source skips empty ground floors and probes ground rather than basement",()=>{
   var f=Streamed();var house=f.Projects[0].Buildings[0];house.Design!.MinLevel=-1;house.Design.Floors.Insert(0,new(){Cells="1111111111111111",Rooms=new(){["1"]="storage"}});house.Design.Floors[1].Cells="....111111111111";
   var empty=House(50,50,"empty ground");empty.Design!.Floors[0].Cells=new('.',16);empty.Design.Floors.Add(new(){Cells="1111111111111111",Rooms=new(){["1"]="bedroom"}});f.Projects[0].Buildings.Add(empty);f.Rebuild(selected:0);
   using var bytes=Archive(f.Entries);using var zip=new ZipArchive(bytes);var points=ServerCheck.Points(zip);Has(points,2580,5141,"left");Check(points.All(p=>p.Name!="empty ground"),"Empty ground floor was probed");
  });
  yield return ("server-check source rejects missing source and missing streamed metadata",()=>{
   Reject([]);var f=Streamed();Reject(f.Entries.Where(e=>!e.Name.EndsWith("world.metadata.json")).ToList());
  });
  yield return ("server-check source rejects index without any exported section",()=>{
   var f=Streamed();Reject(f.Entries.Where(e=>!e.Name.Contains("/sections/")).ToList());
  });
  yield return ("server-check source rejects ambiguous roots duplicate entries and legacy sources",()=>{
   var f=Streamed();Reject([..f.Entries,("Other/source/world.pzworld.index.json",Json(f.Index))]);Reject([..f.Entries,f.Entries[0]]);
   var p=Json(Project(10,20,"one"));Reject([("A/project.pzmap.json",p),("B/project.pzmap.json",p)]);
   Reject([("A/world.pzworld.json",Json(new WorldProject{Maps=[new(){Id=Left,Project=Project(10,20,"one")}]})),("A/project.pzmap.json",p)]);
  });
  yield return ("server-check source rejects malformed null and unsupported index JSON",()=>{
   foreach(var data in new[]{"null","{","{\"format\":\"other\",\"version\":1}","{\"format\":\"SimplePZWorldSections\",\"version\":2}"}){var f=Streamed();Replace(f.Entries,"world.pzworld.index.json",Encoding.UTF8.GetBytes(data));Reject(f.Entries);}
  });
  yield return ("server-check source rejects duplicate unsafe and unindexed section identities",()=>{
   var f=Streamed();f.Index.Sections[1]=f.Index.Sections[1] with{Id=Left};Replace(f.Entries,"world.pzworld.index.json",Json(f.Index));Reject(f.Entries);
   f=Streamed();f.Index.Sections[0]=f.Index.Sections[0] with{Id="../escape"};Replace(f.Entries,"world.pzworld.index.json",Json(f.Index));Reject(f.Entries);
   f=Streamed();f.Entries.Add((Root+"sections/33333333333333333333333333333333.pzmap.json",Json(f.Projects[0])));Reject(f.Entries);
  });
  yield return ("server-check source rejects layout metadata and immutable fingerprint mismatches",()=>{
   var f=Streamed();f.Index.Sections[0]=f.Index.Sections[0] with{WorldX=9};Replace(f.Entries,"world.pzworld.index.json",Json(f.Index));Reject(f.Entries);
   f=Streamed();f.Metadata.Id=Right;f.Rebuild();Reject(f.Entries);
   f=Streamed();f.Index.Sections[0]=f.Index.Sections[0] with{Hash=new('0',64)};Replace(f.Entries,"world.pzworld.index.json",Json(f.Index));Reject(f.Entries);
   f=Streamed();f.Index.MetadataHash=new('0',64);Replace(f.Entries,"world.pzworld.index.json",Json(f.Index));Reject(f.Entries);
  });
  yield return ("server-check source rejects oversized index before parsing",()=>{
   var f=Streamed();Replace(f.Entries,"world.pzworld.index.json",Encoding.UTF8.GetBytes(new string(' ',WorldSectionArchive.ManifestBytes)+Encoding.UTF8.GetString(Json(f.Index))));Reject(f.Entries);
  });
  yield return ("server-check source rejects invalid terrain before indexing",()=>{
   foreach(var terrain in new Terrain[]?[]{null,[],[Terrain.Grass]}){var f=Streamed();f.Projects[0].Terrain=terrain!;f.Rebuild();Reject(f.Entries);}
  });
  if(Environment.GetEnvironmentVariable("PZMAP_SERVERCHECK_SOURCE_ARCHIVE") is {Length:>0} archive)yield return ("server-check source supplied streamed ZIP probes stay inside exported sections",()=>{
   using var zip=ZipFile.OpenRead(archive);var entry=zip.Entries.Single(e=>e.Name=="world.pzworld.index.json");Check(entry.Length<=WorldSectionArchive.ManifestBytes,"Sample index exceeds manifest budget");using var stream=entry.Open();var index=JsonSerializer.Deserialize<WorldSectionArchive.Manifest>(stream,NativeMapExporter.JsonOptions)!;
   var prefix=entry.FullName[..^"world.pzworld.index.json".Length]+"sections/";var exported=index.Sections.Where(s=>zip.Entries.Any(e=>e.FullName==prefix+s.Id+".pzmap.json")).ToArray();
   var points=ServerCheck.Points(zip);Check(points.Count(p=>p.ExpectBuilding) is >0 and <=12,"Sample archive has no bounded building probes");Check(points.Any(p=>p.Name=="control without a building"&&!p.ExpectBuilding),"Sample archive has no empty control");Inside(points,exported.Select(s=>(s.WorldX*256,s.WorldY*256,s.Width,s.Height)).ToArray());
   Console.WriteLine($"Sample source: {exported.Length}/{index.Sections.Count} sections, {points.Count(p=>p.ExpectBuilding)} building probes, {points.Count(p=>!p.ExpectBuilding)} controls/zones");
  });
 }
 static MapProject Project(int x,int y,string name){
  var p=new MapProject{Version=6,WorldSection=true,WorldX=x,WorldY=y,Name=name,Terrain=new Terrain[256*256],Zombies=new int[1024],Buildings=[House(20,20,name)]};
  for(var yy=24;yy<64;yy++)for(var xx=24;xx<64;xx++)p.Terrain[yy*256+xx]=Terrain.Forest;return p;
 }
 static Building House(int x,int y,string name)=>new(){Id=Guid.NewGuid().ToString("N"),X=x,Y=y,Width=4,Height=4,Name=name,Design=new(){Floors=[new(){Cells="1111111111111111",Rooms=new(){["1"]="storage"}}]}};
 sealed class Fixture {
  public MapProject[] Projects=[Project(10,20,"left"),Project(11,20,"right")];
  public WorldSectionArchive.Manifest Index=new(){Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",Name="Fixture",Revision=1};
  public WorldSectionArchive.Metadata Metadata=new(){Version=2,Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",Name="Fixture"};
  public List<(string Name,byte[] Data)> Entries=[];
  public void Rebuild(int? selected=null){
   var meta=Json(Metadata);Index.MetadataBytes=meta.Length;Index.MetadataHash=Hash(meta);Index.Sections=[];Entries=[];
   for(var i=0;i<Projects.Length;i++){var p=Projects[i];var data=Json(p);var id=i==0?Left:i==1?Right:(i+1).ToString("x32");Index.Sections.Add(new(id,Hash(data),p.Name,p.WorldX,p.WorldY,p.Width,p.Height,data.Length,p.Spawns.Count));if(selected is null||selected==i)Entries.Add((Root+"sections/"+id+".pzmap.json",data));}
   Entries.Add((Root+"world.metadata.json",meta));Entries.Add((Root+"world.pzworld.index.json",Json(Index)));
  }
 }
 static Fixture Streamed(){var f=new Fixture();f.Rebuild();return f;}
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 static byte[] Json<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,NativeMapExporter.JsonOptions);
 static MemoryStream Archive(List<(string Name,byte[] Data)> entries){var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))foreach(var e in entries){using var s=zip.CreateEntry(e.Name,CompressionLevel.Fastest).Open();s.Write(e.Data);}bytes.Position=0;return bytes;}
 static void Replace(List<(string Name,byte[] Data)> entries,string name,byte[] data){var i=entries.FindIndex(e=>e.Name.EndsWith(name,StringComparison.Ordinal));entries[i]=(entries[i].Name,data);}
 static void Reject(List<(string Name,byte[] Data)> entries){using var bytes=Archive(entries);using var zip=new ZipArchive(bytes);try{ServerCheck.Points(zip);}catch(ArgumentException){return;}throw new Exception("Invalid archive source was accepted");}
 static void Has(List<ServerCheck.Point> points,int x,int y,string name,bool building=true)=>Check(points.Any(p=>p.X==x&&p.Y==y&&p.Name==name&&p.ExpectBuilding==building),$"Missing {name} at {x},{y}");
 static void Inside(List<ServerCheck.Point> points,(int X,int Y,int Width,int Height)[] areas)=>Check(points.All(p=>areas.Any(a=>p.X>=a.X&&p.Y>=a.Y&&p.X<a.X+a.Width&&p.Y<a.Y+a.Height)),"Probe outside exported sections");
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
