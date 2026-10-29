using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class RoofSurfaceTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("stepped pitched building uses authored flat surface on lower wing roofs only",()=>{
   var b=new Building{X=40,Y=40,Width=12,Height=10,Name="Station roof",Kind="shop"};b.Design=BuildingDesigner.Generate(b,2,42);var upper=b.Design.Floors[1];
   upper.Cells=new string(Enumerable.Range(0,120).Select(i=>i%12<5?upper.Cells[i]:'.').ToArray());upper.Objects.Clear();upper.Edges=BuildingDesigner.AutoEdges(b,upper,1);
   b.Design.Roof="gabled";b.Design.FlatRoofTile="blends_street_01_48";BuildingDesigner.Validate(b);
   var scene=NativeScene.Building(b);Check(scene.Sprites.Any(s=>s.X==10&&s.Y==10&&s.Z==1&&s.Tile=="blends_street_01_48"),"Exposed lower wing ignored its flat roof surface");
   Check(scene.Sprites.Any(s=>s.X==4&&s.Y==10&&s.Z==2&&s.Tile=="floors_interior_tilesandwood_01_40"),"Pitched roof underlay changed");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   Check(AssetTests.NativeTiles(zip,p,48,48,1).SequenceEqual(new[]{"blends_street_01_48"}),"Native lower wing does not match preview");
   b.Design.FlatRoofTile=null;Check(!NativeScene.Building(b).Sprites.Any(s=>s.Tile=="blends_street_01_48"),"Removing the chosen surface was not reversible");
  });
  yield return ("authored flat roof survives JSON preview resource enumeration and native square export",()=>{
   var b=WithRoof(House(),"floors_interior_tilesandwood_01_8");
   var json=JsonSerializer.SerializeToNode(b,NativeMapExporter.JsonOptions)!;
   Check(json["design"]?["flatRoofTile"]?.GetValue<string>()=="floors_interior_tilesandwood_01_8","Explicit flat roof was discarded by JSON roundtrip");
   var roof=NativeScene.Building(b,1).Sprites;
   Check(roof.Count==36&&roof.All(s=>s.Tile=="floors_interior_tilesandwood_01_8"),"Preview substituted the chosen flat roof surface");
   Check(NativeScene.BuildingResourceIds(b).Contains("floors_interior_tilesandwood_01_8"),"Roof missing from resource dependencies");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=4;b.X=b.Y=40;p.Buildings.Add(b);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   Check(AssetTests.NativeTiles(zip,p,42,42,1).SequenceEqual(new[]{"floors_interior_tilesandwood_01_8"}),"Actual native roof square differs from preview");
  });
  yield return ("custom balcony perimeter preserves its authored wall and native collision flags",()=>{
   var b=House();var f=b.Design!.Floors[0];f.Rooms["2"]="balcony";f.Cells=new string('1',30)+new string('2',6);f.Edges=BuildingDesigner.AutoEdges(b,f,0);
   var edge=f.Edges.Single(e=>e.X==0&&e.Y==5&&e.Edge=="w");edge.Kind="wall";edge.WallTile="fixture_railing_0";
   Check(NativeScene.Building(b,0).Sprites.Any(s=>s.X==2&&s.Y==7&&s.Tile=="fixture_railing_0"),"Balcony perimeter silently replaced the authored railing");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=4;b.X=b.Y=40;p.Buildings.Add(b);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   Check(AssetTests.NativeTiles(zip,p,40,45,0).Contains("fixture_railing_0"),"Native balcony railing differs from preview");
   Check((NativeSceneNavigation(p,40,45)&4)!=0,"Custom balcony edge lost west collision");
  });
  yield return ("flat roof defaults and pitched roof underlay remain backward compatible",()=>{
   var b=House();Check(NativeScene.Building(b,1).Sprites.All(s=>s.Tile=="floors_interior_tilesandwood_01_40"),"Default flat roof changed");
   b=WithRoof(b,"floors_interior_tilesandwood_01_8");b.Design!.Roof="gabled";
   var scene=NativeScene.Building(b);Check(!scene.Sprites.Any(s=>s.Z>0&&s.Tile=="floors_interior_tilesandwood_01_8")&&scene.Sprites.Any(s=>s.Z>0&&s.Tile=="floors_interior_tilesandwood_01_40"),"Flat roof option changed pitched roof underlay");
  });
  yield return ("flat roof resources reject malformed IDs and require the appearance format",()=>{
   Reject(()=>BuildingDesigner.Validate(WithRoof(House(),"invalid / roof")));
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=3;var b=WithRoof(House(),"floors_interior_tilesandwood_01_8");b.X=b.Y=40;p.Buildings.Add(b);Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("flat roof validation rejects walls and wet surfaces",()=>{
   var game=Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??"";if(!Directory.Exists(Path.Combine(game,"media"))){Console.WriteLine("SKIP installed-game flat roof resource validation: game absent");return;}
   var assets=new GameAssetCatalog(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory(),game);
   Reject(()=>AssetRules.Building(WithRoof(House(),"walls_exterior_house_01_0"),assets));
   var water=assets.Get("blends_natural_02_0");if(water.Properties.ContainsKey("water"))Reject(()=>AssetRules.Building(WithRoof(House(),water.Id),assets));
   AssetRules.Building(WithRoof(House(),"floors_interior_tilesandwood_01_8"),assets);
  });
 }
 static Building House(){var b=new Building{Width=6,Height=6,Name="Roof fixture",Kind="house",Design=new(){Material="brick",Roof="flat",Floors=[new(){Cells=new('1',36),Rooms=new(){["1"]="livingroom"}}]}};b.Design.Floors[0].Edges=BuildingDesigner.AutoEdges(b,b.Design.Floors[0],0);return b;}
 static Building WithRoof(Building b,string tile){var node=JsonSerializer.SerializeToNode(b,NativeMapExporter.JsonOptions)!;node["design"]!["flatRoofTile"]=tile;return node.Deserialize<Building>(NativeMapExporter.JsonOptions)!;}
 static byte NativeSceneNavigation(MapProject p,int x,int y){var type=typeof(NativeScene).Assembly.GetType("SimplePZMapmaker.Core.Export.TileComposer")!;var instance=Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,[p,0,0,null],null)!;return ((byte[])type.GetProperty("Navigation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(instance)!)[y*256+x];}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid roof accepted");}
}
