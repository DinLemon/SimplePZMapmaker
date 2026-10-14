using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
public static class ZonePlanningTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("agent zone proposals are isolated and reject overlapping invalid batches atomically",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var zones=new[]{new GameZone{X=10,Y=10,Width=20,Height=20,Type="Farm",Name="Farm"}};var method=typeof(GameZones).GetMethod("ApplyProposal");if(method is null)throw new Exception("No isolated gameplay zone planning exists");var before=JsonSerializer.Serialize(p);var next=(MapProject)method.Invoke(null,[p,zones])!;if(p.GameZones.Count!=0||next.GameZones.Count!=1)throw new Exception("Zone planning mutated the source");try{method.Invoke(null,[p,new[]{zones[0],new GameZone{X=15,Y=15,Width=10,Height=10}}]);throw new Exception("Overlap accepted");}catch(System.Reflection.TargetInvocationException e) when(e.InnerException is ArgumentException){}if(JsonSerializer.Serialize(p)!=before)throw new Exception("Rejected batch mutated source");if(!GameZonesExporter.ObjectsLua(next).Contains("Farm"))throw new Exception("Planned zone lost at native export");
  });
 }
}
