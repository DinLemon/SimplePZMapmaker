using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class LargeBuildingPreviewTests
{
 const string Floor="floors_interior_tilesandwood_01_40",FarFloor="large_preview_floor_0",FarWall="large_preview_wall_0",FarOverlay="large_preview_overlay_0";

 public static IEnumerable<(string,Action)> Cases(){
  foreach(var (width,height) in new[]{(87,92),(91,95),(263,24),(24,263),(256,256),(512,512),(2,512),(512,2)}){
   yield return ($"large building preview public and draft preserve complete {width}x{height} footprint",()=>{
    var b=Fixture(width,height);var before=JsonSerializer.Serialize(b);
    foreach(var preview in new Func<Building,int?,NativeScene.Scene>[] {NativeScene.Building,NativeScene.DraftBuilding}){
     var scene=preview(b,null);Check(scene.Width==width+6&&scene.Height==height+6,"Preview dimensions changed");
     Check(scene.MinLevel==0&&scene.MaxLevel==1,"Default floor/roof range changed");
     Check(scene.Sprites.All(s=>s.X>=0&&s.Y>=0&&s.X<scene.Width&&s.Y<scene.Height),"Cell offsets escaped the scene");
     var interior=scene.Sprites.Where(s=>s.Z==0&&(s.Tile==Floor||s.Tile==FarFloor)).ToArray();
     Check(interior.Length==width*height&&interior.Select(s=>(s.X,s.Y)).Distinct().Count()==width*height,"Floor tiles lost or duplicated across native cells");
     Check(interior.Min(s=>s.X)==2&&interior.Max(s=>s.X)==width+1&&interior.Min(s=>s.Y)==2&&interior.Max(s=>s.Y)==height+1,"Building origin is no longer (2,2)");
     Has(scene,width+1,height+1,0,FarFloor);Has(scene,width+2,height+1,0,FarWall);Has(scene,width+2,height+1,0,FarOverlay);
     Check(scene.Sprites.Count(s=>s.Z==1&&s.Tile==Floor)==width*height,"Default roof was truncated");
     var selected=preview(b,0);Check(selected.MinLevel==0&&selected.MaxLevel==0&&selected.Sprites.All(s=>s.Z==0),"Selected floor included other levels");
    }
    Check(before==JsonSerializer.Serialize(b),"Preview changed the original building");
   });
  }
  yield return ("large building preview multilevel 87x92 keeps basement stairs and roof",()=>{
   var b=Fixture(87,92,3,-1);var before=JsonSerializer.Serialize(b);
   foreach(var preview in new Func<Building,int?,NativeScene.Scene>[] {NativeScene.Building,NativeScene.DraftBuilding}){
    var scene=preview(b,null);Check(scene.MinLevel==-1&&scene.MaxLevel==2,"Multilevel range changed");
    foreach(var z in new[]{-1,0,1}){Has(scene,88,93,z,FarFloor);Has(scene,89,93,z,FarWall);}
    Has(scene,3,4,-1,"carpentry_02_98");Has(scene,88,93,2,Floor);
    Check(!scene.Sprites.Any(s=>s.X==3&&s.Y==4&&s.Z==0&&s.Tile==Floor),"Stair opening lost");
    var selected=preview(b,-1);Check(selected.MinLevel==-1&&selected.MaxLevel==-1&&selected.Sprites.All(s=>s.Z==-1),"Basement selection changed");
   }
   Check(before==JsonSerializer.Serialize(b),"Multilevel preview mutated its input");
  });
  foreach(var (width,height) in new[]{(263,24),(24,263),(512,512)}){
   yield return ($"large building preview resource dependencies include far edges of {width}x{height}",()=>{
    var b=Fixture(width,height);var before=JsonSerializer.Serialize(b);var ids=NativeScene.BuildingResourceIds(b);
    Check(new[]{Floor,FarFloor,FarWall,FarOverlay}.All(ids.Contains),"Far-cell floor, wall or overlay resource omitted");
    Check(!ids.Any(id=>id.StartsWith("blends_",StringComparison.Ordinal)),"Dummy terrain leaked into building dependencies");
    Check(before==JsonSerializer.Serialize(b),"Resource discovery mutated its input");
   });
  }
  yield return ("large building preview draft retains access distinction and validation",()=>{
   var b=Fixture(263,24);b.Design!.Floors[0].Edges[0].Kind="wall";var before=JsonSerializer.Serialize(b);
   Check(NativeScene.DraftBuilding(b,0).Sprites.Count>0,"Unfinished large draft was rejected");
   Reject(()=>NativeScene.Building(b,0),"entrance");
   Check(before==JsonSerializer.Serialize(b),"Rejected strict preview mutated its input");
   b.Design.Floors[0].FloorTiles[b.Width*b.Height-1]="invalid / resource";
   Reject(()=>NativeScene.DraftBuilding(b,0),"resources");Reject(()=>NativeScene.BuildingResourceIds(b),"resources");
  });
  yield return ("large building preview generic Cell still limits 96 tiles and one native cell",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});
   Check(NativeScene.Cell(p,0,0,96,96,0).Sprites.Count==9216,"Generic 96x96 preview changed");
   Reject(()=>NativeScene.Cell(p,0,0,97,1),"96");Reject(()=>NativeScene.Cell(p,0,0,1,97),"96");
   Reject(()=>NativeScene.Cell(p,250,0,12,1),"one cell");
  });
  yield return ("large building preview rejects floor budgets before composition or layer validation",()=>{
   var b=Fixture(512,512);b.Design!.Floors.Add(new(){Cells=""});var before=JsonSerializer.Serialize(b);
   Reject(()=>NativeScene.Building(b,0),"floor-cell budget");Reject(()=>NativeScene.DraftBuilding(b,0),"floor-cell budget");
   Reject(()=>NativeScene.BuildingResourceIds(b),"floor-cell budget");Check(before==JsonSerializer.Serialize(b),"Rejected budget mutated input");
   b.Width=513;Reject(()=>NativeScene.Building(b),"dimensions");Reject(()=>NativeScene.DraftBuilding(b),"dimensions");Reject(()=>NativeScene.BuildingResourceIds(b),"dimensions");
  });
  yield return ("large building preview rejects invalid z before composition",()=>{
   var b=Fixture(263,24);
   foreach(var level in new[]{ZLevels.Min-1,ZLevels.Max+1}){Reject(()=>NativeScene.Building(b,level),"level");Reject(()=>NativeScene.DraftBuilding(b,level),"level");}
  });
  yield return ("large building preview aggregate sprite budget spans native cells and levels",()=>{
   // Each cell contributes under 590,000 sprites; together they exceed 786,432.
   var b=Fixture(256,512,2);foreach(var f in b.Design!.Floors){f.Objects.Clear();f.Edges.Clear();
    for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)f.Edges.Add(new(){X=x,Y=y,Kind="door",WallTile=FarWall,OpeningTile="large_preview_opening_0",OverlayTile=FarOverlay});
   }
   var before=JsonSerializer.Serialize(b);
   Reject(()=>NativeScene.DraftBuilding(b),"Too many sprites");
   Check(NativeScene.DraftBuilding(b,0).Sprites.Count>250000,"Building budget fell back to the generic sprite limit");
   Check(NativeScene.BuildingResourceIds(b).Contains("large_preview_opening_0"),"Dependency enumeration was limited by the preview sprite budget");
   Check(before==JsonSerializer.Serialize(b),"Sprite budget failure mutated input");
  });
  yield return ("large building preview legacy undesigned maximum footprint remains usable",()=>{
   var b=new Building{Width=512,Height=512,Kind="warehouse",X=6000,Y=7000};var before=JsonSerializer.Serialize(b);
   foreach(var preview in new Func<Building,int?,NativeScene.Scene>[] {NativeScene.Building,NativeScene.DraftBuilding}){
    var scene=preview(b,null);Check(scene.Width==518&&scene.Height==518&&scene.Sprites.Count>524288,"Legacy building lost its floor or roof");Has(scene,514,513,0,"walls_exterior_house_01_0");
   }
   Check(NativeScene.BuildingResourceIds(b).Contains("carpentry_01_16"),"Legacy far-cell furniture resource missing");
   Check(before==JsonSerializer.Serialize(b),"Legacy preview mutated input");
  });
  yield return ("large building preview small scene retains existing dimensions offsets and sprites",()=>{
   var b=Fixture(12,10);var clone=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(b))!;clone.X=8;clone.Y=8;
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=6;p.Spawns=[new(){X=0,Y=0}];p.Buildings.Add(clone);
   var expected=NativeScene.Cell(p,6,6,18,16,null,6,6);var actual=NativeScene.Building(b);
   Check(actual.Width==expected.Width&&actual.Height==expected.Height&&actual.MinLevel==expected.MinLevel&&actual.MaxLevel==expected.MaxLevel&&actual.Sprites.SequenceEqual(expected.Sprites),"Existing small-building scene contract changed");
  });
 }

 static Building Fixture(int width,int height,int floors=1,int minLevel=0){
  var b=new Building{Width=width,Height=height,Kind="warehouse",X=8000,Y=9000,Design=new(){MinLevel=minLevel}};
  for(var i=0;i<floors;i++){
   var f=new BuildingFloor{Cells=new('1',width*height),Rooms=new(){["1"]="warehouse"}};
   f.FloorTiles[width*height-1]=FarFloor;
   if(i+minLevel==0)f.Edges.Add(new(){X=1,Y=0,Kind="door"});
   f.Edges.Add(new(){X=width,Y=height-1,Edge="w",WallTile=FarWall,OverlayTile=FarOverlay});
   if(i<floors-1)f.Objects.Add(new(){X=1,Y=1,Kind="stairs"});
   b.Design.Floors.Add(f);
  }
  return b;
 }
 static void Has(NativeScene.Scene scene,int x,int y,int z,string tile)=>Check(scene.Sprites.Any(s=>s.X==x&&s.Y==y&&s.Z==z&&s.Tile==tile),$"Missing sprite {tile} at ({x},{y},{z})");
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action,string reason){try{action();}catch(ArgumentException e){Check(e.Message.Contains(reason,StringComparison.OrdinalIgnoreCase),"Wrong rejection: "+e.Message);return;}throw new Exception("Expected validation failure: "+reason);}
}
