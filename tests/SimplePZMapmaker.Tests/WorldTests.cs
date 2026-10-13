using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text.Json;
public static class WorldTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("water-only section stays water and can spawn through another world section",()=>{var world=Fixture(2);var water=world.Maps[1].Project;water.Version=6;water.WorldSection=true;water.Spawns=[];Array.Fill(water.Terrain,Terrain.Water);WorldAssembler.Validate(world);Check(NativeScene.Cell(water,0,0,8,8,level:0).Sprites.All(s=>s.Tile=="blends_natural_02_0"),"Preview invented ground on water");using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(world)));Check(zip.Entries.Count(e=>e.Name.EndsWith(".lotheader"))==2,"Water section missing");try{NativeMapExporter.Export(water);throw new Exception("Water-only section exported standalone");}catch(ArgumentException){}world.Maps.RemoveAt(0);try{WorldAssembler.Validate(world);throw new Exception("World without land spawn accepted");}catch(ArgumentException){}});
  yield return ("world exceeds editor section size without a flattened raster",()=>{var w=Fixture(9);var bounds=WorldAssembler.Validate(w);if(bounds.Width!=2304||bounds.Cells!=9||w.Maps.Any(m=>m.Project.Width!=256))throw new Exception("World flattened or clipped to one section.");using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(w)));if(zip.Entries.Count(e=>e.FullName.EndsWith(".lotheader"))!=9)throw new Exception("Native cells lost at2048 boundary.");});
  yield return ("world move rejects overlap without changing source projects",()=>{var w=Fixture(2);var before=JsonSerializer.Serialize(w);try{WorldAssembler.Move(w,w.Maps[1].Id,100,100);}catch(ArgumentException){if(before!=JsonSerializer.Serialize(w))throw new Exception("Failed move mutated source.");return;}throw new Exception("Overlapping native cells accepted.");});
  yield return ("world seam diagnostics preserve water and report scale differences",()=>{var w=Fixture(2);w.Maps[1].Project.MetresPerTile=2;w.Maps[0].Project.Terrain[255]=Terrain.Water;var warnings=WorldAssembler.SeamWarnings(w);if(warnings.Length!=2||w.Maps[0].Project.Terrain[255]!=Terrain.Water)throw new Exception("Seam issue hidden or terrain rewritten.");});
 }
 public static WorldProject Fixture(int count){var w=new WorldProject{Name="Сшитый мир"};for(var i=0;i<count;i++){var p=MapGenerator.Generate(new(){Kind="blank",Name="Участок"+i});p.WorldX=100+i;p.WorldY=100;w.Maps.Add(new(){Project=p});}return w;}
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
