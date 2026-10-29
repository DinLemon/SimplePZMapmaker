using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
public static class RaisedFacadeTests {
 static Building House(){var b=new Building{X=20,Y=20,Width=8,Height=8};b.Design=BuildingDesigner.Generate(b,1,42);b.Design.Roof="none";return b;}
 static BuildingEdge Edge(Building b)=>b.Design!.Floors[0].Edges.First(e=>e.Kind!="open");
 public static IEnumerable<(string,Action)> Cases(){
  yield return("raised facade default overlay retains old level and formats",()=>{var b=House();var p=new MapProject{Version=6,Buildings=[b]};var originalVersion=p.Version;var e=Edge(b);e.OverlayTile="test_facade_0";Check(p.Version==originalVersion,"Default overlay changed format");Check(NativeScene.Building(b).Sprites.Single(s=>s.Tile==e.OverlayTile).Z==0,"Default overlay moved");});
  yield return("raised facade roof decoration is exported above its wall",()=>{var b=House();var e=Edge(b);e.OverlayTile="test_facade_0";e.OverlayLevelOffset=1;var scene=NativeScene.Building(b);Check(scene.MaxLevel==1,"Raised plane missing from preview bounds");Check(scene.Sprites.Single(s=>s.Tile==e.OverlayTile).Z==1,"Overlay stayed under roof");Check(!NativeScene.Building(b,0).Sprites.Any(s=>s.Tile==e.OverlayTile),"Raised facade leaked onto ground floor view");Check(NativeScene.BuildingResourceIds(b).Contains(e.OverlayTile),"Raised dependency omitted");});
  yield return("raised facade source format promotes map and world",()=>{var b=House();var e=Edge(b);e.OverlayTile="test_facade_0";e.OverlayLevelOffset=1;var p=new MapProject{Buildings=[b]};Check(p.Version==17,"New semantics use an old map version");var w=new WorldProject{Maps=[new(){Project=p}]};Check(w.Version==12,"New semantics use an old world version");var clone=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(b,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;Check(Edge(clone).OverlayLevelOffset==1,"JSON roundtrip lost height");});
  foreach(var offset in new[]{-1,2})yield return($"raised facade rejects offset {offset}",()=>{var b=House();var e=Edge(b);e.OverlayTile="test_facade_0";e.OverlayLevelOffset=offset;Reject(()=>BuildingDesigner.Validate(b));});
  yield return("raised facade rejects missing decoration and signed floor overflow",()=>{var b=House();Edge(b).OverlayLevelOffset=1;Reject(()=>BuildingDesigner.Validate(b));var c=new Building{Width=8,Height=8};c.Design=BuildingDesigner.Generate(c,32,42);c.Design.Roof="none";var e=c.Design.Floors[^1].Edges[0];e.OverlayTile="test_facade_0";e.OverlayLevelOffset=1;Reject(()=>BuildingDesigner.Validate(c));});
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation rejection");}
}
