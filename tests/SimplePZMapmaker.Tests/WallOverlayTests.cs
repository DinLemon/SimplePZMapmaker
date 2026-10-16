using System.IO.Compression;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

// Wall edge overlays: window surrounds, shutters and facade lettering drawn on the same edge after the wall/opening.
public static class WallOverlayTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("wall edge overlay survives JSON and is exported after the wall and window",()=>{
   var b=new Building{X=40,Y=40,Width=10,Height=8,Kind="house"};b.Design=BuildingDesigner.Generate(b,1,7);
   var f=b.Design.Floors[0];
   var window=f.Edges.FirstOrDefault(e=>e.Kind=="window"&&e.Edge=="n"&&e.Y==0)??f.Edges.First(e=>e.Kind=="wall"&&e.Edge=="n"&&e.Y==0);
   window.Kind="window";window.OverlayTile="fixtures_windows_detailing_01_18";
   var copy=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(b,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
   Check(copy.Design!.Floors[0].Edges.Single(e=>e.X==window.X&&e.Y==window.Y&&e.Edge=="n").OverlayTile=="fixtures_windows_detailing_01_18","Overlay lost in JSON");
   Check(ProjectValidator.UsesAssets(b),"Overlay does not mark the building as using game resources");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=4;p.Buildings.Add(b);ProjectValidator.Validate(p);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   var tiles=AssetTests.NativeTiles(zip,p,b.X+window.X,b.Y+window.Y,0);
   Check(tiles.LastOrDefault()=="fixtures_windows_detailing_01_18",$"Overlay not drawn last on its edge: {string.Join(',',tiles)}");
   Check(tiles.Any(t=>t.StartsWith("fixtures_windows_01_")),"Window lost under the overlay");
  });
  yield return ("wall edge overlay rejects open edges and malformed tiles",()=>{
   var b=new Building{X=40,Y=40,Width=10,Height=8,Kind="house"};b.Design=BuildingDesigner.Generate(b,1,7);
   var e=b.Design.Floors[0].Edges.First(e=>e.Kind=="wall");
   e.OverlayTile="not a tile";Reject(()=>BuildingDesigner.Validate(b));
   e.OverlayTile="fixtures_windows_detailing_01_18";e.Kind="open";Reject(()=>BuildingDesigner.Validate(b));
  });
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid wall overlay accepted");}
}
