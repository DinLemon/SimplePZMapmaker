using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
public static class BuildingTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("hotel reception stays on ground when basements are generated",()=>{
   var b=new Building{Width=20,Height=18,Kind="hotel"};BuildingDesigner.Generate(b,2,42,"rect",2);if(!b.Design!.Floors[2].Rooms.Values.Contains("office")||b.Design.Floors.Take(2).Any(f=>f.Rooms.Values.Any(name=>name is not("hall" or "storage"))))throw new Exception("Ground hotel layout shifted underground");
  });
  yield return ("sixteen basements coexist with sixteen above-ground floors and regenerate",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=10};BuildingDesigner.Generate(b,16,42,"rect",16);p.Buildings.Add(b);ProjectValidator.Validate(p);if(b.Design!.MinLevel!=-16||b.Design.Floors.Count!=32)throw new Exception("Full depth missing");var masks=b.Design.Floors.Select(f=>f.Cells.Select(c=>c!='.').ToArray()).ToArray();BuildingDesigner.Regenerate(p,b.Id);if(b.Design.MinLevel!=-16||b.Design.Floors.Count!=32||b.Design.Floors.Where((f,i)=>!f.Cells.Select(c=>c!='.').SequenceEqual(masks[i])).Any())throw new Exception("Depth or masks lost");Reject(()=>BuildingDesigner.Generate(b,1,42,"rect",17));b.Design.MinLevel=-17;Reject(()=>BuildingDesigner.Validate(b));
  });
  yield return ("ground entrance is required despite valid exterior basement entrance",()=>{
   var b=new Building{Width=12,Height=10};BuildingDesigner.Generate(b,1,42,"rect",1);var ground=b.Design!.Floors[1];foreach(var e in ground.Edges.Where(e=>e.Kind=="door"&&(e.Y==0||e.X==0)))e.Kind="wall";b.Design.Floors[0].Edges.Single(e=>e.Y==0&&e.X==0&&e.Edge=="n").Kind="door";Reject(()=>BuildingDesigner.Validate(b));
  });
  yield return ("basement stairs cut ground floor while preserving remote terrain",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=10};BuildingDesigner.Generate(b,1,42,"rect",1);b.Design!.Roof="flat";p.Buildings.Add(b);using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var h=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith(".lotheader")).Open());h.ReadBytes(8);var names=new List<string>();for(var n=h.ReadInt32();n>0;n--){var text=new StringBuilder();for(byte c;(c=h.ReadByte())!=10;)text.Append((char)c);names.Add(text.ToString());}h.ReadInt32();h.ReadInt32();var min=h.ReadInt32();var max=h.ReadInt32();using var pack=new MemoryStream();zip.Entries.Single(e=>e.FullName.EndsWith(".lotpack")).Open().CopyTo(pack);pack.Position=0;using var r=new BinaryReader(pack);r.ReadBytes(12);var offsets=Enumerable.Range(0,1024).Select(_=>r.ReadInt64()).ToArray();
   List<string> Tiles(int x,int y,int z){var chunk=(x/8)*32+y/8;pack.Position=offsets[chunk];var target=(z-min)*64+(x%8)*8+y%8;var square=0;while(square<=target){var count=r.ReadInt32();if(count==-1){square+=r.ReadInt32();if(square>target)return [];continue;}r.ReadInt32();var tiles=Enumerable.Range(1,count-1).Select(_=>names[r.ReadInt32()]).ToList();if(square++==target)return tiles;}throw new Exception("Square missing");}
   var stair=b.Design.Floors[0].Objects.Single(o=>o.Kind=="stairs");for(var i=1;i<=3;i++){var x=b.X+stair.X;var y=b.Y+stair.Y+i;if(!Tiles(x,y,-1).Any(t=>t.StartsWith("carpentry_02_"))||Tiles(x,y,0).Any(t=>t.StartsWith("floors_")))throw new Exception("Basement stairs or ground hole lost");}if(!Tiles(10,10,0).Contains("blends_natural_01_32")||Tiles(10,10,-1).Count!=0)throw new Exception("Unrelated terrain filled underground");
  });
  yield return ("basement export retains signed rooms instead of shifting above ground",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=10};BuildingDesigner.Generate(b,2,42);var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(b,NativeMapExporter.JsonOptions))!;node["design"]!["minLevel"]=-1;b=JsonSerializer.Deserialize<Building>(node.ToJsonString(),NativeMapExporter.JsonOptions)!;
   for(var i=0;i<2;i++){var f=b.Design!.Floors[i];f.Cells=new string('0',120);f.Rooms=new(){["0"]="hall"};f.Objects.Clear();f.Edges=BuildingDesigner.AutoEdges(b,f,i-1);}b.Design!.Floors[0].Objects.Add(new(){Kind="stairs",X=1,Y=1});p.Version=3;p.Buildings.Add(b);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var r=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith(".lotheader")).Open());r.ReadBytes(8);for(var n=r.ReadInt32();n>0;n--)while(r.ReadByte()!=10){}r.ReadInt32();r.ReadInt32();if(r.ReadInt32()!=-1)throw new Exception("Basement minimum lost");if(r.ReadInt32()!=1)throw new Exception("Ground roof shifted");var levels=new HashSet<int>();for(var n=r.ReadInt32();n>0;n--){while(r.ReadByte()!=10){}levels.Add(r.ReadInt32());for(var j=r.ReadInt32()*4;j>0;j--)r.ReadInt32();for(var j=r.ReadInt32()*3;j>0;j--)r.ReadInt32();}if(!levels.SetEquals([-1,0]))throw new Exception("Basement room z lost");p.Version=2;Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("native attribution retains building and unused library credits",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{Id="credit",X=40,Y=40,Attribution=["Building Author"]};BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.BuildingLibrary.Add(new Building{Attribution=["Library Author"]});using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var reader=new StreamReader(zip.Entries.Single(e=>e.FullName.EndsWith("/ATTRIBUTION.txt")).Open());var text=reader.ReadToEnd();if(!text.Contains("Building Author")||!text.Contains("Library Author"))throw new Exception("Nested attribution lost");
  });
  yield return ("small and maximum generated plans are connected for every archetype",()=>{
   foreach(var kind in BuildingDesigner.Kinds)foreach(var shape in new[]{"rect","l","t","courtyard","annex"})foreach(var size in new[]{(6,6),(6,10),(10,6),(12,10),(64,64)})foreach(var seed in new[]{0,1,42}){
    var b=new Building{Width=size.Item1,Height=size.Item2,Kind=kind};BuildingDesigner.Generate(b,2,seed,shape);BuildingDesigner.Validate(b);
   }
  });
  yield return ("furniture reserves its complete footprint across room boundaries",()=>{
   var b=new Building{Width=12,Height=12};BuildingDesigner.Generate(b,1,42);var f=b.Design!.Floors[0];f.Objects.Clear();f.Objects.Add(new(){Kind="bed",X=11,Y=11});Reject(()=>BuildingDesigner.Validate(b));
   f.Objects=[new(){Kind="bed",X=4,Y=2},new(){Kind="seat",X=4,Y=3}];Reject(()=>BuildingDesigner.Validate(b));
   f.Objects=[new(){Kind="bed",X=4,Y=2}];f.Edges.Add(new(){X=4,Y=3,Edge="n",Kind="wall"});Reject(()=>BuildingDesigner.Validate(b));
  });
  yield return ("native furniture contains complete directional bed arrangements",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=12};BuildingDesigner.Generate(b,1,42);var f=b.Design!.Floors[0];f.Cells=new string('0',144);f.Rooms=new(){["0"]="hall"};f.Edges=BuildingDesigner.AutoEdges(b,f,0);f.Objects=[new(){Kind="bed",X=2,Y=2,Direction="n"},new(){Kind="bed",X=6,Y=6,Direction="w"},new(){Kind="seat",X=9,Y=9,Direction="w"}];p.Buildings.Add(b);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var r=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith(".lotheader")).Open());r.ReadBytes(8);var names=new HashSet<string>();for(var n=r.ReadInt32();n>0;n--){var text=new StringBuilder();for(byte c;(c=r.ReadByte())!=10;)text.Append((char)c);names.Add(text.ToString());}
   foreach(var tile in new[]{"furniture_bedding_01_68","furniture_bedding_01_69","furniture_bedding_01_70","furniture_bedding_01_71","furniture_seating_indoor_01_10"})if(!names.Contains(tile))throw new Exception("Missing complete oriented furniture: "+tile);
  });
  yield return ("legacy maps remain readable and rich maps require version2",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=1;ProjectValidator.Validate(p);var b=new Building{X=40,Y=40};b.Design=BuildingDesigner.Generate(b,2,42);p.Buildings.Add(b);Reject(()=>ProjectValidator.Validate(p));p.Version=2;ProjectValidator.Validate(p);
  });
  yield return ("designed multistorey building exports actual upper rooms",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{Id="floors",X=40,Y=40,Width=16,Height=14};
   b.Design=BuildingDesigner.Generate(b,3,7,"l");p.Buildings.Add(b);ProjectValidator.Validate(p);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var r=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith(".lotheader")).Open());
   r.ReadBytes(8);var tiles=r.ReadInt32();for(var i=0;i<tiles;i++)while(r.ReadByte()!=10){}r.ReadInt32();r.ReadInt32();r.ReadInt32();var max=r.ReadInt32();if(max<3)throw new Exception("Upper floors missing");
   var levels=new HashSet<int>();var rooms=r.ReadInt32();for(var i=0;i<rooms;i++){while(r.ReadByte()!=10){}levels.Add(r.ReadInt32());var rectangles=r.ReadInt32();for(var j=0;j<rectangles*4;j++)r.ReadInt32();var objects=r.ReadInt32();for(var j=0;j<objects*3;j++)r.ReadInt32();}if(!levels.SetEquals([0,1,2]))throw new Exception("Room levels lost");
  });
  yield return ("shaped design leaves courtyard land and allows another object",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{Id="court",X=40,Y=40,Width=24,Height=24};b.Design=BuildingDesigner.Generate(b,1,42,"courtyard");p.Buildings.Add(b);
   p.Spawns=[new(){X=52,Y=52}];p.Terrain[52*256+52]=Terrain.Water;try{ProjectValidator.Validate(p);}catch(ArgumentException){p.Terrain[52*256+52]=Terrain.Grass;ProjectValidator.Validate(p);}
   if(BuildingDesigner.Occupies(b,12,12))throw new Exception("Courtyard filled");
  });
  yield return ("building variants are deterministic and distinct",()=>{
   var b=new Building{Width=20,Height=18,Kind="apartments"};var a=BuildingDesigner.Generate(b,3,42,"rect");var copy=BuildingDesigner.Generate(b,3,42,"rect");var next=BuildingDesigner.Generate(b,3,43,"rect");
   if(JsonSerializer.Serialize(a)!=JsonSerializer.Serialize(copy)||JsonSerializer.Serialize(a)==JsonSerializer.Serialize(next))throw new Exception("Variant unstable");
  });
  yield return ("school apartment and house use different layout topology",()=>{
   var house=new Building{Width=30,Height=24,Kind="house"};var school=new Building{Width=30,Height=24,Kind="school"};var apartments=new Building{Width=30,Height=24,Kind="apartments"};
   var h=BuildingDesigner.Generate(house,1,42).Floors[0];var s=BuildingDesigner.Generate(school,1,42).Floors[0];var a=BuildingDesigner.Generate(apartments,1,42).Floors[0];
   if(h.Cells==s.Cells||h.Cells==a.Cells||s.Rooms.Count<=h.Rooms.Count||a.Rooms.Count<=h.Rooms.Count)throw new Exception("Archetypes only rename the same four rooms");
  });
  yield return ("invalid staircase and disconnected floor are rejected",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=16,Height=14};b.Design=BuildingDesigner.Generate(b,2,42,"rect");p.Buildings.Add(b);b.Design.Floors[0].Objects.RemoveAll(o=>o.Kind=="stairs");Reject(()=>ProjectValidator.Validate(p));
   b.Design=BuildingDesigner.Generate(b,1,42,"rect");b.Design.Floors[0].Cells="1"+new string('.',16*14-2)+"1";Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("stair runs cannot cross a wall or another run",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=20,Height=18};b.Design=BuildingDesigner.Generate(b,2,42);p.Buildings.Add(b);var stair=b.Design.Floors[0].Objects.Single(o=>o.Kind=="stairs");
   b.Design.Floors[0].Edges.Add(new(){X=stair.X,Y=stair.Y+2,Edge="n",Kind="wall"});Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("regeneration preserves edited buildings and exact floor masks",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var a=new Building{Id="a",X=40,Y=40,Width=20,Height=18};a.Design=BuildingDesigner.Generate(a,2,42,"l");var b=new Building{Id="b",X=80,Y=80,Width=20,Height=18,Modified=true};b.Design=BuildingDesigner.Generate(b,1,7,"courtyard");p.Buildings=[a,b];var old=JsonSerializer.Serialize(b);var masks=a.Design.Floors.Select(f=>f.Cells.Select(c=>c!='.').ToArray()).ToArray();
   if(BuildingDesigner.Regenerate(p)!=1||JsonSerializer.Serialize(b)!=old||a.Design.Floors.Where((f,i)=>!f.Cells.Select(c=>c!='.').SequenceEqual(masks[i])).Any())throw new Exception("Regeneration lost manual building or footprint");
  });
  yield return ("all archetypes and shapes have valid stair access",()=>{
   foreach(var kind in BuildingDesigner.Kinds)foreach(var shape in new[]{"rect","l","t","courtyard","annex"})foreach(var levels in new[]{1,2,5}){var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{Id="test",X=40,Y=40,Width=20,Height=18,Kind=kind};b.Design=BuildingDesigner.Generate(b,levels,42,shape);p.Buildings.Add(b);ProjectValidator.Validate(p);}
  });
  yield return ("OSM keeps levels type identity and manual reimport changes",()=>{
   var source="""{"elements":[{"type":"way","id":77,"tags":{"building":"apartments","building:levels":"3","building:levels:underground":"2","roof:shape":"gabled"},"geometry":[{"lon":30.0001,"lat":50.0001},{"lon":30.0004,"lat":50.0001},{"lon":30.0004,"lat":50.0004},{"lon":30.0001,"lat":50.0004},{"lon":30.0001,"lat":50.0001}]}]}""";
   var bounds=new GeoBounds{West=30,East=30.003,South=50,North=50.003};var result=GeographicImporter.Import(source,MapGenerator.Generate(new(){Kind="blank"}),bounds).Project;var b=result.Buildings.Single();
   if(b.Kind!="apartments"||b.Design?.Floors.Count!=5||b.Design.MinLevel!=-2||b.SourceBasements!="2"||b.LevelsOrigin!="osm-levels"||b.SourceId!="osm-way-77")throw new Exception("Building tags discarded");
   b.Name="Manually edited house";b.Modified=true;b.Design.Material="brick";var refreshed=GeographicImporter.Import(source,result,bounds,default,true).Project.Buildings.Single();
   if(refreshed.Name!=b.Name||!refreshed.Modified||refreshed.Design?.Material!="brick")throw new Exception("Manual changes lost");
  });
 }
 static void Reject(Action a){try{a();}catch(ArgumentException){return;}throw new Exception("Invalid design accepted");}
}
