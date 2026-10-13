using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class HousingTemplateTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("authored housing and public building templates have independent fresh deterministic plans",()=>{
   if(HousingTemplates.Catalog.Count<24||HousingTemplates.Catalog.Select(d=>d.Id).Distinct().Count()!=HousingTemplates.Catalog.Count)throw new Exception("Housing catalog incomplete");
   var layouts=new HashSet<string>();
   foreach(var descriptor in HousingTemplates.Catalog){
    var a=HousingTemplates.Create(descriptor.Id);var b=HousingTemplates.Create(descriptor.Id);BuildingDesigner.Validate(a);
    if(a.Id==b.Id||a.X!=8||a.Y!=8||!a.Modified||a.LevelsOrigin!="manual")throw new Exception("Preset placement identity not fresh");
    if(a.Width!=descriptor.Width||a.Height!=descriptor.Height||a.Design!.MinLevel!=-descriptor.Basements||a.Design.Floors.Count!=descriptor.Levels+descriptor.Basements)throw new Exception("Descriptor differs from actual plan");
    if(a.Design.Style is not("cottage" or "brick" or "stone" or "modern")||a.Design.Floors.Any(f=>f.Rooms.Count>35))throw new Exception("Unsupported preset style or room count");
    if(descriptor.References.Count==0||descriptor.References.Any(url=>!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")||descriptor.References.Any(url=>!a.Attribution.Contains(url)))throw new Exception("Housing reference lost");
    if(JsonSerializer.Serialize(a.Design)!=JsonSerializer.Serialize(b.Design))throw new Exception("Authored plan varied between calls");
    if(!layouts.Add(a.Width+","+a.Height+","+a.Design.Floors[a.Design.MinLevel*-1].Cells))throw new Exception("Two presets share a plan");
    a.Design.Floors[0].Objects.Clear();a.Design.Floors[0].Rooms.Clear();if(b.Design!.Floors[0].Rooms.Count==0)throw new Exception("Preset mutable state shared");
   }
   try{HousingTemplates.Create("unknown");throw new Exception("Unknown preset accepted");}catch(ArgumentException){}
  });
  yield return ("housing template3 and project5 JSON retain floors sources and authored metadata",()=>{
   foreach(var descriptor in HousingTemplates.Catalog){
    var original=HousingTemplates.Create(descriptor.Id);var json=JsonSerializer.Serialize(new{format="SimplePZBuilding",version=3,building=original,attribution=original.Attribution},NativeMapExporter.JsonOptions);
    using var document=JsonDocument.Parse(json,new(){MaxDepth=48});var loaded=JsonSerializer.Deserialize<Building>(document.RootElement.GetProperty("building").GetRawText(),NativeMapExporter.JsonOptions)!;BuildingDesigner.Validate(loaded);
    if(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions)!=JsonSerializer.Serialize(loaded,NativeMapExporter.JsonOptions))throw new Exception("Template read roundtrip lost authored data");
    var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=5;p.BuildingLibrary.Add(loaded);var restored=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;ProjectValidator.Validate(restored);
    if(restored.BuildingLibrary[0].Attribution.Count!=original.Attribution.Count)throw new Exception("Project read dropped research attribution");
   }
  });
  yield return ("distributed housing source templates and native archive match the built-in catalog",()=>{
   var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!Directory.Exists(Path.Combine(root.FullName,"examples","HousingTemplates")))root=root.Parent;
   if(root is null)throw new Exception("Distributed housing examples directory missing");var directory=Path.Combine(root.FullName,"examples","HousingTemplates");
   foreach(var descriptor in HousingTemplates.Catalog){using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,descriptor.Id+".pzbuilding.json")),new(){MaxDepth=48});if(source.RootElement.GetProperty("format").GetString()!="SimplePZBuilding"||source.RootElement.GetProperty("version").GetInt32()!=3)throw new Exception("Unsupported housing source template format");var loaded=JsonSerializer.Deserialize<Building>(source.RootElement.GetProperty("building").GetRawText(),NativeMapExporter.JsonOptions)!;BuildingDesigner.Validate(loaded);var expected=HousingTemplates.Create(descriptor.Id);if(JsonSerializer.Serialize(loaded.Design)!=JsonSerializer.Serialize(expected.Design)||!loaded.Attribution.SequenceEqual(expected.Attribution))throw new Exception("Distributed housing template differs from built-in plan: "+descriptor.Id);}
   var p=JsonSerializer.Deserialize<MapProject>(File.ReadAllText(Path.Combine(directory,"housing-templates.pzmap.json")),NativeMapExporter.JsonOptions)!;ProjectValidator.Validate(p);if(p.Version!=5||p.Buildings.Count!=HousingTemplates.Catalog.Count||p.WorldX!=100||p.WorldY!=100)throw new Exception("Housing example source metadata lost");using var zip=new ZipArchive(File.OpenRead(Path.Combine(directory,"HousingTemplates-B42.zip")));using var reader=new StreamReader(zip.Entries.Single(e=>e.FullName.EndsWith("/project.pzmap.json")).Open());var native=JsonSerializer.Deserialize<MapProject>(reader.ReadToEnd(),NativeMapExporter.JsonOptions)!;if(JsonSerializer.Serialize(p)!=JsonSerializer.Serialize(native))throw new Exception("Distributed native housing archive stale");
  });
  yield return ("urban sections retain independent entrance cores and private apartment rooms",()=>{
   foreach(var id in new[]{"urban-brick-five","urban-panel-five"}){
    var b=HousingTemplates.Create(id);var d=b.Design!;
    foreach(var (floor,z) in d.Floors.Select((f,z)=>(f,z))){
     if(Components(b,floor)!=2)throw new Exception("Sections joined through apartments or inaccessible rooms exist");
     if(floor.Objects.Count(o=>o.Kind=="stairs")!=(z<4?2:0))throw new Exception("One entrance lost its stairs");
     if(floor.Rooms.Values.Count(r=>r=="kitchen")!=8||floor.Rooms.Values.Count(r=>r=="bathroom")!=8||floor.Rooms.Values.Count(r=>r=="livingroom")!=8)throw new Exception("Eight apartment groups lost");
     var roomLinks=RoomLinks(b,floor);var coreCodes=floor.Objects.Where(o=>o.Kind=="stairs").Select(o=>BuildingDesigner.Cell(b,floor,o.X,o.Y)).ToHashSet();
     // In each core component, removing its public hall leaves four private apartment groups (and their balconies).
     if(z==4){var preceding=d.Floors[z-1];coreCodes=preceding.Objects.Where(o=>o.Kind=="stairs").Select(o=>BuildingDesigner.Cell(b,floor,o.X,o.Y)).ToHashSet();}
     if(coreCodes.Count!=2)throw new Exception("Stair cores merged");
     foreach(var core in coreCodes)if(roomLinks[core].Count!=4)throw new Exception("Core does not serve four apartment entrances");
     if(Components(b,floor,coreCodes)!=8)throw new Exception("Private apartments joined through a neighbouring flat");
     if(id=="urban-panel-five"&&floor.Objects.Count(o=>o.Kind=="bed")!=8)throw new Exception("Combined living bedrooms lack beds");
     if(z==0&&ExteriorEntrances(b,floor)!=2)throw new Exception("Independent entrance missing");
    }
   }
   var tower=HousingTemplates.Create("urban-panel-nine");if(tower.Design!.Floors.Any(f=>f.Rooms.Values.Count(n=>n=="kitchen")!=4)||tower.Design.Floors.Take(8).Any(f=>f.Objects.Count(o=>o.Kind=="stairs")!=1))throw new Exception("Tower floor group missing");
  });
  yield return ("housing native previews preserve signed depth stairs and upper floor holes",()=>{
   foreach(var descriptor in HousingTemplates.Catalog){
    var b=HousingTemplates.Create(descriptor.Id);var scene=NativeScene.Building(b);var d=b.Design!;
    if(scene.MinLevel!=d.MinLevel||scene.MaxLevel<descriptor.Levels)throw new Exception("Native scene truncated housing levels");
    for(var index=0;index<d.Floors.Count-1;index++)foreach(var stair in d.Floors[index].Objects.Where(o=>o.Kind=="stairs"))for(var step=1;step<=3;step++){
     var x=2+stair.X+(stair.Direction=="w"?step:0);var y=2+stair.Y+(stair.Direction=="n"?step:0);var z=index+d.MinLevel;
     if(!scene.Sprites.Any(s=>s.X==x&&s.Y==y&&s.Z==z&&s.Tile.StartsWith("carpentry_02_"))||scene.Sprites.Any(s=>s.X==x&&s.Y==y&&s.Z==z+1&&s.Tile.StartsWith("floors_")))throw new Exception("Native stairs or landing hole lost");
    }
   }
  });
  yield return ("housing example native export retains all room levels and 1024 bounded chunk offsets",()=>{
   var p=Example();using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));var header=zip.Entries.Single(e=>e.FullName.EndsWith("100_100.lotheader"));using var h=new BinaryReader(header.Open());
   if(Encoding.ASCII.GetString(h.ReadBytes(4))!="LOTH"||h.ReadInt32()!=1)throw new Exception("Native header signature");var tileCount=h.ReadInt32();for(var i=0;i<tileCount;i++)ReadLine(h);if(h.ReadInt32()!=8||h.ReadInt32()!=8||h.ReadInt32()!=-1||h.ReadInt32()!=9)throw new Exception("Native map housing depth");
   var rooms=h.ReadInt32();var levels=new HashSet<int>();for(var i=0;i<rooms;i++){ReadLine(h);levels.Add(h.ReadInt32());for(var rectangle=h.ReadInt32();rectangle>0;rectangle--){var x=h.ReadInt32();var y=h.ReadInt32();var width=h.ReadInt32();var height=h.ReadInt32();if(x<0||y<0||width<1||height<1||x+width>256||y+height>256)throw new Exception("Housing room rectangle outside cell");}for(var objects=h.ReadInt32();objects>0;objects--){h.ReadInt32();h.ReadInt32();h.ReadInt32();}}
   if(!levels.SetEquals(Enumerable.Range(-1,10)))throw new Exception("Housing room signed levels lost");if(h.ReadInt32()!=p.Buildings.Count)throw new Exception("Native building groups missing");for(var i=0;i<p.Buildings.Count;i++)for(var n=h.ReadInt32();n>0;n--){var room=h.ReadInt32();if(room<0||room>=rooms)throw new Exception("Native room group out of range");}
   using var bytes=new MemoryStream();zip.Entries.Single(e=>e.FullName.EndsWith("world_100_100.lotpack")).Open().CopyTo(bytes);bytes.Position=0;using var r=new BinaryReader(bytes);if(Encoding.ASCII.GetString(r.ReadBytes(4))!="LOTP"||r.ReadInt32()!=1||r.ReadInt32()!=1024)throw new Exception("Native pack signature");var offsets=Enumerable.Range(0,1024).Select(_=>r.ReadInt64()).ToArray();
   for(var chunk=0;chunk<1024;chunk++){var end=chunk==1023?bytes.Length:offsets[chunk+1];if(offsets[chunk]<8204||offsets[chunk]>=end||end>bytes.Length)throw new Exception("Invalid native chunk offset");bytes.Position=offsets[chunk];var squares=0;while(bytes.Position<end){var n=r.ReadInt32();if(n==-1){var skip=r.ReadInt32();if(skip<1)throw new Exception("Invalid run skip");squares+=skip;continue;}if(n<1||n>256)throw new Exception("Invalid tile record size");var room=r.ReadInt32();if(room<-1||room>=rooms)throw new Exception("Invalid native room index");for(var i=1;i<n;i++){var tile=r.ReadInt32();if(tile<0||tile>=tileCount)throw new Exception("Invalid native tile index");}squares++;}if(bytes.Position!=end||squares!=11*64)throw new Exception("Housing pack chunk did not fill every signed floor");}
   using var credit=new StreamReader(zip.Entries.Single(e=>e.FullName.EndsWith("ATTRIBUTION.txt")).Open());var text=credit.ReadToEnd();if(HousingTemplates.Catalog.SelectMany(d=>d.References).Distinct().Any(url=>!text.Contains(url)))throw new Exception("Native reference attribution missing");
  });
 }
 public static MapProject Example(){var p=MapGenerator.Generate(new(){Kind="blank",Name="Жилые и общественные шаблоны",Seed=20261012});p.Version=5;p.WorldX=100;p.WorldY=100;var x=8;var y=8;var rowHeight=0;foreach(var descriptor in HousingTemplates.Catalog){var b=HousingTemplates.Create(descriptor.Id);if(x+b.Width>=248){x=8;y+=rowHeight+6;rowHeight=0;}if(y+b.Height>=248)throw new Exception("Housing shelf layout exceeded native cell");b.X=x;b.Y=y;x+=b.Width+6;rowHeight=Math.Max(rowHeight,b.Height);p.Buildings.Add(b);}p.Spawns=[new(){X=4,Y=4}];p.Attribution=["Original housing and public building adaptations; see docs/HOUSING-REFERENCES.md for architectural references."];ProjectValidator.Validate(p);return p;}
 static string ReadLine(BinaryReader r){var result=new StringBuilder();for(byte b;(b=r.ReadByte())!=10;)result.Append((char)b);return result.ToString();}
 static int ExteriorEntrances(Building b,BuildingFloor f)=>f.Edges.Count(e=>e.Kind is "door" or "open"&&(BuildingDesigner.Cell(b,f,e.X,e.Y)=='.'||(e.Edge=="n"?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y))=='.'));
 static Dictionary<char,HashSet<char>> RoomLinks(Building b,BuildingFloor f){var links=f.Rooms.Keys.ToDictionary(k=>k[0],_=>new HashSet<char>());foreach(var edge in f.Edges.Where(e=>e.Kind is "door" or "open")){var a=BuildingDesigner.Cell(b,f,edge.X,edge.Y);var c=edge.Edge=="n"?BuildingDesigner.Cell(b,f,edge.X,edge.Y-1):BuildingDesigner.Cell(b,f,edge.X-1,edge.Y);if(a!='.'&&c!='.'){links[a].Add(c);links[c].Add(a);}}return links;}
 static int Components(Building b,BuildingFloor f,HashSet<char>? excluded=null){var blocked=f.Edges.Where(e=>e.Kind is "wall" or "window").Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();var seen=new HashSet<int>();var count=0;for(var i=0;i<f.Cells.Length;i++){if(f.Cells[i]=='.'||excluded?.Contains(f.Cells[i])==true||seen.Contains(i))continue;count++;var q=new Queue<int>();q.Enqueue(i);seen.Add(i);while(q.TryDequeue(out var current)){var x=current%b.Width;var y=current/b.Width;foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var nx=x+dx;var ny=y+dy;var code=BuildingDesigner.Cell(b,f,nx,ny);if(code=='.'||excluded?.Contains(code)==true||blocked.Contains(dx!=0?(Math.Max(x,nx),y,"w"):(x,Math.Max(y,ny),"n")))continue;var next=ny*b.Width+nx;if(seen.Add(next))q.Enqueue(next);}}}return count;}
}
