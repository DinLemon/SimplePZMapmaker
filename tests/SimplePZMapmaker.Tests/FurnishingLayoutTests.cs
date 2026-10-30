using SimplePZMapmaker.Core;
public static class FurnishingLayoutTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("refurnishing preserves authored geometry tile groups and models as one draft operation",()=>{
   var b=Room("kitchen",12,10);var f=b.Design!.Floors[0];f.Objects.Add(new(){Kind="asset",X=4,Y=4,Asset="furniture_storage_01_0",Parts=[new(0,0,"furniture_storage_01_0")]});f.StaticModels.Add(new(){ModelId="Base.BeerBottle",X=6,Y=6,Profile="scenery"});
   var geometry=f.Cells+System.Text.Json.JsonSerializer.Serialize(f.Edges);BuildingDesigner.Refurnish(b,0);
   Check(f.Objects.Any(o=>o.Kind=="asset"&&o.X==4&&o.Y==4)&&f.StaticModels.Count==1,"Refurnish discarded authored resources");Check(geometry==f.Cells+System.Text.Json.JsonSerializer.Serialize(f.Edges),"Refurnish changed the walls or footprint");Check(f.Objects.Any(o=>o.Asset?.StartsWith("stove:")==true),"Kitchen was not furnished");
   var once=System.Text.Json.JsonSerializer.Serialize(f.Objects);BuildingDesigner.Refurnish(b,0);Check(once==System.Text.Json.JsonSerializer.Serialize(f.Objects),"Refurnish is not deterministic");
  });
  yield return ("generated stair circulation reaches rooms beyond the physical steps",()=>{
   foreach(var kind in new[]{"house","apartments","clinic","school","hotel"})for(var seed=0;seed<4;seed++){
    var b=new Building{Width=14,Height=12,Kind=kind};BuildingDesigner.Generate(b,2,seed);
    var report=WalkabilityAnalyzer.Building(b);Check(report.Complete&&!report.Issues.Any(i=>i.Code is "broken-stair" or "unreachable-room"),$"{kind}, seed {seed}: "+string.Join("; ",report.Issues.Where(i=>i.Code is "broken-stair" or "unreachable-room").Select(i=>i.Message)));
   }
  });
  yield return ("furnishing keeps a narrow connection between two entrances walkable",()=>{
   for(var seed=0;seed<40;seed++){
    var b=Room("warehouse",9,5,"111...111111...111111111111111...111111...111");var f=b.Design!.Floors[0];
    f.Edges=BuildingDesigner.AutoEdges(b,f,0);f.Edges.RemoveAll(e=>e.Edge=="w"&&e.Y==2&&(e.X==0||e.X==9));f.Edges.Add(new(){X=0,Y=2,Edge="w",Kind="door"});f.Edges.Add(new(){X=9,Y=2,Edge="w",Kind="door"});
    BuildingDesigner.Furnish(b,0,new(seed));var blocked=f.Objects.SelectMany(BuildingObjects.Cells).Select(p=>p.Y*b.Width+p.X).ToHashSet();var seen=new HashSet<int>{18};var q=new Queue<int>(seen);
    while(q.TryDequeue(out var i))foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var x=i%9+dx;var y=i/9+dy;var next=y*9+x;if(BuildingDesigner.Cell(b,f,x,y)=='1'&&!blocked.Contains(next)&&seen.Add(next))q.Enqueue(next);}
    Check(seen.Contains(26),$"Seed {seed}: furniture cut the only corridor");
   }
  });
  yield return ("furnishing respects model footprints and leaves halls free",()=>{
   var b=Room("hall",8,8);BuildingDesigner.Furnish(b,0,new(1));Check(b.Design!.Floors[0].Objects.Count==0,"Hall was given storage furniture");
   var f=b.Design.Floors[0];f.Rooms["1"]="bedroom";
   for(var y=0;y<8;y++)for(var x=0;x<8;x++)f.StaticModels.Add(new(){ModelId="Base.BeerBottle",X=x,Y=y,Profile="obstacle"});
   BuildingDesigner.Furnish(b,0,new(2));Check(f.Objects.Count==0,"Furniture overlaps authored model footprints");
  });
  yield return ("import room tags replace generated furnishings with the room purpose",()=>{
   var b=Room("bedroom",10,10);BuildingDesigner.Furnish(b,0,new(42));Check(b.Design!.Floors[0].Objects.Any(o=>o.Asset?.StartsWith("bed:")==true),"Fixture has no bed");
   GeographicImporter.ApplyRoomTags(b,new Dictionary<string,string>{{"pz:room","restaurantkitchen"}},s=>throw new Exception(s));
   var objects=b.Design.Floors[0].Objects;Check(objects.Any(o=>o.Asset?.StartsWith("stove:")==true),"Renamed kitchen kept the old bedroom furniture");Check(objects.All(o=>o.Asset?.StartsWith("bed:")!=true),"Kitchen still contains a generated bed");
   var before=System.Text.Json.JsonSerializer.Serialize(objects);GeographicImporter.ApplyRoomTags(b,new Dictionary<string,string>{{"pz:room","restaurantkitchen"}},_=>{});Check(before==System.Text.Json.JsonSerializer.Serialize(objects),"Repeated tag import changes or duplicates the furniture");
  });
 }
 static Building Room(string room,int width,int height,string? cells=null){var b=new Building{Width=width,Height=height};b.Design=new(){Floors=[new(){Cells=cells??new string('1',width*height),Rooms=new(){["1"]=room}}]};b.Design.Floors[0].Edges=BuildingDesigner.AutoEdges(b,b.Design.Floors[0],0);return b;}
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
}
