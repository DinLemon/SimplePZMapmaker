using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
public static class StairGuardTests {
 public static IEnumerable<(string,Action)> Cases(){
  foreach(var direction in new[]{"n","w"})yield return($"stair guards protect the upper shaft while retaining {direction} stair access",()=>{
   var b=Fixture(direction);var unsafeBefore=WalkabilityAnalyzer.Building(b);Check(unsafeBefore.Issues.Any(i=>i.Code=="unsafe-edge"),"Missing control warning");Guard(b,direction);BuildingDesigner.Validate(b);
   var scene=NativeScene.Building(b);Check(b.Design!.Floors[1].Edges.Where(e=>e.WallTile?.StartsWith("carpentry_02_10")==true).All(e=>scene.Sprites.Any(s=>s.Z==1&&s.X==e.X+2&&s.Y==e.Y+2&&s.Tile==e.WallTile)),"Guard sprites missing from native scene: "+string.Join(",",scene.Sprites.Where(s=>s.Z==1&&s.Tile.StartsWith("carpentry_")).Select(s=>s.Tile+"@"+s.X+":"+s.Y)));Check(scene.Sprites.Count(s=>s.Z==0&&s.Tile is "carpentry_02_98" or "carpentry_02_97" or "carpentry_02_96" or "carpentry_02_90" or "carpentry_02_89" or "carpentry_02_88")==3,"Guard changed the stair run");
   var report=WalkabilityAnalyzer.Building(b);Check(report.Complete&&report.Issues.All(i=>i.Code is not("unsafe-edge" or "broken-stair" or "unreachable-room")),"Guarded stair is unsafe or inaccessible");
   var p=MapGenerator.Generate(new(){Kind="blank"});b.X=b.Y=40;p.Buildings.Add(b);using var zip=new System.IO.Compression.ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   var ex=direction=="n"?44:48;var ey=direction=="n"?48:44;Check(AssetTests.NativeTiles(zip,p,ex,ey,1).Contains(direction=="n"?"carpentry_02_101":"carpentry_02_100"),"Native upper end guard omitted");
  });
  yield return("stair guards do not permit barriers in a lower run upper shaft or onward staircase",()=>{
   foreach(var (floor,step,levels) in new[]{(0,4,2),(1,1,2),(1,4,3)}){var b=Fixture("n",levels);b.Design!.Floors[floor].Edges.Add(new(){X=4,Y=4+step,Edge="n",Kind="wall",WallTile="carpentry_02_101"});Reject(()=>BuildingDesigner.Validate(b));}
  });
  yield return("stair guard resources accept directional transparent walls but reject mismatched openings",()=>{
   var game=Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??"";if(!Directory.Exists(Path.Combine(game,"media"))){Console.WriteLine("SKIP installed-game stair guard properties: game absent");return;}
   var assets=new GameAssetCatalog(Path.Combine(Path.GetTempPath(),"spzm-stair-guard-assets"),game);
   AssetRules.CheckEdge(assets,"carpentry_02_101",null,"n","wall");AssetRules.CheckEdge(assets,"carpentry_02_100",null,"w","wall");
   Reject(()=>AssetRules.CheckEdge(assets,"carpentry_02_101",null,"w","wall"));Reject(()=>AssetRules.CheckEdge(assets,"carpentry_02_101",null,"n","door"));Reject(()=>AssetRules.CheckEdge(assets,"carpentry_02_101",null,"n","window"));
  });
 }
 static Building Fixture(string direction,int levels=2){var b=new Building{X=40,Y=40,Width=12,Height=12,Name="Guarded stair fixture",Design=new(){Material="brick",Roof="flat"}};for(var z=0;z<levels;z++){var f=new BuildingFloor{Cells=new('1',144),Rooms=new(){["1"]="hall"}};b.Design.Floors.Add(f);f.Edges=BuildingDesigner.AutoEdges(b,f,z);if(z<levels-1)f.Objects.Add(new(){X=4,Y=4,Kind="stairs",Direction=direction});}return b;}
 static void Guard(Building b,string direction){var upper=b.Design!.Floors[1];for(var j=1;j<=3;j++)foreach(var side in new[]{0,1})upper.Edges.Add(new(){X=direction=="n"?4+side:4+j,Y=direction=="n"?4+j:4+side,Edge=direction=="n"?"w":"n",Kind="wall",WallTile=direction=="n"?"carpentry_02_100":"carpentry_02_101"});upper.Edges.Add(new(){X=direction=="n"?4:8,Y=direction=="n"?8:4,Edge=direction,Kind="wall",WallTile=direction=="n"?"carpentry_02_101":"carpentry_02_100"});}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Blocked stair accepted");}
}
